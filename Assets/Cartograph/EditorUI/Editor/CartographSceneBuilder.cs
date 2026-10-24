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
            const string path = "Assets/Cartograph/EditorUI/CartographEditorDemo.unity";
            if (File.Exists(path))
            {
                Debug.Log("Demo scene already exists: " + path);
                return;
            }
            // Additive: never replace or save over the user's current scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var host = new GameObject("Cartograph Editor");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<UIDocument>();
            host.AddComponent<CartographEditorView>();
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("Created " + path + ". Open it and press Play. Demo data is not persisted.");
        }
    }
}
