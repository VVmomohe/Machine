using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 一局流程部分（基础局 + 结算 + 辅助）：
    ///   上锁 → 滚动 → 等停稳 → 结算解锁。A/B 结算按模式拆分：通用在此文件，模式A 在 GameManager.Flow.A.cs，模式B 在 GameManager.Flow.B.cs；Mini 在 GameManager.Mini.cs。</summary>
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
                //   注意：先让满列收集的 ghost 列转入待释放(_releaseReels)，再清非待释放 overlay——
                //   这样满列收集的原火球 overlay 不删除，下一局卷轴滚动时随卷轴自然滚走(回归滚动队列)。
                m_reelView.ReleaseCollectedForNextSpin(onlyCollected: true);
                m_reelView.ClearFireballOverlaysExceptReleasing();

                // 落了火球，把倍率传给 ShowGrid，滚动阶段就显示倍率。
                // ★ 优先用 res.baseFireballs：基础轮落下的全部火球（不论是否触发 Hold&Spin）都已定倍率，一律显示。
                //   模式B 收集盘还需把【跨局持有火球】(holdSpinState.cells) 也写入 fireMults：
                //   否则这些火球位置的底层格 id=12 但 m_text 为空，表现为"火球没倍数/彩金档"。
                //   同位置若已有持有火球，本局新落同位置会被 ShowHeldFireballs 跳过（保留旧火球），
                //   故 fireMults 中同位置优先保留持有火球倍率，再用 baseFireballs 补全新位置。
                var fireMults = new Dictionary<int, FireballCell>();
                if (IsModeB() && r.holdSpinState != null)
                {
                    var hs = r.holdSpinState;
                    for (int rr = 0; rr < hs.reels; rr++)
                        for (int row = 0; row < hs.cells[rr].Length; row++)
                        {
                            var c = hs.cells[rr][row];
                            if (c.filled) fireMults[c.reel * 100 + c.row] = c;
                        }
                }
                if (r.baseFireballs != null)
                    foreach (var c in r.baseFireballs)
                        if (c.filled)
                        {
                            int key = c.reel * 100 + c.row;
                            if (!fireMults.ContainsKey(key)) fireMults[key] = c;
                        }

                m_reelView.ShowGrid(r.baseGrid, fireMults.Count > 0 ? fireMults : null);
                // ★ 模式B：旋转期即钉住「跨局持有火球」+ 恢复计数器圈数（OnStartKey 的 HideAllCounters 清了，需在旋转期重建），
                //   使收集盘火球与圈数整局持续可见（本局新落火球已由 ShowGrid 底层卷轴显示，跳过避免重影）。
                //   解决"有圈圈时火球/计数器没固定"——开新局 ClearAll/HideAllCounters 清掉上局，若只等停轮后 ShowFeatureState 重钉，旋转期不可见。
                // ★ 不再用 r.holdSpinState != null 作门控：只要模式B 就重建计数器（用户硬规则——有圈圈就显示、没圈圈才隐藏）。
                //   任何"有火球却 board 为 null"的边界都不再让计数器整局隐藏；无 board 但有本局火球时按"新火球→重置3"显示圈。
                if (IsModeB())
                {
                    m_reelView.ShowHeldFireballs(r.holdSpinState, r.baseFireballs);
                    m_reelView.ActivateCounters();   // 旋转期恢复会话级门控（各列是否显示由下面按盘/按火球决定）
                    if (r.holdSpinState != null)
                    {
                        var hs0 = r.holdSpinState;
                        int n = Mathf.Min(hs0.reels, m_reelView.CounterCount());
                        for (int rr = 0; rr < n; rr++)
                        {
                            if (hs0.isFull[rr])
                                m_reelView.SetRespinCounterRow(rr, 0);
                            else if (!hs0.released[rr] && hs0.counter[rr] >= 0)
                                m_reelView.SetRespinCounterRow(rr, hs0.counter[rr]);   // 含 0（3→2→1→0）
                            else
                                m_reelView.HideCounterRow(rr);
                        }
                    }
                    else
                    {
                        // 防御：无持有盘但有本局火球（triggerMin=1 下理论不触发，但保险）。
                        // 这些列本局落了新火球，按"新火球→重置 respinCount"显示圈；其余列隐藏。
                        int rc = (m_machine.config != null && m_machine.config.holdSpin != null)
                            ? m_machine.config.holdSpin.respinCount : 3;
                        var fbReels = new System.Collections.Generic.HashSet<int>();
                        if (r.baseFireballs != null)
                            foreach (var c in r.baseFireballs) if (c != null && c.filled) fbReels.Add(c.reel);
                        int n = m_reelView.CounterCount();
                        for (int rr = 0; rr < n; rr++)
                            if (fbReels.Contains(rr)) m_reelView.SetRespinCounterRow(rr, rc);
                            else m_reelView.HideCounterRow(rr);
                    }
                    // ★ 诊断：旋转期每列计数器最终可见性（核对"有圈圈列是否真的显示了圈"）；受 SlotDebug.VerboseLogs 开关控制。
                    if (SlotDebug.VerboseLogs)
                    {
                        var sb = new System.Text.StringBuilder($"[StartBaseSpin-diag] modeB hold={(r.holdSpinState != null)} baseFb={(r.baseFireballs != null ? r.baseFireballs.Count : 0)}");
                        int n = m_reelView.CounterCount();
                        for (int rr = 0; rr < n; rr++)
                        {
                            var fn = m_reelView.GetCounter(rr);
                            sb.Append($" | r{rr}:act={(fn != null && fn.m_active)} eng={(fn != null && fn.m_engaged)} num={(fn != null ? fn.m_num : -1)}");
                        }
                        UnityEngine.Debug.Log(sb.ToString());
                    }
                }
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

            // ★ 本局出什么：每列符号 + Scatter 标记 + 火球位置 + 触发字段（总是打印，便于核对"这局到底出了什么"——
            //   排查"Scatter 触发=2 但屏上看 r0/r1 没 Scatter"时，可在此确认 3 颗 Scatter 究竟落在哪几列）。
            LogRoundOutput(r);

            // ★ 先算一次 WillEnterMini 结论，供日志与后续逻辑统一使用（避免多次调用口径不一致）。
            bool toMini = WillEnterMini(r);
            // ★ 来源按实际授予拆分（Scatter / FREE 火球单列收集），避免把火球触发的局误标成 Scatter。
            string miniSrc = (r.freeSpinsFromScatter > 0 && r.freeSpinsFromFireball > 0) ? "基础局Scatter+火球"
                           : (r.freeSpinsFromScatter > 0) ? "基础局Scatter"
                           : (r.enterMiniByColumnFill) ? "基础局集满一列"
                           : "基础局火球(FreeSpins单列)";
            LogMiniEntry(miniSrc, r, r.freeSpinsFromScatter, r.freeSpinsFromFireball, r.holdSpinState, toMini);
            if (toMini)
            {
                r.freeSpinsWin = 0;
                r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin;
            }
            if (m_player != null)
            {
                long win = (long)System.Math.Round(r.totalPayout);
                m_player.ShowWinValue(win, !toMini);
                yield return StartCoroutine(WaitForConfirmKey());
                m_player.ResetBet();

                // ★★ 修"赢分没有加到总分"：ShowWinValue 只负责【显示】，不入账。
                //   原代码无条件把 win 塞进 _pendingMiniBaseWin 就完事——只有"进 Mini"那条路会在小游戏结束时
                //   一次性滚进余额；【不进 Mini 的普通局】没有任何地方调 AddWinToCredit/ApplySpinResult，
                //   赢分就永远只是显示，总分不动（且 _pendingMiniBaseWin 残留，下次进 Mini 会被重复付一次）。
                if (toMini)
                {
                    _pendingMiniBaseWin = win;   // 延迟到 Mini 结算时与 Mini 赢分一次性入账
                }
                else
                {
                    _pendingMiniBaseWin = 0;     // 清残留，避免下次进 Mini 重复入账
                    long before = m_player.m_credit_num;
                    m_player.AddWinToCredit(win);   // 普通局：确认后立刻滚进总分（m_win_num 保持显示值不变）
                    Debug.Log($"[入账] 基础局赢分={win} 滚入总分：{before} → {before + win}");
                }
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
            //   ★ 受 SlotDebug.VerboseLogs 开关控制（生产默认关闭，调试置 true 恢复）。
            if (SlotDebug.VerboseLogs)
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

        /// <summary>进 Mini 之前的来源 LOG：区分「Scatter 触发」与「FREE 火球单列收集触发」（A 全局≥triggerMin / B 单列收集），
        /// 各自授予的免费次数已拆到 GameResult.freeSpinsFromScatter / freeSpinsFromFireball，便于排查"莫名进入免费小游戏"。</summary>
        void LogMiniEntry(string whence, GameResult r, int scatterOrig, int freeballOrig, HoldSpinState hs, bool willEnter)
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
            // ★ 关键：willEnter=false 时此局【不会】真正进 Mini（常见根因：m_miniGame 未拖/丢失 MiniGame 组件，
            //   详见 WillEnterMini 内的 [MINI-MISSING] 报错）。标签区分「将进入 / 仅候选」避免误导。
            string tag = willEnter ? "[MINI-ENTRY] ★将进入小游戏" : "[MINI-CANDIDATE] 仅候选·不进入";
            Debug.Log($"{tag} 来源={whence} | scatterCount={r.scatterCount}(左到右={r.scatterL2R}) Scatter触发={scatterOrig} FreeSpins火球追加={freeballOrig} 进入次数(freeSpinsAwarded)={r.freeSpinsAwarded} enterMiniByColumnFill={r.enterMiniByColumnFill} freeSpinsFromScatter={r.freeSpinsFromScatter} freeSpinsFromFireball={r.freeSpinsFromFireball}{fbCells}");
        }

        /// <summary>本局出什么：逐列打印基础棋盘符号 ID（Scatter 标 S）、本局火球位置/种类/倍率、以及 Mini 触发关键字段。
        /// 受 SlotDebug.VerboseLogs 控制（默认 false 不喷），需逐局核对棋盘时设 SlotDebug.VerboseLogs=true 即恢复。</summary>
        void LogRoundOutput(GameResult r)
        {
            if (!SlotDebug.VerboseLogs) return;
            if (r == null || r.baseGrid == null) return;
            int scId = (m_machine != null && m_machine.config != null) ? m_machine.config.ScatterId() : -1;
            var sb = new System.Text.StringBuilder("[本局出什么] ");
            for (int ri = 0; ri < r.baseGrid.Length; ri++)
            {
                sb.Append($"r{ri}[");
                for (int k = 0; k < r.baseGrid[ri].Length; k++)
                {
                    int id = r.baseGrid[ri][k];
                    bool isSc = (scId > 0 && id == scId);
                    sb.Append(id).Append(isSc ? "S" : "").Append(k < r.baseGrid[ri].Length - 1 ? "," : "");
                }
                sb.Append("] ");
            }
            if (r.baseFireballs != null && r.baseFireballs.Count > 0)
            {
                var fbsb = new System.Text.StringBuilder("火球=");
                foreach (var c in r.baseFireballs) if (c != null && c.filled)
                    fbsb.Append($"({c.reel},{c.row},{c.kind},{c.multiplier})");
                sb.Append(fbsb.ToString());
            }
            sb.Append($" | scatterCount={r.scatterCount} scatterL2R={r.scatterL2R} freeSpinsFromScatter={r.freeSpinsFromScatter} freeSpinsFromFireball={r.freeSpinsFromFireball} enterMiniByColumnFill={r.enterMiniByColumnFill}");
            UnityEngine.Debug.Log(sb.ToString());
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
