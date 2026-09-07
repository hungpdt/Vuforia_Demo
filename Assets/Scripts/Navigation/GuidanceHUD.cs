using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Null-safe screen-space HUD for turn guidance and navigation states.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class GuidanceHUD : MonoBehaviour
{
    [SerializeField] CanvasGroup root;

    [Header("Arrow")]
    [SerializeField] Image arrowImage;
    [SerializeField] bool invertLeftAndRight;
    [SerializeField, Range(-180f, 180f)] float rotationOffset;

    [Header("Text")]
    [SerializeField] TMP_Text instructionText;
    [SerializeField] TMP_Text distanceText;
    [SerializeField] TMP_Text destinationText;

    public bool IsVisible => root != null && root.alpha > 0.01f;

    void Awake()
    {
        if (root == null)
            root = GetComponent<CanvasGroup>();

        Hide();
    }

    void OnValidate()
    {
        if (root == null)
            root = GetComponent<CanvasGroup>();

    }

    public void Show(
        TurnGuidanceController.Maneuver maneuver,
        string instruction,
        string destinationName,
        float distanceMeters = -1f)
    {
        SetVisible(true);

        if (arrowImage != null)
        {
            arrowImage.enabled = arrowImage.sprite != null;
            var rotation = RotationFor(maneuver);
            if (invertLeftAndRight && maneuver != TurnGuidanceController.Maneuver.UTurn)
                rotation = -rotation;
            arrowImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation + rotationOffset);
        }

        var formattedDistance = distanceMeters >= 0f ? FormatDistance(distanceMeters) : string.Empty;
        var safeInstruction = string.IsNullOrWhiteSpace(instruction) ? "Đang cập nhật..." : instruction;

        if (instructionText != null)
            instructionText.text = distanceText == null && formattedDistance.Length > 0
                ? safeInstruction + " • " + formattedDistance
                : safeInstruction;

        if (distanceText != null)
            distanceText.text = formattedDistance;

        if (destinationText != null)
            destinationText.text = string.IsNullOrWhiteSpace(destinationName)
                ? string.Empty
                : "Điểm đến: " + destinationName;
    }

    public void ShowMessage(string message, string destinationName = "")
    {
        SetVisible(true);

        if (arrowImage != null)
            arrowImage.enabled = false;

        if (instructionText != null)
            instructionText.text = string.IsNullOrWhiteSpace(message) ? "Đang cập nhật..." : message;

        if (distanceText != null)
            distanceText.text = string.Empty;

        if (destinationText != null)
            destinationText.text = string.IsNullOrWhiteSpace(destinationName)
                ? string.Empty
                : "Điểm đến: " + destinationName;
    }

    public void Hide()
    {
        SetVisible(false);
    }

    void SetVisible(bool visible)
    {
        if (root == null)
            return;

        root.alpha = visible ? 1f : 0f;
        root.interactable = false;
        root.blocksRaycasts = false;
    }

    static float RotationFor(TurnGuidanceController.Maneuver maneuver)
    {
        switch (maneuver)
        {
            case TurnGuidanceController.Maneuver.SlightLeft: return 45f;
            case TurnGuidanceController.Maneuver.Left: return 90f;
            case TurnGuidanceController.Maneuver.SlightRight: return -45f;
            case TurnGuidanceController.Maneuver.Right: return -90f;
            case TurnGuidanceController.Maneuver.UTurn: return 180f;
            default: return 0f;
        }
    }

    static string FormatDistance(float distanceMeters)
    {
        if (float.IsNaN(distanceMeters) || float.IsInfinity(distanceMeters) || distanceMeters < 0f)
            return string.Empty;

        if (distanceMeters >= 1000f)
            return $"sau {distanceMeters / 1000f:0.#} km";

        return $"sau {Mathf.Max(1, Mathf.CeilToInt(distanceMeters))} m";
    }

#if UNITY_EDITOR
    public void Configure(
        CanvasGroup canvasGroup,
        Image arrow,
        TMP_Text instruction,
        TMP_Text distance,
        TMP_Text destination)
    {
        root = canvasGroup;
        arrowImage = arrow;
        instructionText = instruction;
        distanceText = distance;
        destinationText = destination;
    }
#endif
}
