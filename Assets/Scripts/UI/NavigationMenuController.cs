using UnityEngine;
using UnityEngine.UI;

public sealed class NavigationMenuController : MonoBehaviour
{
    [SerializeField] Button navigationButton;
    [SerializeField] GameObject navigationPanel;
    [SerializeField] Button[] poiButtons;

    bool isOpen;

    void Awake()
    {
        SetOpen(false);
    }

    void OnEnable()
    {
        navigationButton.onClick.AddListener(Toggle);

        foreach (var button in poiButtons)
            button.onClick.AddListener(Close);
    }

    void OnDisable()
    {
        navigationButton.onClick.RemoveListener(Toggle);

        foreach (var button in poiButtons)
            button.onClick.RemoveListener(Close);
    }

    public void Toggle()
    {
        SetOpen(!isOpen);
    }

    public void Close()
    {
        SetOpen(false);
    }

    void SetOpen(bool open)
    {
        isOpen = open;

        if (navigationPanel != null)
            navigationPanel.SetActive(open);
    }
}