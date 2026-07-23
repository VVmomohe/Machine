using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CG.MagicMenu
{
    using UnityEditor;
    [CustomEditor(typeof(Menu<>), true)]
    public class MenuEditor : Editor
    {
        private Menu menu;
        public override void OnInspectorGUI()
        {
            menu = target as Menu;

            base.OnInspectorGUI();

            GUILayout.Space(10);

            if (menu.gameObject.IsBeingEditedInIsolatedPrefabMode()) return;

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Cant Open Prefab in 'Play Mode'", MessageType.Info);
                return;
            }

            if (menu.gameObject.IsPrefab())
            {
                if (GUILayout.Button("Open Prefab"))
                {
                    //Debug.Log("Opening Prefab");
                    menu.gameObject.OpenInPrefabMode();
                }
            }
            else
            {
                if (GUILayout.Button("Create Prefab"))
                {
                    //Debug.Log("Creating Prefab");
                    menu.CreatePrefab();
                }
            }
        }
    }
}
