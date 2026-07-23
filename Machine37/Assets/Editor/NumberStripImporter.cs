using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// 数字条切分插件：把 num*_*.png 这类「数字 + 特殊符号」横向条带切成单格精灵，
/// 生成与 CocosAtlasImporter.cs 同格式的 .cocosjson（导入即多精灵）。
///
/// 命名规则（满足“数字用数字、特殊符号当 10,11...”）：
///   - 默认按位置命名：0,1,...,9,10,11...（数字条惯例 0-9 在前，符号在后）
///   - 若提供同名 <名>.chars.txt（字符序列），该位若是 '0'-'9' 用该字符，
///     否则按出现顺序编号 10,11,12...
///
/// 列数判定：文件名 num<N>_ 的 N 优先；否则读同名 .cols.txt；否则跳过。
///
/// 用法：Project 选中数字条 PNG（或所在文件夹） ->
///       Assets/右键 Cocos2d/切分数字条，或 Tools/Cocos2d/切分数字条
///       然后再跑 “Cocos2d/导入选中文件夹的图集” 才会真正写进纹理。
/// </summary>
public class NumberStripImporter
{
    private const string MENU = "Tools/Cocos2d/切分数字条(选中文件或文件夹)";
    private const int PRIORITY = 211;

    // ---- 与 CocosAtlasImporter.cs 完全一致的 JSON 结构 ----
    [System.Serializable] private class Vec2 { public int x; public int y; }
    [System.Serializable] private class SizeWH { public int w; public int h; }
    [System.Serializable] private class RectWH { public int x; public int y; public int w; public int h; }
    [System.Serializable] private class TexSize { public int width; public int height; }
    [System.Serializable] private class SpriteEntry
    {
        public string filename;
        public RectWH frame;
        public bool rotated;
        public bool trimmed;
        public RectWH spriteSourceSize;
        public SizeWH sourceSize;
    }
    [System.Serializable] private class CocosAtlas
    {
        public TexSize texture;
        public SpriteEntry[] sprites;
    }

    [MenuItem(MENU, false, PRIORITY)]
    public static void SliceSelected()
    {
        var objs = Selection.objects;
        if (objs == null || objs.Length == 0)
        {
            EditorUtility.DisplayDialog("数字条切分", "请先在 Project 窗口选中数字条 PNG 或所在文件夹", "OK");
            return;
        }

        var pngs = new List<string>();
        foreach (var o in objs)
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(p)) continue;
            if (Directory.Exists(p)) CollectStrips(p, pngs);
            else if (p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) && IsStrip(p)) pngs.Add(p);
        }

        if (pngs.Count == 0)
        {
            EditorUtility.DisplayDialog("数字条切分", "选中的内容里没有可识别的数字条 (num<N>_*.png，且无 plist/cocosjson)", "OK");
            return;
        }

        int total = 0;
        for (int i = 0; i < pngs.Count; i++)
        {
            EditorUtility.DisplayProgressBar("切分数字条", Path.GetFileName(pngs[i]), (float)i / pngs.Count);
            total += SliceOne(pngs[i]);
        }
        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        Debug.Log(string.Format("[NumberStripImporter] 完成: {0} 个数字条, {1} 个精灵已写入 .cocosjson（再跑『导入选中文件夹的图集』生效）", pngs.Count, total));
    }

    [MenuItem(MENU, true, PRIORITY)]
    public static bool SliceSelectedValidate()
    {
        foreach (var o in Selection.objects)
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(p) && (Directory.Exists(p) || (p.EndsWith(".png") && IsStrip(p))))
                return true;
        }
        return false;
    }

    static void CollectStrips(string dir, List<string> outList)
    {
        foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
            if (IsStrip(f)) outList.Add(f);
    }

    static bool IsStrip(string pngPath)
    {
        string name = Path.GetFileNameWithoutExtension(pngPath);
        if (!Regex.IsMatch(name, @"^num\d+_")) return false;
        if (File.Exists(Path.ChangeExtension(pngPath, ".plist"))) return false;
        if (File.Exists(Path.ChangeExtension(pngPath, ".cocosjson"))) return false;
        return true;
    }

    static int SliceOne(string pngPath)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
        if (tex == null) { Debug.LogWarning("[NumberStripImporter] 无法加载纹理: " + pngPath); return 0; }
        int W = tex.width, H = tex.height;

        string name = Path.GetFileNameWithoutExtension(pngPath);
        var m = Regex.Match(name, @"num(\d+)");
        int cols = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        string colsFile = Path.ChangeExtension(pngPath, ".cols.txt");
        if (cols <= 0 && File.Exists(colsFile) && int.TryParse(File.ReadAllText(colsFile).Trim(), out int c)) cols = c;
        if (cols <= 0) { Debug.LogWarning("[NumberStripImporter] 无法判断列数，跳过: " + pngPath); return 0; }

        int rows = 1;
        string rowsFile = Path.ChangeExtension(pngPath, ".rows.txt");
        if (File.Exists(rowsFile) && int.TryParse(File.ReadAllText(rowsFile).Trim(), out int r) && r > 0) rows = r;

        int cellW = W / cols, cellH = H / rows;

        // 可选字符映射
        string charsPath = Path.ChangeExtension(pngPath, ".chars.txt");
        string charsOrder = File.Exists(charsPath)
            ? File.ReadAllText(charsPath).Replace("\r", "").Replace("\n", "").Trim()
            : "";

        var entries = new List<SpriteEntry>();
        int special = 10;
        for (int i = 0; i < cols * rows; i++)
        {
            int cx = i % cols, cy = i / cols;
            string spName;
            if (!string.IsNullOrEmpty(charsOrder) && i < charsOrder.Length)
            {
                char ch = charsOrder[i];
                if (ch >= '0' && ch <= '9') spName = ch.ToString();
                else spName = (special++).ToString();
            }
            else
            {
                spName = i.ToString();   // 位置命名：数字 0-9，符号自然成 10,11...
            }

            entries.Add(new SpriteEntry
            {
                filename = spName,
                frame = new RectWH { x = cx * cellW, y = cy * cellH, w = cellW, h = cellH },
                rotated = false,
                trimmed = false,
                spriteSourceSize = new RectWH { x = 0, y = 0, w = cellW, h = cellH },
                sourceSize = new SizeWH { w = cellW, h = cellH },
            });
        }

        var atlas = new CocosAtlas
        {
            texture = new TexSize { width = W, height = H },
            sprites = entries.ToArray(),
        };
        string json = JsonUtility.ToJson(atlas, true);
        File.WriteAllText(Path.ChangeExtension(pngPath, ".cocosjson"), json);
        Debug.Log(string.Format("[NumberStripImporter] {0} -> {1} 格 (每格 {2}x{3})", name, cols * rows, cellW, cellH));
        return entries.Count;
    }
}
