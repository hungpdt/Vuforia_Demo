using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vuforia;

/// <summary>
/// Idempotent scene installer for the three-day guidance vertical slice.
/// POIs and buttons remain scene data and are never hard-coded here.
/// </summary>
public static class NavigationGuidanceInstaller
{
    const string ScenePath = "Assets/Scenes/Tang1_B52.unity";

    [MenuItem("Tools/Vuforia Demo/Install Day 3 Guidance")]
    public static void InstallFromMenu()
    {
        Install();
    }

    public static void InstallFromCommandLine()
    {
        try
        {
            Install();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void ValidateFromCommandLine()
    {
        try
        {
            ValidateScene();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void Install()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var manager = CreateOrMigrateNavMeshManager(scene);
        var agent = manager.NavigationAgent;
        var areaTarget = manager.AreaTargetTransform;
        var arCamera = manager.ArCameraTransform;
        var observer = areaTarget != null ? areaTarget.GetComponent<ObserverBehaviour>() : null;

        Require(agent, "MuseumNavMeshManager.NavigationAgent");
        Require(areaTarget, "MuseumNavMeshManager.AreaTargetTransform");
        Require(arCamera, "MuseumNavMeshManager.ArCameraTransform");
        Require(observer, "AreaTarget ObserverBehaviour");

        var hud = CreateOrUpdateHud(scene);
        var controller = CreateOrUpdateController(scene, manager, agent, areaTarget, arCamera, observer, hud);
        ConfigureButtons(scene, controller);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);

        ApplyPortraitSettings();
        AssetDatabase.SaveAssets();
        ValidateScene();
        Debug.Log("DAY3_GUIDANCE_INSTALL_OK: Tang1_B52 has safe-area HUD, editable POIs and resilient route guidance.");
    }

    static GuidanceHUD CreateOrUpdateHud(Scene scene)
    {
        var canvas = FindSceneObjects<Canvas>(scene).FirstOrDefault(item => item.name == "UI_Button")
                     ?? FindOne<Canvas>(scene, "Canvas");

        var existing = FindSceneObjects<GuidanceHUD>(scene).FirstOrDefault();
        var root = existing != null ? existing.gameObject : new GameObject("GuidanceHUD", typeof(RectTransform));
        if (existing == null)
            Undo.RegisterCreatedObjectUndo(root, "Create Day 1 guidance HUD");

        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(CreateOrUpdateSafeAreaRoot(canvas.transform), false);

        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -36f);
        rect.sizeDelta = new Vector2(480f, 230f);

        var background = GetOrAdd<UnityEngine.UI.Image>(root);
        background.color = new Color(0.035f, 0.055f, 0.09f, 0.88f);
        background.raycastTarget = false;

        var canvasGroup = GetOrAdd<CanvasGroup>(root);
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var arrow = CreateOrUpdateArrow(root.transform);

        var instruction = CreateOrUpdateText(root.transform, "Instruction", new Vector2(0f, -88f), new Vector2(440f, 48f), 34f);
        instruction.text = "Đi thẳng";
        instruction.color = Color.white;
        instruction.fontStyle = FontStyles.Bold;

        var distance = CreateOrUpdateText(root.transform, "Distance", new Vector2(0f, -136f), new Vector2(440f, 34f), 24f);
        distance.text = "sau 8 m";
        distance.color = new Color(0.25f, 0.85f, 1f, 1f);
        distance.fontStyle = FontStyles.Bold;

        var destination = CreateOrUpdateText(root.transform, "Destination", new Vector2(0f, -178f), new Vector2(440f, 34f), 22f);
        destination.text = "Điểm đến";
        destination.color = new Color(0.78f, 0.84f, 0.92f, 1f);

        var hud = existing != null ? existing : Undo.AddComponent<GuidanceHUD>(root);
        hud.Configure(canvasGroup, arrow, instruction, distance, destination);
        EditorUtility.SetDirty(hud);
        return hud;
    }

    static RectTransform CreateOrUpdateSafeAreaRoot(Transform canvasTransform)
    {
        if (canvasTransform == null)
            throw new InvalidOperationException("Cannot create SafeAreaRoot without a Canvas transform.");

        var child = canvasTransform.Find("SafeAreaRoot");
        GameObject root;
        if (child == null)
        {
            root = new GameObject("SafeAreaRoot", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create navigation safe area root");
            root.transform.SetParent(canvasTransform, false);
        }
        else
        {
            root = child.gameObject;
        }

        root.layer = LayerMask.NameToLayer("UI");
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        GetOrAdd<SafeAreaFitter>(root);
        return rect;
    }

    static TurnGuidanceController CreateOrUpdateController(
        Scene scene,
        MuseumNavMeshManager manager,
        NavMeshAgent agent,
        Transform areaTarget,
        Transform arCamera,
        ObserverBehaviour observer,
        GuidanceHUD hud)
    {
        var controller = FindSceneObjects<TurnGuidanceController>(scene).FirstOrDefault();
        if (controller == null)
        {
            var go = new GameObject("TurnGuidanceController");
            Undo.RegisterCreatedObjectUndo(go, "Create turn guidance controller");
            SceneManager.MoveGameObjectToScene(go, scene);
            controller = Undo.AddComponent<TurnGuidanceController>(go);
        }

        controller.Configure(manager, agent, areaTarget, arCamera, observer, hud);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static MuseumNavMeshManager CreateOrMigrateNavMeshManager(Scene scene)
    {
        var manager = FindSceneObjects<MuseumNavMeshManager>(scene).FirstOrDefault();
        if (manager != null)
            return manager;

        var legacy = FindOne<NavMeshManager>(scene, "legacy NavMeshManager");
        manager = Undo.AddComponent<MuseumNavMeshManager>(legacy.gameObject);
        manager.Configure(
            legacy.AreaTargetTransform,
            legacy.ArCameraTransform,
            legacy.NavigationAgent,
            legacy.NavigationLine,
            legacy.NavMeshSampleDistance);

        var eventHandler = legacy.AreaTargetTransform != null
            ? legacy.AreaTargetTransform.GetComponent<DefaultAreaTargetEventHandler>()
            : null;
        if (eventHandler != null)
        {
            ReplacePersistentListener(eventHandler.OnTargetFound, legacy, manager, true);
            ReplacePersistentListener(eventHandler.OnTargetLost, legacy, manager, false);
        }

        EditorUtility.SetDirty(manager);
        Undo.DestroyObjectImmediate(legacy);
        return manager;
    }

    static void ReplacePersistentListener(
        UnityEngine.Events.UnityEvent source,
        UnityEngine.Object legacyTarget,
        MuseumNavMeshManager manager,
        bool targetFound)
    {
        for (var index = source.GetPersistentEventCount() - 1; index >= 0; index--)
        {
            var target = source.GetPersistentTarget(index);
            if (target == legacyTarget || target == manager)
                UnityEventTools.RemovePersistentListener(source, index);
        }

        if (targetFound)
            UnityEventTools.AddPersistentListener(source, manager.OnAreaTargetFound);
        else
            UnityEventTools.AddPersistentListener(source, manager.OnAreaTargetLost);
    }

    static void ConfigureButtons(Scene scene, TurnGuidanceController controller)
    {
        var bindings = FindSceneObjects<POINavigationButton>(scene).ToArray();
        if (bindings.Length == 0)
            throw new InvalidOperationException("No POINavigationButton exists in " + ScenePath);

        foreach (var binding in bindings)
        {
            var button = binding.GetComponent<Button>();
            binding.Configure(button, binding.Destination, controller);
            EditorUtility.SetDirty(binding);
        }
    }

    static UnityEngine.UI.Image CreateOrUpdateArrow(Transform parent)
    {
        var containerTransform = parent.Find("ArrowContainer");
        if (containerTransform == null)
        {
            var container = new GameObject("ArrowContainer", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(container, "Create guidance arrow container");
            container.transform.SetParent(parent, false);
            container.layer = LayerMask.NameToLayer("UI");

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 1f);
            containerRect.anchorMax = new Vector2(0.5f, 1f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, -52f);
            containerRect.sizeDelta = new Vector2(96f, 96f);
            containerTransform = container.transform;
        }

        var child = containerTransform.Find("Arrow") ?? parent.Find("Arrow");
        var go = child != null ? child.gameObject : new GameObject("Arrow", typeof(RectTransform));
        if (child == null)
            Undo.RegisterCreatedObjectUndo(go, "Create guidance arrow image");

        if (go.transform.parent != containerTransform)
            go.transform.SetParent(containerTransform, false);

        var oldText = go.GetComponent<TextMeshProUGUI>();
        if (oldText != null)
            Undo.DestroyObjectImmediate(oldText);

        go.layer = LayerMask.NameToLayer("UI");
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = GetOrAdd<UnityEngine.UI.Image>(go);
        image.preserveAspect = true;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
        return image;
    }

    static TMP_Text CreateOrUpdateText(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize)
    {
        var child = parent.Find(name);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = child.gameObject;
        }

        go.layer = LayerMask.NameToLayer("UI");
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var text = go.GetComponent<TextMeshProUGUI>();
        if (text == null)
            text = Undo.AddComponent<TextMeshProUGUI>(go);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);
        return text;
    }

    static void ValidateScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var controller = FindOne<TurnGuidanceController>(scene, "TurnGuidanceController");
        var manager = FindOne<MuseumNavMeshManager>(scene, "MuseumNavMeshManager");
        var hud = FindOne<GuidanceHUD>(scene, "GuidanceHUD");
        var destinations = FindSceneObjects<POIDestination>(scene).ToArray();
        var bindings = FindSceneObjects<POINavigationButton>(scene).ToArray();

        if (destinations.Length == 0)
            throw new InvalidOperationException("At least one POIDestination is required.");
        if (bindings.Length == 0)
            throw new InvalidOperationException("At least one POINavigationButton is required.");
        if (destinations.Any(item => item.Anchor == null || string.IsNullOrWhiteSpace(item.Id)))
            throw new InvalidOperationException("At least one POI has incomplete metadata.");
        if (destinations.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException("POIDestination ids must be unique.");
        if (bindings.Any(item => item.Destination == null || item.GuidanceController != controller))
            throw new InvalidOperationException("At least one POI button is not bound to the guidance controller.");
        if (manager.NavigationAgent == null || manager.AreaTargetTransform == null || manager.ArCameraTransform == null)
            throw new InvalidOperationException("Navigation infrastructure is incomplete.");

        Require(hud, "GuidanceHUD");
        Require(hud.GetComponentInParent<SafeAreaFitter>(), "SafeAreaFitter");
        ValidateSafeAreaMath();
        ValidateAngleClassification();
        ValidateDistanceToPath();
        ValidatePortraitSettings();
        Debug.Log("DAY3_GUIDANCE_VALIDATE_OK: scene wiring, safe area, portrait and navigation math are valid.");
    }

    static void ValidateSafeAreaMath()
    {
        var result = SafeAreaFitter.ClampToScreen(new Rect(-10f, 20f, 1100f, 1900f), 1080f, 1920f);
        if (result.xMin != 0f || result.yMin != 20f || result.xMax != 1080f || result.yMax != 1920f)
            throw new InvalidOperationException("Safe area clamping returned an invalid rectangle.");

        var emptyResult = SafeAreaFitter.ClampToScreen(Rect.zero, 1080f, 1920f);
        if (emptyResult.width != 1080f || emptyResult.height != 1920f)
            throw new InvalidOperationException("Empty safe area must fall back to the full screen.");
    }

    static void ApplyPortraitSettings()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
    }

    static void ValidatePortraitSettings()
    {
        // Autorotation flags are ignored by Unity when the default orientation
        // is a fixed Portrait value, so they must not make validation fail.
        if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
            throw new InvalidOperationException("Player Settings must be Portrait-only.");
    }

    static void ValidateAngleClassification()
    {
        AssertManeuver(0f, TurnGuidanceController.Maneuver.Straight);
        AssertManeuver(40f, TurnGuidanceController.Maneuver.SlightRight);
        AssertManeuver(-40f, TurnGuidanceController.Maneuver.SlightLeft);
        AssertManeuver(90f, TurnGuidanceController.Maneuver.Right);
        AssertManeuver(-90f, TurnGuidanceController.Maneuver.Left);
        AssertManeuver(170f, TurnGuidanceController.Maneuver.UTurn);
    }

    static void AssertManeuver(float angle, TurnGuidanceController.Maneuver expected)
    {
        var actual = TurnGuidanceController.ClassifyAngle(angle, 25f, 65f, 135f);
        if (actual != expected)
            throw new InvalidOperationException($"Angle {angle} expected {expected}, got {actual}.");
    }

    static void ValidateDistanceToPath()
    {
        var corners = new[] { Vector3.zero, new Vector3(0f, 0f, 10f) };
        var distance = TurnGuidanceController.DistanceToPath(new Vector3(2f, 0f, 5f), corners, corners.Length);
        if (!Mathf.Approximately(distance, 2f))
            throw new InvalidOperationException($"Expected path deviation 2, got {distance}.");
    }

    static T FindOne<T>(Scene scene, string label) where T : UnityEngine.Object
    {
        var item = FindSceneObjects<T>(scene).FirstOrDefault();
        if (item == null)
            throw new InvalidOperationException("Missing " + label + " in " + ScenePath);
        return item;
    }

    static IEnumerable<T> FindSceneObjects<T>(Scene scene) where T : UnityEngine.Object
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true));
    }

    static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        var component = gameObject.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(gameObject);
    }

    static void Require(UnityEngine.Object value, string label)
    {
        if (value == null)
            throw new InvalidOperationException("Missing " + label + " in " + ScenePath);
    }

}
