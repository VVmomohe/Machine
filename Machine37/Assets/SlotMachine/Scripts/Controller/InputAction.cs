namespace Com.Controller
{
    /// <summary>
    /// 统一输入动作枚举
    /// 按照MCU协议定义，支持后台小键盘和玩家按键
    /// </summary>
    public enum InputAction
    {
        // === 方向键（后台小键盘） ===
        Left,
        Right,
        Up,
        Down,

        // === 功能键（后台小键盘） ===
        Confirm,        // 确认
        Cancel,         // 取消
        MiniGame,       // 小游戏

        // === 玩家功能键 ===
        Start,          // 启动/发炮/发射
        Enhance,        // 加强/加炮/加分
        Ticket,         // 退币/退彩
        GameSwitch,     // 游戏切换 (Key12)

        // === 押分键1-8（对应协议Key1-Key8） ===
        BetKey1,        // 兔子/菠萝
        BetKey2,        // 猴子/橙子
        BetKey3,        // 熊猫/奇异果
        BetKey4,        // 狮子/铃铛
        BetKey5,        // 飞鹰/柠檬
        BetKey6,        // 孔雀/星星
        BetKey7,        // 鸽子/777
        BetKey8,        // 燕子/BAR

        // === 特殊押分键（对应协议Key9-Key11） ===
        BetKey9,        // 飞禽
        BetKey10,       // 走兽
        BetKey11,       // 金鲨
        BetKey12,       // 金鲨

        // === 调试/管理（仅Editor/PC） ===
        DebugUpCoin,    // 上分 (F3)
        OpenSetting,    // 打开设置 (F12)
        Stop,           // 停止转轮 (S / RightShift)
    }

    /// <summary>
    /// 输入阶段
    /// </summary>
    public enum InputPhase
    {
        None,
        Down,   // 按下瞬间
        Hold,   // 持续按住
        Up      // 松开瞬间
    }

}
