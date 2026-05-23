#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// After Safe Mode or a new project state, Unity often leaves an empty Untitled scene open.
/// Open the AR face filter scene automatically when the active editor scene has no saved path.
/// </summary>
[InitializeOnLoad]
static class ArFaceFilterSceneRestore
{
    const string SceneAssetPath = "Assets/Scenes/ARFaceFilter.unity";

    static ArFaceFilterSceneRestore()
    {
        EditorApplication.delayCall += TryOpenDefaultScene;
    }

    static void TryOpenDefaultScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var active = EditorSceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(active.path))
            return;

        var fullPath = Path.Combine(Application.dataPath, "Scenes", "ARFaceFilter.unity");
        if (!File.Exists(fullPath))
            return;

        EditorSceneManager.OpenScene(SceneAssetPath, OpenSceneMode.Single);
        Debug.Log($"Opened default scene: {SceneAssetPath}");
    }
}
#endif
