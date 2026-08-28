/*===============================================================================
Copyright (c) 2025 PTC Inc. and/or Its Subsidiary Companies. All Rights Reserved.

Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
===============================================================================*/

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class NavigationHandler : MonoBehaviour
{
    public string BackButtonNavigation = "[Name of Scene To Load]";
    public UnityEvent OnBackButtonPressed = new ();

    void Update()
    {
        // On Android, the Back button is mapped to the Esc key
        if (Keyboard.current?.escapeKey.wasReleasedThisFrame == true)
            HandleBackButtonPressed();
    }

    public void HandleBackButtonPressed()
    {
        OnBackButtonPressed?.Invoke();
        if (SceneManager.GetActiveScene().name != BackButtonNavigation)
            LoadScene(BackButtonNavigation);
    }
    
    void LoadScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
            SceneManager.LoadScene(sceneName);
    }
}
