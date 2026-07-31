using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>模式A(China Street / 直线结算 holdMode="Direct") 专属结算：
    ///   A 不进 Hold&Spin——基础轮落火球即算分（featureWin 由逻辑层 SettleFireballsDirect 计入，A/B 共用），
    ///   不把火球钉成持久 overlay（A 无"固定火球收集盘"）。数值结算与收尾共用 Flow.cs 的通用方法。
    ///   与 B 模式(GameManager.Flow.B.cs) 完全分离。</summary>
    public partial class GameManager
    {
        #region 模式A 专属 (Direct 直线结算)

        /// <summary>模式A 基础局结算：等停稳 → 数值结算(连线+Scatter) → 通用收尾(彩金特效/显示赢分/进 Mini)。
        /// A 不钉火球持久 overlay（与 B 的"固定火球收集盘"区分）。</summary>
        IEnumerator SettleBaseA(GameResult r)
        {
            yield return WaitReelsStop();

            // 数值结算（A 模式直线结算 / B 模式共用同一套评估口径）
            int sc;
            float bw = SettleRoundWins(r.baseGrid, m_machine.totalBet, out sc);
            r.baseWin = bw;
            r.scatterCount = sc;

            yield return FinishBaseSettle(r);
        }

        #endregion
    }
}
