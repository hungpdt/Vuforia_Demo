using UnityEngine;
using UnityEngine.AI;
using Vuforia;

/// <summary>
/// On-screen diagnostics for the AR wayfinding setup.
/// Reads the live state of Vuforia tracking, the NavMeshAgent and the LineRenderer
/// so you can tell which stage is blocking the guidance line from appearing.
///
/// The top block answers the two-floor question directly: which floor does the app
/// think you are standing on, which floor is the destination on, and does the
/// computed route actually change floor.
///
/// Attach to any GameObject in the scene and wire the references.
/// A small toggle button is drawn in the bottom-right corner to show/hide the panel.
/// Remove this component once the problem is found.
/// </summary>
public class NavDebugHud : MonoBehaviour
{
    [Header("References to inspect")]
    public ObserverBehaviour AreaTargetObserver;   // Vuforia tracking state
    public NavMeshAgent NavigationAgent;           // path finding state
    public LineRenderer NavigationLine;            // rendering state
    public Transform ArCameraTransform;            // user position

    [Tooltip("NavRoot - the moving group MultiArea drives. Needed to express the " +
             "camera position in the same space the NavMesh was baked in.")]
    public Transform NavRootTransform;

    [Header("Floor heights, measured from the baked NavMesh")]
    public float Floor3Y = -2.75f;
    public float Floor4Y = 0.90f;
    [Tooltip("How far from a floor's height still counts as standing on it.")]
    public float FloorTolerance = 1.5f;
    [Tooltip("A route whose height span exceeds this is treated as changing floor.")]
    public float FloorChangeSpan = 2.0f;

    [Header("Options")]
    [Tooltip("Whether the panel starts expanded. The toggle button is always drawn.")]
    public bool StartVisible = true;

    [Tooltip("Multiplier on every font and button size. Tune this on the device.")]
    [Range(0.5f, 2f)]
    public float FontScale = 1f;

    bool mVisible;
    string mLastStatus = "no event yet";

    // Size text off the SHORT screen edge, not the height. On a phone held upright
    // the height is more than twice the width, which made height-based sizes huge.
    float UiBase => Mathf.Min(Screen.width, Screen.height) * FontScale;

    void Awake()
    {
        mVisible = StartVisible;
    }

    // Vuforia does not expose a pollable "is tracked" flag - it raises an event
    // whenever the status changes, so we subscribe and cache the latest value.
    void OnEnable()
    {
        if (AreaTargetObserver != null)
            AreaTargetObserver.OnTargetStatusChanged += OnStatusChanged;
    }

    // Always unsubscribe: if this component is destroyed while still registered,
    // Vuforia would invoke a callback on a dead object.
    void OnDisable()
    {
        if (AreaTargetObserver != null)
            AreaTargetObserver.OnTargetStatusChanged -= OnStatusChanged;
    }

    void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        // Only cache here. Drawing must happen inside OnGUI.
        mLastStatus = status.Status + " / " + status.StatusInfo;
    }

    /// <summary>Which floor a height belongs to, or "?" for the stairwell between them.</summary>
    string FloorOf(float y)
    {
        if (Mathf.Abs(y - Floor3Y) <= FloorTolerance) return "3";
        if (Mathf.Abs(y - Floor4Y) <= FloorTolerance) return "4";
        return "?";
    }

    void OnGUI()
    {
        DrawToggleButton();

        if (!mVisible)
            return;

        DrawPanel();
    }

    /// <summary>
    /// Small always-visible button that expands or collapses the panel.
    /// Sized relative to the screen so it stays a usable touch target on
    /// high-density phone displays.
    /// </summary>
    void DrawToggleButton()
    {
        float btnW = UiBase * 0.26f;
        float btnH = UiBase * 0.075f;
        float margin = Screen.width * 0.02f;

        var btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(UiBase * 0.026f)
        };

        var rect = new Rect(Screen.width - btnW - margin,
                            Screen.height - btnH - margin,
                            btnW, btnH);

        if (GUI.Button(rect, mVisible ? "DEBUG OFF" : "DEBUG ON", btnStyle))
            mVisible = !mVisible;
    }

    void DrawPanel()
    {
        int bigFont = Mathf.RoundToInt(UiBase * 0.032f);
        int smallFont = Mathf.RoundToInt(UiBase * 0.019f);

        // Height the content actually needs: 3 headline rows plus 14 detail rows.
        float w = Screen.width * 0.95f;
        float h = bigFont * 1.5f * 3f + bigFont * 0.6f + smallFont * 1.35f * 14f + 30f;
        GUI.Box(new Rect(10, 10, w, h), GUIContent.none);

        float pad = 20f;
        float y = 20f;

        // ---- headline: the two-floor question -------------------------------
        var bigStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = bigFont,
            fontStyle = FontStyle.Bold
        };
        float bigH = bigFont * 1.5f;

        string here = "?", dest = "?", route = "no route";
        Color hereColor = Color.yellow, routeColor = Color.yellow;

        if (NavigationAgent != null)
        {
            here = FloorOf(NavigationAgent.transform.position.y);
            hereColor = here == "?" ? Color.yellow : Color.green;

            var corners = NavigationAgent.path == null ? null : NavigationAgent.path.corners;
            if (corners != null && corners.Length > 0)
            {
                dest = FloorOf(corners[corners.Length - 1].y);

                float yMin = float.MaxValue, yMax = float.MinValue;
                foreach (var c in corners)
                {
                    yMin = Mathf.Min(yMin, c.y);
                    yMax = Mathf.Max(yMax, c.y);
                }
                float span = yMax - yMin;
                bool changes = span > FloorChangeSpan;

                // The route is wrong whenever the floors differ but it never climbs.
                bool mismatch = here != "?" && dest != "?" && here != dest && !changes;
                route = (changes ? "CHANGES FLOOR" : "SAME FLOOR") + "  span=" + span.ToString("F2") + "m";
                routeColor = mismatch ? Color.red : (changes ? Color.green : Color.white);
            }
        }

        bigStyle.normal.textColor = hereColor;
        GUI.Label(new Rect(pad, y, w - pad, bigH), "YOU ARE ON: FLOOR " + here, bigStyle);
        y += bigH;

        bigStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(pad, y, w - pad, bigH), "DESTINATION: FLOOR " + dest, bigStyle);
        y += bigH;

        bigStyle.normal.textColor = routeColor;
        GUI.Label(new Rect(pad, y, w - pad, bigH), "ROUTE: " + route, bigStyle);
        y += bigH + bigFont * 0.6f;

        // ---- detail lines ----------------------------------------------------
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = smallFont,
            normal = { textColor = Color.white }
        };

        var sb = new System.Text.StringBuilder();

        sb.AppendLine("floor heights: 3 -> y=" + Floor3Y.ToString("F2")
                      + "   4 -> y=" + Floor4Y.ToString("F2"));

        // Stage 1 - has Vuforia recognised the Area Target?
        // "no event yet" means the observer never reported a status change at all.
        sb.AppendLine("[1] TRACKING : " + mLastStatus);

        // Stage 3 - is the agent standing on the baked NavMesh?
        // If isOnNavMesh is false, SetDestination always fails.
        if (NavigationAgent == null)
        {
            sb.AppendLine("[3] AGENT    : REFERENCE NOT SET");
        }
        else
        {
            var apos = NavigationAgent.transform.position;
            sb.AppendLine("[3] AGENT    : onNavMesh=" + NavigationAgent.isOnNavMesh
                          + "  y=" + apos.y.ToString("F2") + " -> floor " + FloorOf(apos.y));
            sb.AppendLine("             : pos=" + Fmt(apos));

            // corners is the exact array NavMeshManager feeds into the LineRenderer,
            // so corners == 0 guarantees nothing can be drawn.
            sb.AppendLine("    PATH     : hasPath=" + NavigationAgent.hasPath
                          + "  status=" + NavigationAgent.pathStatus
                          + "  corners=" + (NavigationAgent.path == null ? 0 : NavigationAgent.path.corners.Length));

            // destination is meaningless before a path exists, so guard the read.
            sb.AppendLine("    DEST     : " + (NavigationAgent.hasPath || NavigationAgent.pathPending
                          ? Fmt(NavigationAgent.destination) : "no destination set"));
        }

        // Stages 2 and 4 - NavMeshManager drives 'enabled' from its two private
        // flags, so reading it here reveals state we cannot otherwise see.
        if (NavigationLine == null)
            sb.AppendLine("[2] LINE     : REFERENCE NOT SET");
        else
            sb.AppendLine("[2] LINE     : enabled=" + NavigationLine.enabled
                          + "  points=" + NavigationLine.positionCount
                          + "  width=" + NavigationLine.widthMultiplier.ToString("F2"));

        // The camera sits at eye height, so its own y never matches a floor slab.
        // What matters is the height of the NavMesh nearest to it.
        if (ArCameraTransform != null)
        {
            var camWorld = ArCameraTransform.position;
            sb.AppendLine("    CAMERA   : world=" + Fmt(camWorld));

            if (NavRootTransform != null)
            {
                var camLocal = NavRootTransform.InverseTransformPoint(camWorld);
                sb.AppendLine("             : in NavRoot=" + Fmt(camLocal));

                if (NavMesh.SamplePosition(camLocal, out var hit, 5f, NavMesh.AllAreas))
                    sb.AppendLine("    GROUND   : y=" + hit.position.y.ToString("F2")
                                  + " -> floor " + FloorOf(hit.position.y)
                                  + "   (" + Vector3.Distance(camLocal, hit.position).ToString("F2") + "m below cam)");
                else
                    sb.AppendLine("    GROUND   : no NavMesh within 5m of the camera");
            }
            else
            {
                sb.AppendLine("             : NavRootTransform NOT SET - cannot locate the floor");
            }
        }

        GUI.Label(new Rect(pad, y, w - pad, h - y), sb.ToString(), style);
    }

    static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
    }
}
