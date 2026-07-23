using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace CG.MagicMenu
{
    public static class Extensions
    {
        /// <summary>
        /// Checks whether or not the object has anything to do with prefabs.
        /// </summary>
        /// <param name="go"></param>
        /// <returns></returns>
        public static bool IsPrefab(this GameObject go)
        {
#if UNITY_2018_3_OR_NEWER
            return PrefabUtility.IsPartOfAnyPrefab(go);
#else
	    return PrefabUtility.GetPrefabType(go) != PrefabType.None;
#endif
        }

        public static bool IsBeingEditedInIsolatedPrefabMode(this GameObject obj)
        {
            return PrefabStageUtility.GetPrefabStage(obj) != null;
        }

        public static void OpenInPrefabMode(this GameObject prefabGameObject)
        {
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabGameObject)));
        }

        public static void CreatePrefab(this Menu menu)
        {
            // Keep track of the currently selected GameObject(s)
            GameObject[] objectArray = Selection.gameObjects;

            // Loop through every GameObject in the array above
            foreach (GameObject gameObject in objectArray)
            {
                string localPath = CreateFolder("Assets/Resources/MagicMenu/Prefabs/") + gameObject.name + ".prefab";

                // Make sure the file name is unique, in case an existing Prefab has the same name.
                localPath = AssetDatabase.GenerateUniqueAssetPath(localPath);

                // Create the new Prefab and log whether Prefab was saved successfully.            
                PrefabUtility.SaveAsPrefabAsset(gameObject, localPath, out bool prefabSuccess);

                if (prefabSuccess == true)
                {
                    Debug.Log(gameObject.name + " Prefab was saved successfully");

                    PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, localPath, InteractionMode.UserAction);
                }
                else
                {
                    Debug.Log(gameObject.name + "Prefab failed to save" + prefabSuccess);
                }
            }
        }

        private static string CreateFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);

                AssetDatabase.Refresh();
            }

            return path;
        }
    }
}
