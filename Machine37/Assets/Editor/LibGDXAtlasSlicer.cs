using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class LibGDXAtlasDirectSlicer : EditorWindow
{
    class AtlasRegion
    {
        public string name;
        public int x, y, width, height;
        public bool rotated;
    }

    [MenuItem("Assets/⚡ 直接切图 (完美对齐旋转元素)", false, 2000)]
    public static void SliceFromContextMenu()
    {
        Texture2D texture = Selection.activeObject as Texture2D;
        if (texture == null) return;

        string assetPath = AssetDatabase.GetAssetPath(texture);
        string directory = Path.GetDirectoryName(assetPath);
        string fileNameNoExt = Path.GetFileNameWithoutExtension(assetPath);

        // 自动查找同名 .atlas 或 .txt 文件
        string atlasPath = Path.Combine(directory, fileNameNoExt + ".atlas");
        if (!File.Exists(atlasPath))
        {
            atlasPath = Path.Combine(directory, fileNameNoExt + ".txt");
        }

        if (!File.Exists(atlasPath))
        {
            EditorUtility.DisplayDialog("错误", $"在同目录下找不到同名的 .atlas 或 .txt 文件：\n{fileNameNoExt}", "确定");
            return;
        }

        string[] lines = File.ReadAllLines(atlasPath);
        List<SpriteMetaData> metaDataList = new List<SpriteMetaData>();
        
        int texHeight = texture.height;
        AtlasRegion currentRegion = null;

        foreach (string line in lines)
        {
            string t = line.Trim();
            if (string.IsNullOrEmpty(t)) continue;

            // 过滤文件头配置信息
            if (t.EndsWith(".webp") || t.EndsWith(".png") || t.EndsWith(".jpg") || t.EndsWith(".jpeg") ||
                t.StartsWith("size:") || t.StartsWith("format:") || t.StartsWith("filter:") || t.StartsWith("repeat:"))
            {
                continue;
            }

            if (t.Contains(":"))
            {
                if (currentRegion != null)
                {
                    if (t.StartsWith("bounds:"))
                    {
                        string[] parts = t.Substring(7).Split(',');
                        if (parts.Length == 4)
                        {
                            currentRegion.x = int.Parse(parts[0].Trim());
                            currentRegion.y = int.Parse(parts[1].Trim());
                            currentRegion.width = int.Parse(parts[2].Trim());
                            currentRegion.height = int.Parse(parts[3].Trim());
                        }
                    }
                    else if (t.StartsWith("rotate:"))
                    {
                        string v = t.Substring(7).Trim().ToLower();
                        if (v == "true" || v == "90")
                        {
                            currentRegion.rotated = true;
                        }
                    }
                }
            }
            else
            {
                // 新的 Sprite 区域开始
                currentRegion = new AtlasRegion();
                currentRegion.name = t;
                currentRegion.rotated = false;
                
                // 暂时用一个临时列表或直接在遇到下一个时处理
                // 这里我们用一个解析收集机制更稳妥
            }
        }

        // 重新梳理一遍解析逻辑，确保所有属性（bounds 和 rotate）不漏掉
        List<AtlasRegion> regions = new List<AtlasRegion>();
        AtlasRegion parsingRegion = null;

        foreach (string line in lines)
        {
            string t = line.Trim();
            if (string.IsNullOrEmpty(t)) continue;

            if (t.EndsWith(".webp") || t.EndsWith(".png") || t.EndsWith(".jpg") || t.EndsWith(".jpeg") ||
                t.StartsWith("size:") || t.StartsWith("format:") || t.StartsWith("filter:") || t.StartsWith("repeat:"))
            {
                continue;
            }

            if (!t.Contains(":"))
            {
                if (parsingRegion != null) regions.Add(parsingRegion);
                parsingRegion = new AtlasRegion { name = t };
            }
            else if (parsingRegion != null)
            {
                if (t.StartsWith("bounds:"))
                {
                    string[] parts = t.Substring(7).Split(',');
                    if (parts.Length == 4)
                    {
                        parsingRegion.x = int.Parse(parts[0].Trim());
                        parsingRegion.y = int.Parse(parts[1].Trim());
                        parsingRegion.width = int.Parse(parts[2].Trim());
                        parsingRegion.height = int.Parse(parts[3].Trim());
                    }
                }
                else if (t.StartsWith("rotate:"))
                {
                    string v = t.Substring(7).Trim().ToLower();
                    if (v == "true" || v == "90") parsingRegion.rotated = true;
                }
            }
        }
        if (parsingRegion != null) regions.Add(parsingRegion);

        // 生成 Unity 切片数据
        foreach (var reg in regions)
        {
            SpriteMetaData smd = new SpriteMetaData();
            smd.name = reg.name;

            int rectX = reg.x;
            int rectY, rectW, rectH;

            // 🌟 核心修复：如果该元素带有旋转标记，切片框的宽高需要适应图集中的实际占用形态
            if (reg.rotated)
            {
                // 在图集中旋转90度后，实际纹理占用的宽是 height，高是 width
                rectW = reg.height;
                rectH = reg.width;
            }
            else
            {
                rectW = reg.width;
                rectH = reg.height;
            }

            // 转换坐标系 (LibGDX 左上角原点 -> Unity 左下角原点)
            rectY = texHeight - reg.y - rectH;

            smd.rect = new Rect(rectX, rectY, rectW, rectH);
            smd.pivot = new Vector2(0.5f, 0.5f);
            
            metaDataList.Add(smd);
        }

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("无法获取 TextureImporter！");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritesheet = metaDataList.ToArray();
        
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        EditorUtility.DisplayDialog("成功", $"图集切片完成！共切出 {metaDataList.Count} 个 Sprite。\n已自动修正旋转元素的剪裁范围，不再错位！", "确定");
    }

    [MenuItem("Assets/⚡ 直接切图 (完美对齐旋转元素)", true)]
    public static bool ValidateSliceFromContextMenu()
    {
        return Selection.activeObject is Texture2D;
    }
}