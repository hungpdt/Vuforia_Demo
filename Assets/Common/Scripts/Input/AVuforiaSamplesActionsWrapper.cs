/*===============================================================================
Copyright (c) 2025 PTC Inc. and/or Its Subsidiary Companies. All Rights Reserved.

Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
===============================================================================*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public abstract class AVuforiaSamplesActionsWrapper : MonoBehaviour, VuforiaSamplesActions.IVuforiaActions
{
    protected VuforiaSamplesActions VuforiaActions;

    void Awake()
    {
        VuforiaActions = new VuforiaSamplesActions();
        VuforiaActions.Vuforia.SetCallbacks(this);
    }

    void OnEnable()
    {
        VuforiaActions.Vuforia.Enable();
    }

    void OnDisable()
    {
        VuforiaActions.Vuforia.Disable();
    }

    public virtual void OnDoubleTap(InputAction.CallbackContext context) { }
    public virtual void OnTap(InputAction.CallbackContext context) { }
    public virtual void OnPoint(InputAction.CallbackContext context) { }
    public virtual void OnPress(InputAction.CallbackContext context) { }

    protected bool IsInputPositionOverUIElement(GraphicRaycaster raycaster, Vector2 position)
    {
        var eventDataCurrentPosition = new PointerEventData(EventSystem.current)
                                       {
                                           position = position
                                       };

        var results = new List<RaycastResult>();
        raycaster.Raycast(eventDataCurrentPosition, results);

        return results.Count > 0;
    }
}