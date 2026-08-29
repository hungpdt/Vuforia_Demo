using UnityEngine;
using UnityEngine.AI;
using Vuforia;

/// <summary>
/// Reads the active NavMesh path and converts its first meaningful segment into
/// a simple 2D turn instruction.
/// </summary>
[DisallowMultipleComponent]
public sealed class TurnGuidanceController : MonoBehaviour
{
    public enum Maneuver
    {
        Straight,
        SlightLeft,
        SlightRight,
        Left,
        Right,
        UTurn
    }

    [Header("Navigation references")]
    [SerializeField] NavMeshManager navMeshManager;
    [SerializeField] NavMeshAgent navigationAgent;
    [SerializeField] Transform areaTargetTransform;
    [SerializeField] Transform arCameraTransform;
    [SerializeField] ObserverBehaviour trackingObserver;
    [SerializeField] GuidanceHUD hud;

    [Header("Day 1 tuning")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.2f;
    [SerializeField, Min(0.01f)] float minimumCornerDistance = 0.25f;
    [SerializeField, Range(0f, 90f)] float straightAngle = 25f;
    [SerializeField, Range(1f, 120f)] float slightTurnAngle = 65f;
    [SerializeField, Range(1f, 179f)] float turnAngle = 135f;
    [SerializeField] bool assumeTrackingInEditor = true;

    POIDestination currentDestination;
    Vector3 areaTargetOriginalPosition;
    float nextRefreshTime;
    bool isTracked;

    public POIDestination CurrentDestination => currentDestination;
    public bool IsTracked => isTracked;

    void Awake()
    {
        if (areaTargetTransform != null)
            areaTargetOriginalPosition = areaTargetTransform.position;

        if (Application.isEditor && assumeTrackingInEditor)
            isTracked = true;
    }

    void OnEnable()
    {
        if (trackingObserver != null)
        {
            trackingObserver.OnTargetStatusChanged += OnTargetStatusChanged;
            UpdateTracking(trackingObserver.TargetStatus.Status);
        }
    }

    void OnDisable()
    {
        if (trackingObserver != null)
            trackingObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshGuidance();
    }

    public void SetDestination(POIDestination destination)
    {
        if (destination == null || !destination.IsNavigationEnabled || destination.Anchor == null)
        {
            hud?.ShowMessage("Điểm đến không khả dụng");
            return;
        }

        if (navMeshManager == null || navigationAgent == null)
        {
            hud?.ShowMessage("Navigation chưa được cấu hình", destination.DisplayName);
            return;
        }

        currentDestination = destination;
        navMeshManager.NavigateTo(destination.Anchor);
        nextRefreshTime = 0f;
        hud?.ShowMessage("Đang tính đường...", destination.DisplayName);
    }

    public void ClearDestination()
    {
        currentDestination = null;
        if (navigationAgent != null && navigationAgent.isOnNavMesh)
            navigationAgent.ResetPath();
        hud?.Hide();
    }

    void RefreshGuidance()
    {
        if (currentDestination == null)
        {
            hud?.Hide();
            return;
        }

        if (!isTracked)
        {
            hud?.Hide();
            return;
        }

        if (arCameraTransform == null || areaTargetTransform == null || navigationAgent == null)
        {
            hud?.ShowMessage("Navigation chưa được cấu hình", currentDestination.DisplayName);
            return;
        }

        if (Vector3.Distance(arCameraTransform.position, currentDestination.Anchor.position)
            <= currentDestination.ArrivalRadius)
        {
            hud?.ShowMessage("Đã đến nơi", currentDestination.DisplayName);
            return;
        }

        if (!navigationAgent.isOnNavMesh || navigationAgent.pathPending)
        {
            hud?.ShowMessage("Đang tính đường...", currentDestination.DisplayName);
            return;
        }

        var path = navigationAgent.path;
        var corners = path.corners;
        if (path.status != NavMeshPathStatus.PathComplete || corners == null || corners.Length < 2)
        {
            hud?.ShowMessage("Không tìm thấy đường", currentDestination.DisplayName);
            return;
        }

        if (!TryGetNextWorldCorner(corners, out var nextCorner))
        {
            hud?.ShowMessage("Đi thẳng tới điểm đến", currentDestination.DisplayName);
            return;
        }

        var cameraForward = Vector3.ProjectOnPlane(arCameraTransform.forward, Vector3.up).normalized;
        var routeDirection = Vector3.ProjectOnPlane(nextCorner - arCameraTransform.position, Vector3.up).normalized;
        if (cameraForward.sqrMagnitude < 0.001f || routeDirection.sqrMagnitude < 0.001f)
            return;

        var signedAngle = Vector3.SignedAngle(cameraForward, routeDirection, Vector3.up);
        var maneuver = ClassifyAngle(signedAngle, straightAngle, slightTurnAngle, turnAngle);
        hud?.Show(maneuver, InstructionFor(maneuver), currentDestination.DisplayName);
    }

    bool TryGetNextWorldCorner(Vector3[] corners, out Vector3 worldCorner)
    {
        for (var i = 0; i < corners.Length; i++)
        {
            var candidate = areaTargetTransform.TransformPoint(corners[i] - areaTargetOriginalPosition);
            if (Vector3.ProjectOnPlane(candidate - arCameraTransform.position, Vector3.up).magnitude
                >= minimumCornerDistance)
            {
                worldCorner = candidate;
                return true;
            }
        }

        worldCorner = default;
        return false;
    }

    void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        UpdateTracking(status.Status);
    }

    void UpdateTracking(Status status)
    {
        isTracked = status == Status.TRACKED || status == Status.EXTENDED_TRACKED;

        if (Application.isEditor && assumeTrackingInEditor)
            isTracked = true;

        if (!isTracked)
            hud?.Hide();
    }

    public static Maneuver ClassifyAngle(float signedAngle, float straight, float slight, float turn)
    {
        var absoluteAngle = Mathf.Abs(signedAngle);
        if (absoluteAngle < straight)
            return Maneuver.Straight;
        if (absoluteAngle < slight)
            return signedAngle > 0f ? Maneuver.SlightRight : Maneuver.SlightLeft;
        if (absoluteAngle < turn)
            return signedAngle > 0f ? Maneuver.Right : Maneuver.Left;
        return Maneuver.UTurn;
    }

    public static string InstructionFor(Maneuver maneuver)
    {
        switch (maneuver)
        {
            case Maneuver.SlightLeft: return "Chếch trái";
            case Maneuver.SlightRight: return "Chếch phải";
            case Maneuver.Left: return "Rẽ trái";
            case Maneuver.Right: return "Rẽ phải";
            case Maneuver.UTurn: return "Quay lại";
            default: return "Đi thẳng";
        }
    }

#if UNITY_EDITOR
    public void Configure(
        NavMeshManager manager,
        NavMeshAgent agent,
        Transform areaTarget,
        Transform arCamera,
        ObserverBehaviour observer,
        GuidanceHUD guidanceHud)
    {
        navMeshManager = manager;
        navigationAgent = agent;
        areaTargetTransform = areaTarget;
        arCameraTransform = arCamera;
        trackingObserver = observer;
        hud = guidanceHud;

        if (areaTargetTransform != null)
            areaTargetOriginalPosition = areaTargetTransform.position;
    }
#endif
}
