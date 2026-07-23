namespace Com.Tool
{
    /// <summary>
    /*    0x01
HELLO
REQ / RESP
扩展
查询并返回协议版本
0x02
STATUS
REQ / RESP
核心
查询并返回设备状态
0x03
HEARTBEAT
PUSH
扩展
链路心跳
0x04
ERROR
PUSH
核心
MCU 主动推送错误/告警
0x10
LINE_SET
REQ / RESP
核心
设置活跃支付线数并返回结果
0x11
BET_SET
REQ / RESP
核心
设置当前下注档位并返回结果
0x20
SPIN
REQ / RESP
核心
发起 Spin 并返回盘面与结算结果
0x21
BONUS
REQ / RESP
核心
玩家 Bonus 选门并返回结算结果
0x22
FREE_SPIN
REQ / RESP
核心
Free Game 单轮 Spin 请求与结果
0x23
DOUBLE
REQ / RESP
核心
玩家猜硬币并返回单轮结果
0x30
BALANCE
REQ / RESP
核心
查询并返回当前余额
0x31
LAST_RESULT
REQ / RESP
扩展
查询并返回最近一次结果帧摘要
0x40
JP_POOL
PUSH
扩展
JP 奖池金额主推
0x41
KEY
PUSH
核心
外接按键板按键按下通知*/
    /// </summary>
    public enum CommandEnum : byte
    {
        /// <summary>
        ///     
        /// </summary>
        UnKnow = 0xfc,

        /// <summary>
        ///   查询并返回协议版本
        /// </summary>
        Hello = 0x01,

        ///  <summary>
        ///  查询并返回设备状态
        /// </summary>
        STATUS = 0x02,

        ///  <summary>
        ///  链路心跳
        /// </summary>
        HEARTBEAT = 0x03,


        /// <summary>
        ///     显示错误
        /// </summary>
        ERROR = 0x04,

        /// <summary>
        ///   BET_SET 是下注真值来源。SPIN_REQ 不再携带下注。
        /// </summary>
        BET_SET = 0x11,

        /// <summary>
        ///     LINE_SET 是线数真值来源。结算时总下注 = current_bet × current_lines。
        /// </summary>
        LINE_SET = 0x10,

        /// <summary>
        ///    玩家按下 Spin 按钮后，SOC 发送本帧。
        /// </summary>
        SPIN = 0x20,

        /// <summary>
        ///   玩家 Bonus 选门并返回结算结果
        /// </summary>
        BONUS = 0x21,


        /// <summary>
        /// 玩家在 Double Game 中猜硬币。
        /// </summary>
        FREE_SPIN = 0x22,

        /// <summary>
        /// 玩家猜硬币并返回单轮结果
        /// </summary>
        DOUBLE = 0x23,

        /// <summary>
        ///  查询并返回当前余额
        /// </summary>
        BALANCE = 0x30,

        /// <summary>
        ///  查询并返回最近一次结果帧摘要
        /// </summary>
        LAST_RESULT = 0x31,

        /// <summary>
        ///    JP 奖池金额主推
        /// </summary>
        JP_POOL = 0x40,

        /// <summary>
        ///     外接按键板按键按下通知
        /// </summary>
        KEY = 0x41,


        #region Test

        /// <summary>
        ///     打开测试面板
        /// </summary>
        TestEnter = 0x5C,
        TestDisplay = 0x5D,

        /// <summary>
        ///     打开测试面板
        /// </summary>
        TestClose = 0x5F,

        /// <summary>
        ///     推币率面板
        /// </summary>
        TestPush = 0x50,

        /// <summary>
        ///     推币率面板
        /// </summary>
        TestParamsSave = 0x51,


        /// <summary>
        ///     自动投币
        /// </summary>
        UpdateAutoplayCoin = 0x52,

        /// <summary>
        ///     输入输出面板
        /// </summary>
        TestIO = 0x53,

        /// <summary>
        ///     输入输出面板 测试具体项
        /// </summary>
        TestIODevice = 0x54,

        #endregion


        InputResult = 0xEA,
        SendMachineID = 0xEB,
        EnterInputActiveCode = 0xEC,
        ShowActiveCodePanel = 0xED,
    }
}