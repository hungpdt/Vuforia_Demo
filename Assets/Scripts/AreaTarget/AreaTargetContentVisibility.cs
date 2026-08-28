/*==============================================================================
Shows only the augmentations of the room the AR camera is physically standing in,
so content from an adjacent room is not visible through the wall.

Why not use the Area Target's tracking status: once a target has been localized,
it keeps reporting EXTENDED_TRACKED while out of sight, because device tracking
still knows where it is. Tracking status tells you what the engine knows, not
where the user stands, so it cannot separate two rooms.

Instead each room is given one or more trigger volumes matching its floor area,
and its content is shown only while the AR camera is inside one of them.

Setup:
  - Put each room's augmentations under one group GameObject (e.g. "Room1_Content")
    and register it as that room's content below.
  - For each room, create a GameObject with a BoxCollider (isTrigger = true, no
    Rigidbody) scaled to cover that room, parent it to the room's Area Target, and
    register it as a volume. Several volumes per room are fine for L-shaped spaces.
    Do not register the same space twice: overlapping volumes show both rooms.
  - Add this component to the MultiArea root and fill in one Room entry per
    Area Target.
  - If MultiArea.cs is present, set its hideAugmentationsWhenNotTracked to FALSE.
  - Remove or disable DefaultAreaTargetEventHandler on the Area Targets. It toggles
    Renderer.enabled on its children, and once MultiArea re-parents the content out
    of its reach it can leave the renderers permanently off.

A room with no volumes registered falls back to its Area Target's tracking status,
which is the old behaviour and cannot tell adjacent rooms apart.
==============================================================================*/
using System;
using UnityEngine;
using Vuforia;

public class AreaTargetContentVisibility : MonoBehaviour
{
    [Serializable]
    public class Room
    {
        [Tooltip("Only for readability in the Inspector.")]
        public string label;

        public AreaTargetBehaviour areaTarget;

        [Tooltip("Group GameObject holding this room's augmentations.")]
        public GameObject content;

        [Tooltip("Trigger volumes covering this room. The content is shown while the " +
                 "AR camera is inside any of them. Leave empty to fall back to the " +
                 "Area Target's tracking status.")]
        public Collider[] volumes;
    }

    public Room[] rooms;

    [Tooltip("Seconds a room stays visible after the camera leaves its volume. " +
             "Stops the content flickering when standing on a boundary.")]
    public float holdSeconds = 1f;

    [Tooltip("Hide every room while no Area Target is tracked at all, since the poses " +
             "are meaningless until the user has localized somewhere.")]
    public bool hideWhenNothingTracked = true;

    [Tooltip("Log every visibility change. Useful when testing with a Session Recording.")]
    public bool logChanges = false;

    [Tooltip("Re-enable child Renderers when a room is shown, in case something else " +
             "disabled them.")]
    public bool forceRenderersEnabled = true;

    float[] m_LastInsideTime;

    void Start()
    {
        m_LastInsideTime = new float[rooms.Length];
        for (int i = 0; i < rooms.Length; i++)
        {
            m_LastInsideTime[i] = float.NegativeInfinity;
            Show(i, false);
        }
    }

    void Update()
    {
        if (!VuforiaApplication.Instance.IsRunning)
            return;

        var arCamera = VuforiaBehaviour.Instance ? VuforiaBehaviour.Instance.GetComponent<Camera>() : null;
        if (!arCamera)
            return;

        if (hideWhenNothingTracked && !AnyRoomTracked())
        {
            for (int i = 0; i < rooms.Length; i++)
                Show(i, false);
            return;
        }

        var cameraPosition = arCamera.transform.position;

        for (int i = 0; i < rooms.Length; i++)
        {
            var room = rooms[i];
            if (room == null || !room.content)
                continue;

            if (IsUserInRoom(room, cameraPosition))
                m_LastInsideTime[i] = Time.time;

            Show(i, Time.time - m_LastInsideTime[i] <= holdSeconds);
        }
    }

    bool AnyRoomTracked()
    {
        foreach (var room in rooms)
        {
            if (room != null && room.areaTarget && IsTracked(room.areaTarget))
                return true;
        }
        return false;
    }

    static bool IsUserInRoom(Room room, Vector3 cameraPosition)
    {
        // No volumes set up: fall back to tracking status. This cannot separate
        // adjacent rooms, because a room out of sight stays EXTENDED_TRACKED.
        if (room.volumes == null || room.volumes.Length == 0)
            return room.areaTarget && IsTracked(room.areaTarget);

        foreach (var volume in room.volumes)
        {
            if (!volume || !volume.enabled)
                continue;

            // ClosestPoint returns the point itself when it is inside the collider,
            // and unlike bounds.Contains it respects the collider's rotation.
            if ((volume.ClosestPoint(cameraPosition) - cameraPosition).sqrMagnitude < 1e-6f)
                return true;
        }
        return false;
    }

    static bool IsTracked(AreaTargetBehaviour areaTarget)
    {
        if (!areaTarget.enabled)
            return false;

        var status = areaTarget.TargetStatus.Status;
        return status == Status.TRACKED || status == Status.EXTENDED_TRACKED;
    }

    void Show(int index, bool show)
    {
        var content = rooms[index].content;
        if (!content || content.activeSelf == show)
            return;

        content.SetActive(show);

        if (show && forceRenderersEnabled)
        {
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                renderer.enabled = true;
            }
        }

        if (logChanges)
        {
            var name = string.IsNullOrEmpty(rooms[index].label) ? content.name : rooms[index].label;
            Debug.LogFormat("{0}: content {1}", name, show ? "shown" : "hidden");
        }
    }

    void OnDrawGizmosSelected()
    {
        if (rooms == null)
            return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        foreach (var room in rooms)
        {
            if (room?.volumes == null)
                continue;

            foreach (var volume in room.volumes)
            {
                if (volume)
                    Gizmos.DrawCube(volume.bounds.center, volume.bounds.size);
            }
        }
    }
}
