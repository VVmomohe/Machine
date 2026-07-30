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
        public GameObject m_miniEnterEffect;     // 进小游戏前的过渡特效(场景级 overlay，勿挂在 m_miniGame 下)
        public float m_miniEnterDelay = 1f;       // 进小游戏前特效播放时长(秒)

        bool WillEnterMini(GameResult r)
        {
            if (r == null || r.freeSpinsAwarded <= 0 || m_miniGame == null) return false;
            return m_miniGame.GetComponent<MiniGame>() != null;
        }

        void EnterMiniNow(GameResult r, System.Action onRestore = null, int overrideSpins = -1)
        {
            StartCoroutine(EnterMiniCoroutine(r, onRestore, overrideSpins));
        }

        IEnumerator EnterMiniCoroutine(GameResult r, System.Action onRestore, int overrideSpins)
        {
            r.freeSpinsWin = 0;
            _miniActive = true;   // ★ 立即上锁，避免 1s 过渡演出期间被 Start 键穿透(原同步实现也是此刻上锁)
            Debug.Log($"[MINI-ENTRY] ★ 实际进入小游戏: 次数={(overrideSpins >= 0 ? overrideSpins : r.freeSpinsAwarded)} scatterCount={r.scatterCount}");
            // ★ 进入小游戏：清掉基础局赢分显示(归 0)。余额已由 ApplySpinResult 在滚入，
            //   ResetWinDisplay 会先把进行中的滚分落账再清 0，不丢分。Mini 全程主 HUD 仍可见，
            //   不清会一直挂着基础局那笔赢分。
            if (m_player != null) m_player.ResetWinDisplay();

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
    }
}
