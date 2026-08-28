// Runs the real bake with different settings on the selected NavMeshSurface and
// reports how much NavMesh each combination actually produces.
// Nothing is written to disk and the surface's own baked data is never touched.
// Menu: Tools > NavMesh > Run Bake Experiments (select a NavMeshSurface first)
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class NavMeshBakeExperiment
{
    [MenuItem("Tools/NavMesh/Run Bake Experiments")]
    static void Run()
    {
        var surface = Selection.activeGameObject
            ? Selection.activeGameObject.GetComponent<NavMeshSurface>()
            : null;

        if (surface == null)
        {
            Debug.LogError("Select the GameObject holding the NavMeshSurface (e.g. NavStatic), then run again.");
            return;
        }

        var collect = typeof(NavMeshSurface).GetMethod(
            "CollectSources", BindingFlags.NonPublic | BindingFlags.Instance);
        var sources = (List<NavMeshBuildSource>)collect.Invoke(surface, null);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Bake experiments on '{surface.name}' — {sources.Count} source(s) ===");
        sb.AppendLine($"surface pos={surface.transform.position} rot={surface.transform.rotation.eulerAngles}");

        var baseSettings = surface.GetBuildSettings();
        sb.AppendLine($"agent: radius={baseSettings.agentRadius} height={baseSettings.agentHeight} " +
                      $"slope={baseSettings.agentSlope} climb={baseSettings.agentClimb}");
        sb.AppendLine();

        // ---- experiment matrix -------------------------------------------------
        Try(sb, surface, sources, "as-is (current inspector settings)", null, -1, -1, false);
        Try(sb, surface, sources, "minRegionArea = 1", null, -1, 1, false);
        Try(sb, surface, sources, "voxel 0.03", null, 0.03f, -1, false);
        Try(sb, surface, sources, "voxel 0.03 + minRegionArea 1", null, 0.03f, 1, false);
        Try(sb, surface, sources, "voxel 0.03 + minRegion 1 + surface at origin", null, 0.03f, 1, true);

        for (int i = 0; i < sources.Count; i++)
        {
            var single = new List<NavMeshBuildSource> { sources[i] };
            var m = sources[i].sourceObject as Mesh;
            Try(sb, surface, single, $"ONLY source[{i}] ({(m ? m.name : "?")}, {(m ? m.triangles.Length / 3 : 0)} tris)",
                single, 0.03f, 1, false);
        }

        Debug.Log(sb.ToString());
    }

    static void Try(StringBuilder sb, NavMeshSurface s, List<NavMeshBuildSource> allSources,
                    string label, List<NavMeshBuildSource> overrideSources,
                    float voxel, float minRegion, bool atOrigin)
    {
        var sources = overrideSources ?? allSources;

        var settings = s.GetBuildSettings();
        if (voxel > 0) { settings.overrideVoxelSize = true; settings.voxelSize = voxel; }
        if (minRegion >= 0) settings.minRegionArea = minRegion;

        var pos = atOrigin ? Vector3.zero : s.transform.position;
        var rot = atOrigin ? Quaternion.identity : s.transform.rotation;
        var bounds = CalcBounds(pos, rot, sources);

        sb.AppendLine($"--- {label}");
        sb.AppendLine($"    voxel={(settings.overrideVoxelSize ? settings.voxelSize.ToString() : "auto")} " +
                      $"tile={(settings.overrideTileSize ? settings.tileSize.ToString() : "auto(256)")} " +
                      $"minRegionArea={settings.minRegionArea}");
        sb.AppendLine($"    bounds(local) center={bounds.center} size={bounds.size}");

        foreach (var problem in settings.ValidationReport(bounds))
            sb.AppendLine($"    !! VALIDATION: {problem}");

        NavMeshData data = null;
        try { data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, pos, rot); }
        catch (Exception e) { sb.AppendLine($"    EXCEPTION: {e.Message}"); return; }

        if (data == null) { sb.AppendLine("    RESULT: BuildNavMeshData returned null"); return; }

        var before = NavMesh.CalculateTriangulation();
        var inst = NavMesh.AddNavMeshData(data);
        var after = NavMesh.CalculateTriangulation();
        int addedTris = after.indices.Length / 3 - before.indices.Length / 3;
        double area = 0;
        for (int i = before.indices.Length; i + 2 < after.indices.Length; i += 3)
        {
            var a = after.vertices[after.indices[i]];
            var b = after.vertices[after.indices[i + 1]];
            var c = after.vertices[after.indices[i + 2]];
            area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        if (inst.valid) inst.Remove();
        UnityEngine.Object.DestroyImmediate(data);

        sb.AppendLine($"    RESULT: navmesh polys={addedTris}   walkable area={area:F1} m²" +
                      (addedTris == 0 ? "   <<< EMPTY" : ""));
    }

    static Bounds CalcBounds(Vector3 pos, Quaternion rot, List<NavMeshBuildSource> sources)
    {
        var worldToLocal = Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
        var result = new Bounds();
        foreach (var src in sources)
        {
            if (src.shape == NavMeshBuildSourceShape.Mesh && src.sourceObject is Mesh m)
                result.Encapsulate(WorldBounds(worldToLocal * src.transform, m.bounds));
        }
        result.Expand(0.1f);
        return result;
    }

    static Bounds WorldBounds(Matrix4x4 mat, Bounds b)
    {
        var ax = Abs(mat.MultiplyVector(Vector3.right));
        var ay = Abs(mat.MultiplyVector(Vector3.up));
        var az = Abs(mat.MultiplyVector(Vector3.forward));
        return new Bounds(mat.MultiplyPoint(b.center),
                          ax * b.size.x + ay * b.size.y + az * b.size.z);
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
