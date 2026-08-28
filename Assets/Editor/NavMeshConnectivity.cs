// Splits the baked NavMesh into connected components and reports each one's
// size and height range, so you can tell whether two floors are actually linked.
// Menu: Tools > NavMesh > Check Floor Connectivity
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class NavMeshConnectivity
{
    [MenuItem("Tools/NavMesh/Check Floor Connectivity")]
    static void Check()
    {
        var tri = NavMesh.CalculateTriangulation();
        int triCount = tri.indices.Length / 3;

        if (triCount == 0)
        {
            Debug.LogError("No NavMesh in the scene. Bake first.");
            return;
        }

        // Weld vertices by position so triangles from the same surface share indices
        var weld = new Dictionary<Vector3Int, int>();
        var vmap = new int[tri.vertices.Length];
        for (int i = 0; i < tri.vertices.Length; i++)
        {
            var v = tri.vertices[i];
            var key = new Vector3Int(Mathf.RoundToInt(v.x * 1000f),
                                     Mathf.RoundToInt(v.y * 1000f),
                                     Mathf.RoundToInt(v.z * 1000f));
            if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
            vmap[i] = id;
        }

        // Triangles sharing a welded edge belong to the same component
        var parent = new int[triCount];
        for (int i = 0; i < triCount; i++) parent[i] = i;

        var edgeOwner = new Dictionary<long, int>();
        for (int t = 0; t < triCount; t++)
        {
            int a = vmap[tri.indices[t * 3]], b = vmap[tri.indices[t * 3 + 1]], c = vmap[tri.indices[t * 3 + 2]];
            AddEdge(edgeOwner, parent, a, b, t);
            AddEdge(edgeOwner, parent, b, c, t);
            AddEdge(edgeOwner, parent, c, a, t);
        }

        // Gather stats per component
        var comps = new Dictionary<int, Comp>();
        for (int t = 0; t < triCount; t++)
        {
            int root = Find(parent, t);
            if (!comps.TryGetValue(root, out var comp)) { comp = new Comp(); comps[root] = comp; }

            var p0 = tri.vertices[tri.indices[t * 3]];
            var p1 = tri.vertices[tri.indices[t * 3 + 1]];
            var p2 = tri.vertices[tri.indices[t * 3 + 2]];

            comp.tris++;
            comp.area += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            comp.centroid += (p0 + p1 + p2) / 3f;
            foreach (var p in new[] { p0, p1, p2 })
            {
                comp.yMin = Mathf.Min(comp.yMin, p.y);
                comp.yMax = Mathf.Max(comp.yMax, p.y);
            }
            comp.sample = p0;
        }

        var list = new List<Comp>(comps.Values);
        list.Sort((x, y) => y.area.CompareTo(x.area));

        var sb = new StringBuilder();
        sb.AppendLine($"=== NavMesh connectivity — {triCount} polys, {list.Count} connected component(s) ===");
        sb.AppendLine();

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            c.centroid /= c.tris;
            sb.AppendLine($"[{i}] area={c.area,7:F1} m²  polys={c.tris,5}  " +
                          $"y: {c.yMin,6:F2} .. {c.yMax,6:F2}  (span {c.yMax - c.yMin:F2} m)  " +
                          $"centroid={c.centroid}");
        }

        sb.AppendLine();
        if (list.Count == 1)
        {
            var c = list[0];
            sb.AppendLine(c.yMax - c.yMin > 2.0f
                ? $"=> ONE component spanning {c.yMax - c.yMin:F2} m vertically — the floors ARE connected."
                : $"=> ONE component but only {c.yMax - c.yMin:F2} m tall — this looks like a single floor only.");
        }
        else
        {
            sb.AppendLine("=> MULTIPLE components. An agent cannot path between them without a NavMeshLink.");
            sb.AppendLine("   Pairwise path test between the two largest:");

            var a = list[0].sample;
            var b = list[1].sample;
            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
            sb.AppendLine($"   CalculatePath({a} -> {b}) = {(ok ? path.status.ToString() : "FAILED")}");
        }

        Debug.Log(sb.ToString());
    }

    class Comp
    {
        public int tris;
        public float area;
        public Vector3 centroid;
        public float yMin = float.MaxValue, yMax = float.MinValue;
        public Vector3 sample;
    }

    static void AddEdge(Dictionary<long, int> owner, int[] parent, int v0, int v1, int t)
    {
        long key = v0 < v1 ? ((long)v0 << 32) | (uint)v1 : ((long)v1 << 32) | (uint)v0;
        if (owner.TryGetValue(key, out int other)) Union(parent, t, other);
        else owner[key] = t;
    }

    static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

    static void Union(int[] p, int a, int b)
    {
        a = Find(p, a); b = Find(p, b);
        if (a != b) p[b] = a;
    }
}
