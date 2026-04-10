using System;
using System.Linq;
using UnityEngine;

public class OptimizationSnapshot
{
    public struct BreakdownItem
    {
        public string label;
        public float impact;

        public BreakdownItem(string label, float impact)
        {
            this.label = label;
            this.impact = impact;
        }
    }

    public int totalLights;
    public int heavyLights;
    public int overlappingLights;
    public int shadowedPointLights;
    public int staticRenderers;
    public int totalRenderers;
    public int instancingReadyRenderers;
    public int instancingEnabledRenderers;
    public int expensiveCameras;
    public int heavyParticles;
    public int realtimeReflectionProbes;
    public int heavyTerrains;
    public int rigidbodies;
    public int approximateTriangles;
    public float score;
    public float estimatedFps;
    public float lightBurden;
    public float renderBurden;
    public float physicsBurden;
    public float geometryBurden;
    public float optimizationBonus;

    public static OptimizationSnapshot Capture(
        SceneScanner scanner,
        int totalLights,
        int heavyLights,
        int overlappingLights,
        int shadowedPointLights,
        int totalRenderers,
        int staticRenderers,
        int instancingReadyRenderers,
        int instancingEnabledRenderers,
        int expensiveCameras,
        int heavyParticles,
        int realtimeReflectionProbes,
        int heavyTerrains,
        int rigidbodies,
        int approximateTriangles)
    {
        var snapshot = new OptimizationSnapshot();
        snapshot.totalLights = totalLights;
        snapshot.heavyLights = heavyLights;
        snapshot.overlappingLights = overlappingLights;
        snapshot.shadowedPointLights = shadowedPointLights;
        snapshot.totalRenderers = totalRenderers;
        snapshot.staticRenderers = staticRenderers;
        snapshot.instancingReadyRenderers = instancingReadyRenderers;
        snapshot.instancingEnabledRenderers = instancingEnabledRenderers;
        snapshot.expensiveCameras = expensiveCameras;
        snapshot.heavyParticles = heavyParticles;
        snapshot.realtimeReflectionProbes = realtimeReflectionProbes;
        snapshot.heavyTerrains = heavyTerrains;
        snapshot.rigidbodies = rigidbodies;
        snapshot.approximateTriangles = approximateTriangles;

        float sceneScale =
            26f +
            snapshot.totalRenderers * 0.4f +
            snapshot.approximateTriangles / 8500f +
            snapshot.totalLights * 1.15f +
            snapshot.heavyParticles * 0.7f +
            snapshot.rigidbodies * 0.18f;

        snapshot.lightBurden =
            snapshot.totalLights * 1.35f +
            snapshot.heavyLights * 2.1f +
            snapshot.overlappingLights * 3.2f +
            snapshot.shadowedPointLights * 1.7f;

        snapshot.renderBurden =
            snapshot.expensiveCameras * 4.5f +
            snapshot.heavyParticles * 1.6f +
            snapshot.realtimeReflectionProbes * 4.2f +
            snapshot.heavyTerrains * 3.1f;

        snapshot.physicsBurden = Mathf.Max(0, snapshot.rigidbodies - 10) * 0.45f;
        snapshot.geometryBurden = snapshot.totalRenderers * 0.22f + snapshot.approximateTriangles / 12000f;

        snapshot.optimizationBonus =
            Mathf.Min(snapshot.staticRenderers, snapshot.totalRenderers) * 0.08f +
            snapshot.instancingEnabledRenderers * 0.2f;

        float normalizedBurden = (snapshot.lightBurden + snapshot.renderBurden + snapshot.physicsBurden + snapshot.geometryBurden - snapshot.optimizationBonus) / Mathf.Max(15f, sceneScale);
        snapshot.score = Mathf.Clamp(94f - normalizedBurden * 26f, 10f, 98f);

        float baseFps = 165f - normalizedBurden * 30f;
        float sceneComplexityDrag =
            snapshot.approximateTriangles / 70000f +
            snapshot.totalRenderers * 0.18f +
            snapshot.expensiveCameras * 3f +
            snapshot.heavyParticles * 0.45f +
            snapshot.realtimeReflectionProbes * 2f;
        float optimizationLift = snapshot.optimizationBonus * 0.35f;
        snapshot.estimatedFps = Mathf.Clamp(baseFps - sceneComplexityDrag + optimizationLift, 20f, 240f);
        return snapshot;
    }

    public string ToConsoleString(string label)
    {
        return string.Format(
            "{0}\nScore: {1:0.0}\nEstimated Relative FPS: {2:0.0}\nLights: {3} | Heavy: {4} | Overlap: {5} | Shadowed Points: {6}\nRenderers: {7} | Static: {8} | Instancing Enabled: {9}\nTriangles: {10}\nExpensive Cameras: {11} | Heavy Particles: {12} | Reflection Probes: {13} | Heavy Terrains: {14} | Rigidbodies: {15}",
            label,
            score,
            estimatedFps,
            totalLights,
            heavyLights,
            overlappingLights,
            shadowedPointLights,
            totalRenderers,
            staticRenderers,
            instancingEnabledRenderers,
            approximateTriangles,
            expensiveCameras,
            heavyParticles,
            realtimeReflectionProbes,
            heavyTerrains,
            rigidbodies);
    }

    public static string CompareToConsoleString(OptimizationSnapshot before, OptimizationSnapshot after)
    {
        return string.Format(
            "Performance change\nScore: {0:0.0} -> {1:0.0} ({2:+0.0;-0.0;0.0})\nEstimated Relative FPS: {3:0.0} -> {4:0.0} ({5:+0.0;-0.0;0.0})",
            before.score,
            after.score,
            after.score - before.score,
            before.estimatedFps,
            after.estimatedFps,
            after.estimatedFps - before.estimatedFps);
    }

    public BreakdownItem[] GetTopIssues()
    {
        return new[]
        {
            new BreakdownItem("Lighting pressure", lightBurden),
            new BreakdownItem("Rendering and VFX pressure", renderBurden),
            new BreakdownItem("Geometry pressure", geometryBurden),
            new BreakdownItem("Physics pressure", physicsBurden)
        }
        .OrderByDescending(item => item.impact)
        .ToArray();
    }
}
