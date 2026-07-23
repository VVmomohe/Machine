#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cocos2d-x TexturePacker 图集导入器（一键式）
///
/// 支持两种格式（自动检测）：
///   1. *.cocosjson  —— 由 convert_plist_to_tpsheet.py 预先生成
///   2. *.plist      —— Cocos2d-x 原始导出（Apple plist XML 格式），直接解析
///
/// 正确处理：rotated / trimmed / y轴翻转 / pivot
///
/// 用法：
///   把原始资源（.plist + .png）拖入 Unity Assets 任意目录 ->
///   选中该文件夹 -> 菜单 Tools/Cocos2d/导入选中文件夹的图集 (Ctrl+Alt+I)
///   或右键 -> Cocos2d/导入选中文件夹的图集
/// </summary>
public class CocosAtlasImporter
{
    // ── 菜单 ──────────────────────────────────────────────
    [MenuItem("Tools/Cocos2d/导入选中文件夹的图集 %&i")]
    [MenuItem("Assets/Cocos2d/导入选中文件夹的图集", false, 20)]
    public static void ImportSelected()
    {
        string root = GetSelectedFolder();
        if (root == null)
        {
            EditorUtility.DisplayDialog("Cocos 图集导入",
                "请先在 Project 窗口选中一个文件夹（或该文件夹里的任意文件）。", "知道了");
            return;
        }
        ImportFolder(root);
    }

    [MenuItem("Tools/Cocos2d/导入选中文件夹的图集 %&i", true)]
    [MenuItem("Assets/Cocos2d/导入选中文件夹的图集", true, 20)]
    private static bool ImportSelectedValidate()
    {
        return GetSelectedFolder() != null;
    }

    /// <summary>取当前选中项对应的文件夹（相对 Assets）</summary>
    private static string GetSelectedFolder()
    {
        var obj = Selection.activeObject;
        if (obj == null) return null;
        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path)) return null;
        if (Directory.Exists(path)) return path;
        if (File.Exists(path)) return Path.GetDirectoryName(path).Replace('\\', '/');
        return null;
    }

    // ── 主入口 ────────────────────────────────────────────
    public static void ImportFolder(string root)
    {
        if (!root.StartsWith("Assets/") && root != "Assets") root = "Assets/" + root.TrimStart('/');

        // 收集所有可处理的文件：优先 cocosjson，其次 plist（同一图集不重复处理）
        var toProcess = new List<string>();       // 要处理的文件路径列表
        var processedBases = new HashSet<string>(); // 已处理的 basename（不含扩展名）

        // 1) 先找 .cocosjson
        foreach (var f in Directory.GetFiles(root, "*.cocosjson", SearchOption.AllDirectories))
        {
            string baseName = Path.GetFileNameWithoutExtension(f);
            if (processedBases.Add(baseName)) toProcess.Add(f.Replace('\\', '/'));
        }
        // 2) 再找 .plist（跳过已由 cocosjson 覆盖的）
        foreach (var f in Directory.GetFiles(root, "*.plist", SearchOption.AllDirectories))
        {
            string baseName = Path.GetFileNameWithoutExtension(f);
            if (processedBases.Add(baseName)) toProcess.Add(f.Replace('\\', '/'));
        }

        if (toProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("Cocos 图集导入",
                "目录下没有找到 *.cocosjson 或 *.plist 图集文件：\n" + root +
                "\n\n请把 Cocos2d-x 的 .plist + .png 文件拖到该目录下。", "知道了");
            return;
        }

        int atlasCount = 0, spriteCount = 0;
        try
        {
            for (int i = 0; i < toProcess.Count; i++)
            {
                string filePath = toProcess[i];
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string pngPath = Path.ChangeExtension(filePath, ".png");

                string label = Path.GetFileName(pngPath);
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Cocos 图集导入",
                        string.Format("({0}/{1}) {2}", i + 1, toProcess.Count, label),
                        (float)(i + 1) / toProcess.Count))
                {
                    Debug.LogWarning("[CocosAtlasImporter] 用户取消，已导入 " + atlasCount + " 个图集");
                    break;
                }

                if (!File.Exists(pngPath)) { Debug.LogWarning("[CocosAtlasImporter] 找不到同名 png，跳过: " + filePath); continue; }

                int n = 0;
                if (ext == ".cocosjson")
                    n = ProcessFromCocosJson(filePath, pngPath);
                else if (ext == ".plist")
                    n = ProcessFromPlist(filePath, pngPath);

                if (n > 0) { atlasCount++; spriteCount += n; }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(string.Format("[CocosAtlasImporter] 完成: {0} -> {1} 个图集, {2} 个精灵", root, atlasCount, spriteCount));
    }

    // ═══════════════════════════════════════════════════════
    //  数据结构
    // ═══════════════════════════════════════════════════════
    private class SpriteEntry
    {
        public string filename;          // 精灵名称（含 .png 后缀）
        public int fx, fy, fw, fh;       // frame: 在纹理上的矩形
        public bool rotated;
        public bool trimmed;
        public int sx, sy, sw, sh;       // spriteSourceSize: 原图中 trim 后区域
        public int ow, oh;               // sourceSize: 原图完整尺寸
        public int texW, texH;           // 纹理宽高（从 metadata.size 读取）
    }

    // ═══════════════════════════════════════════════════════
    //  方案 A：读取 *.cocosjson（原有逻辑保留）
    // ═══════════════════════════════════════════════════════
    [System.Serializable] private class JVec2 { public int x, y; }
    [System.Serializable] private class JSize { public int w, h; }
    [System.Serializable] private class JRect { public int x, y, w, h; }
    [System.Serializable] private class JTexSize { public int width, height; }
    [System.Serializable] private class JSprite
    {
        public string filename;
        public JRect frame;
        public bool rotated;
        public bool trimmed;
        public JRect spriteSourceSize;
        public JSize sourceSize;
    }
    [System.Serializable] private class JAtlas { public JTexSize texture; public JSprite[] sprites; }

    static int ProcessFromCocosJson(string jsonPath, string pngPath)
    {
        var atlas = JsonUtility.FromJson<JAtlas>(File.ReadAllText(jsonPath));
        if (atlas?.sprites == null || atlas.sprites.Length == 0) return 0;

        int texH = (atlas.texture?.height > 0) ? atlas.texture.height : 2048;
        var list = new List<SpriteMetaData>();

        foreach (var s in atlas.sprites)
        {
            if (s.frame == null || s.frame.w <= 0 || s.frame.h <= 0) continue;
            list.Add(BuildSprite(s.filename, s.frame.x, s.frame.y, s.frame.w, s.frame.h,
                s.rotated, s.trimmed,
                s.spriteSourceSize?.x ?? 0, s.spriteSourceSize?.y ?? 0,
                s.spriteSourceSize?.w ?? s.frame.w, s.spriteSourceSize?.h ?? s.frame.h,
                s.sourceSize?.w ?? s.frame.w, s.sourceSize?.h ?? s.frame.h,
                texH));
        }
        return ApplySprites(pngPath, list);
    }

    // ═══════════════════════════════════════════════════════
    //  方案 B：直接解析 *.plist（新增核心功能）
    // ═══════════════════════════════════════════════════════
    /// <summary>
    /// 解析 Cocos2d-x TexturePacker 导出的 Apple plist XML。
    /// 格式示例：
    ///   &lt;key&gt;frames&lt;/key&gt;&lt;dict&gt;
    ///     &lt;key&gt;sprite.png&lt;/key&gt;&lt;dict&gt;
    ///       &lt;key&gt;frame&lt;/key&gt;&lt;string&gt;{{x,y},{w,h}}&lt;/string&gt;
    ///       &lt;key&gt;rotated&lt;/key&gt;&lt;true/false/&gt;
    ///       &lt;key&gt;sourceColorRect&lt;/key&gt;&lt;string&gt;{{x,y},{w,h}}&lt;/string&gt;
    ///       &lt;key&gt;sourceSize&lt;/key&gt;&lt;string&gt;{w,h}&lt;/string&gt;
    ///     &lt;/dict&gt;
    ///   &lt;/dict&gt;
    ///   &lt;key&gt;metadata&lt;/key&gt;&lt;dict&gt;
    ///     &lt;key&gt;size&lt;/key&gt;&lt;string&gt;{w,h}&lt;/string&gt;
    ///   &lt;/dict&gt;
    /// </summary>
    static int ProcessFromPlist(string plistPath, string pngPath)
    {
        var sprites = new List<SpriteEntry>();
        int texW = 2048, texH = 2048;

        try
        {
            var doc = new XmlDocument();
            doc.Load(plistPath);
            var plist = doc.SelectSingleNode("/plist/dict");
            if (plist == null) { Debug.LogWarning("[CocosAtlasImporter] 无效 plist 结构: " + plistPath); return 0; }

            // 遍历顶层 key-value 对
            var childNodes = plist.SelectNodes("*");
            string currentKey = null;

            for (int i = 0; i < childNodes.Count; i++)
            {
                var node = childNodes[i];
                if (node.Name == "key")
                    currentKey = node.InnerText.Trim();
                else if (currentKey != null && node.Name == "dict")
                {
                    // ── metadata 段 ──
                    if (currentKey == "metadata")
                    {
                        var metaDict = ParseDictNodes(node);
                        if (metaDict.ContainsKey("size"))
                        {
                            var sz = ParseRect(metaDict["size"]); // size 格式同 rect: {w,h}
                            if (sz.Length >= 2) { texW = sz[0]; texH = sz[1]; }
                        }
                    }
                    // ── frames 段 ──
                    else if (currentKey == "frames")
                    {
                        ParseFramesDict(node, sprites, texW, texH);
                    }
                    currentKey = null;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[CocosAtlasImporter] 解析 plist 失败: " + plistPath + "\n" + ex.Message);
            return 0;
        }

        if (sprites.Count == 0)
        {
            Debug.LogWarning("[CocosAtlasImporter] plist 中无有效帧数据: " + plistPath);
            return 0;
        }

        var list = new List<SpriteMetaData>();
        foreach (var s in sprites)
        {
            list.Add(BuildSprite(s.filename, s.fx, s.fy, s.fw, s.fh,
                s.rotated, s.trimmed, s.sx, s.sy, s.sw, s.sh, s.ow, s.oh, s.texH));
        }
        return ApplySprites(pngPath, list);
    }

    // ── Plist XML 辅助解析方法 ──────────────────────────

    /// <summary>把一个 dict 节点下的 key-value 读成字典。value 取 InnerText。</summary>
    static Dictionary<string, string> ParseDictNodes(XmlNode dictNode)
    {
        var result = new Dictionary<string, string>();
        var children = dictNode.SelectNodes("*");
        string key = null;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].Name == "key")
                key = children[i].InnerText.Trim();
            else if (key != null)
            {
                // true/false -> 转文本
                string val = children[i].Name == "true" ? "true"
                           : children[i].Name == "false" ? "false"
                           : children[i].InnerText.Trim();
                result[key] = val;
                key = null;
            }
        }
        return result;
    }

    /// <summary>解析 frames 大 dict（key=精灵名, value=精灵属性 dict）</summary>
    static void ParseFramesDict(XmlNode framesDict, List<SpriteEntry> sprites, int defTexW, int defTexH)
    {
        var children = framesDict.SelectNodes("*");
        string spriteName = null;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].Name == "key")
                spriteName = children[i].InnerText.Trim();
            else if (spriteName != null && children[i].Name == "dict")
            {
                var props = ParseDictNodes(children[i]);
                var entry = new SpriteEntry { filename = spriteName, texW = defTexW, texH = defTexH };

                // frame {{x,y},{w,h}}
                if (props.ContainsKey("frame"))
                {
                    var r = ParseRect(props["frame"]);
                    if (r.Length >= 4) { entry.fx = r[0]; entry.fy = r[1]; entry.fw = r[2]; entry.fh = r[3]; }
                }
                // rotated
                entry.rotated = props.ContainsKey("rotated") && props["rotated"] == "true";
                // sourceColorRect {{x,y},{w,h}} → spriteSourceSize
                if (props.ContainsKey("sourceColorRect"))
                {
                    var r = ParseRect(props["sourceColorRect"]);
                    if (r.Length >= 4) { entry.sx = r[0]; entry.sy = r[1]; entry.sw = r[2]; entry.sh = r[3]; entry.trimmed = true; }
                }
                else
                {
                    entry.sx = 0; entry.sy = 0; entry.sw = entry.fw; entry.sh = entry.fh;
                }
                // sourceSize {w,h}
                if (props.ContainsKey("sourceSize"))
                {
                    var r = ParseRect(props["sourceSize"]);
                    if (r.Length >= 2) { entry.ow = r[0]; entry.oh = r[1]; }
                }
                else
                {
                    entry.ow = entry.fw; entry.oh = entry.fh;
                }

                if (entry.fw > 0 && entry.fh > 0) sprites.Add(entry);
                spriteName = null;
            }
        }
    }

    /// <summary>解析 "{{x,y},{w,h}}" 或 "{w,h}" 格式的字符串为整数数组。</summary>
    static int[] ParseRect(string s)
    {
        // 去掉所有空白和花括号后按逗号分割
        s = s.Replace("{", "").Replace("}", "").Replace(" ", "");
        var parts = s.Split(',');
        var result = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p.Trim(), out int v)) result.Add(v);
        }
        return result.ToArray();
    }

    // ═══════════════════════════════════════════════════════
    //  共用：构建 SpriteMetaData 并写入 TextureImporter
    // ═══════════════════════════════════════════════════════
    static SpriteMetaData BuildSprite(string name, int fx, int fy, int fw, int fh,
        bool rotated, bool trimmed, int sx, int sy, int sw, int sh, int ow, int oh, int texH)
    {
        float x, y, w, h;
        if (!rotated)
        {
            x = fx;
            y = texH - fy - fh;
            w = fw;
            h = fh;
        }
        else
        {
            x = fx;
            y = texH - fy - fw;
            w = fh;
            h = fw;
        }

        var smd = new SpriteMetaData
        {
            name = name,
            rect = new Rect(x, y, w, h),
            alignment = (int)SpriteAlignment.Custom,
        };
        SetSpriteRotation(ref smd, rotated);

        float sW = ow > 0 ? ow : 1;
        float sH = oh > 0 ? oh : 1;
        float px = (sx + sw * 0.5f) / sW;
        float py = 1f - (sy + sh * 0.5f) / sH;
        smd.pivot = new Vector2(px, py);

        return smd;
    }

    static int ApplySprites(string pngPath, List<SpriteMetaData> list)
    {
        var ti = AssetImporter.GetAtPath(pngPath) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[CocosAtlasImporter] 无法获取 TextureImporter: " + pngPath); return 0; }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.spritePixelsPerUnit = 100;
        ti.spritesheet = list.ToArray();
        ti.SaveAndReimport();

        Debug.Log(string.Format("[CocosAtlasImporter] {0} -> {1} 精灵", Path.GetFileName(pngPath), list.Count));
        return list.Count;
    }

    // ── 反射设置 rotation ────────────────────────────────
    private static void SetSpriteRotation(ref SpriteMetaData smd, bool rotated)
    {
        var field = typeof(SpriteMetaData).GetField("rotation");
        if (field == null) return;
        object box = smd;
        if (field.FieldType == typeof(bool))
            field.SetValue(box, rotated);
        else
            field.SetValue(box, rotated ? 90f : 0f);
        smd = (SpriteMetaData)box;
    }
}
#endif
