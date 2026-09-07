// One-click setup for the field diagnostics rig, plus the Play Mode switches
// that make a recording brought back from the museum replayable at the office.
// Menu: Tools > AR Debug > ...
// Safe to run repeatedly - it reuses whatever already exists.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vuforia;

public static class ArDebugSetup
{
    const string kRigName = "ARFieldDebug";
    const string kConfigPath = "Assets/Resources/VuforiaConfiguration.asset";

    // ---------------------------------------------------------------- rig ----

    [MenuItem("Tools/AR Debug/Setup Field Debug Rig")]
    static void SetupRig()
    {
        var go = GameObject.Find(kRigName);
        if (go == null)
        {
            go = new GameObject(kRigName);
            Undo.RegisterCreatedObjectUndo(go, "setup field debug rig");
        }

        var diag = go.GetComponent<ArSessionDiagnostics>() ?? Undo.AddComponent<ArSessionDiagnostics>(go);
        var hud = go.GetComponent<ArFieldHud>() ?? Undo.AddComponent<ArFieldHud>(go);
        var recorder = go.GetComponent<SessionRecorderBehaviour>() ?? Undo.AddComponent<SessionRecorderBehaviour>(go);

        Undo.RecordObject(diag, "setup field debug rig");
        Undo.RecordObject(hud, "setup field debug rig");

        // --- what to watch ---------------------------------------------------
        var observer = Object.FindFirstObjectByType<AreaTargetBehaviour>();
        if (observer == null)
            Debug.LogWarning("ArDebugSetup: no AreaTargetBehaviour in the scene - tracking will report nothing.");
        else
        {
            diag.Observer = observer;
            diag.NavRoot = observer.transform;
        }

        var vuforiaCam = Object.FindFirstObjectByType<VuforiaBehaviour>();
        if (vuforiaCam == null)
            Debug.LogWarning("ArDebugSetup: no VuforiaBehaviour (ARCamera) in the scene.");
        else
            diag.ArCamera = vuforiaCam.transform;

        // Prefer the references the runtime actually uses, so the HUD reports
        // the same objects the navigation drives rather than lookalikes.
        var museumManager = Object.FindFirstObjectByType<MuseumNavMeshManager>();
        if (museumManager != null)
        {
            diag.Agent = museumManager.NavigationAgent;
            diag.NavigationLine = museumManager.NavigationLine;

            if (museumManager.AreaTargetTransform != null)
                diag.NavRoot = museumManager.AreaTargetTransform;

            if (museumManager.ArCameraTransform != null)
                diag.ArCamera = museumManager.ArCameraTransform;
        }
        else
        {
            var manager = Object.FindFirstObjectByType<NavMeshManager>();
            if (manager != null)
            {
                diag.Agent = manager.NavigationAgent;
                diag.NavigationLine = manager.NavigationLine;

                if (manager.AreaTargetTransform != null)
                    diag.NavRoot = manager.AreaTargetTransform;

                if (manager.ArCameraTransform != null)
                    diag.ArCamera = manager.ArCameraTransform;
            }
            else
            {
                diag.Agent = Object.FindFirstObjectByType<NavMeshAgent>();
                diag.NavigationLine = Object.FindFirstObjectByType<LineRenderer>();
                Debug.LogWarning("ArDebugSetup: no NavMesh manager found - agent and line were guessed.");
            }
        }

        hud.Recorder = recorder;

        // --- recorder ---------------------------------------------------------
        // Sensor data is what makes an Area Target session replayable: without
        // the device poses, a replay is just a video and relocalisation cannot
        // be reproduced. Set through SerializedObject so a renamed field in a
        // future Vuforia release degrades to a warning instead of a build error.
        var so = new SerializedObject(recorder);
        var sensors = so.FindProperty("RecordSensorsData");
        if (sensors != null)
        {
            sensors.boolValue = true;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("ArDebugSetup: could not find 'RecordSensorsData' on SessionRecorderBehaviour - "
                             + "enable it by hand in the Inspector, replay needs it.");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = go;

        Debug.Log("AR field debug rig ready on '" + kRigName + "'.\n"
                  + "observer=" + (diag.Observer == null ? "MISSING" : diag.Observer.TargetName)
                  + "  arCamera=" + (diag.ArCamera == null ? "MISSING" : diag.ArCamera.name)
                  + "  navRoot=" + (diag.NavRoot == null ? "MISSING" : diag.NavRoot.name)
                  + "  agent=" + (diag.Agent == null ? "MISSING" : diag.Agent.name)
                  + "  line=" + (diag.NavigationLine == null ? "MISSING" : diag.NavigationLine.name)
                  + "\nSave the scene, then build. Logs land in Application.persistentDataPath on the device.");
    }

    // ---------------------------------------------------------- play mode ----

    [MenuItem("Tools/AR Debug/Play Mode/Use Recording File...")]
    static void UseRecording()
    {
        var picked = EditorUtility.OpenFilePanel("Vuforia session recording brought back from the site", "", "");
        if (string.IsNullOrEmpty(picked))
            return;

        // Vuforia stores this as a project-relative path when the file lives
        // inside the project, which is what the sample recordings use.
        var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        var normalised = picked.Replace('\\', '/');
        if (normalised.StartsWith(projectRoot + "/"))
            normalised = normalised.Substring(projectRoot.Length + 1);

        if (SetPlayMode("RECORD", normalised))
            Debug.Log("Vuforia Play Mode -> RECORDING\npath: " + normalised
                      + "\nPress Play: the session from the site is replayed, tracking and all.");
    }

    [MenuItem("Tools/AR Debug/Play Mode/Use Webcam")]
    static void UseWebcam()
    {
        if (SetPlayMode("WEBCAM", null))
            Debug.Log("Vuforia Play Mode -> WEBCAM");
    }

    [MenuItem("Tools/AR Debug/Play Mode/Use Simulator")]
    static void UseSimulator()
    {
        if (SetPlayMode("SIMULAT", null))
            Debug.Log("Vuforia Play Mode -> SIMULATOR (move with the simulator input in the Game view)");
    }

    [MenuItem("Tools/AR Debug/Play Mode/Report Current")]
    static void ReportPlayMode()
    {
        var so = LoadConfig();
        if (so == null)
            return;

        var mode = so.FindProperty("playmode.playModeType");
        var path = so.FindProperty("playmode.mRecordingPath");

        Debug.Log("Vuforia Play Mode = " + (mode == null ? "?" : NameOf(mode))
                  + "\nrecording path = " + (path == null ? "?" : path.stringValue));
    }

    /// <summary>
    /// Sets the play mode by enum member NAME rather than by a hard-coded
    /// integer: the numeric order of Vuforia's PlayModeType is not part of its
    /// public contract and has changed between releases.
    /// </summary>
    static bool SetPlayMode(string nameFragment, string recordingPath)
    {
        var so = LoadConfig();
        if (so == null)
            return false;

        var mode = so.FindProperty("playmode.playModeType");
        if (mode == null)
        {
            Debug.LogError("ArDebugSetup: 'playmode.playModeType' not found in " + kConfigPath);
            return false;
        }

        var index = -1;
        for (var i = 0; i < mode.enumNames.Length; i++)
        {
            if (mode.enumNames[i].ToUpperInvariant().Contains(nameFragment))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Debug.LogError("ArDebugSetup: no play mode matching '" + nameFragment
                           + "'. Available: " + string.Join(", ", mode.enumNames));
            return false;
        }

        mode.enumValueIndex = index;

        if (recordingPath != null)
        {
            var path = so.FindProperty("playmode.mRecordingPath");
            if (path == null)
            {
                Debug.LogError("ArDebugSetup: 'playmode.mRecordingPath' not found in " + kConfigPath);
                return false;
            }

            path.stringValue = recordingPath;
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return true;
    }

    static SerializedObject LoadConfig()
    {
        var asset = AssetDatabase.LoadAssetAtPath<Object>(kConfigPath);
        if (asset == null)
        {
            Debug.LogError("ArDebugSetup: cannot load " + kConfigPath);
            return null;
        }

        return new SerializedObject(asset);
    }

    static string NameOf(SerializedProperty enumProp)
    {
        var i = enumProp.enumValueIndex;
        return i >= 0 && i < enumProp.enumNames.Length ? enumProp.enumNames[i] : i.ToString();
    }
}
