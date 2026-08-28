using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Vuforia;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class ARObjectManipulator : MonoBehaviour
{
    [SerializeField] private GameObject roarButton;

    [SerializeField] private Transform groundPlaneStage;
    [SerializeField] private Camera arCamera;
    [SerializeField] private PlaneFinderBehaviour planeFinder;

    [SerializeField] private float minScaleFactor = 0.25f;
    [SerializeField] private float maxScaleFactor = 4.0f;
    [SerializeField] private float rotateSpeed = 1f;

    private bool isDragging = false;
    private Vector3 dragOffset;
    private bool isPinching = false;
    private float prePinchDistance;
    private float prePinchAngle;
    private Vector3 initialScale;

    private AudioSource roarAudioSource;
    private Animator animator;

    private void Awake()
    {

        roarAudioSource = GetComponent<AudioSource>();
        if (roarAudioSource == null)
        {
            Debug.LogError("Roar AudioSource not found on the GameObject.");
        }

        animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogError("Animator not found");
        }

        initialScale = transform.localScale;

        if (groundPlaneStage == null)
        {
            Debug.LogWarning("Ground plane stage not assigned. Using parent transform as default.");
            groundPlaneStage = transform.parent;
        }

        if (arCamera == null)
        {
            Debug.LogWarning("AR Camera not assigned. Using main camera as default.");
            arCamera = Camera.main;
        }

        if (planeFinder == null)
        {
            Debug.LogWarning("Plane finder not assigned. Using default plane finder.");
            planeFinder = FindFirstObjectByType<PlaneFinderBehaviour>();
        }
    }

    void Update()
    {
        if (Touch.activeTouches.Count == 1)
        {
            isPinching = false;
            HandleOneFinger(Touch.activeTouches[0]);
        }
        else if (Touch.activeTouches.Count >= 2)
        {
            isDragging = false;
            HandleTwoFingers(Touch.activeTouches[0], Touch.activeTouches[1]);
        }
        else
        {
            isPinching = false;
            isDragging = false;
        }
    }

    void HandleOneFinger(Touch touch)
    {
        if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.finger.index))
                return;

            var ray = arCamera.ScreenPointToRay(touch.screenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit) || (hit.transform != transform && !hit.transform.IsChildOf(transform)))
            {
                planeFinder.PerformHitTest(touch.screenPosition);
                return;
            }

            if (TryGetPointOnPlane(touch.screenPosition, out Vector3 pointOnPlane))
            {
                dragOffset = transform.position - pointOnPlane;
                isDragging = true;
            }
        }
        else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved || touch.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
        {
            if (!isDragging)
                return;

            if (TryGetPointOnPlane(touch.screenPosition, out Vector3 pointOnPlane))
            {
                transform.position = pointOnPlane + dragOffset;
            }
        }
        else
        {
            isDragging = false;
        }
    }

    private void HandleTwoFingers(Touch touch1, Touch touch2)
    {
        Vector2 delta = touch1.screenPosition - touch2.screenPosition;
        float distance = delta.magnitude;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        if (!isPinching)
        {
            prePinchDistance = distance;
            prePinchAngle = angle;
            isPinching = true;
            return;
        }

        //Scale
        if (prePinchDistance < 0.01f)
        {
            prePinchDistance = distance;
            return;
        }

        float ratio = distance / prePinchDistance;
        Vector3 target = transform.localScale * ratio;
        float factor = target.x / initialScale.x; // Assuming uniform scaling, we can use any axis

        factor = Mathf.Clamp(factor, minScaleFactor, maxScaleFactor);
        transform.localScale = initialScale * factor;

        //Rotate
        float deltaAngle = Mathf.DeltaAngle(prePinchAngle, angle);
        transform.Rotate(groundPlaneStage.up, -deltaAngle * rotateSpeed, Space.World);

        prePinchDistance = distance;
        prePinchAngle = angle;
    }

    bool TryGetPointOnPlane(Vector2 screenPosition, out Vector3 pointOnPlane)
    {
        Ray ray = arCamera.ScreenPointToRay(screenPosition);
        Plane plane = new Plane(groundPlaneStage.up, groundPlaneStage.position);

        if (plane.Raycast(ray, out float enter))
        {
            pointOnPlane = ray.GetPoint(enter);
            return true;
        }

        pointOnPlane = Vector3.zero;
        return false;
    }

    public void OnContentPlaced()
    {
        if(roarButton != null)
        {
            roarButton.SetActive(true);
        }

        transform.localPosition = Vector3.zero;
        isDragging = false;
        isPinching = false;
    }

    public void PlayReaction()
    {
        if (roarAudioSource != null)
        {
            roarAudioSource.Play();
        }

        if (animator != null)
        {
            animator.SetTrigger("Trigger");
        }
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }
}
