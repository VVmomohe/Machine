using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>模式A(China Street / 直线结算 holdMode="Direct") 专属流程：
    ///   A 不进 Hold&amp;Spin——基础轮落火球即算分（featureWin 已由 GameSession.A.SettleFireballsDirect 计入），
    ///   此处仅负责把落定的彩金档(res.wonJackpots)播成特效（清池已在 GameSession 即时完成）。
    ///   与 B 模式(GameManager.Hold.B.cs 的 Hold&amp;Spin 子系统) 完全分离，互不影响。</summary>
    public partial class GameManager
    {
        #region 模式A 专属 (Direct 直线结算)

        /// <summary>A 直线结算彩金特效：火球里的彩金档落定即中（清池已在 GameSession 即时完成），此处仅播特效。
        /// 在 SettleAfterReelsStop 基础局路径（holdSpinState==null）末尾调用。</summary>
        void ShowDirectJackpotEffects(GameResult r)
        {
            if (r == null || r.wonJackpots == null || r.wonJackpots.Count == 0 || m_bonus == null) return;
            foreach (var t in r.wonJackpots)
                if (System.Enum.TryParse<FireballKind>(t, out var fk))
                    m_bonus.ShowJackpotEffect(fk, persistent: true);   // 模式A：中了一直播放，开新局才隐藏
        }

        #endregion
    }
}
