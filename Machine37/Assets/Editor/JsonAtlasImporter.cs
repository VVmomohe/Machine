#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TexturePacker 标准 JSON 图集导入器（一键切图集）
///
/// 支持的 JSON 格式（自动检测）：
///   1. TexturePacker "JSON (hash)"  —— { "frames": { "name": { "frame":{x,y,w,h}, "rotated":..., "spriteSourceSize":{x,y,w,h}, "sourceSize":{w,h} } }, "meta": { "size":{w,h}, "image":"x.png" } }
///   2. TexturePacker "JSON (array)" —— { "frames": [ { "filename":"name", "frame":{x,y,w,h}, "rotated":..., ... } ], "meta": { "size":{w,h} } }
///   3. CocosAtlasImporter 的 cocosjson —— { "texture":{width,height}, "sprites":[ { "filename":..., "frame":{x,y,w,h}, ... } ] }
///   4. "Red 7 Exporter" 紧凑数组格式（本项目 ByThebay 图集用这个）——
///      { "images":["纹理基名"], "framerate":24,
///        "frames":[ [x,y,w,h,rotated,offsetX,offsetY,srcW,srcH,?,?], ... ],
///        "animations":{ "精灵名":{ "frames":[帧索引], "next":null }, ... } }
///      纹理由 images[0] 基名 + 扩展名查找（支持 png/webp/jpg...），尺寸从图片头读取。
///
/// 正确处理 rotated / trimmed / y轴翻转 / pivot。纹理支持 .png / .webp（后续统一转 png 亦可）。
///
/// 用法：把 .json + 纹理 拖入 Assets 任意目录 -> 选中文件夹 -> 菜单 Tools/TexturePacker/导入JSON图集
///       （或右键文件夹 -> TexturePacker/导入JSON图集）
/// </summary>
public class JsonAtlasImporter
{
    // ── 菜单 ──────────────────────────────────────────────
    [MenuItem("Tools/TexturePacker/导入选中文件夹的JSON图集 %&j")]
    [MenuItem("Assets/TexturePacker/导入选中文件夹的JSON图集", false, 20)]
    public static void ImportSelected()
    {
        string root = GetSelectedFolder();
        if (root == null)
        {
            EditorUtility.DisplayDialog("JSON 图集导入",
                "请先在 Project 窗口选中一个文件夹（或该文件夹里的任意文件）。", "知道了");
            return;
        }
        ImportFolder(root);
    }

    [MenuItem("Tools/TexturePacker/导入选中文件夹的JSON图集 %&j", true)]
    [MenuItem("Assets/TexturePacker/导入选中文件夹的JSON图集", true, 20)]
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

        var files = new List<string>();
        foreach (var f in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            files.Add(f.Replace('\\', '/'));

        if (files.Count == 0)
        {
            EditorUtility.DisplayDialog("JSON 图集导入", "目录下没有找到 *.json 图集文件：\n" + root, "知道了");
            return;
        }

        int atlasCount = 0, spriteCount = 0;
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string jsonPath = files[i];
                string raw = File.ReadAllText(jsonPath);
                AtlasFormat fmt = DetectFormat(raw);

                string texPath;
                if (fmt == AtlasFormat.Red7)
                {
                    string imgBase = GetRed7ImageBase(raw) ?? Path.GetFileNameWithoutExtension(jsonPath);
                    texPath = FindTexture(jsonPath, imgBase);
                }
                else
                {
                    texPath = FindPng(jsonPath);
                }

                string label = Path.GetFileName(texPath ?? jsonPath);
                if (EditorUtility.DisplayCancelableProgressBar(
                        "JSON 图集导入",
                        string.Format("({0}/{1}) {2}", i + 1, files.Count, label),
                        (float)(i + 1) / files.Count))
                {
                    Debug.LogWarning("[JsonAtlasImporter] 用户取消，已导入 " + atlasCount + " 个图集");
                    break;
                }

                if (string.IsNullOrEmpty(texPath) || !File.Exists(texPath))
                {
                    Debug.LogWarning("[JsonAtlasImporter] 找不到对应纹理，跳过: " + jsonPath);
                    continue;
                }

                int n = (fmt == AtlasFormat.Red7)
                    ? ProcessRed7(jsonPath, raw, texPath)
                    : ProcessFromJson(jsonPath, texPath);
                if (n > 0) { atlasCount++; spriteCount += n; }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(string.Format("[JsonAtlasImporter] 完成: {0} -> {1} 个图集, {2} 个精灵", root, atlasCount, spriteCount));
    }

    /// <summary>找 png：优先同名；否则尝试 json 中 meta.image 指定的文件名</summary>
    static string FindPng(string jsonPath)
    {
        string dir = Path.GetDirectoryName(jsonPath);
        string sameName = Path.ChangeExtension(jsonPath, ".png");
        if (File.Exists(sameName)) return sameName;

        try
        {
            string raw = File.ReadAllText(jsonPath);
            int mi = raw.IndexOf("\"image\"");
            if (mi >= 0)
            {
                int c = raw.IndexOf('"', mi + 7);
                int d = raw.IndexOf('"', c + 1);
                if (c >= 0 && d > c)
                {
                    string img = raw.Substring(c + 1, d - c - 1).Trim();
                    if (!string.IsNullOrEmpty(img))
                    {
                        string p = Path.Combine(dir, img);
                        if (!Path.HasExtension(p)) p += ".png";
                        if (File.Exists(p)) return p.Replace('\\', '/');
                    }
                }
            }
        }
        catch (System.Exception) { }
        return sameName;
    }

    // ═══════════════════════════════════════════════════════
    //  数据类
    // ═══════════════════════════════════════════════════════
    [System.Serializable] private class JVec2 { public int x, y; }
    [System.Serializable] private class JSize { public int w, h; }
    [System.Serializable] private class JRect { public int x, y, w, h; }
    [System.Serializable] private class JTexSize
    {
        public int width, height;
        public int w, h;
        public int TexW => width > 0 ? width : w;
        public int TexH => height > 0 ? height : h;
    }
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

    [System.Serializable] private class TpSize { public int w, h; public int width, height; }
    [System.Serializable] private class TpMeta { public TpSize size; }
    [System.Serializable] private class TpMetaWrap { public TpMeta meta; }
    [System.Serializable] private class TpFrame
    {
        public string filename;
        public JRect frame;
        public bool rotated;
        public bool trimmed;
        public JRect spriteSourceSize;
        public JSize sourceSize;
    }
    [System.Serializable] private class TpAtlasArray { public TpMeta meta; public TpFrame[] frames; }

    static int ProcessFromJson(string jsonPath, string pngPath)
    {
        string raw = File.ReadAllText(jsonPath);

        // 纹理尺寸：优先 meta.size，兼容 {w,h} / {width,height}
        int texW = 2048, texH = 2048;
        var mw = JsonUtility.FromJson<TpMetaWrap>(raw);
        if (mw?.meta?.size != null)
        {
            var s = mw.meta.size;
            if (s.width > 0 || s.w > 0) { texW = s.width > 0 ? s.width : s.w; texH = s.height > 0 ? s.height : s.h; }
        }

        var list = new List<SpriteMetaData>();

        int framesIdx = raw.IndexOf("\"frames\"");
        if (framesIdx < 0)
        {
            // 无 "frames" -> 当作 cocosjson（texture + sprites[]）
            var atlas = JsonUtility.FromJson<JAtlas>(raw);
            if (atlas?.sprites == null || atlas.sprites.Length == 0)
            {
                Debug.LogWarning("[JsonAtlasImporter] 无法识别 JSON 结构(无 frames/sprites): " + jsonPath);
                return 0;
            }
            if (atlas.texture != null && atlas.texture.TexH > 0) texH = atlas.texture.TexH;
            foreach (var sp in atlas.sprites) AddIfValid(list, ToSprite(sp, texH));
            return ApplySprites(pngPath, list);
        }

        // 跳过 "frames" 后的冒号与空白，判断是数组还是对象
        int p = framesIdx + 7;
        while (p < raw.Length && (raw[p] == ':' || char.IsWhiteSpace(raw[p]))) p++;
        if (p < raw.Length && raw[p] == '[')
        {
            // JSON (array)
            var arr = JsonUtility.FromJson<TpAtlasArray>(raw);
            if (arr?.frames == null || arr.frames.Length == 0)
            {
                Debug.LogWarning("[JsonAtlasImporter] frames 数组为空: " + jsonPath);
                return 0;
            }
            foreach (var fr in arr.frames) AddIfValid(list, ToSprite(fr, texH));
        }
        else
        {
            // JSON (hash)：把 "name": { ... } 转成 { "filename":"name", ... } 数组再解析
            string norm = NormalizeHashFrames(raw);
            if (norm == null)
            {
                Debug.LogWarning("[JsonAtlasImporter] 无法解析 frames 对象: " + jsonPath);
                return 0;
            }
            var atlas = JsonUtility.FromJson<JAtlas>(norm);
            if (atlas?.sprites == null || atlas.sprites.Length == 0)
            {
                Debug.LogWarning("[JsonAtlasImporter] frames 对象解析为空: " + jsonPath);
                return 0;
            }
            foreach (var sp in atlas.sprites) AddIfValid(list, ToSprite(sp, texH));
        }

        return ApplySprites(pngPath, list);
    }

    static void AddIfValid(List<SpriteMetaData> list, SpriteMetaData smd)
    {
        if (!string.IsNullOrEmpty(smd.name)) list.Add(smd);
    }

    static SpriteMetaData ToSprite(JSprite s, int texH)
    {
        var smd = default(SpriteMetaData);
        if (s.frame == null || s.frame.w <= 0 || s.frame.h <= 0) return smd;
        return BuildSprite(s.filename, s.frame.x, s.frame.y, s.frame.w, s.frame.h,
            s.rotated, s.trimmed,
            s.spriteSourceSize?.x ?? 0, s.spriteSourceSize?.y ?? 0,
            s.spriteSourceSize?.w ?? s.frame.w, s.spriteSourceSize?.h ?? s.frame.h,
            s.sourceSize?.w ?? s.frame.w, s.sourceSize?.h ?? s.frame.h,
            texH);
    }

    static SpriteMetaData ToSprite(TpFrame fr, int texH)
    {
        var smd = default(SpriteMetaData);
        if (fr.frame == null || fr.frame.w <= 0 || fr.frame.h <= 0) return smd;
        string name = string.IsNullOrEmpty(fr.filename) ? "sprite" : fr.filename;
        return BuildSprite(name, fr.frame.x, fr.frame.y, fr.frame.w, fr.frame.h,
            fr.rotated, fr.trimmed,
            fr.spriteSourceSize?.x ?? 0, fr.spriteSourceSize?.y ?? 0,
            fr.spriteSourceSize?.w ?? fr.frame.w, fr.spriteSourceSize?.h ?? fr.frame.h,
            fr.sourceSize?.w ?? fr.frame.w, fr.sourceSize?.h ?? fr.frame.h,
            texH);
    }

    /// <summary>把 { "frames": { "name": {...}, ... }, "meta": {...} } 转成
    /// { "texture": &lt;size&gt;, "sprites": [ {"filename":"name", ...}, ... ] }，
    /// 以便用 JsonUtility 解析（它不支持动态 key 的字典）。</summary>
    static string NormalizeHashFrames(string raw)
    {
        int framesIdx = raw.IndexOf("\"frames\"");
        int fb = raw.IndexOf('{', framesIdx);
        if (fb < 0) return null;
        string framesBody = ExtractBalanced(raw, fb);
        if (string.IsNullOrEmpty(framesBody)) return null;
        string inner = framesBody.Substring(1, framesBody.Length - 2); // 去掉外层 {}

        var entries = new List<string>();
        int i = 0;
        while (i < inner.Length)
        {
            if (inner[i] == '"')
            {
                int q2 = inner.IndexOf('"', i + 1);
                if (q2 < 0) break;
                string name = inner.Substring(i + 1, q2 - i - 1);
                int j = q2 + 1;
                while (j < inner.Length && (inner[j] == ':' || char.IsWhiteSpace(inner[j]))) j++;
                if (j < inner.Length && inner[j] == '{')
                {
                    string obj = ExtractBalanced(inner, j);
                    if (!string.IsNullOrEmpty(obj))
                    {
                        string objInner = obj.Substring(1, obj.Length - 2);
                        entries.Add("{ \"filename\":\"" + EscapeJson(name) + "\", " + objInner + " }");
                        i = j + obj.Length;
                        continue;
                    }
                }
            }
            i++;
        }

        // meta.size 作为 texture
        string sizeBody = "{ \"width\":2048, \"height\":2048 }";
        int mi = raw.IndexOf("\"meta\"");
        if (mi >= 0)
        {
            int mb = raw.IndexOf('{', mi);
            if (mb >= 0)
            {
                string metaBody = ExtractBalanced(raw, mb);
                int si = metaBody.IndexOf("\"size\"");
                if (si >= 0)
                {
                    int sb = metaBody.IndexOf('{', si);
                    if (sb >= 0)
                    {
                        string sz = ExtractBalanced(metaBody, sb);
                        if (!string.IsNullOrEmpty(sz)) sizeBody = sz;
                    }
                }
            }
        }

        return "{ \"texture\":" + sizeBody + ", \"sprites\":[ " + string.Join(", ", entries) + " ] }";
    }

    static string ExtractBalanced(string s, int openIdx)
    {
        if (openIdx < 0 || s[openIdx] != '{') return null;
        int depth = 0;
        for (int k = openIdx; k < s.Length; k++)
        {
            char c = s[k];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return s.Substring(openIdx, k - openIdx + 1);
            }
        }
        return null;
    }

    static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ═══════════════════════════════════════════════════════
    //  "Red 7 Exporter" 紧凑数组格式
    // ═══════════════════════════════════════════════════════
    enum AtlasFormat { Unknown, TP_Hash, TP_Array, CocosJson, Red7 }

    /// <summary>探测 JSON 属于哪种图集格式（只做轻量扫描，不整篇解析）。</summary>
    static AtlasFormat DetectFormat(string raw)
    {
        int imgIdx = raw.IndexOf("\"images\"");
        int fIdx = raw.IndexOf("\"frames\"");
        if (imgIdx >= 0 && fIdx >= 0)
        {
            int p = fIdx + 7;
            while (p < raw.Length && (raw[p] == ':' || char.IsWhiteSpace(raw[p]))) p++;
            if (p < raw.Length && raw[p] == '[')
            {
                int q = p + 1;
                while (q < raw.Length && char.IsWhiteSpace(raw[q])) q++;
                if (q < raw.Length && raw[q] == '[') return AtlasFormat.Red7;   // frames:[[...]] -> 数组的数组
            }
        }
        if (fIdx >= 0)
        {
            int p = fIdx + 7;
            while (p < raw.Length && (raw[p] == ':' || char.IsWhiteSpace(raw[p]))) p++;
            if (p < raw.Length && raw[p] == '[')
            {
                int q = p + 1;
                while (q < raw.Length && char.IsWhiteSpace(raw[q])) q++;
                return (q < raw.Length && raw[q] == '{') ? AtlasFormat.TP_Array : AtlasFormat.Red7;
            }
            if (p < raw.Length && raw[p] == '{') return AtlasFormat.TP_Hash;
        }
        if (raw.IndexOf("\"texture\"") >= 0 || raw.IndexOf("\"sprites\"") >= 0) return AtlasFormat.CocosJson;
        return AtlasFormat.Unknown;
    }

    /// <summary>取 Red7 的 images[0]（纹理基名）。</summary>
    static string GetRed7ImageBase(string raw)
    {
        int i = raw.IndexOf("\"images\"");
        if (i < 0) return null;
        int b = raw.IndexOf('[', i);
        if (b < 0) return null;
        int q1 = raw.IndexOf('"', b);
        if (q1 < 0) return null;
        int q2 = raw.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return raw.Substring(q1 + 1, q2 - q1 - 1);
    }

    /// <summary>按基名 + 常见图片扩展名在 json 同目录找纹理（优先 png）。</summary>
    static string FindTexture(string jsonPath, string baseName)
    {
        string dir = Path.GetDirectoryName(jsonPath);
        string[] exts = { ".png", ".webp", ".jpg", ".jpeg", ".tga", ".exr", ".psd", ".tif", ".tiff", ".bmp" };
        foreach (var e in exts)
        {
            string p = Path.Combine(dir, baseName + e);
            if (File.Exists(p)) return p.Replace('\\', '/');
        }
        return null;
    }

    /// <summary>从图片文件头读取高度（png / webp），读不到回退 2048。</summary>
    static int GetImageHeight(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                byte[] sig = br.ReadBytes(8);
                if (sig.Length >= 8 && sig[0] == 0x89 && sig[1] == 'P' && sig[2] == 'N' && sig[3] == 'G')
                {
                    fs.Seek(16, SeekOrigin.Begin);
                    byte[] wh = br.ReadBytes(8);
                    if (wh.Length == 8) return (wh[4] << 24) | (wh[5] << 16) | (wh[6] << 8) | wh[7];
                }
                if (sig.Length >= 4 && sig[0] == 'R' && sig[1] == 'I' && sig[2] == 'F' && sig[3] == 'F')
                {
                    fs.Seek(8, SeekOrigin.Begin);
                    byte[] four = br.ReadBytes(4);   // 'WEBP'
                    byte[] fmt = br.ReadBytes(4);
                    if (fmt.Length == 4 && fmt[0] == 'V' && fmt[1] == 'P' && fmt[2] == '8')
                    {
                        if (fmt[3] == 'X')
                        {
                            fs.Seek(24, SeekOrigin.Begin);
                            byte[] b3 = br.ReadBytes(3);
                            int w = (b3[0] | (b3[1] << 8) | (b3[2] << 16)) + 1;
                            byte[] b3b = br.ReadBytes(3);
                            int h = (b3b[0] | (b3b[1] << 8) | (b3b[2] << 16)) + 1;
                            return h;
                        }
                        else if (fmt[3] == ' ')
                        {
                            fs.Seek(26, SeekOrigin.Begin);
                            byte[] d = br.ReadBytes(4);
                            int w = (d[0] | (d[1] << 8)) & 0x3FFF;
                            int h = (d[2] | (d[3] << 8)) & 0x3FFF;
                            return h;
                        }
                        else if (fmt[3] == 'L')
                        {
                            fs.Seek(21, SeekOrigin.Begin);
                            byte[] d = br.ReadBytes(5);
                            ulong bits = 0;
                            for (int k = 0; k < 5; k++) bits |= (ulong)d[k] << (8 * k);
                            int w = (int)(bits & 0x3FFF) + 1;
                            int h = (int)((bits >> 14) & 0x3FFF) + 1;
                            return h;
                        }
                    }
                }
            }
        }
        catch (System.Exception) { }
        Debug.LogWarning("[JsonAtlasImporter] 无法读取纹理尺寸，回退 2048: " + path);
        return 2048;
    }

    // ── 极简 JSON 解析器（仅用于 Red7 的数组/动态 key 结构）──
    class JNode
    {
        public enum K { Obj, Arr, Str, Num, Bool, Null }
        public K kind;
        public Dictionary<string, JNode> o;
        public List<JNode> a;
        public string s;
        public double n;
        public bool b;
        public int AsInt() { return (int)System.Math.Round(n); }
        public JNode this[string key]
        {
            get { return (o != null && o.ContainsKey(key)) ? o[key] : null; }
        }
    }

    static JNode ParseJson(string t)
    {
        int i = 0;
        return ParseValue(t, ref i);
    }
    static void SkipWs(string t, ref int i)
    {
        while (i < t.Length)
        {
            char c = t[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
            else break;
        }
    }
    static JNode ParseValue(string t, ref int i)
    {
        SkipWs(t, ref i);
        if (i >= t.Length) return null;
        char c = t[i];
        if (c == '{') return ParseObj(t, ref i);
        if (c == '[') return ParseArr(t, ref i);
        if (c == '"') { var nd = new JNode { kind = JNode.K.Str, s = ParseStr(t, ref i) }; return nd; }
        if (c == 't') { i += 4; return new JNode { kind = JNode.K.Bool, b = true }; }
        if (c == 'f') { i += 5; return new JNode { kind = JNode.K.Bool, b = false }; }
        if (c == 'n') { i += 4; return new JNode { kind = JNode.K.Null }; }
        return ParseNum(t, ref i);
    }
    static JNode ParseObj(string t, ref int i)
    {
        var nd = new JNode { kind = JNode.K.Obj, o = new Dictionary<string, JNode>() };
        i++; // {
        while (true)
        {
            SkipWs(t, ref i);
            if (i >= t.Length || t[i] == '}') { i++; break; }
            string key = ParseStr(t, ref i);
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ':') i++;
            nd.o[key] = ParseValue(t, ref i);
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ',') i++;
        }
        return nd;
    }
    static JNode ParseArr(string t, ref int i)
    {
        var nd = new JNode { kind = JNode.K.Arr, a = new List<JNode>() };
        i++; // [
        while (true)
        {
            SkipWs(t, ref i);
            if (i >= t.Length || t[i] == ']') { i++; break; }
            nd.a.Add(ParseValue(t, ref i));
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ',') i++;
        }
        return nd;
    }
    static string ParseStr(string t, ref int i)
    {
        i++; // opening quote
        var sb = new System.Text.StringBuilder();
        while (i < t.Length)
        {
            char c = t[i++];
            if (c == '"') break;
            if (c == '\\' && i < t.Length)
            {
                char e = t[i++];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else sb.Append(e);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
    static JNode ParseNum(string t, ref int i)
    {
        int start = i;
        while (i < t.Length)
        {
            char c = t[i];
            if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
            else break;
        }
        var nd = new JNode { kind = JNode.K.Num };
        double v;
        if (double.TryParse(t.Substring(start, i - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v))
            nd.n = v;
        return nd;
    }

    static int ProcessRed7(string jsonPath, string raw, string texPath)
    {
        var root = ParseJson(raw);
        if (root == null || root.kind != JNode.K.Obj)
        {
            Debug.LogWarning("[JsonAtlasImporter] Red7: 无法解析 JSON: " + jsonPath);
            return 0;
        }
        if (!File.Exists(texPath))
        {
            Debug.LogWarning("[JsonAtlasImporter] Red7: 找不到纹理: " + texPath);
            return 0;
        }
        int texH = GetImageHeight(texPath);

        var framesV = root["frames"];
        if (framesV == null || framesV.kind != JNode.K.Arr || framesV.a.Count == 0)
        {
            Debug.LogWarning("[JsonAtlasImporter] Red7: frames 为空: " + jsonPath);
            return 0;
        }

        // 帧索引 -> 精灵名（来自 animations 的 frames:[index]）
        var nameByIndex = new Dictionary<int, string>();
        var anims = root["animations"];
        if (anims != null && anims.kind == JNode.K.Obj)
        {
            foreach (var kv in anims.o)
            {
                var fl = kv.Value["frames"];
                if (fl != null && fl.kind == JNode.K.Arr && fl.a.Count > 0)
                    nameByIndex[fl.a[0].AsInt()] = kv.Key;
            }
        }

        var list = new List<SpriteMetaData>();
        for (int idx = 0; idx < framesV.a.Count; idx++)
        {
            var fr = framesV.a[idx];
            if (fr.kind != JNode.K.Arr || fr.a.Count < 9) continue;

            int fx = fr.a[0].AsInt();
            int fy = fr.a[1].AsInt();
            int fw = fr.a[2].AsInt();
            int fh = fr.a[3].AsInt();
            if (fw <= 0 || fh <= 0) continue;

            bool rotated = fr.a.Count > 4 && fr.a[4].n != 0;
            int srcW = fr.a[7].AsInt();
            int srcH = fr.a[8].AsInt();
            bool trimmed = (srcW != fw || srcH != fh);

            // idx5,6 = 裁剪后精灵中心(相对原图)；未裁剪时恒为 w/2,h/2
            double cxp = fr.a.Count > 5 ? fr.a[5].n : fw / 2.0;
            double cyp = fr.a.Count > 6 ? fr.a[6].n : fh / 2.0;
            int sx = trimmed ? (int)System.Math.Round(cxp - fw / 2.0) : 0;
            int sy = trimmed ? (int)System.Math.Round(cyp - fh / 2.0) : 0;

            string name = nameByIndex.ContainsKey(idx) ? nameByIndex[idx] : ("frame_" + idx);
            list.Add(BuildSprite(name, fx, fy, fw, fh, rotated, trimmed, sx, sy, fw, fh, srcW, srcH, texH));
        }

        if (list.Count == 0)
        {
            Debug.LogWarning("[JsonAtlasImporter] Red7: 无有效帧: " + jsonPath);
            return 0;
        }
        return ApplySprites(texPath, list);
    }

    // ═══════════════════════════════════════════════════════
    //  共用：构建 SpriteMetaData 并写入 TextureImporter
    //  （逻辑与 CocosAtlasImporter 一致：处理 rotated / y轴翻转 / pivot）
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
        if (ti == null) { Debug.LogWarning("[JsonAtlasImporter] 无法获取 TextureImporter: " + pngPath); return 0; }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.spritePixelsPerUnit = 100;
        ti.spritesheet = list.ToArray();
        ti.SaveAndReimport();

        Debug.Log(string.Format("[JsonAtlasImporter] {0} -> {1} 精灵", Path.GetFileName(pngPath), list.Count));
        return list.Count;
    }

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
