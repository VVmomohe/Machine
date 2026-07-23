using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.U2D.Sprites; 

public class SpriteMergerWindow : EditorWindow
{
    private List<Texture2D> sourceTextures = new List<Texture2D>();
    private string outputName = "MergedSpriteAtlas";
    private int padding = 2; // 像素间距，防止导光/边缘溢出

    [MenuItem("Tools/Sprite Merger & Pack Tool")]
    public static void ShowWindow()
    {
        GetWindow<SpriteMergerWindow>("Sprite Merger");
    }

    private void OnGUI()
    {
        GUILayout.Label("Sprite Merger & Meta Generator", EditorStyles.boldLabel);
        outputName = EditorGUILayout.TextField("Output Name", outputName);
        padding = EditorGUILayout.IntField("Padding (Pixels)", padding);

        // 拖拽区域
        Event evt = Event.current;
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag & Drop Textures Here");

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    break;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        if (draggedObject is Texture2D tex)
                        {
                            if (!sourceTextures.Contains(tex))
                                sourceTextures.Add(tex);
                        }
                    }
                }
                break;
        }

        // 显示已选择的图片列表
        GUILayout.Space(10);
        GUILayout.Label($"Selected Textures ({sourceTextures.Count}):", EditorStyles.miniBoldLabel);
        if (sourceTextures.Count > 0)
        {
            if (GUILayout.Button("Clear All"))
            {
                sourceTextures.Clear();
            }
        }

        for (int i = 0; i < sourceTextures.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(sourceTextures[i].name, GUILayout.Width(200));
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                sourceTextures.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Merge and Pack Sprite Atlas", GUILayout.Height(40)))
        {
            if (sourceTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one texture to merge.", "OK");
                return;
            }
            MergeTextures();
        }
    }

    private void MergeTextures()
    {
        List<Texture2D> uncompressedTextures = new List<Texture2D>();

        try
        {
            // 1. 动态解锁并复制源贴图，转换为支持读写的 RGBA32 格式，避开压缩格式报错
            foreach (var tex in sourceTextures)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null && !ti.isReadable)
                {
                    ti.isReadable = true;
                    ti.SaveAndReimport();
                }

                // 创建一个未压缩的临时 Texture2D，并将源图片像素复制过去
                Texture2D uncompressedTex = DuplicateTextureAsRGBA32(tex);
                uncompressedTextures.Add(uncompressedTex);
            }

            // 2. 创建用于合并的纯 RGBA32 目标大图
            Texture2D atlas = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            Rect[] uvs = atlas.PackTextures(uncompressedTextures.ToArray(), padding, 2048); 

            if (uvs == null)
            {
                EditorUtility.DisplayDialog("Error", "Packing failed. Check console or reduce texture sizes.", "OK");
                return;
            }

            // 3. 将大图保存为 PNG 资产
            string directory = "Assets/MergedSprites";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string atlasPath = $"{directory}/{outputName}.png";
            byte[] bytes = atlas.EncodeToPNG(); // 此时大图必定是 RGBA32，100% 编码成功！
            File.WriteAllBytes(atlasPath, bytes);
            AssetDatabase.ImportAsset(atlasPath);

            // 4. 配置新合成大图的 Import Settings 并写入切分信息
            TextureImporter importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport(); // 刷新内部数据状态

                // 5. 使用官方推荐的 Factory 写入 Meta 切片
                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
                dataProvider.InitSpriteEditorDataProvider();

                // 6. 换算并构建全新的 SpriteRect 子切片数据
                List<SpriteRect> rects = new List<SpriteRect>();

                for (int i = 0; i < sourceTextures.Count; i++)
                {
                    Rect uv = uvs[i];
                    var spriteRect = new SpriteRect()
                    {
                        name = sourceTextures[i].name, 
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        rect = new Rect(
                            uv.x * atlas.width,
                            uv.y * atlas.height,
                            uv.width * atlas.width,
                            uv.height * atlas.height
                        ),
                        spriteID = GUID.Generate() 
                    };
                    rects.Add(spriteRect);
                }

                dataProvider.SetSpriteRects(rects.ToArray());
                dataProvider.Apply();

                // 7. 保存并重新导入资产，完成切分
                var assetImporter = dataProvider.targetObject as AssetImporter;
                assetImporter.SaveAndReimport();
            }

            // 释放临时生成的大图
            DestroyImmediate(atlas);
        }
        finally
        {
            // 8. 彻底清理内存中的临时未压缩贴图，防止内存泄漏
            foreach (var tempTex in uncompressedTextures)
            {
                if (tempTex != null)
                {
                    DestroyImmediate(tempTex);
                }
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", $"Sprite Atlas generated successfully!", "OK");
    }

    /// <summary>
    /// 核心修复函数：不管源图片在磁盘上被压缩成什么格式，
    /// 都在内存中安全复制出一份完全解压的、只读写的 RGBA32 临时纹理
    /// </summary>
    private Texture2D DuplicateTextureAsRGBA32(Texture2D source)
    {
        RenderTexture renderTex = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear
        );

        // 使用 GPU 将源图绘制到临时 RenderTexture 上（自动完成格式解压）
        Graphics.Blit(source, renderTex);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTex;

        // 从 RenderTexture 中读取不带压缩的原始像素，写入全新的 RGBA32 纹理
        Texture2D readableText = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readableText.name = source.name;
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();

        // 还原渲染状态并释放临时 RenderTexture
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);

        return readableText;
    }
}