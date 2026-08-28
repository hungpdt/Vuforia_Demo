using System.Globalization;
using TMPro;
using UnityEngine;
using Vuforia;

/// <summary>
/// Replaces DefaultObserverEventHandler on a VuMark template.
///
/// Vuforia clones the VuMark template GameObject once per detected instance, so every
/// clone runs its own copy of this script and picks content based on its own InstanceId.
/// That is the core difference from an Image Target: one target, many codes, many
/// augmentations, all visible at the same time.
/// </summary>
public class VuMarkInstanceSpawner : DefaultObserverEventHandler
{
    [System.Serializable]
    public class IdContent
    {
        [Tooltip("Instance ID as text. This dataset uses numeric IDs, so write \"1\", \"2\", \"3\".")]
        public string Id;

        [Tooltip("Prefab to spawn when this ID is detected.")]
        public GameObject Prefab;
    }

    [Header("Content per Instance ID")]
    public IdContent[] ContentPerId;

    [Header("ID label")]
    [Tooltip("Spawns a floating text label showing the detected ID.")]
    public bool ShowIdLabel = true;

    [Tooltip("Label position in VuMark local space. The marker lies in the XZ plane, +Y is its normal.")]
    public Vector3 LabelLocalPosition = new Vector3(0f, 0.005f, 0.085f);

    [Tooltip("Default lies flat on the marker, reading towards +Z. Set Y to 0 if the text appears mirrored.")]
    public Vector3 LabelLocalEuler = new Vector3(-90f, 180f, 0f);

    public float LabelScale = 0.0004f;

    /// <summary>
    /// Vuforia builds every VuMark instance by cloning the template GameObject, and a clone
    /// carries copies of whatever children the template had at that moment. Private fields
    /// are not serialized, so they come back null on the clone -- content must therefore be
    /// tracked by inspecting the actual children, never by a cached reference.
    /// </summary>
    const string LABEL_NAME = "VuMarkIdLabel";

    VuMarkBehaviour mVuMark;
    TextMeshPro mLabel;

    /// <summary>
    /// Called by the editor when the component is added, to pre-fill the three rows.
    /// </summary>
    void Reset()
    {
        // Only keep the augmentation while the marker is actually in view. The inherited
        // default also accepts EXTENDED_TRACKED and LIMITED, which keeps content on screen
        // from a cached pose after the marker is gone -- wrong for a marker-swapping demo.
        StatusFilter = TrackingStatusFilter.Tracked;

        ContentPerId = new[]
        {
            new IdContent { Id = "1" },
            new IdContent { Id = "2" },
            new IdContent { Id = "3" }
        };
    }

    protected override void Start()
    {
        // Cache the reference BEFORE base.Start(): the base class may fire OnTrackingFound() from Start().
        mVuMark = GetComponent<VuMarkBehaviour>();
        if (mVuMark == null)
            Debug.LogError($"{nameof(VuMarkInstanceSpawner)} requires a VuMarkBehaviour on the same GameObject.", this);

        // Adopt the label copied over by cloning instead of creating a second one.
        var existingLabel = transform.Find(LABEL_NAME);
        if (existingLabel != null)
            mLabel = existingLabel.GetComponent<TextMeshPro>();

        // Drop augmentations inherited from the template; this clone picks its own.
        ClearContent();

        base.Start();
    }

    protected override void OnDestroy()
    {
        ClearContent();
        base.OnDestroy();
    }

    protected override void OnTrackingFound()
    {
        base.OnTrackingFound();

        var id = GetInstanceId();
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[VuMark] Instance ID could not be read.", this);
            return;
        }

        Debug.Log($"[VuMark] found -- target: {mVuMark.TargetName}, id: {id}");

        SpawnContentFor(id);
        UpdateLabel(id);
    }

    protected override void OnTrackingLost()
    {
        base.OnTrackingLost();
        ClearContent();
    }

    /// <summary>
    /// An InstanceId carries one of three data types depending on how the VuMark was
    /// designed. The MarsVuMark dataset uses NUMERIC.
    /// </summary>
    string GetInstanceId()
    {
        var instanceId = mVuMark == null ? null : mVuMark.InstanceId;
        if (instanceId == null)
            return string.Empty;

        switch (instanceId.DataType)
        {
            case InstanceIdType.BYTE:
                return instanceId.HexStringValue;
            case InstanceIdType.STRING:
                return instanceId.StringValue;
            case InstanceIdType.NUMERIC:
                return instanceId.NumericValue.ToString(CultureInfo.InvariantCulture);
            default:
                return string.Empty;
        }
    }

    void SpawnContentFor(string id)
    {
        ClearContent();

        var prefab = ResolvePrefab(id);
        if (prefab == null)
        {
            Debug.LogWarning($"[VuMark] No prefab mapped to ID {id}. Add a row to Content Per Id.", this);
            return;
        }

        var content = Instantiate(prefab, transform, false);
        content.name = $"{prefab.name}_id{id}";
    }

    GameObject ResolvePrefab(string id)
    {
        if (ContentPerId == null)
            return null;

        foreach (var entry in ContentPerId)
        {
            if (entry != null && entry.Prefab != null && entry.Id == id)
                return entry.Prefab;
        }

        return null;
    }

    void UpdateLabel(string id)
    {
        if (!ShowIdLabel)
            return;

        if (mLabel == null)
            mLabel = CreateLabel();

        mLabel.text = $"ID: {id}";
    }

    /// <summary>
    /// The label is created once per clone and reused, so it is not destroyed on tracking
    /// lost. The base class disables its renderer instead.
    /// </summary>
    TextMeshPro CreateLabel()
    {
        var go = new GameObject(LABEL_NAME);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = LabelLocalPosition;
        go.transform.localRotation = Quaternion.Euler(LabelLocalEuler);
        go.transform.localScale = Vector3.one * LabelScale;

        var label = go.AddComponent<TextMeshPro>();
        label.rectTransform.sizeDelta = new Vector2(400f, 120f);
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 90f;
        label.color = Color.white;
        return label;
    }

    /// <summary>
    /// Destroys every child except the ID label, which is reused across found/lost cycles.
    /// </summary>
    void ClearContent()
    {
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == LABEL_NAME)
                continue;

            Destroy(child.gameObject);
        }
    }
}
