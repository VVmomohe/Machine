using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class TextureAtlasUnpacker : EditorWindow
{
    [MenuItem("Tools/Auto-Match Atlas Unpacker (智能分组切图器)")]
    public static void ShowWindow()
    {
        TextureAtlasUnpacker window = GetWindow<TextureAtlasUnpacker>("智能分组切图器");
        window.minSize = new Vector2(480, 260);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Label("智能多元素序列帧 - 分组导出工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "功能说明：\n" +
            "1. 本工具会自动识别图集中的前缀（如 fireball、pot_particles 等）。\n" +
            "2. 相同前缀的元素会被归为一组，并分别导出成一张独立的 _Uniform.png 图集。\n" +
            "3. 组内所有帧会自动还原旋转、自动对齐中心、并统一至该组的最大原始尺寸！", 
            MessageType.Info
        );

        EditorGUILayout.Space();

        Texture2D selectedTexture = Selection.activeObject as Texture2D;
        string dataFilePath = "";
        bool dataFileFound = false;

        if (selectedTexture != null)
        {
            string texPath = AssetDatabase.GetAssetPath(selectedTexture);
            string directory = Path.GetDirectoryName(texPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(texPath);

            string[] possibleExtensions = { ".atlas", ".txt" };
            foreach (var ext in possibleExtensions)
            {
                string checkPath = Path.Combine(directory, fileNameWithoutExtension + ext);
                if (File.Exists(checkPath))
                {
                    dataFilePath = checkPath;
                    dataFileFound = true;
                    break;
                }
            }

            if (dataFileFound)
            {
                GUI.color = Color.green;
                GUILayout.Label($"已匹配数据: {Path.GetFileName(dataFilePath)}", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"未在同级目录下找到与 {selectedTexture.name} 同名的数据文件！", EditorStyles.wordWrappedLabel);
                GUI.color = Color.white;
            }
        }
        else
        {
            GUI.color = Color.red;
            GUILayout.Label("请先在 Project 视图中选中一张大图！", EditorStyles.centeredGreyMiniLabel);
            GUI.color = Color.white;
        }

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(selectedTexture == null || !dataFileFound);
        if (GUILayout.Button("🔥 智能分组并按组导出序列图", GUILayout.Height(50)))
        {
            ExecuteGroupPackAndSlice(selectedTexture, dataFilePath);
        }
        EditorGUI.EndDisabledGroup();
    }

    private static void ExecuteGroupPackAndSlice(Texture2D selectedTexture, string dataFilePath)
    {
        string assetPath = AssetDatabase.GetAssetPath(selectedTexture);
        TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (textureImporter == null) return;

        if (!textureImporter.isReadable)
        {
            textureImporter.isReadable = true;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        string atlasData = File.ReadAllText(dataFilePath);
        string[] lines = atlasData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // 1. 解析所有子图数据
        List<TempSpriteData> tempDataList = ParseAtlasForPacking(lines);
        if (tempDataList.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "未能解析到任何切片数据！", "OK");
            return;
        }

        // 2. 将数据按组分类 (例如: fireball, pot_fx, pot_particles, barrel)
        Dictionary<string, List<TempSpriteData>> groups = new Dictionary<string, List<TempSpriteData>>();
        foreach (var data in tempDataList)
        {
            string groupName = GetGroupName(data.name);
            if (!groups.ContainsKey(groupName))
            {
                groups[groupName] = new List<TempSpriteData>();
            }
            groups[groupName].Add(data);
        }

        string dir = Path.GetDirectoryName(assetPath);
        string baseName = Path.GetFileNameWithoutExtension(assetPath);
        int origTexHeight = selectedTexture.height;
        List<string> generatedPaths = new List<string>();

        // 3. 遍历每个组，单独生成该组的 Uniform 合图
        foreach (var kvp in groups)
        {
            string gName = kvp.Key;
            List<TempSpriteData> groupSprites = kvp.Value;

            // 获取该组的最大原始尺寸作为该组的单格画布大小
            int uniformW = 0;
            int uniformH = 0;
            foreach (var s in groupSprites)
            {
                if (s.originalW > uniformW) uniformW = Mathf.RoundToInt(s.originalW);
                if (s.originalH > uniformH) uniformH = Mathf.RoundToInt(s.originalH);
            }

            if (uniformW <= 0 || uniformH <= 0)
            {
                uniformW = 128;
                uniformH = 128;
            }

            // 计算该组新图的网格行列
            int count = groupSprites.Count;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / columns);

            int newWidth = columns * uniformW;
            int newHeight = rows * uniformH;

            // 创建该组的透明画布
            Texture2D groupTex = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            Color[] clearColors = new Color[newWidth * newHeight];
            for (int i = 0; i < clearColors.Length; i++) clearColors[i] = Color.clear;
            groupTex.SetPixels(clearColors);

            List<SpriteMetaData> metaList = new List<SpriteMetaData>();

            // 填充该组的所有子图
            for (int i = 0; i < count; i++)
            {
                var data = groupSprites[i];
                int col = i % columns;
                int row = rows - 1 - (i / columns);

                int targetX = col * uniformW;
                int targetY = row * uniformH;

                int physW = Mathf.RoundToInt(data.isRotated ? data.bounds.height : data.bounds.width);
                int physH = Mathf.RoundToInt(data.isRotated ? data.bounds.width : data.bounds.height);

                int srcX = Mathf.RoundToInt(data.bounds.x);
                int srcY = origTexHeight - physH - Mathf.RoundToInt(data.bounds.y);

                srcX = Mathf.Clamp(srcX, 0, selectedTexture.width - physW);
                srcY = Mathf.Clamp(srcY, 0, selectedTexture.height - physH);

                Color[] pixels = selectedTexture.GetPixels(srcX, srcY, physW, physH);

                if (data.isRotated)
                {
                    pixels = RotateMatrixCounterClockwise(pixels, physW, physH);
                    int temp = physW;
                    physW = physH;
                    physH = temp;
                }

                int drawX = targetX + Mathf.RoundToInt(data.offsets.x);
                int drawY = targetY + Mathf.RoundToInt(data.offsets.y);

                if (drawX + physW > targetX + uniformW) physW = (targetX + uniformW) - drawX;
                if (drawY + physH > targetY + uniformH) physH = (targetY + uniformH) - drawY;

                if (physW > 0 && physH > 0)
                {
                    groupTex.SetPixels(drawX, drawY, physW, physH, pixels);
                }

                // 记录 Sprite 切片元数据
                SpriteMetaData meta = new SpriteMetaData();
                meta.name = Path.GetFileName(data.name); // 仅保留最后的名字
                meta.rect = new Rect(targetX, targetY, uniformW, uniformH);
                meta.alignment = (int)SpriteAlignment.Center;
                meta.pivot = new Vector2(0.5f, 0.5f);
                metaList.Add(meta);
            }

            groupTex.Apply();

            // 保存当前组的 PNG
            string outPngName = $"{baseName}_{gName}_Uniform.png";
            string outPngPath = Path.Combine(dir, outPngName);

            byte[] pngBytes = groupTex.EncodeToPNG();
            File.WriteAllBytes(outPngPath, pngBytes);
            DestroyImmediate(groupTex);

            AssetDatabase.Refresh();

            // 应用 Sprite 导入切图设置
            TextureImporter importer = AssetImporter.GetAtPath(outPngPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.isReadable = true;
                importer.spritesheet = metaList.ToArray();
                AssetDatabase.ImportAsset(outPngPath, ImportAssetOptions.ForceUpdate);
            }

            generatedPaths.Add(outPngName);
        }

        string resultMsg = "🎉 分组导出成功！已生成以下序列帧大图：\n\n" + string.Join("\n", generatedPaths);
        EditorUtility.DisplayDialog("多序列导出成功", resultMsg, "太棒了");
    }

    // 智能提取组名方法
    private static string GetGroupName(string fullName)
    {
        // 1. 如果包含 '/'，直接取 '/' 前面的部分（如 fireball/fireball_001 -> fireball）
        if (fullName.Contains("/"))
        {
            return fullName.Split('/')[0];
        }
        
        // 2. 如果包含下划线 '_'，取最前面的非数字单词作为组名
        if (fullName.Contains("_"))
        {
            string[] parts = fullName.Split('_');
            if (parts.Length > 0)
            {
                // 防止类似 barrel_back 这种被分成 barrel
                // 如果后面跟着的是 back、front 等，可合并；若后边是数字直接返回第一段
                if (int.TryParse(parts[parts.Length - 1], out _))
                {
                    return parts[0]; // 例如 pot_particles_001 -> pot
                }
                return parts[0]; // 默认取第一截
            }
        }

        return "Misc"; // 其它无法识别的归为杂项
    }

    private static Color[] RotateMatrixCounterClockwise(Color[] srcPixels, int width, int height)
    {
        Color[] dstPixels = new Color[srcPixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIndex = y * width + x;
                int dstX = y;
                int dstY = width - 1 - x;
                int dstIndex = dstY * height + dstX;
                dstPixels[dstIndex] = srcPixels[srcIndex];
            }
        }
        return dstPixels;
    }

    private class TempSpriteData
    {
        public string name;
        public Rect bounds;
        public Vector4 offsets;
        public float originalW;
        public float originalH;
        public bool isRotated;
    }

    private static List<TempSpriteData> ParseAtlasForPacking(string[] lines)
    {
        List<TempSpriteData> list = new List<TempSpriteData>();
        string currentName = "";
        Rect bounds = new Rect();
        Vector4 offsets = Vector4.zero;
        bool isRotated = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.EndsWith(".webp") || line.EndsWith(".png") || line.EndsWith(".jpg") || 
                line.StartsWith("size:") || line.StartsWith("filter:") || line.StartsWith("format:"))
            {
                continue;
            }

            if (!line.Contains(":"))
            {
                if (!string.IsNullOrEmpty(currentName))
                {
                    list.Add(new TempSpriteData {
                        name = currentName,
                        bounds = bounds,
                        offsets = offsets,
                        originalW = offsets.z,
                        originalH = offsets.w,
                        isRotated = isRotated
                    });
                }
                currentName = line; // 保持原始路径结构，方便提取组名
                isRotated = false;
            }
            else
            {
                string[] kv = line.Split(':');
                if (kv.Length < 2) continue;
                string key = kv[0].Trim();
                string val = kv[1].Trim();

                if (key == "bounds")
                {
                    string[] tokens = val.Split(',');
                    bounds = new Rect(int.Parse(tokens[0]), int.Parse(tokens[1]), int.Parse(tokens[2]), int.Parse(tokens[3]));
                }
                else if (key == "offsets")
                {
                    string[] tokens = val.Split(',');
                    offsets = new Vector4(int.Parse(tokens[0]), int.Parse(tokens[1]), int.Parse(tokens[2]), int.Parse(tokens[3]));
                }
                else if (key == "rotate")
                {
                    isRotated = val.Contains("90") || val.ToLower().Contains("true");
                }
            }
        }

        if (!string.IsNullOrEmpty(currentName))
        {
            list.Add(new TempSpriteData {
                name = currentName,
                bounds = bounds,
                offsets = offsets,
                originalW = offsets.z,
                originalH = offsets.w,
                isRotated = isRotated
            });
        }

        return list;
    }
}