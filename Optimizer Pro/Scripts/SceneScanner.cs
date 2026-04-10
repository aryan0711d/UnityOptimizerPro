using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;

public class SceneScanner
{
    public List<Light> lights = new List<Light>();
    public List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
    public List<Rigidbody> rigidbodies = new List<Rigidbody>();
    public List<Camera> cameras = new List<Camera>();
    public List<ParticleSystem> particleSystems = new List<ParticleSystem>();
    public List<ReflectionProbe> reflectionProbes = new List<ReflectionProbe>();
    public List<Terrain> terrains = new List<Terrain>();
    public List<AudioSource> audioSources = new List<AudioSource>();

    public Dictionary<Mesh, int> meshUsage = new Dictionary<Mesh, int>();
    public int approximateTriangleCount;

    public void ScanScene()
    {
        lights = new List<Light>(Object.FindObjectsOfType<Light>());
        meshRenderers = new List<MeshRenderer>(Object.FindObjectsOfType<MeshRenderer>());
        rigidbodies = new List<Rigidbody>(Object.FindObjectsOfType<Rigidbody>());
        cameras = new List<Camera>(Object.FindObjectsOfType<Camera>());
        particleSystems = new List<ParticleSystem>(Object.FindObjectsOfType<ParticleSystem>());
        reflectionProbes = new List<ReflectionProbe>(Object.FindObjectsOfType<ReflectionProbe>());
        terrains = new List<Terrain>(Object.FindObjectsOfType<Terrain>());
        audioSources = new List<AudioSource>(Object.FindObjectsOfType<AudioSource>());

        meshUsage = new Dictionary<Mesh, int>();
        approximateTriangleCount = 0;

        foreach (var renderer in meshRenderers)
        {
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh != null)
            {
                if (!meshUsage.ContainsKey(mf.sharedMesh))
                    meshUsage[mf.sharedMesh] = 0;

                meshUsage[mf.sharedMesh]++;
                for (int subMeshIndex = 0; subMeshIndex < mf.sharedMesh.subMeshCount; subMeshIndex++)
                    approximateTriangleCount += (int)(mf.sharedMesh.GetIndexCount(subMeshIndex) / 3);
            }
        }
    }

    public List<Light> GetHeavyLights(float largeRangeThreshold)
    {
        return lights
            .Where(light => light != null && light.enabled)
            .Where(light => light.shadows != LightShadows.None || light.range > largeRangeThreshold)
            .Distinct()
            .ToList();
    }

    public List<Light> GetOverlappingLights(float overlapFactor)
    {
        HashSet<Light> overlaps = new HashSet<Light>();

        for (int i = 0; i < lights.Count; i++)
        {
            var first = lights[i];
            if (first == null || !first.enabled)
                continue;

            for (int j = i + 1; j < lights.Count; j++)
            {
                var second = lights[j];
                if (second == null || !second.enabled)
                    continue;

                float dist = Vector3.Distance(first.transform.position, second.transform.position);
                float overlapDistance = Mathf.Min(first.range, second.range) * overlapFactor;

                if (dist < overlapDistance)
                {
                    overlaps.Add(first);
                    overlaps.Add(second);
                }
            }
        }

        return overlaps.ToList();
    }

    public List<GameObject> GetSafeStaticCandidates()
    {
        return meshRenderers
            .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
            .Select(renderer => renderer.gameObject)
            .Where(IsSafeStaticCandidate)
            .Distinct()
            .ToList();
    }

    public List<Collider> GetSafeColliderCandidates()
    {
        return meshRenderers
            .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
            .Select(renderer => renderer.GetComponent<Collider>())
            .Where(collider => collider != null && collider.enabled)
            .Where(IsSafeColliderCandidate)
            .Distinct()
            .ToList();
    }

    public List<GameObject> GetInstancingCandidates(int minimumInstances)
    {
        HashSet<GameObject> candidates = new HashSet<GameObject>();

        foreach (var pair in meshUsage)
        {
            if (pair.Key == null || pair.Value < minimumInstances)
                continue;

            foreach (var renderer in meshRenderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh != pair.Key)
                    continue;
                if (renderer.sharedMaterials.Any(material =>
                    material != null &&
                    material.shader != null &&
                    !material.enableInstancing))
                    candidates.Add(renderer.gameObject);
            }
        }

        return candidates.ToList();
    }

    public List<Light> GetShadowedPointLights(float smallLightThreshold)
    {
        return lights
            .Where(light => light != null && light.enabled)
            .Where(light => light.type == LightType.Point && light.shadows != LightShadows.None && light.range <= smallLightThreshold)
            .Distinct()
            .ToList();
    }

    public List<List<Light>> GetLightClusters(float overlapFactor, int minimumClusterSize)
    {
        List<List<Light>> clusters = new List<List<Light>>();
        List<Light> activeLights = lights
            .Where(light => light != null && light.enabled)
            .Where(light => light.type == LightType.Point || light.type == LightType.Spot)
            .ToList();

        HashSet<Light> visited = new HashSet<Light>();

        foreach (var light in activeLights)
        {
            if (visited.Contains(light))
                continue;

            List<Light> cluster = new List<Light>();
            Queue<Light> queue = new Queue<Light>();
            queue.Enqueue(light);
            visited.Add(light);

            while (queue.Count > 0)
            {
                Light current = queue.Dequeue();
                cluster.Add(current);

                foreach (var candidate in activeLights)
                {
                    if (visited.Contains(candidate))
                        continue;

                    float distance = Vector3.Distance(current.transform.position, candidate.transform.position);
                    float clusterDistance = Mathf.Min(current.range, candidate.range) * overlapFactor;

                    if (distance <= clusterDistance)
                    {
                        visited.Add(candidate);
                        queue.Enqueue(candidate);
                    }
                }
            }

            if (cluster.Count >= minimumClusterSize)
                clusters.Add(cluster);
        }

        return clusters;
    }

    public List<Camera> GetExpensiveCameras()
    {
        return cameras
            .Where(camera => camera != null && camera.enabled)
            .Where(camera => camera.depth > 0f)
            .Where(camera => camera.allowHDR || camera.allowMSAA || camera.renderingPath == RenderingPath.DeferredShading)
            .Distinct()
            .ToList();
    }

    public List<ParticleSystem> GetExpensiveParticleSystems(int particleCountThreshold)
    {
        return particleSystems
            .Where(system => system != null && system.gameObject.activeInHierarchy)
            .Where(system =>
            {
                var main = system.main;
                return main.maxParticles >= particleCountThreshold || main.simulationSpace == ParticleSystemSimulationSpace.World;
            })
            .Distinct()
            .ToList();
    }

    public List<ReflectionProbe> GetRealtimeReflectionProbes()
    {
        return reflectionProbes
            .Where(probe => probe != null && probe.enabled)
            .Where(probe => probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime)
            .Distinct()
            .ToList();
    }

    public List<Terrain> GetHeavyTerrains(int detailDensityThreshold, int treeCountThreshold)
    {
        return terrains
            .Where(terrain => terrain != null && terrain.terrainData != null)
            .Where(terrain => terrain.detailObjectDensity > detailDensityThreshold || terrain.terrainData.treeInstanceCount > treeCountThreshold)
            .Distinct()
            .ToList();
    }

    public List<AudioSource> Get3DAudioSources()
    {
        return audioSources
            .Where(source => source != null && source.enabled)
            .Where(source => source.spatialBlend > 0.5f && source.playOnAwake)
            .Distinct()
            .ToList();
    }

    bool IsSafeStaticCandidate(GameObject obj)
    {
        if (obj == null || obj.isStatic)
            return false;

        if (obj.GetComponent<Rigidbody>() != null ||
            obj.GetComponent<Animator>() != null ||
            obj.GetComponent<NavMeshAgent>() != null ||
            obj.GetComponent<ParticleSystem>() != null ||
            obj.GetComponent<SkinnedMeshRenderer>() != null)
        {
            return false;
        }

        if (obj.GetComponentInParent<LODGroup>() != null)
            return false;

        return !HasUserMonoBehaviour(obj);
    }

    bool IsSafeColliderCandidate(Collider collider)
    {
        GameObject obj = collider.gameObject;

        if (collider.isTrigger)
            return false;

        if (obj.GetComponent<Rigidbody>() != null || obj.GetComponentInParent<Rigidbody>() != null)
            return false;

        if (obj.GetComponent<NavMeshObstacle>() != null)
            return false;

        return !HasUserMonoBehaviour(obj);
    }

    public float GetLightImportance(Light light)
    {
        if (light == null)
            return 0f;

        float typeBonus = light.type == LightType.Directional ? 20f : 0f;
        float shadowBonus = light.shadows != LightShadows.None ? 10f : 0f;
        float cameraDistanceWeight = 0f;

        if (cameras != null && cameras.Count > 0)
        {
            float nearestCameraDistance = cameras
                .Where(camera => camera != null && camera.enabled)
                .Select(camera => Vector3.Distance(camera.transform.position, light.transform.position))
                .DefaultIfEmpty(50f)
                .Min();

            cameraDistanceWeight = Mathf.Clamp(25f - nearestCameraDistance, 0f, 25f) * 0.4f;
        }

        return light.intensity * 10f + light.range + typeBonus + shadowBonus + cameraDistanceWeight;
    }

    bool HasUserMonoBehaviour(GameObject obj)
    {
        // Conservative by design: any user-authored MonoBehaviour can imply runtime movement or logic.
        return obj.GetComponents<MonoBehaviour>()
            .Any(component => component != null && IsUserAuthoredMonoBehaviour(component));
    }

    bool IsUserAuthoredMonoBehaviour(MonoBehaviour component)
    {
        var type = component.GetType();
        var assemblyName = type.Assembly.GetName().Name;

        return assemblyName != null &&
               !assemblyName.StartsWith("Unity", System.StringComparison.Ordinal) &&
               !assemblyName.StartsWith("mscorlib", System.StringComparison.Ordinal) &&
               !assemblyName.StartsWith("System", System.StringComparison.Ordinal);
    }
}
