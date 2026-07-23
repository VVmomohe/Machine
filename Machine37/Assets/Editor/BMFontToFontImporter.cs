#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SlotMachine.Editor
{
    /// <summary>
    /// 把 BMFont(.fnt) + 图集贴图 转成 Unity 原生 Custom Font（可直接拖到 Text / TextMesh）。
    /// 用法：在 Project 里选中 .fnt 文件 → 右键 / 菜单 Assets → Create Custom Font From BMFont。
    ///
    /// 注意：很多 Cocos 导出的 .fnt 把所有字形 id 写成同一个（如 110='n'），本导入器会
    /// 忽略原 id，按"图集帧从左到右"重新编码：前 10 帧 = 数字 '0'~'9'，
    /// 其后的帧按 EXTRA_CHAR_IDS 映射（默认 ',' '.' ' '）。可按项目调整。
    /// </summary>
    public static class BMFontToFontImporter
    {
        // 第 10 帧起的字符编码（依你素材顺序改）：',' '.' ' '
        static readonly int[] EXTRA_CHAR_IDS = { 44, 46, 32 };

        [MenuItem("Assets/Create Custom Font From BMFont")]
        static void MenuCreateFont()
        {
            var fnt = Selection.activeObject as TextAsset;
            string path = fnt == null ? "" : AssetDatabase.GetAssetPath(fnt);
            if (!path.EndsWith(".fnt"))
            {
                Debug.LogError("[BMFontToFontImporter] 请先选中一个 .fnt 文件");
                return;
            }
            BuildFont(path);
        }

        [MenuItem("Assets/Create Custom Font From BMFont", true)]
        static bool MenuValidate()
        {
            var fnt = Selection.activeObject as TextAsset;
            return fnt != null && AssetDatabase.GetAssetPath(fnt).EndsWith(".fnt");
        }

        public static void BuildFont(string fntPath)
        {
            var doc = new XmlDocument();
            doc.LoadXml(File.ReadAllText(fntPath));

            var common = doc.SelectSingleNode("/font/common");
            int lineHeight = int.Parse(common.Attributes["lineHeight"].Value);
            int scaleW = int.Parse(common.Attributes["scaleW"].Value);
            int scaleH = int.Parse(common.Attributes["scaleH"].Value);

            var page = doc.SelectSingleNode("/font/pages/page");
            string texFile = page.Attributes["file"].Value;
            string dir = Path.GetDirectoryName(fntPath).Replace("\\", "/");
            string texPath = (dir + "/" + texFile).Replace("\\", "/");

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogError("[BMFontToFontImporter] 找不到图集: " + texPath); return; }

            // 图集贴图：关 mipmap、开 alpha
            var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti != null)
            {
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }

            var chars = doc.SelectNodes("/font/chars/char");
            var infos = new List<CharacterInfo>();
            int i = 0;
            foreach (XmlNode c in chars)
            {
                int x = int.Parse(c.Attributes["x"].Value);
                int y = int.Parse(c.Attributes["y"].Value);
                int w = int.Parse(c.Attributes["width"].Value);
                int h = int.Parse(c.Attributes["height"].Value);
                int xo = int.Parse(c.Attributes["xoffset"].Value);
                int yo = int.Parse(c.Attributes["yoffset"].Value);
                int xa = int.Parse(c.Attributes["xadvance"].Value);

                // 重新编码字符：前 10 = 0~9；其后按 EXTRA_CHAR_IDS
                int id = (i < 10) ? (48 + i)
                        : (i - 10 < EXTRA_CHAR_IDS.Length ? EXTRA_CHAR_IDS[i - 10] : (48 + i));

                // UV（Unity 原点左下；.fnt 的 y 是左上）
                float u0 = x / (float)scaleW;
                float v0 = 1f - (y + h) / (float)scaleH;
                float u1 = (x + w) / (float)scaleW;
                float v1 = 1f - y / (float)scaleH;

                var ci = new CharacterInfo();
                ci.index = id;
                ci.uvBottomLeft = new Vector2(u0, v0);
                ci.uvTopRight = new Vector2(u1, v1);
                ci.minX = xo;
                ci.maxX = xo + w;
                ci.minY = -(yo + h);   // 字形底边（baseline 为 0）
                ci.maxY = -yo;         // 字形顶边
                ci.advance = xa;
                ci.glyphWidth = w;
                ci.glyphHeight = h;
                infos.Add(ci);
                i++;
            }

            // Material
            string matPath = (dir + "/" + Path.GetFileNameWithoutExtension(fntPath) + ".mat").Replace("\\", "/");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("UI/Default"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = tex;
            EditorUtility.SetDirty(mat);

            // Font
            string fontPath = (dir + "/" + Path.GetFileNameWithoutExtension(fntPath) + ".font").Replace("\\", "/");
            Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) { font = new Font(); AssetDatabase.CreateAsset(font, fontPath); }
            font.material = mat;
            font.characterInfo = infos.ToArray();
            var so = new SerializedObject(font);
            so.FindProperty("m_LineSpacing").floatValue = lineHeight;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(font);

            AssetDatabase.SaveAssets();
            Debug.Log($"[BMFontToFontImporter] 已生成 Custom Font: {fontPath}（{infos.Count} 字形）");
        }
    }
}
#endif
