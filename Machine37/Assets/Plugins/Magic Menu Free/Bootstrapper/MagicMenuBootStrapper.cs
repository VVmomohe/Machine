using UnityEngine;

namespace CG.Bootstrapper
{
    internal class MagicMenuBootStrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeMagicMenu()
        {
            // 已改用场景常驻的 Com.Back.SettingPanel 作为唯一的 MagicMenu 容器，
            // 不再自动生成隐形的 "Magic Menu" 对象（避免双容器）。
            // 如需恢复插件默认行为，取消下面这行注释即可：
            // Object.DontDestroyOnLoad(Object.Instantiate(Resources.Load("Magic Menu")));
        }
    }
}
