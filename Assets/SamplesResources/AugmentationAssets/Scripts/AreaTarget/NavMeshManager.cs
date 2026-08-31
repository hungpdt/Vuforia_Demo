/*========================================================================
Copyright (c) 2021 PTC Inc. All Rights Reserved.
 
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
=========================================================================*/

using UnityEngine;
using UnityEngine.AI;

public class NavMeshManager : MonoBehaviour
{
    [Header("Area Target Position")]
    public Transform AreaTargetTransform;
    
    [Header("Navigation Agent and its ARCamera")]
    public Transform ArCameraTransform;
    public NavMeshAgent NavigationAgent;
    
    [Header("Navigation Line")]
    public LineRenderer NavigationLine;

    [Header("NavMesh Position Sync")]
    [Tooltip("Maximum distance used to snap the camera position onto the baked NavMesh.")]
    [Min(0.1f)] public float NavMeshSampleDistance = 2f;
    
    Vector3 mAreaTargetOriginalPosition;
    Vector3? mCurrentDestination;

    const int CORNER_BUFFER_SIZE = 64;
    readonly Vector3[] mCornerBuffer = new Vector3[CORNER_BUFFER_SIZE];

    bool mHasFoundAreaTarget;

    const float DISTANCE_THRESHOLD = 1.5f;

    void Awake()
    {
        if (AreaTargetTransform == null || ArCameraTransform == null || NavigationAgent == null)
        {
            Debug.LogError("NavMeshManager is missing AreaTargetTransform, ArCameraTransform or NavigationAgent.", this);
            enabled = false;
            return;
        }

        mAreaTargetOriginalPosition = AreaTargetTransform.position;

        // The camera drives this agent. Do not let NavMeshAgent write its own
        // simulated position back over the position supplied by AR tracking.
        NavigationAgent.updatePosition = false;
        NavigationAgent.updateRotation = false;
    }

    void Update()
    {
        if (!UpdateNavigationAgentPosition())
        {
            if (NavigationLine != null)
                NavigationLine.enabled = false;
            return;
        }

        UpdateNavigationLineVisibility();
        UpdateNavigationLinePath();
    }

    bool UpdateNavigationAgentPosition()
    {
        if (AreaTargetTransform == null || ArCameraTransform == null || NavigationAgent == null)
            return false;

        // Convert the live AR camera position into the static coordinate space
        // where the NavMesh was baked.
        var arCamPositionInAreaTarget = AreaTargetTransform.InverseTransformPoint(ArCameraTransform.position);
        var requestedPosition = arCamPositionInAreaTarget + mAreaTargetOriginalPosition;

        // The camera is at eye height and can also be slightly outside the walkable
        // polygon, so always snap the requested point onto the NavMesh first.
        if (!NavMesh.SamplePosition(
                requestedPosition,
                out var hit,
                NavMeshSampleDistance,
                NavigationAgent.areaMask))
        {
            return false;
        }

        // hit.position is a world position in the static NavMesh coordinate space.
        // Using world position (not localPosition) prevents a parent transform such
        // as AR from being applied a second time.
        NavigationAgent.transform.position = hit.position;
        NavigationAgent.nextPosition = hit.position;
        return true;
    }
    
    void UpdateNavigationLineVisibility()
    {
        if (NavigationLine == null)
            return;

        NavigationLine.enabled = mHasFoundAreaTarget && mCurrentDestination.HasValue;

        if (NavigationLine.enabled)
        {
            bool isCloseToDestination =
                Vector3.Distance(NavigationAgent.transform.position, mCurrentDestination.Value) < DISTANCE_THRESHOLD;
            if (isCloseToDestination)
            {
                NavigationLine.enabled = false;
            }
        }
    }
    
    void UpdateNavigationLinePath()
    {
        if (NavigationLine != null && NavigationLine.enabled && NavigationAgent != null && !NavigationAgent.pathPending)
        {
            DrawPath();
        }
    }

    public void NavigateTo(Transform destinationTransform)
    {
        TryNavigateTo(destinationTransform);
    }

    public bool TryNavigateTo(Transform destinationTransform)
    {
        ClearNavigation();

        if (destinationTransform == null || !UpdateNavigationAgentPosition())
        {
            Debug.LogWarning("Navigation failed: current camera position is not near the NavMesh.");
            return false;
        }

        // Convert the moving destination into the static coordinate space where
        // the NavMesh was baked, then snap it onto a walkable polygon.
        var localPositionInAreaTarget = AreaTargetTransform.InverseTransformPoint(destinationTransform.position);
        var requestedDestination = localPositionInAreaTarget + mAreaTargetOriginalPosition;

        if (!NavMesh.SamplePosition(
                requestedDestination,
                out var destinationHit,
                NavMeshSampleDistance,
                NavigationAgent.areaMask))
        {
            Debug.LogWarning($"Navigation failed: destination '{destinationTransform.name}' is not near the NavMesh.");
            return false;
        }

        mCurrentDestination = destinationHit.position;

        if (NavigationAgent.isOnNavMesh)
            NavigationAgent.ResetPath();

        if (!NavigationAgent.SetDestination(mCurrentDestination.Value))
        {
            Debug.LogWarning("Navigation failed: NavMeshAgent could not calculate a path from its current position.");
            mCurrentDestination = null;
            return false;
        }

        return true;
    }

    public void ClearNavigation()
    {
        mCurrentDestination = null;

        if (NavigationAgent != null && NavigationAgent.isOnNavMesh)
            NavigationAgent.ResetPath();

        if (NavigationLine != null)
        {
            NavigationLine.enabled = false;
            NavigationLine.positionCount = 0;
        }
    }

    void DrawPath()
    {
        if (NavigationLine == null || NavigationAgent == null || AreaTargetTransform == null)
            return;

        var cornerCount = NavigationAgent.path.GetCornersNonAlloc(mCornerBuffer);
        NavigationLine.positionCount = cornerCount;

        // Transform from the static baked NavMesh space back to the moving Area Target space.
        for (var i = 0; i < cornerCount; i++)
        {
            var worldCorner = AreaTargetTransform.TransformPoint(mCornerBuffer[i] - mAreaTargetOriginalPosition);
            NavigationLine.SetPosition(i, worldCorner);
        }
    }
    
    public void OnAreaTargetFound()
    {
        mHasFoundAreaTarget = true; 
    }

    public void OnAreaTargetLost()
    {
        mHasFoundAreaTarget = false;
    }
}
