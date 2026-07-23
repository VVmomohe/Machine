using System;
using UnityEngine;
using CG.MagicMenu;

namespace Com.Back
{
    /// <summary>
    /// 后台容器（场景常驻，一直 active，平时里面是空的）。
    ///
    /// 设计：不再依赖插件 BootStrapper 那个隐形的 "Magic Menu" 对象。改由本 SettingPanel
    /// 作为唯一的 MagicMenu 容器——自己挂一个 MagicMenu 组件（提供插件的 OpenMenu 栈 +
    /// 设置 Menu.MagicMenuInstance），启动时把各后台屏 prefab 实例化成自己的【子物体】：
    ///   Menus / Acount / GameSelection / GameSeting / ChangePSW / DateTime / EnterPass
    /// 每个屏运行时 AddComponent 对应的 Menu&lt;T&gt; 包装类（父节点 SettingPanel 已提供
    /// Canvas，子屏不再各自加 Canvas），Initialize 后 SetActive(false)。
    ///
    /// F12：OpenRoot() 打开 Menus 屏；再次 F12：CloseAll() 关闭全部子屏（容器本身保持 active）。
    /// 屏间导航走插件原生栈（整屏切换 Open => 父屏停用->输入暂停；返回 Close => 回到上一层），
    /// 由各屏的 View.OnEnter 通过 SettingPanel.OpenScreen(name) 触发（见各 *Screen 与 View）。
    /// </summary>
    public class SettingPanel : MonoBehaviour
    {
        public static SettingPanel Instance { get; private set; }

        // 屏 prefab 文件名(不含扩展名, 相对 Resources/MagicMenu/Prefabs) -> 对应 Menu<T> 包装类
        private static readonly (string prefab, Type menuType)[] Screens = new (string, Type)[]
        {
            ("Menus",         typeof(MainBackendMenu)),
            ("Acount",        typeof(AcountScreen)),
            ("GameSelection", typeof(GameSelectionScreen)),
            ("GameSeting",    typeof(GameSetingScreen)),
            ("ChangePSW",     typeof(ChangePSWScreen)),
            ("DateTime",        typeof(DateTimeScreen)),
            ("EnterPass",     typeof(EnterPassScreen)),
        };
        private const string DIR = "MagicMenu/Prefabs";

        private MagicMenu m_mm;
        private bool m_loaded;

        /// <summary>常驻的密码错误提示：ErrorText prefab 实例化到本容器最底部（渲染最上），默认隐藏，由 ErrorText.StartIE 激活。</summary>
        public ErrorText ErrorText { get; private set; }

        // 密码校验网关状态：EnterPass 作为叠加层打开时，记录"来源屏"与"密码正确后的成功回调"。
        // 来源屏保持可见（仅暂停其输入），取消时回到来源屏并恢复其输入（Account 不能隐藏）。
        private string m_gateReturn;
        private System.Action m_gateOnSuccess;

        /// <summary>后台是否处于打开状态（任一后台屏 active）。</summary>
        public bool IsBackendOpen
        {
            get
            {
                // ErrorText 是常驻提示(按需激活)，不算"后台屏"，否则 F12 切换会误判一直开着
                foreach (Transform t in transform)
                    if (t.gameObject.activeSelf && t.name != "ErrorText") return true;
                return false;
            }
        }

        /// <summary>密码校验网关是否正处于打开状态（EnterPass 叠加层中）。</summary>
        public bool IsGateOpen => m_gateReturn != null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // 本容器需要一个 MagicMenu 组件：提供 OpenMenu 栈，并在屏 Initialize 时设 Menu.MagicMenuInstance
            m_mm = GetComponent<MagicMenu>();
            if (m_mm == null) m_mm = gameObject.AddComponent<MagicMenu>();

            // 清理 BootStrapper 可能生成的多余隐形 MagicMenu，保证唯一容器就是本 SettingPanel
            foreach (var other in FindObjectsOfType<MagicMenu>())
                if (other != m_mm) Destroy(other.gameObject);

            Debug.Log("[SettingPanel] 容器就绪（作为唯一 MagicMenu 容器）");
        }

        private void Start() => EnsureLoaded();

        /// <summary>确保所有屏已加载为子物体（幂等）。</summary>
        public void EnsureLoaded()
        {
            if (m_loaded) return;
            foreach (var (prefab, type) in Screens)
                LoadScreen(prefab, type);
            EnsureErrorText();
            m_loaded = true;
            Debug.Log("[SettingPanel] 后台屏已全部加载到容器下（默认关闭）");
        }

        private Menu LoadScreen(string prefabName, Type menuType)
        {
            // 复用已存在的同名屏，避免重复实例化
            foreach (Transform t in transform)
            {
                if (t.name == prefabName || t.name == prefabName + "(Clone)")
                {
                    var ex = t.GetComponent<Menu>();
                    if (ex != null) { ex.Initialize(m_mm); return ex; }
                }
            }

            GameObject go;
            var asset = Resources.Load<GameObject>($"{DIR}/{prefabName}");
            if (asset != null)
            {
                go = Instantiate(asset, transform);
            }
            else
            {
                Debug.LogWarning($"[SettingPanel] 缺少屏 prefab：{DIR}/{prefabName}，已建空白屏占位（请补做该 prefab，例如 EnterPass）");
                go = new GameObject(prefabName);
                go.transform.SetParent(transform, false);
            }
            go.name = prefabName; // 去掉 (Clone) 便于按名查找

            var menu = go.GetComponent<Menu>();
            if (menu == null) menu = (Menu)go.AddComponent(menuType); // 父节点(SettingPanel)已提供 Canvas，子屏不再各自加 Canvas
            menu.Initialize(m_mm);
            go.SetActive(false);
            return menu;
        }

        private Menu FindScreen(string prefabName)
        {
            foreach (Transform t in transform)
                if (t.name == prefabName) return t.GetComponent<Menu>();
            return null;
        }

        /// <summary>把 ErrorText prefab 实例化挂到本容器最底部（渲染最上），作为共享的密码错误提示。幂等。</summary>
        private void EnsureErrorText()
        {
            if (ErrorText != null) return;
            var asset = Resources.Load<GameObject>($"{DIR}/ErrorText");
            if (asset == null)
            {
                Debug.LogError("[SettingPanel] 缺少 ErrorText prefab：MagicMenu/Prefabs/ErrorText");
                return;
            }
            var go = Instantiate(asset, transform);
            go.name = "ErrorText";            // 去掉 (Clone)，便于识别与 IsBackendOpen 跳过
            go.SetActive(false);              // 被动提示，默认隐藏；由 ErrorText.StartIE 激活
            go.transform.SetAsLastSibling();  // 置于最底部(最后子物体) → 渲染在最上，提示始终浮于屏之上
            ErrorText = go.GetComponent<ErrorText>();
            if (ErrorText != null && ErrorText.m_errorText != null)
                ErrorText.m_errorText.raycastTarget = false; // 提示文字不应拦截输入
        }

        /// <summary>F12 打开后台入口：主界面 Menus + DateTime 同时打开，后台期间 DateTime 一直常驻显示。
        /// DateTime 作为叠加层（不入 MagicMenu 栈），导航整屏切换时不会被关掉，只有整后台退出时才随 CloseAll 隐藏。</summary>
        public void OpenRoot()
        {
            EnsureLoaded();
            var menus = FindScreen("Menus");
            if (menus == null) { Debug.LogError("[SettingPanel] 找不到 Menus 屏"); return; }
            if (m_mm != null) m_mm.Reset(); // 清掉上次会话残留的栈，确保从根干净打开
            m_mm.OpenMenu(menus); // openOnTop=false：只显示 Menus
            var dt = FindScreen("DateTime"); // 常驻叠加层，后台期间一直显示
            if (dt != null) dt.gameObject.SetActive(true);
            Debug.Log("[SettingPanel] 打开后台(Menus + DateTime)");
        }

        /// <summary>F12 关闭整个后台：清栈并停用全部子屏（容器本身保持 active）。</summary>
        public void CloseAll()
        {
            if (m_mm != null) m_mm.Reset();
            // 整后台退出时，任何打开的密码网关一并撤销
            m_gateReturn = null;
            m_gateOnSuccess = null;
            foreach (Transform t in transform) t.gameObject.SetActive(false);
            Debug.Log("[SettingPanel] 关闭后台");
        }

        /// <summary>按屏名整屏切换打开（关闭其它屏，只显示目标屏）。找不到屏会打错误日志。</summary>
        public void OpenScreen(string prefabName)
        {
            EnsureLoaded();
            var m = FindScreen(prefabName);
            if (m == null)
            {
                Debug.LogError($"[SettingPanel] OpenScreen 失败：找不到屏 '{prefabName}'（请确认 Back/Resources/MagicMenu/Prefabs 下有同名 prefab，且已被本容器加载）");
                return;
            }
            m_mm.OpenMenu(m); // openOnTop=false：关闭其它屏，只显示本屏
            Debug.Log($"[SettingPanel] 打开屏：{prefabName}");
        }

        /// <summary>返回上一层（插件栈弹栈，回到上一屏并恢复其输入）。
        /// 叠加屏场景(如密码网关 EnterPass 盖在来源屏上)：被覆盖屏在打开时被暂停 isCur、且整段保持 active(未被停用)，
        /// CloseMenu 弹栈后它不会被重新激活(OnEnable 不重跑)，故这里显式把当前仍 active 的屏 isCur 置 true，保证输入恢复。</summary>
        public void Back()
        {
            if (m_mm != null) m_mm.CloseMenu();
            // 恢复新栈顶(被揭示)屏的输入：对仍 active 的屏(叠加返回时未经历 OnEnable)显式置 true
            foreach (Transform t in transform)
            {
                if (!t.gameObject.activeSelf || t.name == "ErrorText") continue;
                var v = t.GetComponent<MoveView>();
                if (v != null) v.isCur = true;
            }
        }

        /// <summary>
        /// 打开密码校验网关：把 EnterPass 作为【叠加层】盖在 returnScreen 之上。
        /// 来源屏保持可见（Account 不能隐藏），仅暂停其输入(isCur=false)；EnterPass 成为当前可操作屏。
        /// 密码正确 → 执行 onSuccess（可整屏跳转到目标屏，或就地执行某操作后留在来源屏）；
        /// 取消(OnCancel) → 回到 returnScreen 并恢复其 isCur。
        /// </summary>
        public void OpenPasswordGate(string returnScreen, System.Action onSuccess)
        {
            EnsureLoaded();
            var src = FindScreen(returnScreen);
            var enter = FindScreen("EnterPass");
            if (src == null || enter == null)
            {
                Debug.LogError($"[SettingPanel] 打开密码网关失败：return={returnScreen}（找不到对应屏，请确认 prefab 与 Screens 已加载）");
                return;
            }
            var srcView = src.GetComponent<MoveView>();
            if (srcView != null) srcView.isCur = false; // 暂停来源屏输入，但保持可见
            m_gateReturn = returnScreen;
            m_gateOnSuccess = onSuccess;
            m_mm.OpenMenu(enter, true); // openOnTop=true：叠加，来源屏不被隐藏
            Debug.Log($"[SettingPanel] 打开密码网关（来源={returnScreen}）");
        }

        /// <summary>密码网关结算。success=true 执行成功回调；false=取消，回到来源屏并恢复输入。</summary>
        public void ResolvePasswordGate(bool success)
        {
            var ret = m_gateReturn;
            var act = m_gateOnSuccess;
            m_gateReturn = null;
            m_gateOnSuccess = null;

            // 先弹掉 EnterPass 叠加层，恢复来源屏（栈顶下方的 peek）。
            if (m_mm != null) m_mm.CloseMenu();

            // 恢复来源屏输入（叠加期间来源屏未被禁用，OnEnable 不会重跑，故显式恢复 isCur）。
            var srcView = (ret != null ? FindScreen(ret) : null)?.GetComponent<MoveView>();
            if (srcView != null) srcView.isCur = true;

            if (success && act != null) act();
        }
    }
}
