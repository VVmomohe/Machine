using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 的 Mini 免费小游戏入口（partial 拆分自 GameManager.Flow.cs）。</summary>
    public partial class GameManager
    {
        #region Mini 免费小游戏入口
        public float m_miniEnterDelay = 1f;       // 进小游戏前特效播放时长(秒)
        public GameObject m_miniEnterEffect;     // 进小游戏前的过渡特效(场景级 overlay，勿挂在 m_miniGame 下)

        private long _pendingMiniBaseWin = 0;     // 进 Mini 前延迟入账的基础赢分(由 Flow.cs 两条路径设置；Hold 路径已即时落账故为 0)

        bool WillEnterMini(GameResult r)
        {
            if (r == null) return false;
            // ★ 触发条件按模式区分：
            //   · 模式A(China Street)：免费次数>0（Scatter 波动性）即进 Mini。
            //   · 模式B(Cash Falls)：仅【整列集满】(enterMiniByColumnFill) 才进 Mini（用户硬性要求，避免单颗 FREE 火球就进小游戏）；
            //     FREE 火球累计的免费次数已在 SettleBaseB 中并入 freeSpinsAwarded，仅作 Mini 局数，不作为 B 的独立触发条件。
            bool trigger = IsModeB() ? r.enterMiniByColumnFill : (r.freeSpinsAwarded > 0);
            if (!trigger) return false;
            if (m_miniGame == null)
            {
                // ★ 防御诊断：触发但场景/预制体没拖 MiniGame → 免费游戏将被静默吞掉（"该进没进"最常见根因）。
                Debug.LogError($"[MINI-MISSING] 触发Mini(IsModeB={IsModeB()}, freeSpinsAwarded={r.freeSpinsAwarded}, enterMiniByColumnFill={r.enterMiniByColumnFill}) 但 m_miniGame 未赋值（场景/预制体需在 Inspector 拖 MiniGame），免费游戏将无法进入！");
                return false;
            }
            return m_miniGame.GetComponent<MiniGame>() != null;
        }

        void EnterMiniNow(GameResult r, System.Action onRestore = null, int overrideSpins = -1)
        {
            long deferredBaseWin = _pendingMiniBaseWin;   // 捕获本次待延迟入账的基础赢分(进 Mini 前的路径已设置)
            _pendingMiniBaseWin = 0;
            StartCoroutine(EnterMiniCoroutine(r, onRestore, overrideSpins, deferredBaseWin));
        }

        IEnumerator EnterMiniCoroutine(GameResult r, System.Action onRestore, int overrideSpins, long deferredBaseWin)
        {
            r.freeSpinsWin = 0;
            _miniActive = true;   // ★ 立即上锁，避免 1s 过渡演出期间被 Start 键穿透(原同步实现也是此刻上锁)
            Debug.Log($"[MINI-ENTRY] ★ 实际进入小游戏: 次数={(overrideSpins >= 0 ? overrideSpins : r.freeSpinsAwarded)} scatterCount={r.scatterCount} 来自Scatter={r.freeSpinsFromScatter} 来自火球={r.freeSpinsFromFireball}");
            // ★ 进小游戏：立即收掉可能残留的大赢庆祝特效(火球 respin 局或进 Mini 前的赢分显示若触发过大赢，
            //   其 3s 特效会盖在进小游戏过渡/小游戏 HUD 上)。主 HUD 小游戏期间仍可见，故必须在此清掉避免重叠。
            if (m_player != null) m_player.CancelBigWin();
            // ★ 进小游戏：同时收掉上局残留的彩金中奖特效(Mini/Minor/Major/Mega)，
            //   否则会带进小游戏 HUD 与场内新中奖特效重叠（与 EnterHoldSpin / OnStartKey 顶部同一范式）。
            if (m_bonus != null) m_bonus.HideAllJackpotEffects();
            // ★ 注意：此处【不再】调用 ResetWinDisplay 清 0 —— 基础赢分已用 ShowWinValue 显示在 HUD，
            //   进 Mini 期间主 HUD 仍可见，应保留该赢分显示(用户要求"赢分显示到小游戏赢分中先")，
            //   待小游戏结算(onDone)才把"基础赢分+Mini赢分"一次性滚入总分并刷新显示。

            // ★ 进小游戏过渡特效：先激活，约 m_miniEnterDelay 秒后隐藏，再真正进入小游戏
            if (m_miniEnterEffect != null)
            {
                m_miniEnterEffect.SetActive(true);
                yield return new WaitForSeconds(m_miniEnterDelay);
                m_miniEnterEffect.SetActive(false);
            }

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
                // ★ 小游戏结算：把"延迟入账的基础赢分 + Mini 火球赢分"一次性滚入总分。
                //   基础赢分此前未滚入(进 Mini 前只 ShowWinValue 显示，未 ApplySpinResult)，避免与进小游戏过渡动画重叠播放。
                //   Hold 中途FREE/IsOver 路径因赢分已即时落账，deferredBaseWin=0，此处等价于原 AddFeatureWin(res.fireTotal)。
                long miniWin = (res != null) ? (long)System.Math.Round(res.fireTotal) : 0L;
                long combined = deferredBaseWin + miniWin;
                if (m_player != null && combined > 0L)
                {
                    m_player.ShowWinValue(combined);
                    m_player.AddWinToCredit(combined);
                }
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
            r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin;
            Settle(r);   // 日志 + 奖池脉冲（不含免费赢分）
            EnterMiniNow(r);
            return true;
        }
        #endregion
    }
}
