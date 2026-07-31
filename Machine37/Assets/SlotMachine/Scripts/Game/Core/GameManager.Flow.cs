using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 一局流程部分（基础局 + 结算 + 辅助）：
    ///   上锁 → 滚动 → 等停稳 → (Hold&Spin重转循环见 GameManager.Hold.B.cs，仅模式B) → 结算解锁。
    ///   模式A 专属流程在 GameManager.Flow.A.cs；Hold&Spin(B) 子系统在 GameManager.Hold.B.cs；Mini 在 GameManager.Mini.cs。</summary>
    public partial class GameManager
    {
        #region 基础局
        /// <summary>启动一局基础旋转：上锁 → 滚动 → 等停稳 → (可能进入Hold&Spin) → 结算解锁。</summary>
        void StartBaseSpin(GameResult r)
        {
            _spinPending = true;

            // ★ 兜底：所有新局必经此函数，在此统一隐藏上局残留的彩金特效。
            //   主路径已在 OnStartKey 调过一次（用户按 Start 时立即响应），
            //   此处防御 MiniGame 结束回退/其他绕过 OnStartKey 的路径遗漏。
            if (m_bonus != null) m_bonus.HideAllJackpotEffects();

            if (m_reelView != null)
            {
                // ★ 开新基础局：清掉上局残留的火球 overlay（基础局"固定火球"由本局重新钉出；
                //   Mini 持久 overlay 已在 MiniGame 结束时自行 ClearFireballOverlays，此处不影响）。
                m_reelView.ClearFireballOverlays();

                // 落了火球，把倍率传给 ShowGrid，滚动阶段就显示倍率。
                // ★ 优先用 res.baseFireballs：基础轮落下的全部火球（不论是否触发 Hold&Spin）都已定倍率，一律显示。
                //   触发 Hold&Spin 时 hs.cells 与 baseFireballs 同源，二者结果一致，fallback 仅作保险。
                var fireMults = new Dictionary<int, FireballCell>();
                if (r.baseFireballs != null)
                    foreach (var c in r.baseFireballs)
                        if (c.filled) fireMults[c.reel * 100 + c.row] = c;

                m_reelView.ShowGrid(r.baseGrid, fireMults.Count > 0 ? fireMults : null);
                // ★ 按模式分流结算：A → SettleBaseA(Flow.A.cs)，B → SettleBaseB(Flow.B.cs)，通用步骤在 Flow.cs。
                StartCoroutine(IsModeB() ? SettleBaseB(r) : SettleBaseA(r));
            }
            else
            {
                Settle(r);
                _spinPending = false;
            }
        }
        #endregion

        #region 结算（通用 + 模式分发）

        bool IsModeB()
        {
            return m_machine != null && m_machine.config != null
                && m_machine.config.modeName != null
                && m_machine.config.modeName.IndexOf("ModeB", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        IEnumerator WaitReelsStop()
        {
            while (m_reelView != null && m_reelView.IsSpinning())
                yield return null;
        }

        IEnumerator FinishBaseSettle(GameResult r)
        {
            if (m_player != null)
                LogSettle("基础", m_machine.totalBet, r.baseWin + r.scatterPayout + r.featureWin);

            // 基础局通用彩金特效：火球里的彩金档落定即中（清池已在 GameSession 即时完成），A/B 模式都播。
            ShowDirectJackpotEffects(r);

            // 先显示赢分 → 等按确认键 → 再滚动到总分
            LogMiniEntry("基础局Scatter触发", r, r.freeSpinsAwarded, 0, null);
            if (WillEnterMini(r))
            {
                r.freeSpinsWin = 0;
                r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin;
            }
            if (m_player != null)
            {
                long win = (long)System.Math.Round(r.totalPayout);
                m_player.ShowWinValue(win, !WillEnterMini(r));
                yield return StartCoroutine(WaitForConfirmKey());
                m_player.ResetBet();
                _pendingMiniBaseWin = win;
            }

            // 免费游戏触发 → 进入 Mini；否则正常结算解锁
            if (MaybeEnterMini(r)) { _spinPending = false; yield break; }
            Settle(r);
            _spinPending = false;
        }

        /// <summary>基础局通用彩金特效（A/B 模式都播）：火球里的彩金档落定即中（清池已在 GameSession 即时完成），此处仅播特效。
        /// 在 FinishBaseSettle 内调用。</summary>
        void ShowDirectJackpotEffects(GameResult r)
        {
            if (r == null || r.wonJackpots == null || r.wonJackpots.Count == 0 || m_bonus == null) return;
            foreach (var t in r.wonJackpots)
                if (System.Enum.TryParse<FireballKind>(t, out var fk))
                    m_bonus.ShowJackpotEffect(fk, persistent: true);   // 中了一直播放，开新局才隐藏
        }

        /// <summary>
        /// 统一结算「一轮」的普通符号连线赢分 + Scatter 统计。基础旋转与 Hold&amp;Spin 每轮 respin 共用此函数，
        /// 保证两类"一局"的结算口径完全一致（评估/高亮/音效/Scatter 统计）。
        /// grid：权威数据网格（基础旋转传 r.baseGrid；respin 传 BuildRespinGrid 结果）。
        /// 返回 lineWin（普通连线赢分）；scatterCount 通过 out 回传（respin 池不含 Scatter，自然为 0）。
        /// 调用方负责把 lineWin 累加进本局赢分、把 scatterCount 折算成免费次数（仅基础旋转需要）。
        /// </summary>
        float SettleRoundWins(int[][] grid, float bet, out int scatterCount)
        {
            scatterCount = 0;
            float lineWin = 0f;
            if (m_machine == null || m_machine.session == null || grid == null) return 0f;

            // 1) 普通连线赢分（连线/Ways/逐列，由 winEval 决定）
            var wins = m_machine.session.EvaluateGrid(grid, bet);
            foreach (var w in wins) lineWin += w.payout;

            // ★ 诊断 [WIN]：输出结算网格(逐列坐标) + 每个赢的 符号/连数/参与格子。
            //   用于定位「屏幕某行有 K(5) 却算成 5 连 10」争议：逐列(Rows)模式下，
            //   只要 reel3 的 6 行里任意一行有 ID2(数字10) 整列即命中 → 5 连合法；
            //   若 [WIN-Grid] 显示 reel3 全列无 ID2 却仍 5 连，才是真 bug（K 被错算进 10）。
            {
                var sbG = new System.Text.StringBuilder("[WIN-Grid] ");
                for (int ri = 0; ri < grid.Length; ri++)
                {
                    sbG.Append($"r{ri}[");
                    for (int k = 0; k < grid[ri].Length; k++) sbG.Append(grid[ri][k]).Append(k < grid[ri].Length - 1 ? "," : "");
                    sbG.Append("] ");
                }
                UnityEngine.Debug.Log(sbG.ToString());
                foreach (var w in wins)
                {
                    var sbn = new System.Text.StringBuilder($"[WIN] sym={w.symbolId} count={w.count} pay={w.payout:F2} pos=");
                    foreach (var p in w.positions) sbn.Append($"({p / 100},{p % 100})");
                    UnityEngine.Debug.Log(sbn.ToString());
                }
                if (wins.Count == 0) UnityEngine.Debug.Log("[WIN] 无普通连线赢分");
            }

            if (wins.Count > 0 && m_reelView != null)
            {
                // A 模式(sequentialWinAnimation=true)：赢线逐条顺序高亮播放；B 模式：所有线同时高亮。
                m_reelView.m_winSequential = (m_machine.config != null && m_machine.config.holdSpin != null
                                              && m_machine.config.holdSpin.sequentialWinAnimation);
                m_reelView.HighlightWins(wins);
            }
            if (lineWin > 0)
            {
                if (m_player != null) m_player.ShowWinValue((long)System.Math.Round(lineWin));
                if (FMODSoundMgr.Instance != null) FMODSoundMgr.Instance.PlaySound("event:/Sounds/111");
            }

            // 2) Scatter 统计（respin 池不含 Scatter，自然为 0；基础旋转据此折算免费次数）
            int scId = (m_machine.config != null) ? m_machine.config.ScatterId() : -1;
            if (scId > 0)
            {
                int sc = 0;
                for (int ri = 0; ri < grid.Length; ri++)
                    for (int k = 0; k < grid[ri].Length; k++)
                        if (grid[ri][k] == scId) sc++;
                scatterCount = sc;
            }
            return lineWin;
        }

        /// <summary>结算：收分滚动 / 彩金脉冲 / 奖池刷新 / 中奖高亮。</summary>
        void Settle(GameResult r)
        {
            if (m_bonus != null)
            {
                m_bonus.PlayJackpots(r);
                m_bonus.ShowPots(m_machine.session.Pots);
            }

            string fbTag = "none";
            Debug.Log($"[Spin] mode={m_machine.config.modeName} total={r.totalPayout:F2} " +
                      $"base={r.baseWin:F2} scatter={r.scatterPayout:F2}({r.scatterCount}) " +
                      $"feature={r.featureWin:F2} fs={r.freeSpinsWin:F2}(x{r.freeSpinsAwarded}) " +
                      $"fireballs={fbTag}");
        }
        #endregion

        #region 辅助
        /// <summary>结算日志：输出 压分/总分/赢分（调试用，便于核对每轮 respin 是否扣压分 / 赢分是否入账）。</summary>
        void LogSettle(string tag, float bet, float win)
        {
            long credit = (m_player != null) ? m_player.m_credit_num : 0;
            Debug.Log($"[结算:{tag}] 压分={bet:F0} 赢分={win:F0} 总分={credit}");
        }

        /// <summary>进 Mini 之前的来源 LOG：区分是「Scatter 触发」还是「Hold&Spin 收集 FreeSpins 火球」触发，
        /// 并统计实际的 FreeSpins 火球格数，便于排查"莫名进入免费小游戏"。</summary>
        void LogMiniEntry(string whence, GameResult r, int scatterOrig, int freeballOrig, HoldSpinState hs = null)
        {
            if (r == null) return;
            string fbCells = "";
            if (hs != null)
            {
                int cnt = 0;
                for (int rr = 0; rr < hs.reels; rr++)
                    if (hs.isFull[rr])   // ★ 仅满列内的 FreeSpins 火球才算（与 award 门槛一致）
                        for (int row = 0; row < hs.cells[rr].Length; row++)
                            if (hs.cells[rr][row].filled && hs.cells[rr][row].kind == FireballKind.FreeSpins) cnt++;
                fbCells = $" 满列内FreeSpins火球格数={cnt}";
            }
            Debug.Log($"[MINI-ENTRY] 来源={whence} | scatterCount={r.scatterCount} Scatter触发={scatterOrig} FreeSpins火球追加={freeballOrig} 进入次数={r.freeSpinsAwarded}{fbCells}");
        }

        /// <summary>
        /// 等待玩家按确认键（Start）后才继续。期间 Start 键由 Input.Update 拦截设 _waitingConfirm=false。
        /// 自动结算：DataManager.Instance.Setting[1].auto == 1 时，短暂展示赢分后自动确认，
        /// 无需按确认键（玩家仍可在展示期间按确认键立即跳过等待）。
        /// </summary>
        IEnumerator WaitForConfirmKey(bool allowAuto = true)
        {
            _waitingConfirm = true;
            try
            {
                // ★ 自动模式（F1 autoPlay 或 sd.auto==1）：按可调时长停留，避免「秒过」直接进下一局。
                //   手动确认 / 连续按确认 不进入此分支，仍纯等确认键（不受影响）。
                bool autoSettle = autoPlay ||
                    (allowAuto && DataManager.Instance != null &&
                     DataManager.Instance.Setting != null &&
                     DataManager.Instance.Setting.TryGetValue(1, out var sd) &&
                     sd.auto == 1);
                if (autoSettle)
                {
                    yield return new WaitForSeconds(settleAutoShowSeconds);
                    yield break;
                }

                // 手动确认 / 连续按确认：纯等确认键。
                while (_waitingConfirm)
                    yield return null;
            }
            finally
            {
                _waitingConfirm = false;
            }
        }
        #endregion
    }
}
