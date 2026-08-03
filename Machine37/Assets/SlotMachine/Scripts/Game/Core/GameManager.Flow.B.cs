using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;

namespace com.slot
{
    /// <summary>模式B(Cash Falls / 直线结算 holdMode="Direct") 专属结算 + 收集盘 respin：
    ///   基础轮落下的火球钉成持久 overlay(固定火球/收集盘显示)；若有火球则进入【收集盘 respin】——
    ///   只做显示+动画(钉 overlay / tong / 计数器圈数)，不滚盘：每轮推进(落新火球/减圈/满列/释放)，
    ///   满列 → 进 Mini(enterMiniByColumnFill)；FREE 火球单列累计 → 追加免费次数(进 Mini)。
    ///   与 A 模式(GameManager.Flow.A.cs) 完全分离；通用收尾(SettleRoundWins/FinishBaseSettle)在 Flow.cs。</summary>
    public partial class GameManager
    {
        #region 模式B 专属 (Cash Falls 收集盘结算 + respin)

        IEnumerator SettleBaseB(GameResult r)
        {
            yield return WaitReelsStop();

            // ★ 基础局"固定火球"显示：把落下的火球钉成持久 overlay（收集盘/固定火球），
            //   FREE 火球累加的免费次数已在逻辑层(SettleFireballsDirect / respin 累加)算入 r.freeSpinsAwarded。
            if (m_reelView != null && r.baseFireballs != null)
            {
                foreach (var c in r.baseFireballs)
                    if (c.filled) m_reelView.ShowFireballOverlay(c.reel, c.row, c, playSound: false);
            }

            // ★ 模式B 收集盘 respin：基础局落了火球 → 进入（显示+动画，不滚盘）收集循环。
            if (r.holdSpinState != null && m_reelView != null)
            {
                var hs = r.holdSpinState;
                m_reelView.ShowFeatureState(hs);   // 钉初始火球（统一由 hs 重排，覆盖上面逐颗钉法）
                m_reelView.ActivateCounters();
                for (int rr = 0; rr < hs.reels; rr++)
                    if (hs.counter[rr] > 0) m_reelView.SetRespinCounterRow(rr, hs.counter[rr]);

                int guard = 0;
                int freeFromFireball = 0;   // 本轮 respin 内 FREE 火球(单列收集)累计的免费次数（不在中途并入 freeSpinsAwarded，避免单颗 FREE 就进小游戏）
                while (hs.active && guard++ < 200)
                {
                    var step = GameSession.RespinHoldSpin(hs, m_machine.config, m_machine.rng,
                        m_machine.totalBet, allowFreeMode: true);

                    // 释放列(counter 归零)：清 overlay + 底层符号回归普通(下一局自然滚) + 隐藏该列计数器
                    if (step.releasedReels != null)
                        foreach (int rel in step.releasedReels)
                        {
                            m_reelView.ClearColumnFireballs(rel);
                            m_reelView.ReleaseColumnToSpinQueue(rel);
                            m_reelView.HideCounterRow(rel);
                        }

                    // 满列：tong 演出 + 标记进 Mini(整列集满才进小游戏) + 计数器显(已集满，圈数归零保留显示)
                    if (step.fullReels != null)
                        foreach (int full in step.fullReels)
                        {
                            m_reelView.PlayTong(full);
                            m_reelView.SetRespinCounterRow(full, 0);
                            r.enterMiniByColumnFill = true;
                        }

                    // 其余活跃列（本轮无新火球）：刷新圈数（递减 3→2→1→0）
                    if (step.newCounters != null)
                        for (int rr = 0; rr < step.newCounters.Length; rr++)
                            if (!hs.isFull[rr] && !hs.released[rr] && hs.counter[rr] > 0)
                                m_reelView.SetRespinCounterRow(rr, hs.counter[rr]);

                    // 本轮 FREE 火球(单列收集)累计的免费次数：先累计，待「整列集满」开 Mini 时再并入（防单颗 FREE 就进小游戏）
                    freeFromFireball += step.freeSpinsAdded;

                    yield return new WaitForSeconds(0.35f);  // 给 tong/overlay 动画一点节奏
                }

                // ★ 模式B：FREE 火球累计的免费次数仅在「整列集满」开 Mini 时并入免费局数（用户硬性要求：整列集满才进 Mini）
                if (r.enterMiniByColumnFill)
                    r.freeSpinsAwarded += freeFromFireball;
                r.freeSpinsFromFireball = freeFromFireball;

                // 总火球派彩（hs.accumulated 已含初始 + 各轮逐颗 ×bet）
                r.featureWin = hs.accumulated;
                // wonJackpots 最终以 hs 为准（含初始），去重置池已在上面逐轮处理
                if (r.wonJackpots == null) r.wonJackpots = new List<string>(hs.wonJackpots);
                else foreach (var t in hs.wonJackpots) if (!r.wonJackpots.Contains(t)) r.wonJackpots.Add(t);

                if (r.enterMiniByColumnFill)
                    Debug.Log($"[MINI-TRIGGER] 模式B 整列集满 → 进 Mini（scatter={r.freeSpinsFromScatter} + FREE={freeFromFireball} = freeSpinsAwarded={r.freeSpinsAwarded}）");
            }

            // 数值结算（与 A 共用同一套评估口径）
            int sc;
            float bw = SettleRoundWins(r.baseGrid, m_machine.totalBet, out sc);
            r.baseWin = bw;
            r.scatterCount = sc;
            r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin;

            yield return FinishBaseSettle(r);
        }

        #endregion
    }
}
