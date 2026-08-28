// Diagnostic helper: reports exactly what each NavMeshSurface in the open scene
// collects when you press Bake. Menu: Tools > NavMesh > Diagnose Bake Sources
// Safe to delete once the bake issue is resolved.
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class NavMeshBakeDiagnostics
{
    [MenuItem("Tools/NavMesh/Diagnose Bake Sources")]
    static void Diagnose()
    {
        var surfaces = Object.FindObjectsByType<NavMeshSurface>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (surfaces.Length == 0)
        {
            Debug.LogWarning("No NavMeshSurface found in the open scene.");
            return;
        }

        var collect = typeof(NavMeshSurface).GetMethod(
            "CollectSources", BindingFlags.NonPublic | BindingFlags.Instance);

        var sb = new StringBuilder();
        sb.AppendLine($"=== NavMesh bake diagnostics — {surfaces.Length} surface(s) ===");

        foreach (var s in surfaces)
        {
            sb.AppendLine();
            sb.AppendLine($"### {Path(s.transform)}");
            sb.AppendLine($"    GameObject active : {s.gameObject.activeInHierarchy}");
            sb.AppendLine($"    Component enabled : {s.enabled}");
            sb.AppendLine($"    Agent Type        : {s.agentTypeID} " +
                          $"('{NavMesh.GetSettingsNameFromID(s.agentTypeID)}')");
            sb.AppendLine($"    Collect Objects   : {s.collectObjects}");
            sb.AppendLine($"    Use Geometry      : {s.useGeometry}");
            sb.AppendLine($"    Layer Mask        : {s.layerMask.value:X8}");
            sb.AppendLine($"    Voxel Size        : {(s.overrideVoxelSize ? s.voxelSize.ToString() : "(auto)")}");
            sb.AppendLine($"    Min Region Area   : {s.minRegionArea}");
            sb.AppendLine($"    Surface transform : pos={s.transform.position} " +
                          $"rot={s.transform.rotation.eulerAngles} scale={s.transform.lossyScale}");

            // What the bake will actually see
            var sources = (List<NavMeshBuildSource>)collect.Invoke(s, null);
            sb.AppendLine($"    --> COLLECTED SOURCES: {sources.Count}");

            int tris = 0;
            var bounds = new Bounds();
            bool first = true;
            foreach (var src in sources)
            {
                if (src.shape == NavMeshBuildSourceShape.Mesh && src.sourceObject is Mesh m)
                {
                    tris += m.triangles.Length / 3;
                    var b = m.bounds;
                    b.center = src.transform.MultiplyPoint(b.center);
                    if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                }
            }
            sb.AppendLine($"    --> total triangles  : {tris}");
            if (!first) sb.AppendLine($"    --> geometry bounds  : center={bounds.center} size={bounds.size}");

            foreach (var src in sources)
            {
                var name = src.component ? Path(src.component.transform) : "(no component)";
                var mesh = src.sourceObject as Mesh;
                sb.AppendLine($"        - {src.shape,-8} {name}" +
                              (mesh ? $"  mesh='{mesh.name}' verts={mesh.vertexCount} tris={mesh.triangles.Length / 3}" : ""));
            }

            if (sources.Count == 0)
                sb.AppendLine("        (nothing collected — bake will produce an empty NavMesh)");

            // Every candidate renderer under this surface, and why it may be skipped
            if (s.collectObjects == CollectObjects.Children)
            {
                sb.AppendLine("    --- candidate MeshFilters in hierarchy ---");
                var filters = s.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length == 0) sb.AppendLine("        (none)");
                foreach (var f in filters)
                {
                    var r = f.GetComponent<MeshRenderer>();
                    sb.AppendLine($"        - {Path(f.transform)}");
                    sb.AppendLine($"            activeInHierarchy={f.gameObject.activeInHierarchy}" +
                                  $"  renderer={(r ? (r.enabled ? "enabled" : "DISABLED") : "MISSING")}" +
                                  $"  layer={LayerMask.LayerToName(f.gameObject.layer)}({f.gameObject.layer})" +
                                  $"  mesh={(f.sharedMesh ? f.sharedMesh.name : "NULL")}" +
                                  $"  tris={(f.sharedMesh ? f.sharedMesh.triangles.Length / 3 : 0)}");
                }
            }
        }

        Debug.Log(sb.ToString());
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
