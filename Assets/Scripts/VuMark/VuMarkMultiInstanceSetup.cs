using UnityEngine;
using Vuforia;

/// <summary>
/// Vuforia tracks only 4 targets at once by default, so extra VuMarks laid out on a desk
/// would be ignored. Put this on any manager GameObject in the scene to raise the limit.
/// </summary>
public class VuMarkMultiInstanceSetup : MonoBehaviour
{
    [Tooltip("Number of VuMarks tracked simultaneously. Higher values cost more CPU.")]
    [Range(1, 10)]
    public int MaxSimultaneousInstances = 3;

    const int VUFORIA_DEFAULT_MAX = 4;

    void Start()
    {
        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped += OnVuforiaStopped;

        if (VuforiaApplication.Instance.IsRunning)
            OnVuforiaStarted();
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnVuforiaStopped;

        if (VuforiaApplication.Instance.IsRunning)
            OnVuforiaStopped();
    }

    void OnVuforiaStarted()
    {
        VuforiaBehaviour.Instance.SetMaximumSimultaneousTrackedImages(MaxSimultaneousInstances);
        Debug.Log($"[VuMark] Max simultaneous tracked images = {MaxSimultaneousInstances}");
    }

    void OnVuforiaStopped()
    {
        VuforiaBehaviour.Instance.SetMaximumSimultaneousTrackedImages(VUFORIA_DEFAULT_MAX);
    }
}
