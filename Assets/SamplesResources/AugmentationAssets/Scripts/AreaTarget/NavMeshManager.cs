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

    bool mHasFoundAreaTarget;

    const float DISTANCE_THRESHOLD = 1.5f;

    void Awake()
    {
        mAreaTargetOriginalPosition = AreaTargetTransform.transform.position;

        // The camera drives this agent. Do not let NavMeshAgent write its own
        // simulated position back over the position supplied by AR tracking.
        NavigationAgent.updatePosition = false;
        NavigationAgent.updateRotation = false;
    }

    void Update()
    {
        UpdateNavigationAgentPosition();
        UpdateNavigationLineVisibility();
        UpdateNavigationLinePath();
    }

    bool UpdateNavigationAgentPosition()
    {
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
        if (NavigationLine.enabled && !NavigationAgent.pathPending)
        {
            DrawPath();
        }
    }

    public void NavigateTo(Transform destinationTransform)
    {
        if (destinationTransform == null || !UpdateNavigationAgentPosition())
        {
            Debug.LogWarning("Navigation failed: current camera position is not near the NavMesh.");
            return;
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
            return;
        }

        mCurrentDestination = destinationHit.position;

        if (NavigationAgent.isOnNavMesh)
            NavigationAgent.ResetPath();

        if (!NavigationAgent.SetDestination(mCurrentDestination.Value))
            Debug.LogWarning("Navigation failed: NavMeshAgent could not calculate a path from its current position.");
    }

    void DrawPath()
    {
        NavigationLine.positionCount = NavigationAgent.path.corners.Length;
        // we have to transform the positions from the Static Navmesh space back to the Moving AreaTarget space
        var transformedNavigationCorners = new Vector3[NavigationLine.positionCount];
        for (int i = 0; i < transformedNavigationCorners.Length; i++)
            transformedNavigationCorners[i] = AreaTargetTransform.TransformPoint(NavigationAgent.path.corners[i] - mAreaTargetOriginalPosition);
        NavigationLine.SetPositions(transformedNavigationCorners);
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
