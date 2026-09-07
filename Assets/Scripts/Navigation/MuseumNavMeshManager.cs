using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Project-owned NavMesh bridge between Vuforia world space and the static
/// coordinate space used to bake the museum NavMesh.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuseumNavMeshManager : MonoBehaviour
{
    [Header("Area Target Position")]
    [SerializeField] Transform areaTargetTransform;

    [Header("Navigation Agent and AR Camera")]
    [SerializeField] Transform arCameraTransform;
    [SerializeField] NavMeshAgent navigationAgent;

    [Header("Navigation Line")]
    [SerializeField] LineRenderer navigationLine;

    [Header("NavMesh Position Sync")]
    [SerializeField, Min(0.1f)] float navMeshSampleDistance = 2f;

    const int CornerBufferSize = 64;
    const float DestinationLineHideDistance = 1.5f;

    readonly Vector3[] cornerBuffer = new Vector3[CornerBufferSize];

    Vector3 areaTargetOriginalPosition;
    Vector3? currentDestination;
    bool hasFoundAreaTarget;

    public Transform AreaTargetTransform => areaTargetTransform;
    public Transform ArCameraTransform => arCameraTransform;
    public NavMeshAgent NavigationAgent => navigationAgent;
    public LineRenderer NavigationLine => navigationLine;
    public float NavMeshSampleDistance => navMeshSampleDistance;

    void Awake()
    {
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);

        if (!HasRequiredReferences())
        {
            Debug.LogError("MuseumNavMeshManager is missing Area Target, AR Camera or NavMeshAgent.", this);
            enabled = false;
            return;
        }

        areaTargetOriginalPosition = areaTargetTransform.position;
        navigationAgent.updatePosition = false;
        navigationAgent.updateRotation = false;
    }

    void OnValidate()
    {
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
    }

    void Update()
    {
        if (!UpdateNavigationAgentPosition())
        {
            SetLineVisible(false);
            return;
        }

        UpdateNavigationLineVisibility();
        UpdateNavigationLinePath();
    }

    public bool TryNavigateTo(Transform destinationTransform)
    {
        ClearNavigation();

        if (destinationTransform == null || !UpdateNavigationAgentPosition())
        {
            Debug.LogWarning("Navigation failed: camera position is not near the NavMesh.", this);
            return false;
        }

        var localDestination = areaTargetTransform.InverseTransformPoint(destinationTransform.position);
        var requestedDestination = localDestination + areaTargetOriginalPosition;

        if (!NavMesh.SamplePosition(
                requestedDestination,
                out var destinationHit,
                navMeshSampleDistance,
                navigationAgent.areaMask))
        {
            Debug.LogWarning($"Navigation failed: destination '{destinationTransform.name}' is not near the NavMesh.", this);
            return false;
        }

        currentDestination = destinationHit.position;
        if (navigationAgent.isOnNavMesh)
            navigationAgent.ResetPath();

        if (!navigationAgent.isOnNavMesh || !navigationAgent.SetDestination(currentDestination.Value))
        {
            Debug.LogWarning("Navigation failed: NavMeshAgent could not calculate a path.", this);
            currentDestination = null;
            return false;
        }

        return true;
    }

    public void ClearNavigation()
    {
        currentDestination = null;

        if (navigationAgent != null && navigationAgent.isActiveAndEnabled && navigationAgent.isOnNavMesh)
            navigationAgent.ResetPath();

        if (navigationLine != null)
        {
            navigationLine.enabled = false;
            navigationLine.positionCount = 0;
        }
    }

    public void OnAreaTargetFound()
    {
        hasFoundAreaTarget = true;
    }

    public void OnAreaTargetLost()
    {
        hasFoundAreaTarget = false;
        SetLineVisible(false);
    }

    bool HasRequiredReferences()
    {
        return areaTargetTransform != null && arCameraTransform != null && navigationAgent != null;
    }

    bool UpdateNavigationAgentPosition()
    {
        if (!HasRequiredReferences() || !navigationAgent.isActiveAndEnabled)
            return false;

        var cameraInAreaTarget = areaTargetTransform.InverseTransformPoint(arCameraTransform.position);
        var requestedPosition = cameraInAreaTarget + areaTargetOriginalPosition;

        if (!NavMesh.SamplePosition(
                requestedPosition,
                out var hit,
                navMeshSampleDistance,
                navigationAgent.areaMask))
        {
            return false;
        }

        navigationAgent.transform.position = hit.position;
        navigationAgent.nextPosition = hit.position;

        if (!navigationAgent.isOnNavMesh && !navigationAgent.Warp(hit.position))
            return false;

        return navigationAgent.isOnNavMesh;
    }

    void UpdateNavigationLineVisibility()
    {
        if (navigationLine == null)
            return;

        var shouldShow = hasFoundAreaTarget && currentDestination.HasValue;
        if (shouldShow && navigationAgent != null)
        {
            shouldShow = Vector3.Distance(navigationAgent.transform.position, currentDestination.Value)
                         >= DestinationLineHideDistance;
        }

        SetLineVisible(shouldShow);
    }

    void UpdateNavigationLinePath()
    {
        if (navigationLine == null || !navigationLine.enabled || navigationAgent == null
            || navigationAgent.pathPending || areaTargetTransform == null)
        {
            return;
        }

        var cornerCount = navigationAgent.path.GetCornersNonAlloc(cornerBuffer);
        navigationLine.positionCount = cornerCount;

        for (var index = 0; index < cornerCount; index++)
        {
            var worldCorner = areaTargetTransform.TransformPoint(
                cornerBuffer[index] - areaTargetOriginalPosition);
            navigationLine.SetPosition(index, worldCorner);
        }
    }

    void SetLineVisible(bool visible)
    {
        if (navigationLine != null)
            navigationLine.enabled = visible;
    }

#if UNITY_EDITOR
    public void Configure(
        Transform areaTarget,
        Transform arCamera,
        NavMeshAgent agent,
        LineRenderer line,
        float sampleDistance)
    {
        areaTargetTransform = areaTarget;
        arCameraTransform = arCamera;
        navigationAgent = agent;
        navigationLine = line;
        navMeshSampleDistance = Mathf.Max(0.1f, sampleDistance);
        areaTargetOriginalPosition = areaTarget != null ? areaTarget.position : Vector3.zero;
    }
#endif
}
