using System;

namespace SlotMachine.Core
{
    /// <summary>
    /// 统一诊断日志开关。
    ///
    /// - <see cref="VerboseLogs"/>：verbose 级诊断日志总开关，默认 <c>false</c>（生产构建干净）。
    ///   调试时可在任意处（代码/Inspector 挂的初始化脚本）设 <c>SlotDebug.VerboseLogs = true</c>，
    ///   即可恢复 [WIN-Grid] / [SettleBaseB-*] / [StartBaseSpin-diag] / [Fireball-B-cols] 等逐局 verbose 诊断。
    ///
    /// - 报错（LogError）/ 警告（LogWarning）/ 关键流程日志（如 [Spin] / [结算:] / [入账] / [MINI-TRIGGER] /
    ///   [MINI-ENTRY]★）不受此开关影响，始终输出——排障能力不受影响。
    ///
    /// - 用法：把「每次结算/推进都喷」的 verbose 诊断块用 <c>if (SlotDebug.VerboseLogs) { ... }</c> 包裹；
    ///   单条日志用 <c>if (SlotDebug.VerboseLogs) Debug.Log(...)</c>。这样开关关闭时不构造字符串、零分配。
    /// </summary>
    public static class SlotDebug
    {
        public static bool VerboseLogs = false;
    }
}
