using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class SpriteSheetImporter
{
    [MenuItem("Assets/Slice Sprite Sheet from JSON")]
    private static void SliceSpriteSheet()
    {
        // 1. 获取选中的纹理
        Texture2D texture = Selection.activeObject as Texture2D;
        if (texture == null)
        {
            EditorUtility.DisplayDialog("错误", "请先在 Project 窗口中选中对应的图片（Texture2D）！", "确定");
            return;
        }

        string texturePath = AssetDatabase.GetAssetPath(texture);
        string jsonPath = Path.ChangeExtension(texturePath, ".json");

        if (!File.Exists(jsonPath))
        {
            EditorUtility.DisplayDialog("错误", $"未在同目录下找到匹配的 JSON 文件：\n{jsonPath}", "确定");
            return;
        }

        string jsonText = File.ReadAllText(jsonPath);

        // 2. 提取 "frames": [ ... ] 内部数据
        int framesStartIndex = jsonText.IndexOf("\"frames\":");
        if (framesStartIndex == -1)
        {
            EditorUtility.DisplayDialog("错误", "JSON 文件中未找到 'frames' 数据！", "确定");
            return;
        }

        int arrayStart = jsonText.IndexOf("[", framesStartIndex);
        int bracketCount = 0;
        int arrayEnd = -1;
        for (int i = arrayStart; i < jsonText.Length; i++)
        {
            if (jsonText[i] == '[') bracketCount++;
            else if (jsonText[i] == ']')
            {
                bracketCount--;
                if (bracketCount == 0)
                {
                    arrayEnd = i;
                    break;
                }
            }
        }

        if (arrayEnd == -1)
        {
            EditorUtility.DisplayDialog("错误", "解析 JSON 数组结构失败！", "确定");
            return;
        }

        string framesSegment = jsonText.Substring(arrayStart, arrayEnd - arrayStart + 1);

        // 3. 正则提取子切片数据
        string pattern = @"\[\s*([\d\.-]+)\s*,\s*([\d\.-]+)\s*,\s*([\d\.-]+)\s*,\s*([\d\.-]+)\s*,\s*([\d\.-]+)\s*,\s*([\d\.-]+)\s*,\s*([\d\.-]+)[^\]]*\]";
        MatchCollection matches = Regex.Matches(framesSegment, pattern);

        if (matches.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未能在 frames 中匹配到任何有效的切片坐标！", "确定");
            return;
        }

        // 4. 获取并配置 TextureImporter
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) return;

        bool wasReadable = importer.isReadable;
        importer.isReadable = true;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        AssetDatabase.ImportAsset(texturePath); 

        int texHeight = texture.height;
        SpriteMetaData[] metaData = new SpriteMetaData[matches.Count];

        // 5. 遍历切片
        for (int i = 0; i < matches.Count; i++)
        {
            Match match = matches[i];
            float x = float.Parse(match.Groups[1].Value);
            float y = float.Parse(match.Groups[2].Value);
            float w = float.Parse(match.Groups[3].Value);
            float h = float.Parse(match.Groups[4].Value);
            float regX = float.Parse(match.Groups[6].Value);
            float regY = float.Parse(match.Groups[7].Value);

            SpriteMetaData meta = new SpriteMetaData();
            
            // 【核心修复】强制加上 FX_ 前缀，彻底摧毁 Unity 运行时的数字缺省名冲突 Bug
            meta.name = $"FX_{texture.name}_{i}";
            
            // 翻转 Y 轴坐标
            float unityY = texHeight - y - h;
            meta.rect = new Rect(x, unityY, w, h);

            // 设置 Custom 中心锚点
            meta.alignment = (int)SpriteAlignment.Custom;
            meta.pivot = new Vector2(regX / w, 1.0f - (regY / h));

            metaData[i] = meta;
        }

        // 6. 应用切片并强制刷新硬盘缓存
        importer.spritesheet = metaData;
        importer.isReadable = wasReadable; 
        
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("成功", $"切片完成！共生成了 {matches.Count} 个带安全前缀的 Sprite 子图。", "太棒了");
    }
}