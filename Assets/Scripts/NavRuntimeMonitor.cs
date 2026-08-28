// Diagnostic only — reads state and logs it, changes nothing.
// Put this on the NavManager GameObject and press Play.
//
// It answers one question: when you stand on floor 3 and ask for floor 4, does
// the agent actually start on floor 3, and does the computed path change floor?
// Delete once navigation behaves.
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Vuforia;

public class NavRuntimeMonitor : MonoBehaviour
{
    [Tooltip("Seconds between log lines.")]
    public float Interval = 1f;

    [Header("Walkable height of each floor, read off the NavMesh")]
    public float Floor3Y = -2.6f;
    public float Floor4Y = 1.1f;
    [Tooltip("How far from a floor's height still counts as being on it.")]
    public float FloorTolerance = 1.2f;

    NavMeshManager mManager;
    NavMeshAgent mAgent;
    LineRenderer mLine;
    FieldInfo mFoundField, mDestField, mOrigPosField;
    float mNext;

    void Awake()
    {
        mManager = GetComponent<NavMeshManager>();
        if (mManager == null)
        {
            Debug.LogError("NavRuntimeMonitor: no NavMeshManager on this GameObject.");
            enabled = false;
            return;
        }

        mAgent = mManager.NavigationAgent;
        mLine = mManager.NavigationLine;

        var t = typeof(NavMeshManager);
        mFoundField = t.GetField("mHasFoundAreaTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        mDestField = t.GetField("mCurrentDestination", BindingFlags.NonPublic | BindingFlags.Instance);
        mOrigPosField = t.GetField("mAreaTargetOriginalPosition", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    string Floor(float y)
    {
        if (Mathf.Abs(y - Floor3Y) < FloorTolerance) return "FLOOR-3";
        if (Mathf.Abs(y - Floor4Y) < FloorTolerance) return "FLOOR-4";
        return "between/stairs";
    }

    void Update()
    {
        if (Time.time < mNext) return;
        mNext = Time.time + Interval;

        var root = mManager.AreaTargetTransform;
        var cam = mManager.ArCameraTransform;
        if (root == null || cam == null) { Debug.LogError("NavMeshManager is missing NavRoot or ARCamera."); return; }

        var sb = new StringBuilder();
        sb.AppendLine("========== nav monitor ==========");

        // 0. reference frame sanity
        sb.AppendLine($"NavRoot pos={root.position} rot={root.rotation.eulerAngles}");
        sb.AppendLine($"  mAreaTargetOriginalPosition={mOrigPosField?.GetValue(mManager)}  (expected ~0,0,0)");
        sb.AppendLine($"  mHasFoundAreaTarget={(mFoundField != null && (bool)mFoundField.GetValue(mManager))}");
        foreach (var at in FindObjectsByType<AreaTargetBehaviour>(FindObjectsSortMode.None))
            sb.AppendLine($"  '{at.TargetName}' {at.TargetStatus.Status} pos={at.transform.position}");

        // 1. where the user is, in the frame the NavMesh lives in
        var camInRoot = root.InverseTransformPoint(cam.position);
        sb.AppendLine($"CAMERA world={cam.position}");
        sb.AppendLine($"  in NavRoot space={camInRoot}  y={camInRoot.y:F2} -> {Floor(camInRoot.y)}");
        if (NavMesh.SamplePosition(camInRoot, out var camHit, 5f, NavMesh.AllAreas))
            sb.AppendLine($"  nearest NavMesh={camHit.position} y={camHit.position.y:F2} -> {Floor(camHit.position.y)}" +
                          $"  ({Vector3.Distance(camInRoot, camHit.position):F2} m away)");
        else
            sb.AppendLine("  no NavMesh within 5 m of the camera");

        if (mAgent == null) { sb.AppendLine("AGENT IS NULL"); Debug.Log(sb.ToString()); return; }

        // 2. where the agent ended up after snapping, and 3. how far that moved it
        var agentPos = mAgent.transform.position;
        sb.AppendLine($"AGENT pos={agentPos} isOnNavMesh={mAgent.isOnNavMesh}");
        sb.AppendLine($"  y={agentPos.y:F2} -> {Floor(agentPos.y)}");
        sb.AppendLine($"  drift from intended position = {Vector3.Distance(agentPos, camInRoot):F2} m" +
                      "   (a big number means it snapped to the wrong slab)");

        // 4. the destination
        var dest = mDestField?.GetValue(mManager);
        if (dest == null) sb.AppendLine("DEST: none yet (no button pressed)");
        else
        {
            var d = (Vector3)dest;
            sb.AppendLine($"DEST requested={d} y={d.y:F2} -> {Floor(d.y)}");
            sb.AppendLine($"  agent.destination={mAgent.destination} y={mAgent.destination.y:F2} -> {Floor(mAgent.destination.y)}");
        }

        // 5. the decisive part: does the path actually change floor?
        var corners = mAgent.path.corners;
        sb.AppendLine($"PATH status={mAgent.pathStatus} pending={mAgent.pathPending} corners={corners.Length}");
        if (corners.Length > 0)
        {
            float yMin = float.MaxValue, yMax = float.MinValue, len = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                yMin = Mathf.Min(yMin, corners[i].y);
                yMax = Mathf.Max(yMax, corners[i].y);
                if (i > 0) len += Vector3.Distance(corners[i - 1], corners[i]);
            }
            sb.AppendLine($"  length={len:F1} m   y {yMin:F2} .. {yMax:F2}   span={yMax - yMin:F2} m");
            sb.AppendLine(yMax - yMin > 2.0f
                ? "  => path DOES change floor"
                : "  => path STAYS ON ONE FLOOR  <<< the bug, if you asked for the other floor");

            var ys = new StringBuilder("  corner y:");
            foreach (var c in corners) ys.Append($" {c.y:F2}");
            sb.AppendLine(ys.ToString());
            sb.AppendLine($"  first={corners[0]}  last={corners[corners.Length - 1]}");
        }

        if (mLine != null)
            sb.AppendLine($"LINE enabled={mLine.enabled} positionCount={mLine.positionCount} width={mLine.widthMultiplier}");

        Debug.Log(sb.ToString());
    }
}
