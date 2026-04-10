using UnityEngine;
using System.Linq;

public static class OptimizationRules
{
    public static OptimizationReport Evaluate(
        SceneScanner scanner,
        System.Collections.Generic.IReadOnlyList<Light> heavyLights,
        System.Collections.Generic.IReadOnlyList<Light> overlappingLights,
        System.Collections.Generic.IReadOnlyList<Light> shadowedPointLights,
        System.Collections.Generic.IReadOnlyList<GameObject> staticCandidates,
        System.Collections.Generic.IReadOnlyList<Collider> colliderCandidates,
        System.Collections.Generic.IReadOnlyList<GameObject> instancingCandidates,
        System.Collections.Generic.IReadOnlyList<Camera> expensiveCameras,
        System.Collections.Generic.IReadOnlyList<ParticleSystem> expensiveParticles,
        System.Collections.Generic.IReadOnlyList<ReflectionProbe> realtimeReflectionProbes,
        System.Collections.Generic.IReadOnlyList<Terrain> heavyTerrains,
        System.Collections.Generic.IReadOnlyList<AudioSource> playOnAwake3DAudio)
    {
        OptimizationReport report = new OptimizationReport();

        if (scanner.lights.Count > 10)
        {
            report.Add(
                $"High light count: {scanner.lights.Count} (recommended <= 10)",
                "Too many lights increase GPU cost. Reduce or optimize overlapping lights.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(scanner.lights.Select(light => light.gameObject));
        }

        if (overlappingLights.Count > 0)
        {
            report.Add(
                "Overlapping lights detected",
                "Lights overlapping waste performance. Reduce range or remove duplicates.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(overlappingLights.Select(light => light.gameObject));
        }

        if (shadowedPointLights.Count > 0)
        {
            report.Add(
                "Expensive shadow-casting point lights",
                "Disable shadows on smaller point lights first because the visual change is usually minimal.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(shadowedPointLights.Select(light => light.gameObject));
        }

        if (heavyLights.Count > 0)
        {
            report.Add(
                $"Heavy lights detected: {heavyLights.Count}",
                "Reduce light range and shadow usage on non-critical lights to lower GPU cost.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(heavyLights.Select(light => light.gameObject));
        }

        if (scanner.meshRenderers.Count > 300)
        {
            report.Add(
                $"High mesh count: {scanner.meshRenderers.Count}",
                "Use batching, static flags, or mesh combination where safe.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(scanner.meshRenderers.Select(renderer => renderer.gameObject));
        }

        if (instancingCandidates.Count > 0)
        {
            report.Add(
                "Repeated meshes detected",
                "Enable GPU instancing on compatible materials used by repeated meshes.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(instancingCandidates);
        }

        if (staticCandidates.Count > 0)
        {
            report.Add(
                $"Static batching candidates: {staticCandidates.Count}",
                "Marking clearly non-moving renderers as static can reduce rendering overhead.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(staticCandidates);
        }

        if (colliderCandidates.Count > 0)
        {
            report.Add(
                $"Review collider candidates: {colliderCandidates.Count}",
                "Disable unnecessary scene colliders on decorative objects after manual review.",
                OptimizationReport.Severity.Low
            );

            report.AddObjects(colliderCandidates.Select(collider => collider.gameObject));
        }

        if (scanner.rigidbodies.Count > 50)
        {
            report.Add(
                $"High rigidbody count: {scanner.rigidbodies.Count}",
                "Too many physics objects can hurt performance.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(scanner.rigidbodies.Select(rigidbody => rigidbody.gameObject));
        }

        if (expensiveCameras.Count > 0)
        {
            report.Add(
                $"Expensive cameras detected: {expensiveCameras.Count}",
                "Review HDR, MSAA, and deferred rendering on secondary cameras.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(expensiveCameras.Select(camera => camera.gameObject));
        }

        if (expensiveParticles.Count > 0)
        {
            report.Add(
                $"Heavy particle systems: {expensiveParticles.Count}",
                "Reduce max particles, prefer local simulation, and review overdraw-heavy effects.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(expensiveParticles.Select(system => system.gameObject));
        }

        if (realtimeReflectionProbes.Count > 0)
        {
            report.Add(
                $"Realtime reflection probes: {realtimeReflectionProbes.Count}",
                "Baked probes are usually cheaper unless reflections must update continuously.",
                OptimizationReport.Severity.High
            );

            report.AddObjects(realtimeReflectionProbes.Select(probe => probe.gameObject));
        }

        if (heavyTerrains.Count > 0)
        {
            report.Add(
                $"Heavy terrains detected: {heavyTerrains.Count}",
                "Review terrain detail density, tree count, and terrain shadow usage.",
                OptimizationReport.Severity.Medium
            );

            report.AddObjects(heavyTerrains.Select(terrain => terrain.gameObject));
        }

        if (playOnAwake3DAudio.Count > 10)
        {
            report.Add(
                $"3D audio play-on-awake sources: {playOnAwake3DAudio.Count}",
                "Too many active 3D sources can add CPU and voice-management cost.",
                OptimizationReport.Severity.Low
            );

            report.AddObjects(playOnAwake3DAudio.Select(source => source.gameObject));
        }

        return report;
    }
}
