using TMPro;
using UnityEngine;

/// <summary>
/// Minimal screen-space HUD for the Day 1 navigation vertical slice.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuidanceHUD : MonoBehaviour
{
    [SerializeField] CanvasGroup root;
    [SerializeField] TMP_Text arrowText;
    [SerializeField] TMP_Text instructionText;
    [SerializeField] TMP_Text destinationText;

    public bool IsVisible => root != null && root.alpha > 0.01f;

    void Awake()
    {
        Hide();
    }

    public void Show(TurnGuidanceController.Maneuver maneuver, string instruction, string destinationName)
    {
        SetVisible(true);

        if (arrowText != null)
        {
            arrowText.text = "\u2191";
            arrowText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, RotationFor(maneuver));
        }

        if (instructionText != null)
            instructionText.text = instruction;

        if (destinationText != null)
            destinationText.text = string.IsNullOrWhiteSpace(destinationName)
                ? string.Empty
                : "Điểm đến: " + destinationName;
    }

    public void ShowMessage(string message, string destinationName = "")
    {
        SetVisible(true);

        if (arrowText != null)
            arrowText.text = string.Empty;

        if (instructionText != null)
            instructionText.text = message;

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

#if UNITY_EDITOR
    public void Configure(CanvasGroup canvasGroup, TMP_Text arrow, TMP_Text instruction, TMP_Text destination)
    {
        root = canvasGroup;
        arrowText = arrow;
        instructionText = instruction;
        destinationText = destination;
    }
#endif
}
