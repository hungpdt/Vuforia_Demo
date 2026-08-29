using UnityEngine;

/// <summary>
/// Editable metadata for a destination anchor in the current scene.
/// Move the anchor or change these fields in the Inspector without changing
/// the route calculation code.
/// </summary>
[DisallowMultipleComponent]
public sealed class POIDestination : MonoBehaviour
{
    [SerializeField] string id;
    [SerializeField] string displayName;
    [SerializeField, Min(0.1f)] float arrivalRadius = 1.5f;
    [SerializeField] Transform anchor;
    [SerializeField] bool navigationEnabled = true;

    public string Id => id;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float ArrivalRadius => arrivalRadius;
    public Transform Anchor => anchor != null ? anchor : transform;
    public bool IsNavigationEnabled => navigationEnabled && enabled && gameObject.activeInHierarchy;

    void Reset()
    {
        anchor = transform;
        id = name.ToLowerInvariant().Replace("poi_", string.Empty).Replace(' ', '-');
        displayName = name.Replace("POI_", string.Empty);
    }

    void OnValidate()
    {
        if (anchor == null)
            anchor = transform;

        arrivalRadius = Mathf.Max(0.1f, arrivalRadius);
    }

#if UNITY_EDITOR
    public void Configure(string destinationId, string destinationName, float radius, Transform destinationAnchor)
    {
        id = destinationId;
        displayName = destinationName;
        arrivalRadius = Mathf.Max(0.1f, radius);
        anchor = destinationAnchor != null ? destinationAnchor : transform;
        navigationEnabled = true;
    }
#endif
}
