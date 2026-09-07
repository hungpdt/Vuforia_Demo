using UnityEngine;
using UnityEngine.UI;

public sealed class DebugController : MonoBehaviour
{
    [SerializeField] Button debugButton;
    [SerializeField] GameObject debugRoot;

    bool isOpen;

    void Awake()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        debugButton.gameObject.SetActive(true);
        SetOpen(false);
#else
        debugButton.gameObject.SetActive(false);
        debugRoot.SetActive(false);
        enabled = false;
#endif
    }

    void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        debugButton.onClick.AddListener(Toggle);
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        debugButton.onClick.RemoveListener(Toggle);
#endif
    }

    void Toggle()
    {
        SetOpen(!isOpen);
    }

    void SetOpen(bool open)
    {
        isOpen = open;

        if (debugRoot != null)
            debugRoot.SetActive(open);

        var label = debugButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (label != null)
            label.text = open ? "Đóng Debug" : "Debug";
    }
}