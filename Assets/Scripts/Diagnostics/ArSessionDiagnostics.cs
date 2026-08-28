// Collects everything needed to tell "the museum changed" apart from "the app
// is broken", and writes it to a file on the device.
// Pairs with ArFieldHud, which renders the same state on screen.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Vuforia;

/// <summary>
/// Watches the Vuforia session and the navigation stack, keeps running
/// statistics, and writes both a transition timeline and a periodic sample line
/// to <see cref="ArFieldLog"/>.
///
/// The statistics are the point. A single "not tracked" reading says nothing on
/// its own; "never relocalised anywhere in 4 minutes, 71% of that time reported
/// INSUFFICIENT_FEATURES" is a diagnosis you can act on without going back.
/// </summary>
public class ArSessionDiagnostics : MonoBehaviour
{
    [Header("What to watch (left empty = found automatically)")]
    public ObserverBehaviour Observer;
    public Transform ArCamera;

    [Tooltip("The transform the NavMesh was baked under - the AreaTarget root.")]
    public Transform NavRoot;

    public NavMeshAgent Agent;
    public LineRenderer NavigationLine;

    [Header("Sampling")]
    [Tooltip("Seconds between periodic sample lines in the log file.")]
    public float SampleInterval = 1f;

    [Tooltip("A session with no fix for this long is reported as a hard failure.")]
    public float NoFixIsFatalAfter = 60f;

    // ---- live state, read by ArFieldHud ---------------------------------
    public bool VuforiaStarted { get; private set; }
    public bool HasInitError { get; private set; }
    public string InitErrorText { get; private set; }
    public Status CurrentStatus { get; private set; }
    public StatusInfo CurrentStatusInfo { get; private set; }
    public float SecondsInCurrentStatus { get; private set; }
    public float SessionSeconds { get; private set; }

    /// <summary>Seconds from session start to the very first tracked frame, or -1.</summary>
    public float SecondsToFirstFix { get; private set; }

    public int LostCount { get; private set; }
    public float TrackedSeconds { get; private set; }
    public float TrackedPercent => SessionSeconds > 0.01f ? 100f * TrackedSeconds / SessionSeconds : 0f;
    public float AmbientIntensity { get; private set; }
    public bool HasAmbientIntensity { get; private set; }

    // How long each StatusInfo reason was reported. This is what separates
    // "the room changed" (INSUFFICIENT_FEATURES / RELOCALIZING forever) from
    // "it was too dark" (INSUFFICIENT_LIGHT) from "I walked too fast"
    // (EXCESSIVE_MOTION).
    readonly Dictionary<StatusInfo, float> mReasonSeconds = new Dictionary<StatusInfo, float>();

    float mNextSample;
    float mStatusChangedAt;
    bool mHeaderWritten;
    int mSnapshotCount;
    World mWorld;

    // Same object as Observer whenever it is an Area Target. Kept separately
    // because the pollable TargetStatus property is read off this type.
    AreaTargetBehaviour mAreaTarget;

    public string LogPath => ArFieldLog.FilePath;

    void Awake()
    {
        InitErrorText = "none";
        CurrentStatus = Status.NO_POSE;
        CurrentStatusInfo = StatusInfo.UNKNOWN;
        SecondsToFirstFix = -1f;

        ArFieldLog.Open(gameObject.scene.name);
        AutoWire();

        // Written here rather than on OnVuforiaStarted: if Vuforia never
        // starts at all - the single worst case to debug remotely - the log
        // still names the device, the app version and what the scene contains.
        WriteHeader();
    }

    /// <summary>
    /// Fills in whatever the inspector left empty, so the rig keeps working
    /// after the scene is rearranged.
    /// </summary>
    void AutoWire()
    {
        if (Observer == null)
            Observer = FindFirstObjectByType<AreaTargetBehaviour>();

        mAreaTarget = Observer as AreaTargetBehaviour;

        if (Observer != null && NavRoot == null)
            NavRoot = Observer.transform;

        if (Agent == null)
            Agent = FindFirstObjectByType<NavMeshAgent>();

        if (NavigationLine == null)
            NavigationLine = FindFirstObjectByType<LineRenderer>();

        if (ArCamera == null && VuforiaBehaviour.Instance != null)
            ArCamera = VuforiaBehaviour.Instance.transform;

        if (ArCamera == null && Camera.main != null)
            ArCamera = Camera.main.transform;
    }

    void OnEnable()
    {
        VuforiaApplication.Instance.OnVuforiaInitialized += OnVuforiaInitialized;
        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped += OnVuforiaStopped;

        if (Observer != null)
            Observer.OnTargetStatusChanged += OnTargetStatusChanged;

        mStatusChangedAt = Time.realtimeSinceStartup;
    }

    void OnDisable()
    {
        VuforiaApplication.Instance.OnVuforiaInitialized -= OnVuforiaInitialized;
        VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnVuforiaStopped;

        if (Observer != null)
            Observer.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void OnDestroy()
    {
        WriteSummary();
        ArFieldLog.Close();
    }

    // Flush on the way to the background: on Android a swiped-away app never
    // reaches OnDestroy, and that is exactly when the log matters most.
    void OnApplicationPause(bool paused)
    {
        if (!paused)
            return;

        ArFieldLog.Line("app paused");
        WriteSummary();
        ArFieldLog.Flush();
    }

    // ---- Vuforia lifecycle ----------------------------------------------

    void OnVuforiaInitialized(VuforiaInitError error)
    {
        HasInitError = error != VuforiaInitError.NONE;
        InitErrorText = error.ToString();
        ArFieldLog.Line("vuforia initialized: " + error);
    }

    void OnVuforiaStarted()
    {
        VuforiaStarted = true;
        mWorld = VuforiaBehaviour.Instance != null ? VuforiaBehaviour.Instance.World : null;

        // Only available once the engine is up, so it cannot go in the header.
        if (VuforiaBehaviour.Instance != null && VuforiaBehaviour.Instance.DevicePoseBehaviour != null)
            ArFieldLog.Line("devicePose enabled=" + VuforiaBehaviour.Instance.DevicePoseBehaviour.enabled);

        ArFieldLog.Line("vuforia started");
    }

    void OnVuforiaStopped()
    {
        VuforiaStarted = false;
        ArFieldLog.Line("vuforia stopped");
        WriteSummary();
    }

    void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        var wasTracked = IsTracked(CurrentStatus);

        CurrentStatus = status.Status;
        CurrentStatusInfo = status.StatusInfo;
        mStatusChangedAt = Time.realtimeSinceStartup;

        var nowTracked = IsTracked(CurrentStatus);

        if (nowTracked && SecondsToFirstFix < 0f)
        {
            SecondsToFirstFix = SessionSeconds;
            ArFieldLog.Line("FIRST FIX after " + SecondsToFirstFix.ToString("F1") + "s");
        }

        if (wasTracked && !nowTracked)
        {
            LostCount++;
            ArFieldLog.Line("TRACKING LOST (#" + LostCount + ")");
        }

        ArFieldLog.Line("status -> " + status.Status + " / " + status.StatusInfo
                        + "   cam=" + Fmt(ArCamera != null ? ArCamera.position : Vector3.zero));
        ArFieldLog.Flush();
    }

    static bool IsTracked(Status s)
    {
        return s == Status.TRACKED || s == Status.EXTENDED_TRACKED;
    }

    // ---- per-frame accounting -------------------------------------------

    void Update()
    {
        // Safety net for the status event. OnTargetStatusChanged is the primary
        // source, but a missed callback would silently invalidate every
        // statistic in the log - and a wasted trip is expensive. Because the
        // handler updates the cache itself, this is a no-op in the normal case
        // and cannot double-count a transition.
        if (mAreaTarget != null)
        {
            var live = mAreaTarget.TargetStatus;
            if (live.Status != CurrentStatus || live.StatusInfo != CurrentStatusInfo)
                OnTargetStatusChanged(mAreaTarget, live);
        }

        var dt = Time.unscaledDeltaTime;
        SessionSeconds += dt;
        SecondsInCurrentStatus = Time.realtimeSinceStartup - mStatusChangedAt;

        if (IsTracked(CurrentStatus))
            TrackedSeconds += dt;

        mReasonSeconds.TryGetValue(CurrentStatusInfo, out var acc);
        mReasonSeconds[CurrentStatusInfo] = acc + dt;

        if (mWorld != null && mWorld.IlluminationData.AmbientIntensity != null)
        {
            AmbientIntensity = mWorld.IlluminationData.AmbientIntensity.Value;
            HasAmbientIntensity = true;
        }

        if (Time.realtimeSinceStartup < mNextSample)
            return;

        mNextSample = Time.realtimeSinceStartup + SampleInterval;
        WriteSample();
    }

    // ---- log writing -----------------------------------------------------

    void WriteHeader()
    {
        if (mHeaderWritten)
            return;

        mHeaderWritten = true;

        ArFieldLog.Section("session header");
        ArFieldLog.Line("app        : " + Application.productName + " v" + Application.version);
        ArFieldLog.Line("scene      : " + gameObject.scene.name);
        ArFieldLog.Line("device     : " + SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem);
        ArFieldLog.Line("gpu        : " + SystemInfo.graphicsDeviceName);
        ArFieldLog.Line("screen     : " + Screen.width + "x" + Screen.height);
        ArFieldLog.Line("vuforia    : " + VuforiaApplication.GetVuforiaLibraryVersion());
        ArFieldLog.Line("log folder : " + ArFieldLog.Folder);

        if (Observer != null)
            ArFieldLog.Line("target     : " + Observer.TargetName + "  (" + Observer.GetType().Name + ")");
        else
            ArFieldLog.Line("target     : NO OBSERVER FOUND IN SCENE  <<< app misconfigured");

        ArFieldLog.Line("agent      : " + (Agent == null ? "MISSING" : Agent.name));
        ArFieldLog.Line("navLine    : " + (NavigationLine == null ? "MISSING" : NavigationLine.name));
        ArFieldLog.Section("timeline");
        ArFieldLog.Flush();
    }

    void WriteSample()
    {
        var sb = new StringBuilder();
        sb.Append(CurrentStatus).Append("/").Append(CurrentStatusInfo);
        sb.Append("  fps=").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("F0"));

        if (HasAmbientIntensity)
            sb.Append("  lumens=").Append(AmbientIntensity.ToString("F0"));

        if (ArCamera != null)
        {
            sb.Append("  cam=").Append(Fmt(ArCamera.position));

            // The camera expressed in NavRoot space is the number that has to
            // line up with the baked NavMesh; the world position alone proves
            // nothing once the AreaTarget root has been moved by tracking.
            if (NavRoot != null)
            {
                var local = NavRoot.InverseTransformPoint(ArCamera.position);
                sb.Append("  inNavRoot=").Append(Fmt(local));
                sb.Append(NavMesh.SamplePosition(local, out var hit, 5f, NavMesh.AllAreas)
                    ? "  ground=" + hit.position.y.ToString("F2")
                    : "  ground=NONE<5m");
            }
        }

        if (Agent != null)
        {
            sb.Append("  onNavMesh=").Append(Agent.isOnNavMesh);
            sb.Append(" path=").Append(Agent.pathStatus);
            sb.Append(" corners=").Append(Agent.hasPath ? Agent.path.corners.Length : 0);
        }

        if (NavigationLine != null)
            sb.Append("  line=").Append(NavigationLine.enabled ? NavigationLine.positionCount.ToString() : "off");

        ArFieldLog.Line(sb.ToString());
    }

    void WriteSummary()
    {
        ArFieldLog.Section("summary");
        ArFieldLog.Line("session length   : " + SessionSeconds.ToString("F1") + "s");
        ArFieldLog.Line("vuforia started  : " + VuforiaStarted + "   init error: " + InitErrorText);
        ArFieldLog.Line("time to first fix: " + (SecondsToFirstFix < 0f ? "NEVER" : SecondsToFirstFix.ToString("F1") + "s"));
        ArFieldLog.Line("tracking lost    : " + LostCount + " times");
        ArFieldLog.Line("tracked          : " + TrackedSeconds.ToString("F1") + "s (" + TrackedPercent.ToString("F0") + "%)");

        foreach (var kv in mReasonSeconds)
        {
            var percent = SessionSeconds > 0.01f ? 100f * kv.Value / SessionSeconds : 0f;
            ArFieldLog.Line("  reason " + kv.Key + " : " + kv.Value.ToString("F1") + "s (" + percent.ToString("F0") + "%)");
        }

        ArFieldLog.Line("verdict          : " + Verdict());
        ArFieldLog.Flush();
    }

    /// <summary>
    /// The one line worth reading first, both on screen and in the file.
    /// Deliberately blunt: it has to be usable while standing in the museum
    /// with limited time, not after an evening of log reading.
    /// Unaccented Vietnamese on purpose - the built-in OnGUI font renders it
    /// reliably on every device.
    /// </summary>
    public string Verdict()
    {
        if (HasInitError)
            return "INIT FAILED (" + InitErrorText + ") - loi cau hinh / license, KHONG phai bao tang";

        if (!VuforiaStarted)
            return "VUFORIA CHUA CHAY - kiem tra quyen camera";

        if (Observer == null)
            return "SCENE THIEU OBSERVER - loi app";

        if (SecondsToFirstFix < 0f && SessionSeconds > NoFixIsFatalAfter)
            return "CHUA RELOCALIZE SAU " + SessionSeconds.ToString("F0")
                   + "s -> NGHI BAO TANG DA THAY DOI, can scan lai Area Target";

        if (SecondsToFirstFix < 0f)
            return "DANG RELOCALIZE... dung dung cho da bat dau scan, quet cham 360 do";

        if (!IsTracked(CurrentStatus))
            return "DA TUNG TRACK DUOC (" + SecondsToFirstFix.ToString("F0")
                   + "s) nhung dang mat - quay lai vung da nhan dien";

        if (Agent != null && !Agent.isOnNavMesh)
            return "TRACK OK nhung AGENT KHONG NAM TREN NAVMESH -> loi APP, khong phai bao tang";

        if (NavigationLine != null && !NavigationLine.enabled)
            return "TRACK OK, agent OK - chua chon diem den";

        return "OK - tracking va navigation deu chay";
    }

    /// <summary>
    /// Freezes one moment: a screenshot plus a full state block in the log.
    /// Press it the instant something looks wrong, so the exact spot in the
    /// building is recoverable later.
    /// </summary>
    public void Snapshot(string note)
    {
        mSnapshotCount++;

        var file = "ARSHOT_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + mSnapshotCount + ".png";
        var path = System.IO.Path.Combine(Application.persistentDataPath, file);

        ArFieldLog.Section("snapshot #" + mSnapshotCount + "  " + note);
        ArFieldLog.Line("screenshot : " + file);
        WriteSample();
        ArFieldLog.Line("verdict    : " + Verdict());

        if (ArCamera != null)
            ArFieldLog.Line("camera rot : " + ArCamera.eulerAngles);

        if (Agent != null && Agent.hasPath)
        {
            var sb = new StringBuilder("path corners:");
            foreach (var p in Agent.path.corners)
                sb.Append(" ").Append(Fmt(p));

            ArFieldLog.Line(sb.ToString());
        }

        ArFieldLog.Flush();

        StartCoroutine(CaptureRoutine(path));
    }

    /// <summary>
    /// Grabs the frame and writes the png here rather than through
    /// ScreenCapture.CaptureScreenshot, which reports failures only through
    /// Unity's own console - unreadable on a phone and impossible to act on in
    /// the field. Doing the encode and the write ourselves means any failure
    /// becomes a plain line in the log, next to the state it belongs to.
    /// </summary>
    IEnumerator CaptureRoutine(string path)
    {
        // CaptureScreenshotAsTexture is only valid once the frame is rendered.
        yield return new WaitForEndOfFrame();

        Texture2D tex = null;
        try
        {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            var png = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);
            ArFieldLog.Line("screenshot saved: " + (png.Length / 1024) + " KB");
        }
        catch (System.Exception e)
        {
            ArFieldLog.Line("SCREENSHOT FAILED: " + e.GetType().Name + " - " + e.Message);
        }
        finally
        {
            if (tex != null)
                Destroy(tex);
        }

        ArFieldLog.Flush();
    }

    static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
    }
}
