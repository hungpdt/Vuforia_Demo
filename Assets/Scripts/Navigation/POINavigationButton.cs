using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds one existing UI button to one editable POI destination.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class POINavigationButton : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] POIDestination destination;
    [SerializeField] TurnGuidanceController guidanceController;

    public POIDestination Destination => destination;
    public TurnGuidanceController GuidanceController => guidanceController;

    void Reset()
    {
        button = GetComponent<Button>();
        guidanceController = FindFirstObjectByType<TurnGuidanceController>();
    }

    void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();
    }

    void OnEnable()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveListener(Navigate);
            button.onClick.AddListener(Navigate);
        }

        RefreshInteractable();
    }

    void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(Navigate);
    }

    public void Navigate()
    {
        if (guidanceController == null || destination == null || !destination.IsNavigationEnabled)
            return;

        guidanceController.SetDestination(destination);
    }

    public void RefreshInteractable()
    {
        if (button != null)
            button.interactable = destination != null && destination.IsNavigationEnabled;
    }

#if UNITY_EDITOR
    public void Configure(Button sourceButton, POIDestination poi, TurnGuidanceController controller)
    {
        button = sourceButton;
        destination = poi;
        guidanceController = controller;
        RefreshInteractable();
    }
#endif
}
