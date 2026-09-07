using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Displays a LineRenderer path as pooled floor arrows.
/// The assigned sprite must point to the right along its local X axis.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class NavigationArrowPathRenderer : MonoBehaviour
{
    [Header("Arrow Image")]
    [Tooltip("Assign a sprite whose arrow points to the right (+X).")]
    [SerializeField] Sprite arrowSprite;

    [Header("Arrow Layout")]
    [SerializeField, Min(0.1f)] float spacing = 0.8f;
    [SerializeField, Min(0.01f)] float arrowLength = 0.55f;
    [SerializeField, Min(0f)] float floorOffset = 0.035f;
    [SerializeField, Min(1)] int maximumArrowCount = 80;

    readonly List<SpriteRenderer> arrowPool = new List<SpriteRenderer>();
    readonly List<Vector3> pathPoints = new List<Vector3>();

    LineRenderer sourceLine;

    void Awake()
    {
        sourceLine = GetComponent<LineRenderer>();
    }

    void OnEnable()
    {
        sourceLine = GetComponent<LineRenderer>();
        sourceLine.forceRenderingOff = arrowSprite != null;
    }

    void OnValidate()
    {
        spacing = Mathf.Max(0.1f, spacing);
        arrowLength = Mathf.Max(0.01f, arrowLength);
        floorOffset = Mathf.Max(0f, floorOffset);
        maximumArrowCount = Mathf.Max(1, maximumArrowCount);
    }

    void LateUpdate()
    {
        // MuseumNavMeshManager updates the path during Update(). Reading it in
        // LateUpdate ensures the arrows use the latest LineRenderer positions.
        // Restore the original line when no sprite is assigned, including when
        // the sprite is cleared from the Inspector during Play Mode.
        if (sourceLine != null)
            sourceLine.forceRenderingOff = arrowSprite != null;

        if (sourceLine == null || arrowSprite == null || !sourceLine.enabled
            || sourceLine.positionCount < 2)
        {
            SetVisibleArrowCount(0);
            return;
        }

        ReadWorldPath();
        LayoutArrows();
    }

    void OnDisable()
    {
        SetVisibleArrowCount(0);

        if (sourceLine != null)
            sourceLine.forceRenderingOff = false;
    }

    void OnDestroy()
    {
        if (sourceLine != null)
            sourceLine.forceRenderingOff = false;
    }

    void ReadWorldPath()
    {
        pathPoints.Clear();

        for (var index = 0; index < sourceLine.positionCount; index++)
        {
            var point = sourceLine.GetPosition(index);

            // Convert local positions when a LineRenderer does not use world space.
            if (!sourceLine.useWorldSpace)
                point = sourceLine.transform.TransformPoint(point);

            pathPoints.Add(point);
        }
    }

    void LayoutArrows()
    {
        var arrowIndex = 0;
        var distanceUntilNextArrow = spacing * 0.5f;

        for (var segmentIndex = 0;
             segmentIndex < pathPoints.Count - 1 && arrowIndex < maximumArrowCount;
             segmentIndex++)
        {
            var start = pathPoints[segmentIndex];
            var end = pathPoints[segmentIndex + 1];
            var segment = end - start;
            var segmentLength = segment.magnitude;

            if (segmentLength < 0.001f)
                continue;

            var segmentDirection = segment / segmentLength;
            var floorDirection = Vector3.ProjectOnPlane(segmentDirection, Vector3.up);

            if (floorDirection.sqrMagnitude < 0.001f)
                continue;

            floorDirection.Normalize();

            while (distanceUntilNextArrow <= segmentLength && arrowIndex < maximumArrowCount)
            {
                var arrow = GetArrow(arrowIndex++);

                // Distribute arrows at a constant distance along every path segment.
                arrow.transform.position = start
                                           + segmentDirection * distanceUntilNextArrow
                                           + Vector3.up * floorOffset;

                // The source image points right. Align its local X axis with the
                // route direction while keeping the sprite flat on the floor.
                var spriteUp = Vector3.Cross(Vector3.up, floorDirection);
                arrow.transform.rotation = Quaternion.LookRotation(Vector3.up, spriteUp);
                arrow.transform.localScale = Vector3.one * GetSpriteScale();

                distanceUntilNextArrow += spacing;
            }

            // Continue the same spacing across corners instead of restarting it.
            distanceUntilNextArrow -= segmentLength;
        }

        SetVisibleArrowCount(arrowIndex);
    }

    SpriteRenderer GetArrow(int index)
    {
        if (index < arrowPool.Count)
        {
            var existingArrow = arrowPool[index];
            existingArrow.sprite = arrowSprite;
            existingArrow.gameObject.SetActive(true);
            return existingArrow;
        }

        var arrowObject = new GameObject("NavigationArrow_" + index);
        arrowObject.transform.SetParent(transform, false);

        var arrowRenderer = arrowObject.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = arrowSprite;
        arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        arrowRenderer.receiveShadows = false;
        arrowRenderer.sortingOrder = 10;

        arrowPool.Add(arrowRenderer);
        return arrowRenderer;
    }

    float GetSpriteScale()
    {
        // Preserve the image aspect ratio and express arrowLength in world units.
        var spriteWidth = arrowSprite.bounds.size.x;
        return spriteWidth > 0.0001f ? arrowLength / spriteWidth : 1f;
    }

    void SetVisibleArrowCount(int visibleCount)
    {
        for (var index = 0; index < arrowPool.Count; index++)
            arrowPool[index].gameObject.SetActive(index < visibleCount);
    }
}
