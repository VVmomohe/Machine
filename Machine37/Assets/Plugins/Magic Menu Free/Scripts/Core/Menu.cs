using UnityEngine;
using UnityEngine.UI;

namespace CG.MagicMenu 
{
    public abstract class Menu<T> : Menu where T : Menu<T>
    {
        private static T instance;
        private static T Instance { get { return instance; } }

        protected virtual void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
            }
            else
            {
                instance = (T)this;
            }
        }

        protected virtual void OnDestroy() => instance = null;

        public static void Open()
        {
            if (MagicMenuInstance != null && Instance != null)
            {
                MagicMenuInstance.OpenMenu(Instance);
            }
        }

        public static void OpenOnTop()
        {
            if (MagicMenuInstance != null && Instance != null)
            {
                MagicMenuInstance.OpenMenu(Instance, true);
            }
        }

        public static void Close()
        {
            if (MagicMenuInstance != null && Instance != null)
            {
                MagicMenuInstance.CloseMenu();
            }
        }
    }

    // 本项目后台采用「父容器(SettingPanel)持有 Canvas，子屏作为其 UI 子节点」的架构，
    // 子屏本身不需要各自的 Canvas/Scaler/Raycaster——父 Canvas 的渲染与射线检测已覆盖整棵子树
    // （子屏上的 UI 按钮点击也照常生效）。故取消强制 RequireComponent，避免每个屏被嵌套加一份
    // Canvas 造成重叠/缩放冲突与额外开销。
    public abstract class Menu : MonoBehaviour
    {
        [SerializeField] bool showFirst;
        public bool ShowFirst => showFirst;

        private void Reset()
        {
            // 仅在自身确实带 Canvas 时才配置（独立根菜单场景）。
            // 本项目中子屏嵌套在有 Canvas 的 SettingPanel 下，自身无 Canvas，此处跳过。
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1;
                canvas.vertexColorAlwaysGammaSpace = true;
            }

            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        public static MagicMenu MagicMenuInstance { get; private set; }

        public virtual void OnBackPressed() => MagicMenuInstance.CloseMenu();

        public void Initialize(MagicMenu magicMenu) => MagicMenuInstance = magicMenu;
    }
}


