using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 一局流程部分：
    ///   上锁 → 滚动 → 等停稳 → (Hold&Spin重转循环) → 结算解锁。</summary>
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

        /// <summary>进入 Hold&Spin：显示初始锁定状态 + 每列计数器，然后等待玩家按 Start 逐轮推进。</summary>
        void EnterHoldSpin(GameResult r, HoldSpinState hs)
        {
            _activeHold = hs;
            _holdResult = r;
            _holdRolling = false;
            _holdScatterSpins = r.freeSpinsAwarded;   // ★ 记录 Scatter 触发的原始次数（不含 FREE 火球追加），用于区分 collectedFree

            if (m_reelView != null)
            {
                // ★ 不在此处 ClearWinHighlight：基础旋转的中奖高亮须保留到玩家按 Start 进第一轮 respin 时才清，
                //   否则 HighlightWins 刚播就立刻被清掉（EnterHoldSpin 紧跟 SettleAfterReelsStop 的 HighlightWins 之后调用），
                //   导致用户看不到普通符号(如 J)的中奖动画（2026-07-25 用户报"J没有播放中奖动画"）。
                //   清理已移至 AdvanceHoldSpin 开头（respin 滚动前）。
                m_reelView.ShowFeatureState(hs);   // 火球格锁定 + 倍率文字 + 有火球的列显示计数器3
            }
            // 注意：IsOver 判定已移到 SettleAfterReelsStop 协程（需要先播满列掉落动画再收尾）
        }

        // ★ Hold&Spin 单轮赢分累加器：由 RunRespinRound 写入、主协程/ResolveAfterRound 读取。
        //   抽成协程后无法用 ref/out 参数回传，故提升为实例字段，仅在一轮 AdvanceHoldSpin 生命周期内有效。
        private float _holdRoundWin;

        /// <summary>按一次 Start 推进一轮 Hold&Spin：扣压分 → 落火球/减计数器 → 真卷轴滚动 → 停稳结算 → 满列派彩。
        /// 每轮结束若还有活跃列则返回等下次 Start，若全部结束则收尾+统一结算(ShowWin→Confirm→Apply→Mini)。</summary>
        IEnumerator AdvanceHoldSpin()
        {
            if (_activeHold == null) yield break;
            _holdRolling = true;
            var hs = _activeHold;

            try
            {
                // ★ 清掉基础旋转的中奖高亮（原在 EnterHoldSpin 立即清，导致 HighlightWins 刚播就被清——用户看不到 J 等符号的中奖动画）。
                //   移到这里：玩家按 Start 进第一轮 respin 时才清，基础高亮在"等确认"期间持续可见。
                if (m_reelView != null) m_reelView.ClearWinHighlight();

                // 扣本轮流注分（每轮单独押注，余额不足则补 LastBet，仍不足则退出）
                if (!TryDeductRoundBet()) yield break;

                // === 单轮 respin（逻辑推进→滚动→停稳→线奖→满列派彩） ===
                _holdRoundWin = 0f;
                yield return StartCoroutine(RunRespinRound(hs));

                // 结算日志 + 本轮押注消费
                LogSettle("respin", m_machine.totalBet, _holdRoundWin);
                if (m_player != null) m_player.ResetBet();

                // === 判断本轮后如何推进（续轮 / 进 Mini / 收尾） ===
                yield return StartCoroutine(ResolveAfterRound(hs));
            }
            finally
            {
                _holdRolling = false;
            }
        }

        /// <summary>扣本轮流注分：余额不足补 LastBet，仍不足返回 false（主协程据此 yield break）。</summary>
        bool TryDeductRoundBet()
        {
            if (m_player == null) return true;
            if (m_player.m_bet_num <= 0) m_player.LastBet();
            if (m_player.m_bet_num <= 0) return false;
            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);
            m_player.ResetWinDisplay();
            return true;
        }

        /// <summary>单轮 respin 核心：推进逻辑→滚动列→真卷轴滚动→停稳结算→普通线奖→满列派彩+列清理。
        /// 写 _holdRoundWin 累加本轮赢分；含 yield（SpinHoldRound / CollectFullReelAnimation）。</summary>
        IEnumerator RunRespinRound(HoldSpinState hs)
        {
            // 1) 推进一轮逻辑（落火球/减计数器/释放列/满列派彩）
            // ★ 释放判定改由显示层 m_engaged 驱动：先把各列 engaged 状态读出来传给逻辑层（在 CheckEngagedAll 之后、本回合滚动之前）。
            bool[] engagedCols = (m_reelView != null) ? m_reelView.GetEngagedColumns() : null;
            var step = GameSession.RespinHoldSpin(hs, m_machine.config, m_machine.rng,
                m_machine.totalBet, m_machine.session.Pots, allowFreeMode: true, engaged: engagedCols);

            // 2) 滚动列 = 未集满列 + 收集满列后"释放滚走"中的幽灵列
            var spun = new List<int>();
            for (int rr = 0; rr < hs.reels; rr++)
            {
                if (!hs.isFull[rr]) { spun.Add(rr); continue; }
                if (m_reelView != null && m_reelView.IsReelReleasing(rr))
                    spun.Add(rr);
            }

            // 3) 真卷轴滚动（新火球作为真实条带符号 id12 随卷轴滚入，停稳后由 ApplyRespinStep 生成锁定 overlay）
            if (m_reelView != null)
            {
                m_reelView.ClearWinHighlight();
                if (step.reelSpun != null)
                    foreach (int reel in step.reelSpun) m_reelView.ReleaseReel(reel);

                var newFireMults = new Dictionary<int, FireballCell>();
                if (step.newFireballs != null)
                    foreach (var c in step.newFireballs) newFireMults[c.reel * 100 + c.row] = c;

                yield return StartCoroutine(m_reelView.SpinHoldRound(spun, 0.75f, newFireMults, step.respinGrid));
            }

            // 4) 停稳后结算：锁新火球 / 释放列 / 计数器-1 / 满列脉冲
            if (m_reelView != null)
                m_reelView.ApplyRespinStep(step, hs);

            // 5) 本轮普通线奖（★ 与基础旋转共用 SettleRoundWins：评估/高亮/音效/诊断同一套口径）
            if (m_machine != null && m_machine.session != null)
            {
                int[][] grid = BuildRespinGrid(hs);
                int sc;
                float win = SettleRoundWins(grid, m_machine.totalBet, out sc);
                if (win > 0)
                {
                    if (_holdResult != null) _holdResult.respinLineWin += win;
                    _holdRoundWin += win;
                }
                // 注：respin 符号池不含 Scatter，sc 恒为 0，不折算免费次数；
                //     Hold&Spin 的免费次数由 FreeSpins 火球（满列）统一负责（CountFreeFireballs/AwardFreeballSpinsFromMain）。
            }

            // 6) 满列派彩 + FREE 火球统计 + 列清理
            if (step.fullReels != null && step.fullReels.Count > 0)
            {
                float collectWin = 0f;
                foreach (var fr in step.fullReels) collectWin += fr.payout;

                // 满列 / 释放列 FREE 火球统一统计
                foreach (var fr in step.fullReels) CountFreeFireballs(hs, fr.reel);
                if (step.reelSpun != null)
                    foreach (int rr in step.reelSpun) CountFreeFireballs(hs, rr, clearAfter: true);

                if (m_reelView != null)
                    foreach (var fr in step.fullReels)
                    {
                        yield return StartCoroutine(m_reelView.CollectFullReelAnimation(fr.reel));
                        // 彩金特效：检查该列是否有彩金火球
                        if (m_bonus != null)
                            ShowJackpotEffectsForReel(hs, fr.reel);
                        // 火球收集成功：按本列总倍数分支播放音效（>8→18，否则→110）
                        if (FMODSoundMgr.Instance != null)
                            FMODSoundMgr.Instance.PlaySound(fr.sum > 8f ? "event:/Sounds/18" : "event:/Sounds/110");
                    }

                if (collectWin > 0)
                {
                    if (m_player != null) m_player.ShowWinValue((long)System.Math.Round(collectWin));
                    _holdRoundWin += collectWin;
                }

                foreach (var fr in step.fullReels)
                {
                    int rr = fr.reel;
                    hs.isFull[rr] = false;
                    hs.released[rr] = true;
                    hs.counter[rr] = 0;
                    for (int row = 0; row < hs.cells[rr].Length; row++)
                        hs.cells[rr][row].filled = false;
                    if (m_reelView != null)
                    {
                        m_reelView.ReleaseCollectedReel(rr);
                        // ★ 满列收集后火球已随 CollectFullReelAnimation 滚走；该列累计倍率 X 已通过 AddMultiplier 显示在计数器中。
                        //   不再单独 Freeze——可见性由 ReelFireNum 自管（active 且 rate>0 即显示 X），撑到结算清零/开新局。
                    }
                }
                // ★ 满列收集后刷新特效——收集后该列不再差1个火球，m_effect 应关闭
                if (m_reelView != null) m_reelView.RefreshColumnEffects(hs);
            }
        }

        /// <summary>单轮结束后分流：续轮(防狂按等待) / 中途收集到 FREE 火球进 Mini(保留 _activeHold) / IsOver 收尾。
        /// 各分支最终都结束本协程（主协程其后无业务代码，仅 finally 复位 _holdRolling）。</summary>
        IEnumerator ResolveAfterRound(HoldSpinState hs)
        {
            var holdR = _holdResult;

            // ★ collectedFree: 仅当 HoldSpin 期间「新增」了 FREE 火球奖励（不含 Scatter 触发的原始次数）。
            //   之前这里用 freeSpinsAwarded>0 会误把 Scatter 的 10 次也算进去，导致首次 respin 就进 Mini。
            int freeballAdded = (holdR != null) ? holdR.freeSpinsAwarded - _holdScatterSpins : 0;
            bool collectedFree = freeballAdded > 0;

            // 本轮后尚未结束且未收集 FREE：等信用滚动(IsRolling)动画播完才放行下一轮 Start（防狂按穿透）
            if (!hs.IsOver() && !collectedFree)
            {
                // ★ 防狂按穿透（用户 2026-07-25 拍板：选"急停+结算完才推进"）：
                //   本轮赢分信用滚动(IsRolling)动画期间仍保持 _holdRolling=true，忽略所有 Start 输入，
                //   等动画结束才放行下一轮 Start——避免"结算（赢分滚动）还没播完，狂按就又触发下一轮 respin"（用户反馈的 BUG）。
                int waitFrames = 0;
                while (m_player != null && m_player.IsRolling && waitFrames++ < 600)
                    yield return null;
                yield break;
            }

            // ★ 收集到 FREE 但 HoldSpin 还没结束（counter>0 列仍在）→ 先进 Mini，保留 _activeHold
            //   Mini 结束后在回调里恢复火球/计数器，继续跑剩余列。
            if (collectedFree && !hs.IsOver())
            {
                // 结算当前赢分（不调 FinishHoldSpin——保留 _activeHold 给回调恢复）
                holdR.featureWin = hs.accumulated;
                holdR.totalPayout = holdR.baseWin + holdR.scatterPayout + holdR.respinLineWin + holdR.featureWin + holdR.freeSpinsWin;
                Settle(holdR);
                LogSettle("特性结束(进Mini)", m_machine.totalBet, holdR.featureWin);

                if (m_player != null)
                {
                    long tw = (long)System.Math.Round(holdR.totalPayout);
                    m_player.ShowWinValue(tw);
                    yield return StartCoroutine(WaitForConfirmKey());
                    m_player.ApplySpinResult(holdR);
                }

                int awardSpins = holdR.freeSpinsAwarded;   // 先保存（下面会清零）

                // ★ 清零 ALL FREE 火球 cells（全列遍历），防止 Mini 回来后 IsOver 时
                //   AwardFreeballSpinsFromMain 把同一批火球重数一遍 → 二次进 Mini → 次数膨胀。
                for (int r = 0; r < hs.reels; r++)
                    for (int row = 0; row < hs.cells[r].Length; row++)
                        if (hs.cells[r][row].filled && hs.cells[r][row].kind == FireballKind.FreeSpins)
                            hs.cells[r][row].filled = false;

                // 清零已结算字段，Mini 后 IsOver 时只算增量（防重复结算）
                holdR.baseWin = 0;
                holdR.scatterPayout = 0;
                holdR.respinLineWin = 0;
                holdR.freeSpinsAwarded = 0;
                _holdScatterSpins = 0;          // Scatter 奖励已随本次 Mini 消耗完毕
                hs.accumulated = 0;

                // 隐藏计数器（Mini 回来后恢复），但不清 _activeHold
                if (m_reelView != null) m_reelView.HideAllCounters();

                // 进入 Mini，回调中恢复 HoldSpin
                var savedHs = hs;
                LogMiniEntry("Hold&Spin中途收集FreeSpins火球", holdR, _holdScatterSpins, freeballAdded, savedHs);
                EnterMiniNow(holdR, () =>
                {
                    _activeHold = savedHs;
                    if (m_reelView != null)
                        m_reelView.ShowFeatureState(savedHs);
                }, awardSpins);
                yield break;
            }

            // === IsOver → 正常收尾（调 FinishHoldSpin） ===
            if (hs.IsOver()) AwardFreeballSpinsFromMain(hs, holdR);
            FinishHoldSpin();
            // ★ 特性结束、结算完成：num 与 rate 【不清零】——保留显示到玩家按确认开新局。
            //   隐藏时机：开新基础局（OnStartKey / ShowGrid → HideAllCounters → ResetAll），
            //   届时每列 num、rate 归 0，(num==0 && rate==0) 成立即隐藏（含满列 X 倍列）。
            LogSettle("特性结束", m_machine.totalBet, holdR != null ? holdR.featureWin : 0f);

            if (m_player != null && holdR != null)
            {
                long tw = (long)System.Math.Round(holdR.totalPayout);
                m_player.ShowWinValue(tw);
                yield return StartCoroutine(WaitForConfirmKey());
                m_player.ApplySpinResult(holdR);
                // ★ 计数器不在确认时隐藏：保留"收集到多少倍"的显示，直到玩家按确认开新基础局
                //   (OnStartKey → Spin → ShowGrid 入口的 HideAllCounters) 才统一消失。
            }

            int fbEnd = holdR.freeSpinsAwarded - _holdScatterSpins;
            LogMiniEntry("Hold&Spin收尾(IsOver)", holdR, _holdScatterSpins, fbEnd, hs);
            if (WillEnterMini(holdR)) { EnterMiniNow(holdR); yield break; }
            _spinPending = false;
        }

        void FinishHoldSpin()
        {
            var r = _holdResult;
            var hs = _activeHold;
            _activeHold = null;
            _holdResult = null;
            _holdRolling = false;
            _spinPending = false;

            // ★ FinishHoldSpin 本身不直接清火球计数器；清的时机在玩家"按确认开新一局"那一刻：
            //   ① OnStartKey（GameManager.Input.cs:60，IsRolling 守卫前）→ HideAllCounters，按下 Start 开新局瞬间即清；
            //   ② ShowGrid 入口（ReelView.Reels.cs:45，ReleaseCollectedForNextSpin 之后）兜底再清一遍（幂等）。
            //   ★ 结算/确认(respin 收尾)时一律不清——计数器(含 0 圈静止帧、满列 X 文本)撑到开新局才隐藏（用户要求）。
            //   两者都不在 respin 每轮触发（每轮走 AdvanceHoldSpin，计数器须常显）。进 Mini 路径 Flow.cs:368 也会清。

            if (r != null && hs != null)
            {
                r.featureWin = hs.accumulated;
                r.totalPayout = r.baseWin + r.scatterPayout + r.respinLineWin + r.featureWin + r.freeSpinsWin;
            }

            if (r != null) Settle(r);
        }

        /// <summary>按列统计 FreeSpins 火球，分档追加免费次数（IsOver 兜底路径用）。
        /// ★ 仅统计「已集满(isFull)」的列：未满列即便有 FreeSpins 火球也不计入，须先填满一整列（播满列收集动画）才给免费次数。</summary>
        void AwardFreeballSpinsFromMain(HoldSpinState hs, GameResult r)
        {
            if (hs == null || r == null || m_machine?.config?.freeSpins == null) return;
            var fs = m_machine.config.freeSpins;
            int before = r.freeSpinsAwarded;
            for (int reel = 0; reel < hs.reels; reel++)
            {
                if (!hs.isFull[reel]) continue;   // ★ 门槛：仅满列才统计 FreeSpins 火球
                var col = hs.cells[reel];
                if (col == null) continue;
                int cnt = 0;
                for (int row = 0; row < col.Length; row++)
                    if (col[row].filled && col[row].kind == FireballKind.FreeSpins) cnt++;
                if (cnt > 0) r.freeSpinsAwarded += fs.FreeballAwardFor(cnt);
            }
            if (r.freeSpinsAwarded != before)
                Debug.Log($"[FREE] 兜底统计(满列): {before} → {r.freeSpinsAwarded}");
        }

        /// <summary>统计单列 FREE 火球数并累加到 _holdResult.freeSpinsAwarded。
        /// ★ 仅统计「已集满(isFull)」的列：未满列（含刚转出的散落 FreeSpins 火球）不计入，须先填满一整列才给免费次数。</summary>
        void CountFreeFireballs(HoldSpinState hs, int reel, bool clearAfter = false)
        {
            if (hs == null || reel < 0 || reel >= hs.reels) return;
            if (!hs.isFull[reel]) return;   // ★ 门槛：仅满列才统计 FreeSpins 火球
            if (_holdResult == null || m_machine?.config?.freeSpins == null) return;
            int cnt = 0;
            for (int row = 0; row < hs.cells[reel].Length; row++)
                if (hs.cells[reel][row].filled && hs.cells[reel][row].kind == FireballKind.FreeSpins) cnt++;
            if (cnt > 0) _holdResult.freeSpinsAwarded += m_machine.config.freeSpins.FreeballAwardFor(cnt);
            if (clearAfter)
                for (int row = 0; row < hs.cells[reel].Length; row++)
                    hs.cells[reel][row].filled = false;
        }

        /// <summary>扫描某列中的彩金火球（Mini/Minor/Major/Mega），逐个触发 BonusView 特效。</summary>
        void ShowJackpotEffectsForReel(HoldSpinState hs, int reel)
        {
            if (hs == null || m_bonus == null) return;
            for (int row = 0; row < hs.cells[reel].Length; row++)
            {
                var c = hs.cells[reel][row];
                if (c.filled && c.kind >= FireballKind.Mini && c.kind <= FireballKind.Mega)
                    m_bonus.ShowJackpotEffect(c.kind);
            }
        }

        /// <summary>
        /// 构建 Hold&amp;Spin respin 结算用的符号网格。
        /// ★ 优先用 step.respinGrid（RespinHoldSpin 生成的权威数据），
        ///   不再绕道 GetVisibleSymbol → shownSym（经过 SpinHoldRound 滚动渲染后的缓存，
        ///   displayStrip→shownSym 的多层偏移映射可能导致与权威数据不一致，
        ///   表现为"屏幕有连号符号但赢分=0"）。
        /// </summary>
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

        #region Mini 免费小游戏入口
        bool WillEnterMini(GameResult r)
        {
            if (r == null || r.freeSpinsAwarded <= 0 || m_miniGame == null) return false;
            return m_miniGame.GetComponent<MiniGame>() != null;
        }

        void EnterMiniNow(GameResult r, System.Action onRestore = null, int overrideSpins = -1)
        {
            r.freeSpinsWin = 0;
            _miniActive = true;
            Debug.Log($"[MINI-ENTRY] ★ 实际进入小游戏: 次数={(overrideSpins >= 0 ? overrideSpins : r.freeSpinsAwarded)} scatterCount={r.scatterCount}");
            // ★ 进入小游戏：清掉基础局赢分显示(归 0)。余额已由 ApplySpinResult 在滚入，
            //   ResetWinDisplay 会先把进行中的滚分落账再清 0，不丢分。Mini 全程主 HUD 仍可见，
            //   不清会一直挂着基础局那笔赢分。
            if (m_player != null) m_player.ResetWinDisplay();
            // 进入小游戏：切换 BGM 到 event:/Sounds/8（PlayBGM 内部自动停掉主游戏 BGM）
            if (FMODSoundMgr.Instance != null)
            {
                FMODSoundMgr.Instance.PlayBGM("event:/Sounds/8");
                FMODSoundMgr.Instance.PlaySound("event:/Sounds/7");
            }
            int spins = overrideSpins >= 0 ? overrideSpins : r.freeSpinsAwarded;
            var mini = m_miniGame.GetComponent<MiniGame>();
            mini.StartMini(spins, (res) =>
            {
                _miniActive = false;
                if (m_player != null && res != null && res.fireTotal > 0f)
                    m_player.AddFeatureWin(res.fireTotal);
                // Mini 结束后恢复主游戏 BGM（event:/Sounds/11）
                if (FMODSoundMgr.Instance != null)
                    FMODSoundMgr.Instance.PlayBGM("event:/Sounds/11");
                // Mini 结束后恢复主游戏 HoldSpin（如有）
                onRestore?.Invoke();
            });
        }

        /// <summary>无火球分支用：判定 + 结算（Settle）+ 进 Mini。
        /// 返回 true 表示已进入 Mini（调用方应 yield break，不再走主游戏结算）。</summary>
        bool MaybeEnterMini(GameResult r)
        {
            if (!WillEnterMini(r)) return false;
            r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin + r.respinLineWin;
            Settle(r);   // 日志 + 奖池脉冲（不含免费赢分）
            EnterMiniNow(r);
            return true;
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
            //   0.9s 延时给玩家看清赢分/高亮后自动推进。
            if (allowAuto && DataManager.Instance != null &&
                DataManager.Instance.Setting != null &&
                DataManager.Instance.Setting.TryGetValue(1, out var sd) &&
                sd.auto == 1)
            {
                yield return new WaitForSeconds(0.9f);   // 给玩家看清赢分/高亮
                _waitingConfirm = false;
                yield break;
            }

            while (_waitingConfirm)
                yield return null;
        }
        #endregion
    }
}
