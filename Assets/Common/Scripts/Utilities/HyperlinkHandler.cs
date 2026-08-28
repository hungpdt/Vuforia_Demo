/*===============================================================================
Copyright (c) 2025 PTC Inc. and/or Its Subsidiary Companies. All Rights Reserved.

Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
===============================================================================*/

using UnityEngine;
using TMPro;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class HyperlinkHandler : AVuforiaSamplesActionsWrapper
{
    public UnityEvent OnActivateGameObjectEvent;

    TextMeshProUGUI mTextMeshPro;
    Camera mCamera;

    void Start()
    {
        mTextMeshPro = GetComponentInChildren<TextMeshProUGUI>();

        // Get a reference to the camera if Canvas Render Mode is not ScreenSpace Overlay
        var canvas = GetComponentInParent<Canvas>();
        mCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    public override void OnTap(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        CheckIfLinkAndOpenURL(VuforiaActions.Vuforia.Point.ReadValue<Vector2>());
    }

    void CheckIfLinkAndOpenURL(Vector2 screenPosition)
    {
        var linkIndex = TMP_TextUtilities.FindIntersectingLink(mTextMeshPro, screenPosition, mCamera);

        if (linkIndex != -1)
        {
            var linkInfo = mTextMeshPro.textInfo.linkInfo[linkIndex];
            var linkId = linkInfo.GetLinkID();

            if (linkId.StartsWith("http"))
            {
                Application.OpenURL(linkInfo.GetLinkID());
            }
            else if (linkId.StartsWith("event"))
            {
                var eventElements = linkId.Split(':');
                if (eventElements.Length != 2)
                {
                    Debug.LogError("Invalid event ID.");
                }
                else
                {
                    var eventId = eventElements[1];
                    if (eventId == "ACTIVATE_GAMEOBJECT")
                        OnActivateGameObjectEvent.Invoke();
                }
            }
        }
    }
}