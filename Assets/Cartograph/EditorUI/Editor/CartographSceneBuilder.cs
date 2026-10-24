using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI.Editor
{
    public static class CartographSceneBuilder
    {
        [MenuItem("CARTOWORDS/Create Editor Demo Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Playを停止してからエディターシーンを開いてください。"); return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string path = "Assets/Cartograph/EditorUI/CartographEditorDemo.unity";
            if (File.Exists(path))
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return;
            }
            // Save/cancel prompt above protects an untitled or modified current scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("Cartograph Editor");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<UIDocument>();
            host.AddComponent<CartographEditorView>();
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("Created " + path + ". Press Play, then choose a CARTOWORDS project folder.");
        }
    }
}
