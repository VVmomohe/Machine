using System.IO;
using UnityEngine;

namespace CG.MagicMenu
{    
    using UnityEditor;

    [CustomEditor(typeof(Readme))]
    [InitializeOnLoad]
    public class ReadmeEditor : Editor
    {
        static string s_ShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";

        const float k_Space = 16f;
        static ReadmeEditor() => EditorApplication.delayCall += SelectReadmeAutomatically;

        static void SelectReadmeAutomatically()
        {
            if (!SessionState.GetBool(s_ShowedReadmeSessionStateName, false))
            {
                var readme = SelectReadme();
                SessionState.SetBool(s_ShowedReadmeSessionStateName, true);
            }
        }

        static Readme SelectReadme()
        {
            var ids = AssetDatabase.FindAssets("Readme t:Readme");
            if (ids.Length == 1)
            {
                var readmeObject = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(ids[0]));

                Selection.objects = new Object[] { readmeObject };

                return (Readme)readmeObject;
            }
            else
            {
                Debug.Log("Couldn't find a readme");
                return null;
            }
        }

        protected override void OnHeaderGUI()
        {
            var readme = (Readme)target;

            Init();

            GUILayout.Space(20);

            if (readme.icon != null)
            {
                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.UpperCenter;
                GUILayout.Label(readme.icon, style);
            }

            GUILayout.BeginVertical();

            GUILayout.Label(readme.title, TitleStyle);

            GUILayout.EndVertical();
        }

        public override void OnInspectorGUI()
        {
            var readme = (Readme)target;

            Init();

            RenderMainContent(readme);

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            if (GUILayout.Button("Get Started", GUILayout.Height(40)))
            {
                //open 'GetStarted.pdf'
                string path = "/Plugins/Magic Menu Free/Documentation/" + "GettingStartedGuide.pdf";
                string finalPath = Path.Combine(Application.dataPath + path);
                Application.OpenURL(finalPath);
            }
        }

        private void RenderMainContent(Readme readme)
        {
            foreach (var section in readme.sections)
            {
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                if (!string.IsNullOrEmpty(section.heading))
                {
                    GUILayout.Label(section.heading, HeadingStyle);
                }

                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                if (!string.IsNullOrEmpty(section.text))
                {
                    GUILayout.Label(section.text, BodyStyle);
                }

                if (!string.IsNullOrEmpty(section.linkText))
                {
                    if (LinkLabel(new GUIContent(section.linkText)))
                    {
                        Application.OpenURL(section.url);
                    }
                }
                GUILayout.Space(k_Space);
            }
        }

        #region Style

        bool m_Initialized;
        GUIStyle LinkStyle { get { return m_LinkStyle; } }
        [SerializeField] GUIStyle m_LinkStyle;
        GUIStyle TitleStyle { get { return m_TitleStyle; } }
        [SerializeField] GUIStyle m_TitleStyle;
        GUIStyle HeadingStyle { get { return m_HeadingStyle; } }
        [SerializeField] GUIStyle m_HeadingStyle;
        GUIStyle BodyStyle { get { return m_BodyStyle; } }
        [SerializeField] GUIStyle m_BodyStyle;        

        void Init()
        {
            if (m_Initialized)
                return;
            m_BodyStyle = new GUIStyle(EditorStyles.label);
            m_BodyStyle.wordWrap = true;
            m_BodyStyle.fontSize = 14;
            m_BodyStyle.richText = true;

            m_TitleStyle = new GUIStyle(m_BodyStyle);
            m_TitleStyle.fontSize = 26;

            m_HeadingStyle = new GUIStyle(m_BodyStyle);
            m_HeadingStyle.fontStyle = FontStyle.Bold;
            m_HeadingStyle.fontSize = 18;

            m_LinkStyle = new GUIStyle(m_BodyStyle);
            m_LinkStyle.wordWrap = false;

            // Match selection color which works nicely for both light and dark skins
            m_LinkStyle.normal.textColor = new Color(0x00 / 255f, 0x78 / 255f, 0xDA / 255f, 1f);
            m_LinkStyle.stretchWidth = false;          

            m_Initialized = true;
        }

        bool LinkLabel(GUIContent label, params GUILayoutOption[] options)
        {
            var position = GUILayoutUtility.GetRect(label, LinkStyle, options);

            Handles.BeginGUI();
            Handles.color = LinkStyle.normal.textColor;
            Handles.DrawLine(new Vector3(position.xMin, position.yMax), new Vector3(position.xMax, position.yMax));
            Handles.color = Color.white;
            Handles.EndGUI();

            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);

            return GUI.Button(position, label, LinkStyle);
        }

        #endregion        
    }
}

