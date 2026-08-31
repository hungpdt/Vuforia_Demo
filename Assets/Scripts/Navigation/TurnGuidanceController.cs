using UnityEngine;
using UnityEngine.AI;
using Vuforia;

/// <summary>
/// Owns the navigation state for the current POI and converts a NavMesh path
/// into stable, screen-space turn guidance.
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

    public enum NavigationState
    {
        Idle,
        Calculating,
        Navigating,
        TrackingLost,
        NoRoute,
        Arrived
    }

    [Header("Navigation references")]
    [SerializeField] NavMeshManager navMeshManager;
    [SerializeField] NavMeshAgent navigationAgent;
    [SerializeField] Transform areaTargetTransform;
    [SerializeField] Transform arCameraTransform;
    [SerializeField] ObserverBehaviour trackingObserver;
    [SerializeField] GuidanceHUD hud;

    [Header("Guidance tuning")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.25f;
    [SerializeField, Min(0.01f)] float minimumCornerDistance = 0.25f;
    [SerializeField, Range(0f, 90f)] float straightAngle = 25f;
    [SerializeField, Range(1f, 120f)] float slightTurnAngle = 65f;
    [SerializeField, Range(1f, 179f)] float turnAngle = 135f;
    [SerializeField, Min(0f)] float maneuverConfirmationTime = 0.45f;

    [Header("Route recovery")]
    [SerializeField, Min(0.1f)] float deviationDistance = 1.25f;
    [SerializeField, Min(0f)] float deviationConfirmationTime = 1f;
    [SerializeField, Min(0f)] float rerouteCooldown = 2f;
    [SerializeField, Min(0.1f)] float pathCalculationTimeout = 3f;

    [Header("Tracking and arrival")]
    [SerializeField, Min(0f)] float trackingLostDelay = 0.75f;
    [SerializeField, Min(0f)] float trackingRecoveryDelay = 0.5f;
    [SerializeField, Min(0f)] float arrivalConfirmationTime = 0.5f;
    [SerializeField] bool assumeTrackingInEditor = true;

    const int CornerBufferSize = 64;

    readonly Vector3[] cornerBuffer = new Vector3[CornerBufferSize];

    POIDestination currentDestination;
    Vector3 areaTargetOriginalPosition;
    NavigationState state = NavigationState.Idle;
    Maneuver stableManeuver;
    Maneuver candidateManeuver;
    float nextRefreshTime;
    float pathRequestTime;
    float nextAllowedRerouteTime;
    float deviationStartedTime = -1f;
    float arrivalStartedTime = -1f;
    float candidateManeuverStartedTime;
    float rawTrackingChangedTime;
    bool hasStableManeuver;
    bool hasCandidateManeuver;
    bool rawTracked;
    bool isTracked;

    public POIDestination CurrentDestination => currentDestination;
    public NavigationState State => state;
    public bool IsTracked => isTracked;

    void Awake()
    {
        CacheAreaTargetOrigin();

        if (Application.isEditor && assumeTrackingInEditor)
        {
            rawTracked = true;
            isTracked = true;
        }
    }

    void OnEnable()
    {
        if (trackingObserver != null)
        {
            trackingObserver.OnTargetStatusChanged += OnTargetStatusChanged;
            SetRawTracking(IsUsableTracking(trackingObserver.TargetStatus.Status), true);
        }
        else if (!(Application.isEditor && assumeTrackingInEditor))
        {
            SetRawTracking(false, true);
        }
    }

    void OnDisable()
    {
        if (trackingObserver != null)
            trackingObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void Update()
    {
        var now = Time.unscaledTime;
        UpdateStableTracking(now);

        if (now < nextRefreshTime)
            return;

        nextRefreshTime = now + refreshInterval;
        RefreshGuidance(now);
    }

    public void SetDestination(POIDestination destination)
    {
        if (destination == null || !destination.IsNavigationEnabled || destination.Anchor == null)
        {
            hud?.ShowMessage("Điểm đến không khả dụng");
            return;
        }

        if (!HasRequiredNavigationReferences())
        {
            hud?.ShowMessage("Navigation chưa được cấu hình", destination.DisplayName);
            return;
        }

        currentDestination = destination;
        ResetTransientState(true);

        if (!isTracked)
        {
            SetState(NavigationState.TrackingLost);
            hud?.ShowMessage("Không xác định được vị trí", destination.DisplayName);
            return;
        }

        RequestRoute(false);
    }

    public void ClearDestination()
    {
        currentDestination = null;
        ResetTransientState(true);
        navMeshManager?.ClearNavigation();
        SetState(NavigationState.Idle);
        hud?.Hide();
    }

    void RefreshGuidance(float now)
    {
        if (currentDestination == null)
        {
            SetState(NavigationState.Idle);
            hud?.Hide();
            return;
        }

        if (!isTracked)
        {
            SetState(NavigationState.TrackingLost);
            hud?.ShowMessage("Không xác định được vị trí", currentDestination.DisplayName);
            return;
        }

        if (!HasRequiredNavigationReferences())
        {
            hud?.ShowMessage("Navigation chưa được cấu hình", currentDestination.DisplayName);
            return;
        }

        if (state == NavigationState.Arrived)
        {
            hud?.ShowMessage("Đã đến nơi", currentDestination.DisplayName);
            return;
        }

        if (ConfirmArrival(now))
        {
            SetState(NavigationState.Arrived);
            navMeshManager.ClearNavigation();
            hud?.ShowMessage("Đã đến nơi", currentDestination.DisplayName);
            return;
        }

        if (state == NavigationState.NoRoute)
        {
            hud?.ShowMessage("Không tìm thấy đường", currentDestination.DisplayName);
            return;
        }

        if (!navigationAgent.isOnNavMesh)
        {
            SetNoRoute();
            return;
        }

        if (navigationAgent.pathPending)
        {
            SetState(NavigationState.Calculating);
            if (now - pathRequestTime >= pathCalculationTimeout)
                SetNoRoute();
            else
                hud?.ShowMessage("Đang tính đường...", currentDestination.DisplayName);
            return;
        }

        var path = navigationAgent.path;
        if (path == null || path.status != NavMeshPathStatus.PathComplete)
        {
            SetNoRoute();
            return;
        }

        var cornerCount = path.GetCornersNonAlloc(cornerBuffer);
        if (cornerCount < 2)
        {
            SetNoRoute();
            return;
        }

        SetState(NavigationState.Navigating);

        if (ShouldReroute(cornerBuffer, cornerCount, now))
        {
            RequestRoute(true);
            return;
        }

        if (!TryGetNextWorldCorner(cornerBuffer, cornerCount, out var nextCorner, out var nextCornerIndex))
        {
            hud?.Show(Maneuver.Straight, "Đi thẳng tới điểm đến", currentDestination.DisplayName,
                CalculateRemainingDistance(cornerBuffer, cornerCount));
            return;
        }

        var cameraForward = Vector3.ProjectOnPlane(arCameraTransform.forward, Vector3.up).normalized;
        var routeDirection = Vector3.ProjectOnPlane(nextCorner - arCameraTransform.position, Vector3.up).normalized;
        if (cameraForward.sqrMagnitude < 0.001f || routeDirection.sqrMagnitude < 0.001f)
            return;

        var signedAngle = Vector3.SignedAngle(cameraForward, routeDirection, Vector3.up);
        var rawManeuver = ClassifyAngle(signedAngle, straightAngle, slightTurnAngle, turnAngle);
        var maneuver = StabilizeManeuver(rawManeuver, now);
        var distanceToTurn = CalculateDistanceToCorner(cornerBuffer, cornerCount, nextCornerIndex);
        hud?.Show(maneuver, InstructionFor(maneuver), currentDestination.DisplayName, distanceToTurn);
    }

    void RequestRoute(bool isReroute)
    {
        if (currentDestination == null || currentDestination.Anchor == null || navMeshManager == null)
        {
            SetNoRoute();
            return;
        }

        ResetRouteDetection();
        SetState(NavigationState.Calculating);
        pathRequestTime = Time.unscaledTime;

        if (isReroute)
            nextAllowedRerouteTime = pathRequestTime + rerouteCooldown;
        else
            ResetManeuverFilter();

        hud?.ShowMessage(isReroute ? "Đang cập nhật đường..." : "Đang tính đường...",
            currentDestination.DisplayName);

        if (!navMeshManager.TryNavigateTo(currentDestination.Anchor))
            SetNoRoute();
    }

    void SetNoRoute()
    {
        SetState(NavigationState.NoRoute);
        ResetRouteDetection();
        navMeshManager?.ClearNavigation();
        hud?.ShowMessage("Không tìm thấy đường", currentDestination != null ? currentDestination.DisplayName : "");
    }

    bool ConfirmArrival(float now)
    {
        if (currentDestination == null || currentDestination.Anchor == null || arCameraTransform == null)
            return false;

        var offset = Vector3.ProjectOnPlane(
            currentDestination.Anchor.position - arCameraTransform.position,
            Vector3.up);

        if (offset.magnitude > currentDestination.ArrivalRadius)
        {
            arrivalStartedTime = -1f;
            return false;
        }

        if (arrivalStartedTime < 0f)
            arrivalStartedTime = now;

        return now - arrivalStartedTime >= arrivalConfirmationTime;
    }

    bool ShouldReroute(Vector3[] corners, int cornerCount, float now)
    {
        if (now < nextAllowedRerouteTime || navigationAgent == null)
        {
            deviationStartedTime = -1f;
            return false;
        }

        var distance = DistanceToPath(navigationAgent.transform.position, corners, cornerCount);
        if (distance <= deviationDistance)
        {
            deviationStartedTime = -1f;
            return false;
        }

        if (deviationStartedTime < 0f)
            deviationStartedTime = now;

        return now - deviationStartedTime >= deviationConfirmationTime;
    }

    bool TryGetNextWorldCorner(
        Vector3[] corners,
        int cornerCount,
        out Vector3 worldCorner,
        out int cornerIndex)
    {
        if (corners == null || areaTargetTransform == null || arCameraTransform == null)
        {
            worldCorner = default;
            cornerIndex = -1;
            return false;
        }

        for (var i = 0; i < cornerCount; i++)
        {
            var candidate = areaTargetTransform.TransformPoint(corners[i] - areaTargetOriginalPosition);
            var horizontalDistance = Vector3.ProjectOnPlane(
                candidate - arCameraTransform.position,
                Vector3.up).magnitude;

            if (horizontalDistance < minimumCornerDistance)
                continue;

            worldCorner = candidate;
            cornerIndex = i;
            return true;
        }

        worldCorner = default;
        cornerIndex = -1;
        return false;
    }

    float CalculateDistanceToCorner(Vector3[] corners, int cornerCount, int targetCornerIndex)
    {
        if (navigationAgent == null || corners == null || cornerCount == 0 || targetCornerIndex < 0)
            return 0f;

        var lastPoint = navigationAgent.transform.position;
        var distance = 0f;
        var finalIndex = Mathf.Min(targetCornerIndex, cornerCount - 1);

        for (var i = 0; i <= finalIndex; i++)
        {
            distance += Vector3.Distance(lastPoint, corners[i]);
            lastPoint = corners[i];
        }

        return distance;
    }

    float CalculateRemainingDistance(Vector3[] corners, int cornerCount)
    {
        return CalculateDistanceToCorner(corners, cornerCount, cornerCount - 1);
    }

    Maneuver StabilizeManeuver(Maneuver rawManeuver, float now)
    {
        if (!hasStableManeuver)
        {
            stableManeuver = rawManeuver;
            hasStableManeuver = true;
            hasCandidateManeuver = false;
            return stableManeuver;
        }

        if (rawManeuver == stableManeuver)
        {
            hasCandidateManeuver = false;
            return stableManeuver;
        }

        if (!hasCandidateManeuver || candidateManeuver != rawManeuver)
        {
            candidateManeuver = rawManeuver;
            candidateManeuverStartedTime = now;
            hasCandidateManeuver = true;
            return stableManeuver;
        }

        if (now - candidateManeuverStartedTime >= maneuverConfirmationTime)
        {
            stableManeuver = candidateManeuver;
            hasCandidateManeuver = false;
        }

        return stableManeuver;
    }

    void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        SetRawTracking(IsUsableTracking(status.Status), false);
    }

    void SetRawTracking(bool tracked, bool initialize)
    {
        if (Application.isEditor && assumeTrackingInEditor)
            tracked = true;

        if (!initialize && rawTracked == tracked)
            return;

        rawTracked = tracked;
        rawTrackingChangedTime = Time.unscaledTime;

        if (initialize && tracked)
            isTracked = true;
    }

    void UpdateStableTracking(float now)
    {
        if (rawTracked == isTracked)
            return;

        var requiredDelay = rawTracked ? trackingRecoveryDelay : trackingLostDelay;
        if (now - rawTrackingChangedTime < requiredDelay)
            return;

        isTracked = rawTracked;
        ResetRouteDetection();

        if (!isTracked)
        {
            SetState(currentDestination == null ? NavigationState.Idle : NavigationState.TrackingLost);
            if (currentDestination != null)
                hud?.ShowMessage("Không xác định được vị trí", currentDestination.DisplayName);
            return;
        }

        if (currentDestination != null && state != NavigationState.Arrived)
            RequestRoute(true);
    }

    bool HasRequiredNavigationReferences()
    {
        return navMeshManager != null
               && navigationAgent != null
               && areaTargetTransform != null
               && arCameraTransform != null;
    }

    void CacheAreaTargetOrigin()
    {
        if (areaTargetTransform != null)
            areaTargetOriginalPosition = areaTargetTransform.position;
    }

    void ResetTransientState(bool resetRerouteCooldown)
    {
        ResetManeuverFilter();
        ResetRouteDetection();
        arrivalStartedTime = -1f;
        pathRequestTime = 0f;
        if (resetRerouteCooldown)
            nextAllowedRerouteTime = 0f;
    }

    void ResetManeuverFilter()
    {
        hasStableManeuver = false;
        hasCandidateManeuver = false;
    }

    void ResetRouteDetection()
    {
        deviationStartedTime = -1f;
    }

    void SetState(NavigationState nextState)
    {
        state = nextState;
    }

    static bool IsUsableTracking(Status status)
    {
        return status == Status.TRACKED || status == Status.EXTENDED_TRACKED;
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

    public static float DistanceToPath(Vector3 point, Vector3[] corners, int cornerCount)
    {
        if (corners == null || cornerCount <= 0)
            return float.PositiveInfinity;

        var validCount = Mathf.Min(cornerCount, corners.Length);
        if (validCount == 1)
            return Vector3.Distance(point, corners[0]);

        var nearestSqrDistance = float.PositiveInfinity;
        for (var i = 0; i < validCount - 1; i++)
        {
            var start = corners[i];
            var segment = corners[i + 1] - start;
            var segmentSqrLength = segment.sqrMagnitude;
            var t = segmentSqrLength > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / segmentSqrLength)
                : 0f;
            var nearestPoint = start + segment * t;
            nearestSqrDistance = Mathf.Min(nearestSqrDistance, (point - nearestPoint).sqrMagnitude);
        }

        return Mathf.Sqrt(nearestSqrDistance);
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
        CacheAreaTargetOrigin();
    }
#endif
}
