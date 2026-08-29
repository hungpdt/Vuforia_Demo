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
/// Idempotent scene installer for the Day 1 guidance vertical slice.
/// It intentionally only touches Tang1_B52 and the four existing POI buttons.
/// </summary>
public static class Day1NavigationInstaller
{
    const string ScenePath = "Assets/Scenes/Tang1_B52.unity";

    static readonly DestinationSetup[] Destinations =
    {
        new DestinationSetup("POI_HCM", "HCM", "hcm", "HCM"),
        new DestinationSetup("POI_Exit", "Exit", "exit", "Exit"),
        new DestinationSetup("POI_Outside", "Outside", "outside", "Outside"),
        new DestinationSetup("POI_Dinosaur", "Dinosour", "dinosaur", "Dinosaur")
    };

    [MenuItem("Tools/Vuforia Demo/Install Day 1 Guidance")]
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
        var manager = FindOne<NavMeshManager>(scene, "NavMeshManager");
        var agent = manager.NavigationAgent;
        var areaTarget = manager.AreaTargetTransform;
        var arCamera = manager.ArCameraTransform;
        var observer = areaTarget != null ? areaTarget.GetComponent<ObserverBehaviour>() : null;

        Require(agent, "NavMeshManager.NavigationAgent");
        Require(areaTarget, "NavMeshManager.AreaTargetTransform");
        Require(arCamera, "NavMeshManager.ArCameraTransform");
        Require(observer, "AreaTarget ObserverBehaviour");

        var hud = CreateOrUpdateHud(scene);
        var controller = CreateOrUpdateController(scene, manager, agent, areaTarget, arCamera, observer, hud);
        ConfigureDestinationsAndButtons(scene, controller);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);

        ValidateScene();
        AssetDatabase.SaveAssets();
        Debug.Log("DAY1_GUIDANCE_INSTALL_OK: Tang1_B52 has 4 editable POIs, 4 button bindings and a 2D guidance HUD.");
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
        root.transform.SetParent(canvas.transform, false);

        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -36f);
        rect.sizeDelta = new Vector2(480f, 210f);

        var background = GetOrAdd<UnityEngine.UI.Image>(root);
        background.color = new Color(0.035f, 0.055f, 0.09f, 0.88f);
        background.raycastTarget = false;

        var canvasGroup = GetOrAdd<CanvasGroup>(root);
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var arrow = CreateOrUpdateText(root.transform, "Arrow", new Vector2(0f, -8f), new Vector2(110f, 88f), 68f);
        arrow.text = "\u2191";
        arrow.color = new Color(0.25f, 0.85f, 1f, 1f);
        arrow.fontStyle = FontStyles.Bold;

        var instruction = CreateOrUpdateText(root.transform, "Instruction", new Vector2(0f, -96f), new Vector2(440f, 54f), 36f);
        instruction.text = "Đi thẳng";
        instruction.color = Color.white;
        instruction.fontStyle = FontStyles.Bold;

        var destination = CreateOrUpdateText(root.transform, "Destination", new Vector2(0f, -154f), new Vector2(440f, 38f), 24f);
        destination.text = "Điểm đến";
        destination.color = new Color(0.78f, 0.84f, 0.92f, 1f);

        var hud = existing != null ? existing : Undo.AddComponent<GuidanceHUD>(root);
        hud.Configure(canvasGroup, arrow, instruction, destination);
        EditorUtility.SetDirty(hud);
        return hud;
    }

    static TurnGuidanceController CreateOrUpdateController(
        Scene scene,
        NavMeshManager manager,
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

    static void ConfigureDestinationsAndButtons(Scene scene, TurnGuidanceController controller)
    {
        foreach (var setup in Destinations)
        {
            var anchor = FindGameObject(scene, setup.AnchorName);
            if (anchor == null)
                throw new InvalidOperationException("Missing destination anchor: " + setup.AnchorName);

            var destination = anchor.GetComponent<POIDestination>();
            if (destination == null)
                destination = Undo.AddComponent<POIDestination>(anchor);

            destination.Configure(setup.Id, setup.DisplayName, 1.5f, anchor.transform);
            EditorUtility.SetDirty(destination);

            var buttonObject = FindGameObject(scene, setup.ButtonName);
            var button = buttonObject != null ? buttonObject.GetComponent<Button>() : null;
            if (button == null)
                throw new InvalidOperationException("Missing POI button: " + setup.ButtonName);

            // The old scene called NavMeshManager.NavigateTo directly. The new
            // binding owns this click so it can enforce POI enabled/disabled state.
            for (var index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
                UnityEventTools.RemovePersistentListener(button.onClick, index);

            var binding = button.GetComponent<POINavigationButton>();
            if (binding == null)
                binding = Undo.AddComponent<POINavigationButton>(button.gameObject);

            binding.Configure(button, destination, controller);
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(binding);
        }
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
        var hud = FindOne<GuidanceHUD>(scene, "GuidanceHUD");
        var destinations = FindSceneObjects<POIDestination>(scene).ToArray();
        var bindings = FindSceneObjects<POINavigationButton>(scene).ToArray();

        if (destinations.Length != Destinations.Length)
            throw new InvalidOperationException($"Expected {Destinations.Length} POIs, found {destinations.Length}.");
        if (bindings.Length != Destinations.Length)
            throw new InvalidOperationException($"Expected {Destinations.Length} button bindings, found {bindings.Length}.");
        if (destinations.Any(item => item.Anchor == null || string.IsNullOrWhiteSpace(item.Id)))
            throw new InvalidOperationException("At least one POI has incomplete metadata.");
        if (bindings.Any(item => item.Destination == null || item.GuidanceController != controller))
            throw new InvalidOperationException("At least one POI button is not bound to the guidance controller.");

        Require(hud, "GuidanceHUD");
        ValidateAngleClassification();
        Debug.Log("DAY1_GUIDANCE_VALIDATE_OK: scene wiring and turn thresholds are valid.");
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

    static GameObject FindGameObject(Scene scene, string objectName)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name == objectName)
            .Select(item => item.gameObject)
            .FirstOrDefault();
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

    readonly struct DestinationSetup
    {
        public readonly string AnchorName;
        public readonly string ButtonName;
        public readonly string Id;
        public readonly string DisplayName;

        public DestinationSetup(string anchorName, string buttonName, string id, string displayName)
        {
            AnchorName = anchorName;
            ButtonName = buttonName;
            Id = id;
            DisplayName = displayName;
        }
    }
}
