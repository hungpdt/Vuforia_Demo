using UnityEngine;

/// <summary>
/// Fits a full-screen RectTransform inside the current device safe area.
/// The component only reapplies anchors when the screen geometry changes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaFitter : MonoBehaviour
{
    RectTransform target;
    Rect lastSafeArea;
    Vector2Int lastScreenSize;
    ScreenOrientation lastOrientation;
    bool hasApplied;

    void Awake()
    {
        target = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        ApplyIfNeeded(true);
    }

    void Update()
    {
        ApplyIfNeeded(false);
    }

    void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled)
            ApplyIfNeeded(false);
    }

    void ApplyIfNeeded(bool force)
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        if (target == null || Screen.width <= 0 || Screen.height <= 0)
            return;

        var safeArea = ClampToScreen(Screen.safeArea, Screen.width, Screen.height);
        var screenSize = new Vector2Int(Screen.width, Screen.height);
        var orientation = Screen.orientation;

        if (!force && hasApplied && safeArea == lastSafeArea
            && screenSize == lastScreenSize && orientation == lastOrientation)
        {
            return;
        }

        var anchorMin = safeArea.position;
        var anchorMax = safeArea.position + safeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;
        lastOrientation = orientation;
        hasApplied = true;
    }

    public static Rect ClampToScreen(Rect safeArea, float screenWidth, float screenHeight)
    {
        if (screenWidth <= 0f || screenHeight <= 0f)
            return Rect.zero;

        var xMin = Mathf.Clamp(safeArea.xMin, 0f, screenWidth);
        var yMin = Mathf.Clamp(safeArea.yMin, 0f, screenHeight);
        var xMax = Mathf.Clamp(safeArea.xMax, xMin, screenWidth);
        var yMax = Mathf.Clamp(safeArea.yMax, yMin, screenHeight);

        // Some editor/device startup frames report an empty safe area. Filling
        // the screen is safer than collapsing the complete UI for that frame.
        if (xMax - xMin < 1f || yMax - yMin < 1f)
            return new Rect(0f, 0f, screenWidth, screenHeight);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}
