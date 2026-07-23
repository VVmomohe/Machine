using CG.MagicMenu;

namespace Com.Back
{
    /// <summary>后台入口屏(Menus.prefab 根节点)。F12 打开它即进入后台。</summary>
    public class MainBackendMenu : Menu<MainBackendMenu>
    {
        // 根层按返回键 = 退出整个后台（而不是回到插件栈里的其它屏）
        public override void OnBackPressed() => Backend.Exit();
    }
}
