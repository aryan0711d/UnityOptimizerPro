using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class OptimizationWindow : EditorWindow
{
    Vector2 scroll;

    SceneScanner scanner = new SceneScanner();

    List<Light> heavyLights = new List<Light>();
    List<Light> overlappingLights = new List<Light>();
    List<Light> shadowedPointLights = new List<Light>();
    List<Collider> colliderCandidates = new List<Collider>();
    List<GameObject> staticCandidates = new List<GameObject>();
    List<GameObject> instancingCandidates = new List<GameObject>();
    List<Camera> expensiveCameras = new List<Camera>();
    List<ParticleSystem> expensiveParticles = new List<ParticleSystem>();
    List<ReflectionProbe> realtimeReflectionProbes = new List<ReflectionProbe>();
    List<Terrain> heavyTerrains = new List<Terrain>();
    OptimizationReport report = new OptimizationReport();
    OptimizationSnapshot currentSnapshot = new OptimizationSnapshot();
    OptimizationSnapshot baselineSnapshot = new OptimizationSnapshot();

    float heavyLightRangeThreshold = 15f;
    float overlapFactor = 0.5f;
    int instancingThreshold = 5;
    int particleThreshold = 200;
    float smallShadowLightRangeThreshold = 10f;
    float recommendedShadowDistance = 80f;
    int recommendedPixelLightCount = 2;
    bool autoRefreshInPlayMode = true;
    double nextAutoScanTime;
    int shadowBudget = 2;
    int strongLightsPerCluster = 2;
    bool showAdvancedControls;
    bool showSceneHeatmap = true;
    float heatmapSize = 1.5f;
    string lastOptimizationSummary = "No optimization has been applied yet.";
    string lastOptimizationHeadline = "Run Boost Performance to apply the safest one-click FPS improvements.";

    string suggestions = string.Empty;
    GUIStyle heroButtonStyle;

    [MenuItem("Tools/Unity Optimizer Pro")]
    public static void ShowWindow()
    {
        GetWindow<OptimizationWindow>("Optimizer Pro");
    }

    void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += OnEditorUpdate;
        SceneView.duringSceneGui += OnSceneGui;
        ScanSceneSmart();
    }

    void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.update -= OnEditorUpdate;
        SceneView.duringSceneGui -= OnSceneGui;
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
            ScanSceneSmart();
    }

    void OnEditorUpdate()
    {
        if (!autoRefreshInPlayMode || !EditorApplication.isPlaying || !hasFocus)
            return;

        if (EditorApplication.timeSinceStartup < nextAutoScanTime)
            return;

        nextAutoScanTime = EditorApplication.timeSinceStartup + 0.5d;
        ScanSceneSmart();
        Repaint();
    }

    void OnSceneGui(SceneView sceneView)
    {
        if (!showSceneHeatmap)
            return;

        DrawHeatmapObjects(overlappingLights.Select(light => light != null ? light.gameObject : null), new Color(1f, 0.25f, 0.2f, 0.2f), new Color(1f, 0.25f, 0.2f, 0.85f));
        DrawHeatmapObjects(shadowedPointLights.Select(light => light != null ? light.gameObject : null), new Color(1f, 0.6f, 0.1f, 0.18f), new Color(1f, 0.65f, 0.15f, 0.8f));
        DrawHeatmapObjects(expensiveParticles.Select(system => system != null ? system.gameObject : null), new Color(1f, 0.85f, 0.1f, 0.14f), new Color(1f, 0.85f, 0.2f, 0.75f));
        DrawHeatmapObjects(realtimeReflectionProbes.Select(probe => probe != null ? probe.gameObject : null), new Color(0.45f, 0.7f, 1f, 0.16f), new Color(0.45f, 0.7f, 1f, 0.85f));
    }

    void DrawHeatmapObjects(IEnumerable<GameObject> objects, Color fillColor, Color outlineColor)
    {
        foreach (var obj in objects.Where(candidate => candidate != null).Distinct())
        {
            Vector3 position = obj.transform.position;
            float size = HandleUtility.GetHandleSize(position) * heatmapSize;
            Handles.color = fillColor;
            Handles.SphereHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
            Handles.color = outlineColor;
            Handles.DrawWireDisc(position, Vector3.up, size);
        }
    }

    void OnGUI()
    {
        bool scrollViewStarted = false;

        try
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            scrollViewStarted = true;

            DrawHeader();
            GUILayout.Space(8f);
            DrawScanControls();
            GUILayout.Space(10f);
            DrawReport();
            GUILayout.Space(10f);
            DrawActions();
            GUILayout.Space(10f);
            DrawSuggestions();
            GUILayout.Space(10f);
            DrawControls();
        }
        finally
        {
            if (scrollViewStarted)
                EditorGUILayout.EndScrollView();
        }
    }

    void DrawHeader()
    {
        GUILayout.Label("Unity Optimizer Pro", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            EditorApplication.isPlaying
                ? "Play Mode scan is active. Use Boost Performance for runtime-safe wins and compare the score and estimated relative FPS before and after."
                : "One-click optimization is ready. Boost Performance applies the safest high-value changes first, then advanced controls let power users go deeper.",
            MessageType.Info);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button(showSceneHeatmap ? "Hide Heatmap" : "Show Heatmap", GUILayout.Height(28f)))
        {
            showSceneHeatmap = !showSceneHeatmap;
            SceneView.RepaintAll();
        }

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Mobile Preset", GUILayout.Height(28f)))
                ApplyPlatformPreset(45f, 1, 0.7f, false);

            if (GUILayout.Button("VR Preset", GUILayout.Height(28f)))
                ApplyPlatformPreset(35f, 1, 0.8f, false);

            if (GUILayout.Button("PC Preset", GUILayout.Height(28f)))
                ApplyPlatformPreset(90f, 3, 1.3f, true);
        }

        GUILayout.EndHorizontal();
    }

    void DrawScanControls()
    {
        EditorGUI.BeginChangeCheck();
        heavyLightRangeThreshold = EditorGUILayout.Slider("Heavy Light Range", heavyLightRangeThreshold, 5f, 40f);
        smallShadowLightRangeThreshold = EditorGUILayout.Slider("Small Shadow Light Range", smallShadowLightRangeThreshold, 3f, 20f);
        overlapFactor = EditorGUILayout.Slider("Overlap Sensitivity", overlapFactor, 0.2f, 0.9f);
        instancingThreshold = EditorGUILayout.IntSlider("Instancing Threshold", instancingThreshold, 2, 20);
        particleThreshold = EditorGUILayout.IntSlider("Particle Threshold", particleThreshold, 50, 1000);
        autoRefreshInPlayMode = EditorGUILayout.Toggle("Auto Refresh In Play Mode", autoRefreshInPlayMode);
        heatmapSize = EditorGUILayout.Slider("Heatmap Size", heatmapSize, 0.5f, 3f);

        if (EditorGUI.EndChangeCheck())
            ScanSceneSmart();

        if (GUILayout.Button("Scan Scene", GUILayout.Height(36f)))
            ScanSceneSmart();
    }

    // ===================== SCAN =====================

    void ScanSceneSmart()
    {
        scanner.ScanScene();

        heavyLights = scanner.GetHeavyLights(heavyLightRangeThreshold);
        overlappingLights = scanner.GetOverlappingLights(overlapFactor);
        shadowedPointLights = scanner.GetShadowedPointLights(smallShadowLightRangeThreshold);
        colliderCandidates = scanner.GetSafeColliderCandidates();
        staticCandidates = scanner.GetSafeStaticCandidates();
        instancingCandidates = scanner.GetInstancingCandidates(instancingThreshold);
        expensiveCameras = scanner.GetExpensiveCameras();
        expensiveParticles = scanner.GetExpensiveParticleSystems(particleThreshold);
        realtimeReflectionProbes = scanner.GetRealtimeReflectionProbes();
        heavyTerrains = scanner.GetHeavyTerrains(1, 500);
        int enabledInstancingRendererCount = scanner.meshRenderers.Count(renderer =>
            renderer != null &&
            renderer.enabled &&
            renderer.sharedMaterials != null &&
            renderer.sharedMaterials.Any(material => material != null && material.enableInstancing));

        report = OptimizationRules.Evaluate(
            scanner,
            heavyLights,
            overlappingLights,
            shadowedPointLights,
            staticCandidates,
            colliderCandidates,
            instancingCandidates,
            expensiveCameras,
            expensiveParticles,
            realtimeReflectionProbes,
            heavyTerrains,
            scanner.Get3DAudioSources());
        currentSnapshot = OptimizationSnapshot.Capture(
            scanner,
            scanner.lights.Count(light => light != null && light.enabled),
            heavyLights.Count,
            overlappingLights.Count,
            shadowedPointLights.Count,
            scanner.meshRenderers.Count(renderer => renderer != null && renderer.enabled),
            scanner.meshRenderers.Count(renderer => renderer != null && renderer.gameObject.isStatic),
            instancingCandidates.Count,
            enabledInstancingRendererCount,
            expensiveCameras.Count,
            expensiveParticles.Count,
            realtimeReflectionProbes.Count,
            heavyTerrains.Count,
            scanner.rigidbodies.Count,
            scanner.approximateTriangleCount);

        if (baselineSnapshot.score <= 0f)
            baselineSnapshot = currentSnapshot;

        List<string> suggestionLines = new List<string>();

        if (scanner.lights.Count > 10)
            suggestionLines.Add("Too many realtime lights: reduce, bake, or lower their range.");

        if (overlappingLights.Count > 0)
            suggestionLines.Add("Overlapping lights should usually be range-limited before removal.");

        if (shadowedPointLights.Count > 0)
            suggestionLines.Add("Disable shadows on small point lights first for a safer GPU gain.");

        if (staticCandidates.Count > 0)
            suggestionLines.Add("Static batching candidates are available for non-moving, script-free objects.");

        if (instancingCandidates.Count > 0)
            suggestionLines.Add("Repeated mesh/material combinations can benefit from GPU instancing.");

        if (expensiveParticles.Count > 0)
            suggestionLines.Add("Heavy particle systems often benefit from lower max particle counts and local simulation.");

        if (realtimeReflectionProbes.Count > 0)
            suggestionLines.Add("Realtime reflection probes are expensive; baked probes are usually better.");

        suggestionLines.Add("Review occlusion culling, LOD groups, light baking, texture compression, and shadow distance per target platform.");
        suggestions = string.Join("\n", suggestionLines.Select(line => "- " + line));
    }

    // ===================== UI =====================

    void DrawReport()
    {
        GUILayout.Label("Performance", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Current Score: {currentSnapshot.score:0.0}/100\nEstimated Relative FPS: {currentSnapshot.estimatedFps:0.0}\nProject Baseline Score: {baselineSnapshot.score:0.0}/100\nProject Baseline Relative FPS: {baselineSnapshot.estimatedFps:0.0}\nChange vs Project Baseline: {(currentSnapshot.score - baselineSnapshot.score):+0.0;-0.0;0.0} score, {(currentSnapshot.estimatedFps - baselineSnapshot.estimatedFps):+0.0;-0.0;0.0} relative FPS\n\nScene snapshot\nLights: {currentSnapshot.totalLights} | Overlap: {currentSnapshot.overlappingLights} | Shadowed Points: {currentSnapshot.shadowedPointLights}\nRenderers: {currentSnapshot.totalRenderers} | Static: {currentSnapshot.staticRenderers} | Instancing Enabled: {currentSnapshot.instancingEnabledRenderers}\nApprox. Triangles: {currentSnapshot.approximateTriangles}\nCameras: {currentSnapshot.expensiveCameras} | Particles: {currentSnapshot.heavyParticles} | Probes: {currentSnapshot.realtimeReflectionProbes}",
            MessageType.Info);

        var topIssues = currentSnapshot.GetTopIssues();
        if (topIssues.Length > 0)
        {
            GUILayout.Label("Performance Breakdown", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                string.Join("\n", topIssues.Take(3).Select((item, index) => $"{index + 1}. {item.label}: {item.impact:0.0}")),
                MessageType.None);
        }

        DrawProblemGroup("High Impact Problems", OptimizationReport.Severity.High, MessageType.Warning);
        DrawProblemGroup("Medium Impact Problems", OptimizationReport.Severity.Medium, MessageType.Info);
        DrawProblemGroup("Low Impact Problems", OptimizationReport.Severity.Low, MessageType.None);
    }

    void DrawProblemGroup(string title, OptimizationReport.Severity severity, MessageType messageType)
    {
        List<OptimizationReport.Entry> entries = report.entries
            .Where(entry => entry.severity == severity)
            .ToList();

        if (entries.Count == 0)
            return;

        GUILayout.Label(title, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            string.Join("\n\n", entries.Select(entry => $"{entry.warning}\n{entry.suggestion}")),
            messageType);
    }

    void DrawActions()
    {
        GUILayout.Label("Actions", EditorStyles.boldLabel);

        if (EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Play Mode keeps runtime-safe actions available. Static flags, shared materials, and quality asset edits are disabled.", MessageType.Warning);

        EditorGUILayout.HelpBox(lastOptimizationHeadline, MessageType.Info);

        if (heroButtonStyle == null)
        {
            heroButtonStyle = new GUIStyle(GUI.skin.button);
            heroButtonStyle.fontSize = 15;
            heroButtonStyle.fontStyle = FontStyle.Bold;
            heroButtonStyle.fixedHeight = 54f;
        }

        if (GUILayout.Button("Boost Performance", heroButtonStyle))
            RunBoostPerformance();

        EditorGUILayout.HelpBox(lastOptimizationSummary, MessageType.None);

        showAdvancedControls = EditorGUILayout.Foldout(showAdvancedControls, "Manual Controls (Advanced)", true);
        if (showAdvancedControls)
        {
            DrawAdvancedControls();
        }
    }

    void DrawAdvancedControls()
    {
        shadowBudget = EditorGUILayout.IntSlider("Shadow Budget", shadowBudget, 0, 8);
        strongLightsPerCluster = EditorGUILayout.IntSlider("Strong Lights Per Cluster", strongLightsPerCluster, 1, 4);
        EditorGUILayout.HelpBox("Shadow Budget = 0 disables shadows on all non-protected lights. Increase it to preserve more shadow-casting lights per scene.", MessageType.Warning);
        EditorGUILayout.HelpBox("Strong Lights Per Cluster controls how many important lights remain dominant inside each nearby light cluster.", MessageType.None);

        if (GUILayout.Button("Reset Advanced Light Settings"))
        {
            shadowBudget = 2;
            strongLightsPerCluster = 2;
        }

        if (GUILayout.Button("Optimize Lights"))
            RunOptimizeLights();

        if (GUILayout.Button("Optimize Rendering"))
            RunOptimizeRendering();

        if (GUILayout.Button("Optimize VFX And Cameras"))
            RunOptimizeVfxAndCameras();

        DrawLightActions();
        DrawStaticAndInstancingActions();
        DrawSelectionActions();
        DrawProjectOptimizations();
        DrawColliderActions();
    }

    void RunBoostPerformance()
    {
        List<string> changes = new List<string>();
        lastOptimizationHeadline = "Boost Performance is analyzing the scene and applying safe production-ready optimizations.";

        try
        {
            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Analyzing scene", 0.1f);
            ScanSceneSmart();

            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing lights", 0.35f);
            OptimizeLightsInternal(changes);

            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing rendering", 0.6f);
            OptimizeRenderingInternal(changes);

            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing VFX and cameras", 0.8f);
            OptimizeVfxAndCamerasInternal(changes);

            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Refreshing score", 1f);
            CompleteOptimization("Boost Performance", changes);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void RunOptimizeLights()
    {
        List<string> changes = new List<string>();
        lastOptimizationHeadline = "Light optimization is running.";

        try
        {
            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing lights", 0.5f);
            OptimizeLightsInternal(changes);
            CompleteOptimization("Optimize Lights", changes);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void RunOptimizeRendering()
    {
        List<string> changes = new List<string>();
        lastOptimizationHeadline = "Rendering optimization is running.";

        try
        {
            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing rendering", 0.5f);
            OptimizeRenderingInternal(changes);
            CompleteOptimization("Optimize Rendering", changes);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void RunOptimizeVfxAndCameras()
    {
        List<string> changes = new List<string>();
        lastOptimizationHeadline = "VFX and camera optimization is running.";

        try
        {
            EditorUtility.DisplayProgressBar("Unity Optimizer Pro", "Optimizing VFX and cameras", 0.5f);
            OptimizeVfxAndCamerasInternal(changes);
            CompleteOptimization("Optimize VFX And Cameras", changes);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void OptimizeLightsInternal(List<string> changes)
    {
        int clusteredLights = 0;
        int clusteredGroups = 0;
        int overlapAdjusted = 0;
        int clusterShadowsDisabled = 0;
        int budgetShadowsDisabled = 0;
        HashSet<Light> shadowProcessedLights = new HashSet<Light>();

        foreach (var cluster in scanner.GetLightClusters(Mathf.Max(overlapFactor, 0.65f), 3))
        {
            var orderedCluster = cluster
                .OrderByDescending(scanner.GetLightImportance)
                .ToList();

            for (int i = strongLightsPerCluster; i < orderedCluster.Count; i++)
            {
                Light light = orderedCluster[i];
                Undo.RecordObject(light, "Cluster Lights");
                light.intensity *= 0.7f;
                light.range *= 0.75f;
                if (light.shadows != LightShadows.None)
                {
                    light.shadows = LightShadows.None;
                    clusterShadowsDisabled++;
                    shadowProcessedLights.Add(light);
                }
                clusteredLights++;
            }

            clusteredGroups++;
        }

        if (clusteredGroups > 0)
            changes.Add($"Clustered {clusteredGroups} nearby light groups and toned down {clusteredLights} lower-impact lights.");

        foreach (var light in overlappingLights)
        {
            Undo.RecordObject(light, "Reduce Overlapping Light Range");
            light.range *= 0.8f;
            overlapAdjusted++;

            if (light.type != LightType.Directional && scanner.GetLightImportance(light) < 15f)
            {
                if (light.shadows != LightShadows.None)
                {
                    light.shadows = LightShadows.None;
                    clusterShadowsDisabled++;
                    shadowProcessedLights.Add(light);
                }
            }
        }

        if (overlapAdjusted > 0)
            changes.Add($"{overlapAdjusted} overlapping lights were optimized.");

        var shadowKeepers = new HashSet<Light>(
            scanner.lights
                .Where(light => light != null && light.enabled && light.shadows != LightShadows.None)
                .OrderByDescending(scanner.GetLightImportance)
                .Take(shadowBudget));

        foreach (var light in scanner.lights)
        {
            if (light == null || !light.enabled || light.shadows == LightShadows.None)
                continue;

            if (shadowKeepers.Contains(light))
                continue;

            Undo.RecordObject(light, "Apply Shadow Budget");
            light.shadows = LightShadows.None;
            budgetShadowsDisabled++;
            shadowProcessedLights.Add(light);
        }

        if (budgetShadowsDisabled > 0)
            changes.Add($"Applied a shadow budget and disabled shadows on {budgetShadowsDisabled} lower-impact lights.");

        foreach (var light in shadowedPointLights)
        {
            if (shadowProcessedLights.Contains(light))
                continue;

            if (shadowKeepers.Contains(light))
                continue;

            Undo.RecordObject(light, "Disable Small Point Light Shadows");
            if (light.shadows != LightShadows.None)
            {
                light.shadows = LightShadows.None;
                clusterShadowsDisabled++;
                shadowProcessedLights.Add(light);
            }
        }

        int totalLightsTouched = clusteredLights + overlapAdjusted;
        if (totalLightsTouched > 0)
            changes.Add($"{totalLightsTouched} lights were tuned for performance.");

        int totalShadowChanges = clusterShadowsDisabled + budgetShadowsDisabled;
        if (totalShadowChanges > 0)
            changes.Add($"{totalShadowChanges} low-impact shadows were disabled overall.");
    }

    void OptimizeRenderingInternal(List<string> changes)
    {
        int staticApplied = 0;
        if (!EditorApplication.isPlaying)
        {
            foreach (var obj in staticCandidates)
            {
                if (obj.isStatic)
                    continue;

                Undo.RecordObject(obj, "Apply Safe Static Flags");
                obj.isStatic = true;
                staticApplied++;
            }
        }

        if (staticApplied > 0)
            changes.Add($"{staticApplied} objects were batched as safe static content.");

        int instancingEnabled = 0;
        if (!EditorApplication.isPlaying)
        {
            foreach (var obj in instancingCandidates)
            {
                MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                if (renderer == null)
                    continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || material.enableInstancing)
                        continue;

                    Undo.RecordObject(material, "Enable GPU Instancing");
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    instancingEnabled++;
                }
            }
        }

        if (instancingEnabled > 0)
            changes.Add($"{instancingEnabled} repeated-mesh materials received GPU instancing.");

        if (!EditorApplication.isPlaying)
        {
            float oldShadowDistance = QualitySettings.shadowDistance;
            int oldPixelLightCount = QualitySettings.pixelLightCount;

            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, recommendedShadowDistance);
            QualitySettings.pixelLightCount = Mathf.Min(QualitySettings.pixelLightCount, recommendedPixelLightCount);
            QualitySettings.vSyncCount = 0;
            QualitySettings.realtimeReflectionProbes = false;

            if (!Mathf.Approximately(oldShadowDistance, QualitySettings.shadowDistance) || oldPixelLightCount != QualitySettings.pixelLightCount)
                changes.Add($"Applied safe quality settings: shadow distance {oldShadowDistance:0} -> {QualitySettings.shadowDistance:0}, pixel lights {oldPixelLightCount} -> {QualitySettings.pixelLightCount}.");
        }
    }

    void OptimizeVfxAndCamerasInternal(List<string> changes)
    {
        int particlesReduced = 0;
        int particlesLocalized = 0;
        foreach (var system in expensiveParticles)
        {
            Undo.RecordObject(system, "Optimize Particle System");
            var main = system.main;
            int oldMaxParticles = main.maxParticles;
            if (oldMaxParticles > particleThreshold / 2)
            {
                main.maxParticles = Mathf.Max(64, Mathf.RoundToInt(oldMaxParticles * 0.75f));
                if (main.maxParticles != oldMaxParticles)
                    particlesReduced++;
            }

            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                particlesLocalized++;
            }
        }

        if (particlesReduced > 0 || particlesLocalized > 0)
            changes.Add($"{Mathf.Max(particlesReduced, particlesLocalized)} particle systems were optimized.");

        int camerasOptimized = 0;
        foreach (var camera in expensiveCameras)
        {
            if (camera == null)
                continue;

            Undo.RecordObject(camera, "Optimize Camera");
            bool changed = false;

            if (camera.depth > 0f && camera.allowHDR)
            {
                camera.allowHDR = false;
                changed = true;
            }

            if (camera.depth > 0f && camera.allowMSAA)
            {
                camera.allowMSAA = false;
                changed = true;
            }

            if (camera.depth > 0f && camera.renderingPath == RenderingPath.DeferredShading)
            {
                camera.renderingPath = RenderingPath.UsePlayerSettings;
                changed = true;
            }

            if (changed)
                camerasOptimized++;
        }

        if (camerasOptimized > 0)
            changes.Add($"Optimized {camerasOptimized} secondary cameras.");
    }

    void CompleteOptimization(string actionName, List<string> changes)
    {
        RefreshAfterSceneChange();

        var before = baselineSnapshot;
        var after = currentSnapshot;
        float fpsGain = after.estimatedFps - before.estimatedFps;
        float scoreGain = after.score - before.score;

        if (changes.Count == 0)
            changes.Add("No safe changes were needed for the current scene.");

        lastOptimizationHeadline = $"{actionName} complete: {fpsGain:+0.0;-0.0;0.0} relative FPS, {scoreGain:+0.0;-0.0;0.0} score.";

        lastOptimizationSummary = $"{actionName} finished.\nScore: {before.score:0.0} -> {after.score:0.0} ({scoreGain:+0.0;-0.0;0.0})\nEstimated Relative FPS: {before.estimatedFps:0.0} -> {after.estimatedFps:0.0} ({fpsGain:+0.0;-0.0;0.0})\n\n" +
                                  string.Join("\n", changes.Select(change => "- " + change));

        Debug.Log(
            $"[Unity Optimizer Pro] {actionName}\n" +
            before.ToConsoleString("Before") + "\n" +
            after.ToConsoleString("After") + "\n" +
            OptimizationSnapshot.CompareToConsoleString(before, after) + "\n" +
            string.Join("\n", changes.Select(change => "- " + change)));
    }

    void DrawLightActions()
    {
        if (overlappingLights.Count > 0 && GUILayout.Button("Reduce Overlapping Light Range"))
        {
            foreach (var light in overlappingLights)
            {
                Undo.RecordObject(light, "Reduce Overlapping Light Range");
                light.range *= 0.8f;

                if (light.type == LightType.Point && light.range <= 8f)
                    light.shadows = LightShadows.None;
            }

            RefreshAfterSceneChange();
        }

        if (heavyLights.Count > 0 && GUILayout.Button("Reduce Heavy Light Cost"))
        {
            foreach (var light in heavyLights)
            {
                Undo.RecordObject(light, "Reduce Heavy Light Cost");
                light.range *= 0.9f;

                if (light.type == LightType.Point && light.range <= 10f)
                    light.shadows = LightShadows.None;
            }

            RefreshAfterSceneChange();
        }

        if (shadowedPointLights.Count > 0 && GUILayout.Button("Disable Shadows On Small Point Lights"))
        {
            foreach (var light in shadowedPointLights)
            {
                Undo.RecordObject(light, "Disable Small Point Light Shadows");
                light.shadows = LightShadows.None;
            }

            RefreshAfterSceneChange();
        }
    }

    void DrawStaticAndInstancingActions()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (staticCandidates.Count > 0 && GUILayout.Button("Select Safe Static Candidates"))
                Selection.objects = staticCandidates.ToArray();

            if (staticCandidates.Count > 0 && GUILayout.Button("Apply Safe Static Flags"))
            {
                foreach (var obj in staticCandidates)
                {
                    Undo.RecordObject(obj, "Apply Safe Static Flags");
                    obj.isStatic = true;
                }

                RefreshAfterSceneChange();
            }

            if (instancingCandidates.Count > 0 && GUILayout.Button("Enable GPU Instancing On Compatible Materials"))
            {
                foreach (var obj in instancingCandidates)
                {
                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        continue;

                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null || material.enableInstancing)
                            continue;

                        Undo.RecordObject(material, "Enable GPU Instancing");
                        material.enableInstancing = true;
                        EditorUtility.SetDirty(material);
                    }
                }

                RefreshAfterSceneChange();
            }
        }
    }

    void DrawColliderActions()
    {
        if (colliderCandidates.Count > 0 && GUILayout.Button("Select Collider Candidates"))
            Selection.objects = colliderCandidates.Select(collider => collider.gameObject).ToArray();

        if (colliderCandidates.Count > 0 && GUILayout.Button("Disable Selected Candidate Colliders"))
        {
            foreach (var obj in Selection.gameObjects)
            {
                Collider collider = obj.GetComponent<Collider>();
                if (collider == null || !colliderCandidates.Contains(collider))
                    continue;

                Undo.RecordObject(collider, "Disable Candidate Collider");
                collider.enabled = false;
            }

            RefreshAfterSceneChange();
        }
    }

    void DrawSelectionActions()
    {
        if (GUILayout.Button("Select All Reported Objects"))
            Selection.objects = report.problemObjects.Where(obj => obj != null).Distinct().ToArray();

        if (GUILayout.Button("Disable Selected Lights"))
        {
            foreach (var obj in Selection.gameObjects)
            {
                Light light = obj.GetComponent<Light>();
                if (light == null)
                    continue;

                Undo.RecordObject(light, "Disable Selected Light");
                light.enabled = false;
            }

            RefreshAfterSceneChange();
        }

        if (expensiveParticles.Count > 0 && GUILayout.Button("Select Heavy Particle Systems"))
            Selection.objects = expensiveParticles.Select(system => system.gameObject).ToArray();

        if (expensiveCameras.Count > 0 && GUILayout.Button("Select Expensive Cameras"))
            Selection.objects = expensiveCameras.Select(camera => camera.gameObject).ToArray();

        if (realtimeReflectionProbes.Count > 0 && GUILayout.Button("Select Realtime Reflection Probes"))
            Selection.objects = realtimeReflectionProbes.Select(probe => probe.gameObject).ToArray();

        if (heavyTerrains.Count > 0 && GUILayout.Button("Select Heavy Terrains"))
            Selection.objects = heavyTerrains.Select(terrain => terrain.gameObject).ToArray();
    }

    void DrawProjectOptimizations()
    {
        GUILayout.Label("Project Quality Optimizations", EditorStyles.boldLabel);

        recommendedShadowDistance = EditorGUILayout.Slider("Recommended Shadow Distance", recommendedShadowDistance, 15f, 150f);
        recommendedPixelLightCount = EditorGUILayout.IntSlider("Recommended Pixel Light Count", recommendedPixelLightCount, 0, 8);

        EditorGUILayout.HelpBox(
            $"Current settings\nShadow Distance: {QualitySettings.shadowDistance}\nPixel Light Count: {QualitySettings.pixelLightCount}\nLOD Bias: {QualitySettings.lodBias}\nVSync Count: {QualitySettings.vSyncCount}\nRealtime Reflection Probes: {QualitySettings.realtimeReflectionProbes}",
            MessageType.None);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Apply Safe Quality Preset"))
            {
                QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, recommendedShadowDistance);
                QualitySettings.pixelLightCount = Mathf.Min(QualitySettings.pixelLightCount, recommendedPixelLightCount);
                QualitySettings.vSyncCount = 0;
                QualitySettings.realtimeReflectionProbes = false;
                Repaint();
            }

            if (GUILayout.Button("Disable VSync"))
            {
                QualitySettings.vSyncCount = 0;
                Repaint();
            }

            if (GUILayout.Button("Clamp Shadow Distance"))
            {
                QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, recommendedShadowDistance);
                Repaint();
            }

            if (GUILayout.Button("Clamp Pixel Light Count"))
            {
                QualitySettings.pixelLightCount = Mathf.Min(QualitySettings.pixelLightCount, recommendedPixelLightCount);
                Repaint();
            }
        }
    }

    void ApplyPlatformPreset(float shadowDistance, int pixelLightCount, float lodBias, bool enableRealtimeReflectionProbes)
    {
        QualitySettings.shadowDistance = shadowDistance;
        QualitySettings.pixelLightCount = pixelLightCount;
        QualitySettings.lodBias = lodBias;
        QualitySettings.vSyncCount = 0;
        QualitySettings.realtimeReflectionProbes = enableRealtimeReflectionProbes;
        lastOptimizationHeadline = "Platform preset applied.";
        lastOptimizationSummary = $"Applied platform preset.\nShadow Distance: {shadowDistance:0}\nPixel Lights: {pixelLightCount}\nLOD Bias: {lodBias:0.0}\nRealtime Reflection Probes: {enableRealtimeReflectionProbes}";
        RefreshAfterSceneChange();
    }

    void DrawSuggestions()
    {
        GUILayout.Label("Suggestions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            suggestions + "\n\nScoring model\n- Lights, overlaps, shadows, cameras, particles, probes, terrains, and excess rigidbodies reduce score.\n- Static renderers and instancing-enabled renderers raise score.\n- Estimated Relative FPS is a weighted estimate for before/after comparison, not a profiler reading from target hardware.",
            MessageType.Info);
    }

    void DrawControls()
    {
        GUILayout.Label("Controls", EditorStyles.boldLabel);

        if (GUILayout.Button("Set Current State As Baseline"))
        {
            baselineSnapshot = currentSnapshot;
            lastOptimizationHeadline = "Baseline updated.";
            lastOptimizationSummary = "Baseline updated to the current scene state.";
        }

        if (GUILayout.Button("Undo Last Action"))
        {
            EditorApplication.delayCall += () =>
            {
                Undo.PerformUndo();
                ScanSceneSmart();
                Repaint();
            };
        }

        if (GUILayout.Button("Refresh Scan"))
            ScanSceneSmart();
    }

    void RefreshAfterSceneChange()
    {
        if (!EditorApplication.isPlaying)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        ScanSceneSmart();
        Repaint();
    }
}
