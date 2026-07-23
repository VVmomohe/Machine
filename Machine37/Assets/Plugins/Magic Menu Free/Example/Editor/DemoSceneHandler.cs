using System;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace CG.MagicMenu.Example
{
    [InitializeOnLoad]
    public class DemoSceneHandler : UnityEditor.Editor
    {
        static DemoSceneHandler() => EditorApplication.delayCall += HandleDemoScene;

        private static void HandleDemoScene()
        {
            //Debug.Log("Adding Demo Scenes");

            if (!IsSceneAddedInBuildSettings("Main_menu"))
            {
                AddSceneToBuildSettings("Assets/Plugins/Magic Menu Free/Example/Scenes/Main_menu.unity");
            }

            if (!IsSceneAddedInBuildSettings("scene_gamePlay"))
            {
                AddSceneToBuildSettings("Assets/Plugins/Magic Menu Free/Example/Scenes/scene_gamePlay.unity");
            }
        }

        private static void AddSceneToBuildSettings(string pathToScene)
        {
            var original = EditorBuildSettings.scenes;
            var newSettings = new EditorBuildSettingsScene[original.Length + 1];
            Array.Copy(original, newSettings, original.Length);
            var sceneToAdd = new EditorBuildSettingsScene(pathToScene, true);
            newSettings[newSettings.Length - 1] = sceneToAdd;
            EditorBuildSettings.scenes = newSettings;
        }

        private static bool IsSceneAddedInBuildSettings(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var sceneNameInBuildSetting = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
                if (sceneNameInBuildSetting.Equals(sceneName))
                {
                    return true;
                }
            }
            return false;
        }
    }
}