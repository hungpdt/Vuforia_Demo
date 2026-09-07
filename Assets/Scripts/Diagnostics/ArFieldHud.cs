// On-device control panel for the museum trip.
// Big verdict line so the situation is readable at a glance while walking,
// plus the four buttons that decide whether the trip was worth it:
// RECORD / STOP / SNAPSHOT / SHARE.
using UnityEngine;
using Vuforia;

/// <summary>
/// Draws <see cref="ArSessionDiagnostics"/> on screen and drives Vuforia's
/// <see cref="SessionRecorderBehaviour"/>.
///
/// IMGUI on purpose: no Canvas, no prefab, no wiring, and it cannot be hidden
/// behind the AR content or broken by a camera mistake - which matters when the
/// whole point is diagnosing a scene that may be misconfigured.
///
/// The recorder is the part that saves a second trip: it captures the camera
/// feed and the device poses, so the exact session can be replayed in the
/// Editor at the office. Vuforia refuses to record from Editor Play Mode, so
/// this only does anything on the device.
/// </summary>
[RequireComponent(typeof(ArSessionDiagnostics))]
public class ArFieldHud : MonoBehaviour
{
    [Tooltip("Optional. Left empty, it is looked up on this GameObject.")]
    public SessionRecorderBehaviour Recorder;

    [Tooltip("Multiplier on every font and button size. Tune this on the device.")]
    [Range(0.5f, 2f)]
    public float FontScale = 1f;

    [Tooltip("Whether the detail block starts expanded.")]
    public bool StartExpanded = true;

    ArSessionDiagnostics mDiag;
    bool mExpanded;
    string mToast = "";
    float mToastUntil;

    // Size text off the SHORT screen edge, not the height. On a phone held
    // upright the height is more than twice the width, which makes
    // height-based sizes unusable.
    float UiBase => Mathf.Min(Screen.width, Screen.height) * FontScale;

    bool IsRecording => Recorder != null && Recorder.GetRecordingStatus() == RecordingStatus.RUNNING;

    void Awake()
    {
        mDiag = GetComponent<ArSessionDiagnostics>();
        mExpanded = StartExpanded;

        if (Recorder == null)
            Recorder = GetComponent<SessionRecorderBehaviour>();
    }

    void OnGUI()
    {
        // Screen.safeArea uses a bottom-left origin, while IMGUI uses a
        // top-left origin. Convert it once so every HUD element stays clear of
        // the Dynamic Island, notch and home indicator in any orientation.
        var safeArea = GuiSafeArea();
        var pad = safeArea.width * 0.02f;
        var x = safeArea.xMin + pad;
        var w = safeArea.width - pad * 2f;

        var bigFont = Mathf.RoundToInt(UiBase * 0.030f);
        var smallFont = Mathf.RoundToInt(UiBase * 0.019f);

        var y = safeArea.yMin + pad;

        // ---- verdict banner -------------------------------------------------
        var verdict = mDiag.Verdict();

        var banner = new GUIStyle(GUI.skin.box)
        {
            fontSize = bigFont,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            padding = new RectOffset(12, 12, 10, 10)
        };
        banner.normal.textColor = VerdictColor(verdict);

        var bannerH = banner.CalcHeight(new GUIContent(verdict), w);
        GUI.Box(new Rect(x, y, w, bannerH), verdict, banner);
        y += bannerH + pad * 0.4f;

        // ---- one-line status ------------------------------------------------
        var line = new GUIStyle(GUI.skin.box)
        {
            fontSize = smallFont,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            padding = new RectOffset(12, 12, 8, 8)
        };
        line.normal.textColor = Color.white;

        var headline = string.Format("{0} / {1}   ({2:F0}s)     tracked {3:F0}%     lost {4}x     {5}{6}",
                                     mDiag.CurrentStatus, mDiag.CurrentStatusInfo,
                                     mDiag.SecondsInCurrentStatus, mDiag.TrackedPercent, mDiag.LostCount,
                                     IsRecording ? "[REC] " : "",
                                     ArFieldLog.ErrorCount > 0 ? "ERR " + ArFieldLog.ErrorCount : "");

        var headlineH = line.CalcHeight(new GUIContent(headline), w);
        GUI.Box(new Rect(x, y, w, headlineH), headline, line);
        y += headlineH + pad * 0.4f;

        // ---- detail block ---------------------------------------------------
        if (mExpanded)
        {
            var details = BuildDetails();
            var detailH = line.CalcHeight(new GUIContent(details), w);
            GUI.Box(new Rect(x, y, w, detailH), details, line);
            y += detailH + pad * 0.4f;
        }

        // ---- last error ------------------------------------------------------
        // Unity's development console prints this at a fixed size that is far
        // too small to read on a phone, so it is reprinted here at HUD size.
        // Tap it to dismiss once it has been read.
        if (ArFieldLog.ErrorCount > 0)
        {
            var errStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = smallFont,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(12, 12, 8, 8)
            };
            errStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);

            var text = "ERROR x" + ArFieldLog.ErrorCount + " (cham de an)\n" + ArFieldLog.LastError;
            var errH = errStyle.CalcHeight(new GUIContent(text), w);

            if (GUI.Button(new Rect(x, y, w, errH), text, errStyle))
                ArFieldLog.ClearErrors();

            y += errH + pad * 0.4f;
        }

        // ---- transient confirmation ----------------------------------------
        if (Time.realtimeSinceStartup < mToastUntil)
        {
            var toastH = line.CalcHeight(new GUIContent(mToast), w);
            GUI.Box(new Rect(x, y, w, toastH), mToast, line);
        }

        DrawButtons(safeArea, pad, smallFont);
    }

    static Rect GuiSafeArea()
    {
        var safeArea = Screen.safeArea;

        // An empty safe area can briefly be reported during device startup.
        if (safeArea.width < 1f || safeArea.height < 1f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        return new Rect(safeArea.xMin,
                        Screen.height - safeArea.yMax,
                        safeArea.width,
                        safeArea.height);
    }

    string BuildDetails()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("first fix : " + (mDiag.SecondsToFirstFix < 0f
                                        ? "CHUA CO"
                                        : mDiag.SecondsToFirstFix.ToString("F1") + "s"));
        sb.AppendLine("session   : " + mDiag.SessionSeconds.ToString("F0") + "s"
                      + "   vuforia=" + (mDiag.VuforiaStarted ? "running" : "NOT RUNNING")
                      + "   init=" + mDiag.InitErrorText);

        if (mDiag.HasAmbientIntensity)
            sb.AppendLine("light     : " + mDiag.AmbientIntensity.ToString("F0") + " lumens");

        if (mDiag.Agent != null)
            sb.AppendLine("agent     : onNavMesh=" + mDiag.Agent.isOnNavMesh
                          + "   path=" + mDiag.Agent.pathStatus
                          + "   corners=" + (mDiag.Agent.hasPath ? mDiag.Agent.path.corners.Length : 0));
        else
            sb.AppendLine("agent     : MISSING");

        if (mDiag.NavigationLine != null)
            sb.AppendLine("line      : enabled=" + mDiag.NavigationLine.enabled
                          + "   points=" + mDiag.NavigationLine.positionCount);

        sb.AppendLine("recorder  : " + (Recorder == null
                                        ? "KHONG CO SessionRecorderBehaviour"
                                        : Recorder.GetRecordingStatus().ToString()));

        // The path is on screen because that is the folder to plug in and copy
        // once back at the office.
        sb.Append("files     : " + ArFieldLog.Folder);

        return sb.ToString();
    }

    void DrawButtons(Rect safeArea, float pad, int fontSize)
    {
        var btnH = UiBase * 0.085f;
        var btnW = (safeArea.width - pad * 5f) / 4f;
        var bottomBarReserved = UiBase * 0.12f;
        var y = safeArea.yMax - btnH - pad - bottomBarReserved;

        var style = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        var x = safeArea.xMin + pad;

        // RECORD / STOP share one slot: only one of them is ever valid.
        GUI.enabled = Recorder != null;
        if (GUI.Button(new Rect(x, y, btnW, btnH), IsRecording ? "STOP REC" : "RECORD", style))
            ToggleRecording();
        GUI.enabled = true;
        x += btnW + pad;

        if (GUI.Button(new Rect(x, y, btnW, btnH), "SNAPSHOT", style))
        {
            mDiag.Snapshot("nut SNAPSHOT");
            Toast("Da luu screenshot + log");
        }
        x += btnW + pad;

        GUI.enabled = Recorder != null && !IsRecording;
        if (GUI.Button(new Rect(x, y, btnW, btnH), "SHARE", style))
        {
            Recorder.ShareRecording();
            ArFieldLog.Line("share recording requested");
        }
        GUI.enabled = true;
        x += btnW + pad;

        if (GUI.Button(new Rect(x, y, btnW, btnH), mExpanded ? "LESS" : "MORE", style))
            mExpanded = !mExpanded;
    }

    void ToggleRecording()
    {
        if (IsRecording)
        {
            Recorder.StopRecording();
            ArFieldLog.Line("recording stopped by user");
            Toast("Da dung ghi. Bam SHARE de gui file ve.");
            return;
        }

        Recorder.StartRecording();
        ArFieldLog.Line("recording start requested");

        // Vuforia refuses to record from Editor Play Mode, and a few device
        // errors (no disk space, orientation unknown) also fail silently from
        // this side - so confirm against the real status instead of assuming.
        Toast(IsRecording
              ? "Dang ghi session..."
              : "KHONG ghi duoc - xem log. (Editor Play Mode khong ho tro ghi)");
    }

    static Color VerdictColor(string verdict)
    {
        if (verdict.StartsWith("OK"))
            return Color.green;

        if (verdict.StartsWith("DANG") || verdict.StartsWith("TRACK OK, agent OK"))
            return Color.yellow;

        return new Color(1f, 0.45f, 0.4f);
    }

    void Toast(string message)
    {
        mToast = message;
        mToastUntil = Time.realtimeSinceStartup + 4f;
        ArFieldLog.Line("hud: " + message);
    }
}
