// Draws the baked NavMesh directly in the Scene view, on top of everything,
// coloured by height so stacked floors are easy to tell apart.
// Independent of Unity's own "AI Navigation" overlay settings.
// Menu: Tools > NavMesh > Toggle NavMesh Overlay
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class NavMeshOverlayDraw
{
    const string kPref = "NavMeshOverlayDraw.Enabled";

    static bool s_Enabled;
    static NavMeshTriangulation s_Tri;
    static double s_LastRefresh;
    static float s_YMin, s_YMax;

    static NavMeshOverlayDraw()
    {
        s_Enabled = EditorPrefs.GetBool(kPref, false);
        if (s_Enabled) SceneView.duringSceneGui += OnScene;
    }

    [MenuItem("Tools/NavMesh/Toggle NavMesh Overlay")]
    static void Toggle()
    {
        s_Enabled = !s_Enabled;
        EditorPrefs.SetBool(kPref, s_Enabled);

        SceneView.duringSceneGui -= OnScene;
        if (s_Enabled) SceneView.duringSceneGui += OnScene;

        s_LastRefresh = 0;
        SceneView.RepaintAll();

        var tri = NavMesh.CalculateTriangulation();
        Debug.Log($"NavMesh overlay {(s_Enabled ? "ON" : "OFF")} — " +
                  $"{tri.indices.Length / 3} polys currently in the scene NavMesh.");
    }

    [MenuItem("Tools/NavMesh/Toggle NavMesh Overlay", validate = true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked("Tools/NavMesh/Toggle NavMesh Overlay", s_Enabled);
        return true;
    }

    static void Refresh()
    {
        s_Tri = NavMesh.CalculateTriangulation();
        s_YMin = float.MaxValue;
        s_YMax = float.MinValue;
        foreach (var v in s_Tri.vertices)
        {
            if (v.y < s_YMin) s_YMin = v.y;
            if (v.y > s_YMax) s_YMax = v.y;
        }
        s_LastRefresh = EditorApplication.timeSinceStartup;
    }

    static void OnScene(SceneView sv)
    {
        if (EditorApplication.timeSinceStartup - s_LastRefresh > 1.0)
            Refresh();

        int triCount = s_Tri.indices.Length / 3;
        if (triCount == 0) return;

        var oldZ = Handles.zTest;
        Handles.zTest = CompareFunction.Always;   // draw through the scanned room mesh

        float span = Mathf.Max(0.001f, s_YMax - s_YMin);
        var v = s_Tri.vertices;
        var idx = s_Tri.indices;
        var poly = new Vector3[3];

        for (int t = 0; t < triCount; t++)
        {
            poly[0] = v[idx[t * 3]];
            poly[1] = v[idx[t * 3 + 1]];
            poly[2] = v[idx[t * 3 + 2]];

            float h = ((poly[0].y + poly[1].y + poly[2].y) / 3f - s_YMin) / span;
            Handles.color = Color.HSVToRGB(0.62f * (1f - h), 0.85f, 1f) * new Color(1, 1, 1, 0.55f);
            Handles.DrawAAConvexPolygon(poly);

            Handles.color = new Color(0, 0, 0, 0.35f);
            Handles.DrawAAPolyLine(1.5f, poly[0], poly[1], poly[2], poly[0]);
        }

        Handles.zTest = oldZ;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 260, 60), GUI.skin.box);
        GUILayout.Label($"NavMesh: {triCount} polys");
        GUILayout.Label($"height {s_YMin:F2} .. {s_YMax:F2} m  (blue = low, red = high)");
        GUILayout.EndArea();
        Handles.EndGUI();
    }
}
