/*===============================================================================
Copyright (c) 2025 PTC Inc. and/or Its Subsidiary Companies. All Rights Reserved.

Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
===============================================================================*/

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public class TouchHandler : MonoBehaviour
{
    public Transform AugmentationObject;
    public bool EnablePinchScaling;

    public int TouchCount => EnhancedTouchSupport.enabled ? Touch.activeTouches.Count : 0;
    public bool IsSingleFingerStationary => TouchCount == 1 && Touch.activeTouches[0].phase == TouchPhase.Stationary;
    public bool IsSingleFingerDragging => TouchCount == 1 && Touch.activeTouches[0].phase == TouchPhase.Moved;
    public Vector2 SingleTouchPosition => TouchCount > 0 ? Touch.activeTouches[0].screenPosition : Vector2.zero;

    const float SCALE_RANGE_MIN = 0.1f;
    const float SCALE_RANGE_MAX = 2.0f;

    Touch[] mTouches;
    bool mEnableRotation;
    bool mIsFirstFrameWithTwoTouches;
    float mCachedTouchAngle;
    float mCachedTouchDistance;
    float mCachedAugmentationScale;
    Vector3 mCachedAugmentationRotation;

    /// <summary>
    /// Enables rotation input.
    /// It is registered to ContentPositioningBehaviour.OnContentPlaced.
    /// </summary>
    public void EnableRotation()
    {
        mEnableRotation = true;
    }

    /// <summary>
    /// Disables rotation input.
    /// It is registered to UI Reset Button and also DevicePoseBehaviourManager.DevicePoseReset event.
    /// </summary>
    public void DisableRotation()
    {
        mEnableRotation = false;
    }

    void Start()
    {
        mCachedAugmentationScale = AugmentationObject.localScale.x;
        mCachedAugmentationRotation = AugmentationObject.localEulerAngles;
    }

    void OnEnable()
    {
        // Enable EnhancedTouchSupport on touchscreen devices to access touch data at runtime
        if (Touchscreen.current != null)
            EnhancedTouchSupport.Enable();
    }

    void OnDisable()
    {
        // Disable EnhancedTouchSupport to avoid consuming resources when not needed anymore
        if (Touchscreen.current != null)
            EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        if (TouchCount == 2)
        {
            GetTouchAngleAndDistance(Touch.activeTouches[0], Touch.activeTouches[1],
                out var currentTouchAngle, out var currentTouchDistance);

            if (mIsFirstFrameWithTwoTouches)
            {
                mCachedTouchDistance = currentTouchDistance;
                mCachedTouchAngle = currentTouchAngle;
                mIsFirstFrameWithTwoTouches = false;
            }

            var angleDelta = currentTouchAngle - mCachedTouchAngle;
            var scaleMultiplier = currentTouchDistance / mCachedTouchDistance;
            var scaleAmount = mCachedAugmentationScale * scaleMultiplier;
            var scaleAmountClamped = Mathf.Clamp(scaleAmount, SCALE_RANGE_MIN, SCALE_RANGE_MAX);

            if (mEnableRotation)
                AugmentationObject.localEulerAngles = mCachedAugmentationRotation - new Vector3(0, angleDelta * 3f, 0);
            
            // Optional Pinch Scaling can be enabled via Inspector for this Script Component
            if (mEnableRotation && EnablePinchScaling)
                AugmentationObject.localScale = new Vector3(scaleAmountClamped, scaleAmountClamped, scaleAmountClamped);
        }
        else if (TouchCount < 2)
        {
            mCachedAugmentationScale = AugmentationObject.localScale.x;
            mCachedAugmentationRotation = AugmentationObject.localEulerAngles;
            mIsFirstFrameWithTwoTouches = true;
        }
        // enable runtime testing of pinch scaling
        else if (TouchCount == 6)
            EnablePinchScaling = true;
        // disable runtime testing of pinch scaling
        else if (TouchCount == 5)
            EnablePinchScaling = false;
    }

    void GetTouchAngleAndDistance(Touch firstTouch, Touch secondTouch, out float touchAngle, out float touchDistance)
    {
        touchDistance = Vector2.Distance(firstTouch.screenPosition, secondTouch.screenPosition);
        var diffY = firstTouch.screenPosition.y - secondTouch.screenPosition.y;
        var diffX = firstTouch.screenPosition.x - secondTouch.screenPosition.x;
        touchAngle = Mathf.Atan2(diffY, diffX) * Mathf.Rad2Deg;
    }
}