using UnityEngine;

namespace Com.Back
{
    /// <summary>
    /// 后台开关转发层。实际容器是场景常驻的 <see cref="SettingPanel"/>
    /// （它自持 MagicMenu、把各屏加载为子物体）。F12 由此进入/退出后台，
    /// 各屏间跳转通过 <see cref="SettingPanel.OpenScreen(string)"/> 按名整屏切换。
    /// </summary>
    public static class Backend
    {
        public static bool IsOpen => SettingPanel.Instance != null && SettingPanel.Instance.IsBackendOpen;

        /// <summary>F12 打开后台入口(Menus)。</summary>
        public static void OpenRoot()
        {
            if (SettingPanel.Instance == null)
            {
                Debug.LogError("[Backend] 场景中未找到 SettingPanel 容器。请确认 Game 场景放了 SettingPanel，且它处于 active、挂了 Com.Back.SettingPanel 脚本。");
                return;
            }
            SettingPanel.Instance.OpenRoot();
        }

        /// <summary>退出整个后台。</summary>
        public static void Exit()
        {
            if (SettingPanel.Instance != null) SettingPanel.Instance.CloseAll();
        }

        /// <summary>按屏名整屏切换打开（关闭其它屏，只显示目标屏）。</summary>
        public static void OpenScreen(string name)
        {
            if (SettingPanel.Instance == null)
            {
                Debug.LogError("[Backend] 场景中未找到 SettingPanel 容器，无法打开屏：" + name);
                return;
            }
            SettingPanel.Instance.OpenScreen(name);
        }

        /// <summary>返回上一层（插件栈弹栈）。</summary>
        public static void Back()
        {
            if (SettingPanel.Instance != null) SettingPanel.Instance.Back();
        }
    }
}
