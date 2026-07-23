using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using YooAsset;

/// <summary>
/// YooAssets 运行时管理器（全内置 APK / 离线模式，无热更、无服务器）。
///
/// 资源按「类型」分包：Atlas / Audio / Font / Video / Misc。
/// 各包都在 StreamingAssets 里，离线模式直接读，安装即全有。
///
/// 用法（挂一个空 GameObject 上，场景最先加载）：
///   StartCoroutine(YooAssetsManager.Instance.InitAll());
///   // 取精灵：
///   var h = YooAssetsManager.Instance.LoadAssetAsync<Texture2D>("Atlas", "Assets/ByThebay/images/bigWord1.png");
///   yield return h; var tex = h.GetAssetObject<Texture2D>(); ...
///   // 取图集里的子精灵（多精灵纹理）：
///   yield return YooAssetsManager.Instance.LoadSpriteFromAtlas("Atlas",
///       "Assets/ByThebay/images/bigWord1.png", "big_word_bigwin_12.png", spr => { ... });
///   // 注意：China 分支没有 Texture2D.GetSprite，这里用 YooAsset 的 LoadSubAssetsAsync 取子精灵。
/// </summary>
public class YooAssetsManager : MonoBehaviour
{
    public static YooAssetsManager Instance { get; private set; }

    /// <summary>按资源类型分的包名（须与 YooAsset Setting 里定义的包一致）。</summary>
    public static readonly string[] PackageNames = { "Atlas", "Audio", "Font", "Video", "Misc" };

    private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>();

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    /// <summary>初始化所有包（离线/内置模式）。在游戏启动最早处调用。</summary>
    public IEnumerator InitAll()
    {
        YooAssets.Initialize();

        foreach (var pkgName in PackageNames)
        {
            var package = YooAssets.CreatePackage(pkgName);
            _packages[pkgName] = package;

            // 离线模式：资源来自 StreamingAssets（内置文件）
            var options = new OfflinePlayModeOptions();
            options.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();

            var op = package.InitializePackageAsync(options);
            yield return op;

            if (op.Status != EOperationStatus.Succeeded)
                Debug.LogError($"[YooAssets] 初始化包「{pkgName}」失败: {op.Error}");
            else
                Debug.Log($"[YooAssets] 包「{pkgName}」就绪");
        }
    }

    private ResourcePackage GetPkg(string packageName)
    {
        if (_packages.TryGetValue(packageName, out var p)) return p;
        // 回退到全局查询（兼容未走 InitAll 的情况）
        return YooAssets.TryGetPackage(packageName, out var pkg) ? pkg : null;
    }

    /// <summary>异步加载任意资源。location 为资源的 Unity 工程内路径（如 Assets/.../xxx.png）。</summary>
    public AssetHandle LoadAssetAsync<TObject>(string packageName, string location) where TObject : Object
    {
        var package = GetPkg(packageName);
        if (package == null) { Debug.LogError($"[YooAssets] 包「{packageName}」未初始化"); return null; }
        return package.LoadAssetAsync<TObject>(location);
    }

    /// <summary>从多精灵图集里取某个命名子精灵（你用 CocosAtlasImporter 导入的图集就是这种）。
    /// 注意：China 定制分支删掉了 Texture2D.GetSprite()，所以改用 YooAsset 的 LoadSubAssetsAsync 取子精灵。</summary>
    public IEnumerator LoadSpriteFromAtlas(string packageName, string atlasLocation, string spriteName, System.Action<Sprite> onLoaded)
    {
        var package = GetPkg(packageName);
        if (package == null) { onLoaded?.Invoke(null); yield break; }

        // 多精灵纹理：用 LoadSubAssetsAsync 拿子精灵集合（China 分支无 Texture2D.GetSprite）
        var handle = package.LoadSubAssetsAsync<Sprite>(atlasLocation);
        yield return handle;
        Sprite result = handle.GetSubAssetObject<Sprite>(spriteName);
        onLoaded?.Invoke(result);
        handle.Release();
    }

    /// <summary>释放句柄（LoadAssetAsync 返回的 handle 用完调用）。</summary>
    public void Release(AssetHandle handle)
    {
        handle?.Release();
    }
}
