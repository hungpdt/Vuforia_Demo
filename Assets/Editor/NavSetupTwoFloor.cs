// One-click wiring of the runtime navigation pieces for Navigation_2_floor.
// Creates ARCameraNavAgent + NavigationLine + NavManager and hooks the Vuforia
// target events. Safe to run repeatedly — it reuses whatever already exists.
// Menu: Tools > NavMesh > Setup Two-Floor Navigation
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public static class NavSetupTwoFloor
{
    const string kAgentTypeName = "ARUser";
    const string kMatPath = "Assets/Scenes/Navigation_2_floor/NavLine.mat";

    [MenuItem("Tools/NavMesh/Setup Two-Floor Navigation")]
    static void Setup()
    {
        // --- locate what the scene already has -------------------------------
        var multiArea = Object.FindFirstObjectByType<MultiArea>();
        if (multiArea == null) { Debug.LogError("No MultiArea found — expected it on 'NavRoot'."); return; }
        var navRoot = multiArea.transform;

        var vuforiaCam = Object.FindFirstObjectByType<Vuforia.VuforiaBehaviour>();
        if (vuforiaCam == null) { Debug.LogError("No VuforiaBehaviour found — expected it on 'ARCamera'."); return; }
        var arCamera = vuforiaCam.transform;

        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null) Debug.LogWarning("No NavMeshSurface in the scene.");
        else if (surface.navMeshData == null) Debug.LogWarning($"'{surface.name}' has no baked data — bake it before testing.");

        int agentTypeId = FindAgentTypeId(kAgentTypeName);
        if (agentTypeId == int.MinValue)
        {
            Debug.LogError($"Agent type '{kAgentTypeName}' not found in Navigation settings.");
            return;
        }

        // --- NavMeshAgent -----------------------------------------------------
        var agentGo = Find("ARCameraNavAgent") ?? Create("ARCameraNavAgent");
        var agent = agentGo.GetComponent<NavMeshAgent>() ?? Undo.AddComponent<NavMeshAgent>(agentGo);
        Undo.RecordObject(agent, "nav setup");
        agent.agentTypeID = agentTypeId;
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.speed = 0f;              // driven by NavMeshManager, never moves itself
        agent.angularSpeed = 120f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 0f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        // --- LineRenderer -----------------------------------------------------
        var lineGo = Find("NavigationLine") ?? Create("NavigationLine");
        var line = lineGo.GetComponent<LineRenderer>() ?? Undo.AddComponent<LineRenderer>(lineGo);
        Undo.RecordObject(line, "nav setup");
        line.useWorldSpace = true;     // NavMeshManager feeds world-space corners
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.widthMultiplier = 0.12f;
        line.positionCount = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        if (line.sharedMaterial == null) line.sharedMaterial = GetOrCreateLineMaterial();
        line.enabled = false;          // NavMeshManager turns it on when navigating

        // --- NavMeshManager ---------------------------------------------------
        var managerGo = Find("NavManager") ?? Create("NavManager");
        var manager = managerGo.GetComponent<NavMeshManager>() ?? Undo.AddComponent<NavMeshManager>(managerGo);
        Undo.RecordObject(manager, "nav setup");
        manager.AreaTargetTransform = navRoot;   // the rigid group MultiArea drives
        manager.ArCameraTransform = arCamera;
        manager.NavigationAgent = agent;
        manager.NavigationLine = line;

        // --- hook every Area Target's found/lost events -----------------------
        int wired = 0;
        foreach (var h in Object.FindObjectsByType<DefaultAreaTargetEventHandler>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(h, "nav setup");
            Rewire(h.OnTargetFound, manager, nameof(NavMeshManager.OnAreaTargetFound));
            Rewire(h.OnTargetLost, manager, nameof(NavMeshManager.OnAreaTargetLost));
            wired++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.SetDirty(manager);

        Debug.Log(
            "Two-floor navigation wired up:\n" +
            $"  agent      : {agentGo.name} (agent type '{kAgentTypeName}' = {agentTypeId})\n" +
            $"  line       : {lineGo.name} (world space, material '{line.sharedMaterial.name}')\n" +
            $"  manager    : {managerGo.name}, AreaTargetTransform = {navRoot.name}\n" +
            $"  ARCamera   : {arCamera.name}\n" +
            $"  target evts: {wired} Area Target handler(s) hooked\n" +
            "Save the scene, then call NavManager.NavigateTo(<Transform>) from a button to route.");

        Selection.activeGameObject = managerGo;
    }

    // Hide the bake geometry after baking. The NavMesh asset is already saved, so
    // NavMeshSurface still loads it — the source meshes are only needed to re-bake.
    [MenuItem("Tools/NavMesh/Toggle Bake Geometry (hide after baking)")]
    static void ToggleBakeGeometry()
    {
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null) { Debug.LogError("No NavMeshSurface found."); return; }

        int n = 0;
        bool turningOff = false;
        foreach (Transform child in surface.transform)
        {
            if (n++ == 0) turningOff = child.gameObject.activeSelf;
            Undo.RecordObject(child.gameObject, "toggle bake geometry");
            child.gameObject.SetActive(!turningOff);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"{n} bake-geometry object(s) under '{surface.name}' turned {(turningOff ? "OFF (runtime-ready)" : "ON (ready to re-bake)")}.");
    }

    // --- helpers ------------------------------------------------------------
    static void Rewire(UnityEvent evt, NavMeshManager target, string method)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            if (evt.GetPersistentTarget(i) is NavMeshManager)
                UnityEventTools.RemovePersistentListener(evt, i);

        var action = (UnityAction)System.Delegate.CreateDelegate(
            typeof(UnityAction), target, method);
        UnityEventTools.AddPersistentListener(evt, action);
    }

    static int FindAgentTypeId(string name)
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            var s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == name) return s.agentTypeID;
        }
        return int.MinValue;
    }

    static GameObject Find(string name)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    static GameObject Create(string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "nav setup");
        return go;
    }

    static Material GetOrCreateLineMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(kMatPath);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        mat = new Material(shader) { name = "NavLine" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.1f, 0.8f, 1f));
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.1f, 0.8f, 1f));
        AssetDatabase.CreateAsset(mat, kMatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
