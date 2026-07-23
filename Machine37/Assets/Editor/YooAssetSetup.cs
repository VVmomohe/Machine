#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using YooAsset.Editor;

/// <summary>
/// YooAsset 编辑器辅助：按资源类型建立包 + 打开官方构建窗口。
/// 适配 YooAsset v3（3.0.3-beta）：BundleCollectorSetting / BundleCollectorPackage / BundleCollectorGroup / BundleCollector。
///
/// 用法：
///   1. 菜单 Tools / YooAsset / Setup Type-Based Packages  -> 建立 Atlas/Audio/Font/Video/Misc 五个包
///   2. 菜单 Tools / YooAsset / Open Builder Window         -> 官方构建窗口，选平台 Build
/// </summary>
public class YooAssetSetup
{
    // 类型包 -> 该类型包含的扩展名（逗号分隔）
    static readonly Dictionary<string, string> TypeExtensions = new Dictionary<string, string>
    {
        { "Atlas",  ".png,.jpg,.jpeg,.tga,.psd,.bmp,.tif,.tiff,.exr,.hdr" },
        { "Audio",  ".mp3,.wav,.ogg,.aiff,.aif" },
        { "Font",   ".ttf,.otf,.fontsettings" },
        { "Video",  ".mp4,.mov,.avi,.webm" },
        { "Misc",   ".prefab,.mat,.shader,.asset,.unity,.bytes,.txt,.json,.csv,.xml" },
    };

    const string SettingPath = "Assets/AssetBundleCollectorSetting.asset";

    /// <summary>按完整资源路径寻址（地址 = Assets/.../xxx.png），与运行时 LoadAssetAsync 的 location 一致。</summary>
    public class AddressByFullPath : IAddressRule
    {
        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            return data.AssetPath.Replace('\\', '/');
        }
    }

    /// <summary>只收集指定扩展名，并排除编辑器/插件内部资源。</summary>
    public class CollectByExtensionRule : IAssetFilterRule
    {
        public string FindAssetType => EAssetFilterType.All.ToString();
        public bool IsCollectAsset(AssetFilterRuleData data)
        {
            var path = data.AssetPath.Replace('\\', '/');
            if (path.Contains("/Editor/")) return false;
            if (path.StartsWith("Assets/YooAsset")) return false;
            if (path.StartsWith("Assets/AssetBundles")) return false;
            if (string.IsNullOrEmpty(data.UserData)) return true;

            var lower = path.ToLowerInvariant();
            foreach (var e in data.UserData.Split(','))
            {
                var ext = e.Trim().ToLowerInvariant();
                if (ext.Length > 0 && lower.EndsWith(ext)) return true;
            }
            return false;
        }
    }

    [MenuItem("Tools/YooAsset/Setup Type-Based Packages")]
    public static void SetupTypeBasedPackages()
    {
        var setting = GetOrCreateSetting();
        setting.ClearAll();

        foreach (var kv in TypeExtensions)
        {
            var pkg = new BundleCollectorPackage
            {
                PackageName = kv.Key,
                EnableAddressable = true,
                SupportExtensionless = true,
                AutoCollectShaders = true,
            };

            var group = new BundleCollectorGroup
            {
                GroupName = "Default",
                ActiveRuleName = nameof(EnableGroup),
            };

            var collector = new BundleCollector
            {
                CollectPath = "Assets",
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFullPath),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectByExtensionRule),
                UserData = kv.Value,
            };

            group.Collectors.Add(collector);
            pkg.Groups.Add(group);
            setting.Packages.Add(pkg);
        }

        BundleCollectorSettingData.SaveFile();
        AssetDatabase.SaveAssets();
        Debug.Log("[YooAsset] 已建立 5 个类型包（按扩展名过滤，收集 Assets 下全部游戏资源）。打开 Builder Window 选平台构建即可。");
    }

    static BundleCollectorSetting GetOrCreateSetting()
    {
        if (!BundleCollectorSettingData.HasSettingAsset())
        {
            var s = ScriptableObject.CreateInstance<BundleCollectorSetting>();
            AssetDatabase.CreateAsset(s, SettingPath);
            AssetDatabase.SaveAssets();
        }
        // 返回已缓存/落盘的实例，确保后续 Packages 修改能被 SaveFile 保存
        return BundleCollectorSettingData.Setting;
    }

    [MenuItem("Tools/YooAsset/Open Builder Window")]
    public static void OpenBuilder()
    {
        EditorWindow.GetWindow<BundleBuilderWindow>("YooAsset Builder");
    }
}
#endif
