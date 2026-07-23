using CG.MagicMenu;

namespace Com.Back
{
    /// <summary>密码校验屏(EnterPass.prefab 根节点)。由密码网关(SettingPanel.OpenPasswordGate)作为叠加层打开；
    /// 密码正确由网关成功回调决定去向，取消则回到来源屏并恢复其输入。</summary>
    public class EnterPassScreen : Menu<EnterPassScreen>
    {
    }
}
