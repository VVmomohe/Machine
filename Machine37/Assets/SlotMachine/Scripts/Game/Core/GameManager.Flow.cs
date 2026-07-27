using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 一局流程部分（基础局 + 结算 + 辅助）：
    ///   上锁 → 滚动 → 等停稳 → (Hold&Spin重转循环见 GameManager.Hold.cs) → 结算解锁。
    ///   Hold&Spin 与 Mini 子系统已拆分到 GameManager.Hold.cs / GameManager.Mini.cs。</summary>
    public partial class GameManager
    {
        #region 基础局
        /// <summary>启动一局基础旋转：上锁 → 滚动 → 等停稳 → (可能进入Hold&Spin) → 结算解锁。</summary>
        void StartBaseSpin(GameResult r)
        {
            _spinPending = true;

            if (m_reelView != null)
            {
                // 落了火球，把倍率传给 ShowGrid，滚动阶段就显示倍率
                var fireMults = new Dictionary<int, FireballCell>();
                if (r.holdSpinState != null)
                {
                    var hs = r.holdSpinState;
                    for (int reel = 0; reel < hs.reels; reel++)
                        for (int row = 0; row < hs.cells[reel].Length; row++)
                            if (hs.cells[reel][row].filled)
                                fireMults[reel * 100 + row] = hs.cells[reel][row];
                }
                m_reelView.ShowGrid(r.baseGrid, fireMults.Count > 0 ? fireMults : null);
                StartCoroutine(SettleAfterReelsStop(r));
            }
            else
            {
                Settle(r);
                _spinPending = false;
            }
        }
        #endregion

        #region 结算
        /// <summary>等转轮停稳后：先亮赢分→停顿一拍→滚进总分；若落了火球则进入 Hold&Spin（等玩家逐轮按 Start）。</summary>
        IEnumerator SettleAfterReelsStop(GameResult r)
        {
            // 1) 等转轮停稳（含 waterfall），确保视觉上完全停了才结算
            while (m_reelView != null && m_reelView.IsSpinning())
                yield return null;

            // 2) 基础旋转结算（★ 与 Hold&Spin 每轮 respin 共用 SettleRoundWins：同一套评估/高亮/音效/Scatter 统计口径）
            {
                int sc;
                float bw = SettleRoundWins(r.baseGrid, m_machine.totalBet, out sc);
                r.baseWin = bw;
                r.scatterCount = sc;
                if (m_machine.config != null && m_machine.config.freeSpins != null)
                    r.freeSpinsAwarded = m_machine.config.freeSpins.SpinsFor(sc);
                // 注：r.scatterPayout 仍由 GameSession.Play 按 ScatterUtil.Payout 计算（与历史一致），此处不重复折算。
            }

            // ★ 停轮即结算 LOG：全部滚动停下 = 本局物理结果已确定，立即输出 压分/总分/赢分，
            //   不推迟到「确认」之后（确认只是赢分滚入总分的演出，不是结算本身）。
            //   这样无论是否进 Hold&Spin，基础旋转停轮都有一条结算日志；Hold&Spin 每轮由 AdvanceHoldSpin 的 [结算:respin] 覆盖。
            if (m_player != null)
                LogSettle("基础", m_machine.totalBet, r.baseWin + r.scatterPayout + r.respinLineWin);

            // ★ Hold & Spin 入口：基础旋转落了火球 → 收集/推进火球特性 → 统一结算
            if (r.holdSpinState != null)
            {
                EnterHoldSpin(r, r.holdSpinState);

                if (r.holdSpinState.IsOver())
                {
                    // 初始即满列：播掉落动画
                    _holdRolling = true;
                    if (m_reelView != null)
                        for (int reel = 0; reel < r.holdSpinState.reels; reel++)
                            if (r.holdSpinState.isFull[reel])
                            {
                                yield return StartCoroutine(m_reelView.CollectFullReelAnimation(reel));
                                if (m_bonus != null) ShowJackpotEffectsForReel(r.holdSpinState, reel);
                                // 火球收集成功：按本列总倍数分支播放音效（>8→18，否则→110）
                                if (FMODSoundMgr.Instance != null)
                                    FMODSoundMgr.Instance.PlaySound(HoldSpinState.ReelSum(r.holdSpinState, reel) > 8f ? "event:/Sounds/18" : "event:/Sounds/110");
                            }
                    _holdRolling = false;
                    // 统计 FREE 火球 + 完成结算（FinishHoldSpin 设 totalPayout + 日志/奖池 + 清理）
                    AwardFreeballSpinsFromMain(r.holdSpinState, r);
                    FinishHoldSpin();
                }
                else
                {
                    // 初始火球已展示（EnterHoldSpin 显示了火球格/计数器）。
                    // 每轮 respin 由 Start 键通过 OnStartKey → AdvanceHoldSpin 触发。
                    // 这里直接结束，等玩家按 Start 开始第一轮收集。
                    yield break;
                }

                // ★ 仅初始即满列（IsOver）走这里：统一结算
                if (m_player != null)
                {
                    long tw = (long)System.Math.Round(r.totalPayout);
                    m_player.ShowWinValue(tw);
                    yield return StartCoroutine(WaitForConfirmKey()); // auto 1s 或手动确认
                    m_player.ApplySpinResult(r);
                    // ★ 计数器不在确认时隐藏（同正常收尾口径）：保留显示到玩家开新基础局才清。
                }

                int fbInit = r.freeSpinsAwarded - _holdScatterSpins;
                LogMiniEntry("Hold&Spin初始即满列(IsOver)", r, _holdScatterSpins, fbInit, r.holdSpinState);
                if (WillEnterMini(r)) { EnterMiniNow(r); yield break; }
                _spinPending = false;
                yield break;
            }

            // 3) 无火球：先显示赢分 → 等按确认键 → 再滚动到总分
            // ★ 若本局奖励免费旋转且将进入 Mini：先把内部免费旋转赢分剔除（改由 Mini 统一结算火球），
            //   避免下方 ApplySpinResult 把内部 freeSpinsWin 一起滚进余额造成重复派彩。
            LogMiniEntry("基础局Scatter触发", r, r.freeSpinsAwarded, 0, null);
            if (WillEnterMini(r))
            {
                r.freeSpinsWin = 0;
                r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin + r.respinLineWin;
            }
            if (m_player != null)
            {
                long win = (long)System.Math.Round(r.totalPayout);
                m_player.ShowWinValue(win);              // 先静态显示赢分
                yield return StartCoroutine(WaitForConfirmKey()); // 等待玩家按确认键
                m_player.ApplySpinResult(r);             // 开始滚进总分
            }

            // 4) 免费游戏触发 → 进入 Mini（隐藏 Main，Mini 内统一结算火球）；否则正常结算解锁
            if (MaybeEnterMini(r)) { _spinPending = false; yield break; }
            Settle(r);
            _spinPending = false;
        }

        /// <summary>
        /// 构建 Hold&amp;Spin respin 结算网格（★ 从视图层读取，保证与屏幕显示 100% 一致）。
        ///   优先级：火球 overlay（玩家最上层看到的）&gt; shownSym（卷轴格定格值）。
        ///   火球 overlay 与 shownSym 是两套独立系统：overlay 由 ApplyRespinStep→ShowFireballOverlay 在格上方
        ///   盖一个独立 GameObject，而 shownSym 记录的是 displayStrip 的原始符号（可能被 overlay 盖住）。
        ///   若只读 shownSym，会出现"屏幕显示火球但结算按 Wild/普通符算"的矛盾。
        /// </summary>
        int[][] BuildRespinGrid(HoldSpinState hs)
        {
            int fbId = (m_machine != null && m_machine.config != null) ? m_machine.config.fireballSymbolId : 12;
            int n = hs.reels;
            int[][] grid = new int[n][];
            for (int r = 0; r < n; r++)
            {
                int rows = hs.cells[r].Length;
                grid[r] = new int[rows];
                for (int row = 0; row < rows; row++)
                {
                    // ★ 有火球 overlay → 玩家看到的是火球（overlay 盖在卷轴格上面）
                    if (m_reelView != null && m_reelView.HasFireballOverlay(r, row))
                    {
                        grid[r][row] = fbId;
                        continue;
                    }
                    // ★ 无 overlay → 读卷轴格 shownSym（SetCell 定格值）
                    grid[r][row] = (m_reelView != null) ? m_reelView.GetVisibleSymbol(r, row) : 0;
                }
            }
            return grid;
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

            if (wins.Count > 0 && m_reelView != null) m_reelView.HighlightWins(wins);
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

            string fbTag = (r.holdSpinState != null) ? "HOLD&SPIN" : "none";
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

            // ★ 自动结算：allowAuto==true 且 auto==1 时延时后自动继续（不再等确认键）。
            //   基础时长用可调 settleAutoShowSeconds（原 0.9s），并以 settleMinShowSeconds 为下限（取较大者）。
            if (allowAuto && DataManager.Instance != null &&
                DataManager.Instance.Setting != null &&
                DataManager.Instance.Setting.TryGetValue(1, out var sd) &&
                sd.auto == 1)
            {
                float autoShow = Mathf.Max(settleAutoShowSeconds, settleMinShowSeconds);
                yield return new WaitForSeconds(autoShow);
                _waitingConfirm = false;
                yield break;
            }

            // 手动确认 / F1 自动连转 / 连续按确认：等确认键，但保证最短显示时间。
            // ★ 即便玩家在赢分/选中高亮刚出现的瞬间就按确认，也至少停留 settleMinShowSeconds 秒，
            //   让赢分滚动和连线高亮播完，避免「秒过」看不清结算。时间可在 Inspector 调。
            float enterT = Time.time;
            float minShow = Mathf.Max(0f, settleMinShowSeconds);
            while (_waitingConfirm)
                yield return null;
            float remain = minShow - (Time.time - enterT);
            if (remain > 0f)
                yield return new WaitForSeconds(remain);
        }
        #endregion
    }
}
