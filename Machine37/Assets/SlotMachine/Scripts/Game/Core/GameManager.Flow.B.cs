using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;

namespace com.slot
{
    /// <summary>模式B(Cash Falls / 收集盘 holdMode="Direct") 专属结算：
    ///   基础轮落下的火球钉成持久 overlay(固定火球/收集盘显示)，FREE 火球累加免费次数已在逻辑层算好；
    ///   其余数值结算与收尾与 A 共用 Flow.cs 的通用方法(SettleRoundWins / FinishBaseSettle)。
    ///   与 A 模式(GameManager.Flow.A.cs) 完全分离。</summary>
    public partial class GameManager
    {
        #region 模式B 专属 (Cash Falls 收集盘结算)

        IEnumerator SettleBaseB(GameResult r)
        {
            yield return WaitReelsStop();

            // ★ 基础局"固定火球"显示：把落下的火球钉成持久 overlay（收集盘/固定火球），
            //   与旧 Hold&Spin 的 ShowFeatureState 表现一致——火球停在各自格子上、不随下一局卷轴滚走。
            //   FREE 火球累加的免费次数已在逻辑层(SettleFireballsDirect)算入 r.freeSpinsAwarded，此处只管显示。
            if (m_reelView != null && r.baseFireballs != null)
            {
                foreach (var c in r.baseFireballs)
                    if (c.filled) m_reelView.ShowFireballOverlay(c.reel, c.row, c, playSound: false);
            }

            // 数值结算（与 A 共用同一套评估口径）
            int sc;
            float bw = SettleRoundWins(r.baseGrid, m_machine.totalBet, out sc);
            r.baseWin = bw;
            r.scatterCount = sc;

            yield return FinishBaseSettle(r);
        }

        #endregion
    }
}
