using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;   // FireballKind / FireballCell（火球类型判定）

namespace com.slot
{
    /// <summary>ReelView 符号资源部分：GameObject 创建 / 精灵加载缓存 / 单格设置 / RNG。</summary>
    public partial class ReelView
    {
        Dictionary<int, Sprite> _symCache = new Dictionary<int, Sprite>();
        uint _rngState = 0x9E3779B9u;
        static int s_cellSerial = 0;   // ★ 格子克隆全局自增编号（创建时定值，用于追踪/调试）

        /// <summary>行号 -> 局部 Y（底部对齐，row 0 在最下面）。</summary>
        float RowToY(int row) => row * m_cellSize + m_rowBaseY;

        GameObject CreateCell(Transform parent, int symbolId, int visualRow)
        {
            GameObject go;
            if (m_symbolPrefab != null)
            {
                go = Instantiate(m_symbolPrefab, parent);
                go.SetActive(true);
            }
            else
            {
                go = CreateImageGO(parent, $"cell_{parent.name}_{visualRow}");
            }
            // 静态棋盘用：按行号定位（底部对齐，row 0 在最下面）
            var rt = go.transform as RectTransform;
            if (rt != null) rt.anchoredPosition = new Vector2(0f, RowToY(visualRow));
            SetCellSprite(go, symbolId);
            var citem = go.GetComponent<ReelItem>();
            if (citem != null) citem.m_serial = s_cellSerial++;
            return go;
        }

        GameObject CreateImageGO(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            if (img != null) { img.raycastTarget = false; img.color = Color.white; }
            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(m_cellSize, m_cellSize);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            }
            return go;
        }

        /// <summary>解析第 k 格的 Image：
        /// 优先用 prefab 上的 ReelItem.m_image，无 ReelItem（无 prefab 回退路径）时用缓存的 cellImgs[k]。</summary>
        Image CellImage(ReelState st, int k)
        {
            if (k < 0 || k >= st.cellItems.Count) return null;
            var item = st.cellItems[k];
            if (item != null && item.m_image != null) return item.m_image;
            if (k < st.cellImgs.Count) return st.cellImgs[k];
            return null;
        }

        /// <summary>一次性设置某 GameObject 的精灵（创建格时用）。优先 ReelItem.m_image。
        /// 火球：显示 m_fire、隐藏 m_image（不替换普通图标）；非火球：隐藏 m_fire、显示 m_image。
        /// 创建时符号非火球，则默认隐藏倍率文字 m_text。</summary>
        void SetCellSprite(GameObject go, int id)
        {
            var item = go.GetComponent<ReelItem>();
            if (item != null)
            {
                item.m_id = id;   // 创建时赋值点（SetCellSprite 仅由 CreateCell 调用）；定格路径 SetCell(syncId=true) 也会写回最终 id。
                if (id == m_fireballSymbolId)
                {
                    item.ShowFire(true, m_inFreeSpins);    // 火球：FreeSpins 时亮 m_freeFire，否则 m_fire；隐藏 m_image
                }
                else
                {
                    item.ShowFire(false);
                    var s = GetSymbol(id);
                    if (s == null) item.m_image.enabled = false;
                    else
                    {
                        item.m_image.enabled = true;
                        item.m_image.sprite = s;
                        var col = item.m_image.color;
                        item.m_image.color = new Color(col.r, col.g, col.b, m_symbolAlpha);
                    }
                    // ★ 确保 UIImageAnimator 正在播放（同 SetCell 兜底）
                    var anim = item.GetComponent<UIImageAnimator>();
                    if (anim != null && !anim.IsPlaying) anim.Restart();
                }
                if (item.m_text != null && id != m_fireballSymbolId)
                    item.m_text.gameObject.SetActive(false);
            }
            else
            {
                Image img = go.GetComponent<Image>();
                if (img == null) return;
                var s = GetSymbol(id);
                if (s == null) img.enabled = false;
                else { img.enabled = true; img.sprite = s; }
                if (id != m_fireballSymbolId)
                {
                    var txt = go.GetComponentInChildren<Text>();
                    if (txt != null) txt.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>滚动/定格时设置第 k 格：走缓存，符号未变则跳过（避免每帧重复赋值）。
        /// 火球：显示 m_fire、隐藏 m_image；非火球：隐藏 m_fire、显示 m_image 并设图标。
        /// 非火球符号会顺带隐藏该格的倍率文字（m_text），避免滚动中残留上一轮 x1.5。</summary>
        void SetCell(ReelState st, int k, int id, bool syncId = false)
        {
            if (k < 0 || k >= st.cellItems.Count) return;

            // ★ 百搭统一拦截点（根治"第一排出百搭"）：
            //   所有定格路径——基础旋转 SnapFinal / Hold&Spin respin SpinHoldRound —— 最终都经 SetCell 写入 shownSym，
            //   在此一处拦截，保证顶行 / 第一列(reel0) 永远不显示/不记录百搭(Wild=m_wildId)。
            //   ★ 坐标系：row = k - m_buf，row 越大越靠上，row=0 是底行、row=rows-1 才是屏幕"第一行"(顶部)，
            //     故拦截的是 row == st.rows - 1（之前误写成 row==0 拦的是底行，导致顶部百搭漏网）。
            //   只拦 Wild，保留火球(fireballSymbolId)与免费(Scatter)；非顶行/非 reel0 的百搭照常显示。
            //   换成确定性普通符(1..m_symbolMax-1)，不推进 RNG、不闪烁。
            if (id == m_wildId)
            {
                int row = k - m_buf;
                if (st.reelIdx == 0 || row == st.rows - 1)
                    id = m_symbolMin + ((st.stripBase + k) % (m_symbolMax - m_symbolMin));
            }

            // ★ Mini 持久 overlay 模式（v2）：卷轴内仅抑制"老火球"(上一轮已锁定)符号——它们由持久 overlay 固定显示，
            //   避免与滚动中的卷轴符号重影；新火球(不在 m_preLockedFireRows)照常渲染、随卷轴自然滚入，不再突兀。
            //   shownSym 仍记 12，不影响逻辑/对齐判定。
            if (m_persistentFireOverlays && id == m_fireballSymbolId)
            {
                int row = k - m_buf;
                int key = st.reelIdx * 100 + row;
                bool isOld = (m_preLockedFireRows != null) && m_preLockedFireRows.Contains(key);
                if (isOld)
                {
                    if (st.shownSym[k] == id) return;
                    st.shownSym[k] = id;
                    var sit = st.cellItems[k];
                    if (sit != null)
                    {
                        // 老火球：本格被持久 overlay 接管（图隐、真实火球在 overlay 上），m_id 保持创建时的火球值(12)即可（本格无可见图标）；
                        // 新火球走下方正常分支，按 syncId 定格时统一写回最终 id。
                        sit.ShowFire(false);
                        if (sit.m_image != null) sit.m_image.enabled = false;
                        if (sit.m_text != null) sit.m_text.gameObject.SetActive(false);
                    }
                    return;
                }
                // 新火球：不 return，落到下面正常渲染分支（随卷轴滚入）
            }

            // ★ 定格同步 m_id：仅 syncId=true（结果 List 算定、定格时）才把 id 写回 m_id，
            //   确保 Hold&Spin 每轮换数据后 m_id 对齐到本轮 List 结果；滚动中(syncId=false)不碰
            //   （用户拍板"中途不变"）。置于 shownSym 提前返回之前：即便符号未变也能把 m_id 对齐。
            if (syncId)
            {
                var it0 = st.cellItems[k];
                if (it0 != null) it0.m_id = id;
            }

            if (st.shownSym[k] == id) return;        // 没变，跳过
            st.shownSym[k] = id;
            var item = st.cellItems[k];
            if (item != null)
            {
                // ★ m_id 定值点：① 创建时（SetCellSprite，ShowGrid 用该格最终符号 grid[reel][rowForK]）；
                //   ② 定格时（syncId=true：SnapFinal 基础旋转 / SpinHoldRound 收尾 每轮 respin）。
                //   滚动中 SetCell(syncId=false) 不碰 m_id → 严格贯彻"创建/定格定值、中途不变"。
                //   数据网格 finalSyms 同样只在 OutcomeGenerator 生成层一次算定，永不被此处改写。
                if (id == m_fireballSymbolId)
                {
                    // 火球是否亮 m_freeFire：FreeSpins 免费游戏(m_inFreeSpins) 或 该火球自身为 FreeSpins 类型（主游戏生成的免费模式火球）。
                    // 网格路径查 _baseFireMults(reel*100+row → FireballCell) 拿 kind；overlay 路径直接用 cell.kind。
                    bool freeFire = m_inFreeSpins;
                    if (!freeFire && _baseFireMults != null)
                    {
                        int row = k - m_buf;
                        if (_baseFireMults.TryGetValue(st.reelIdx * 100 + row, out var fc) && fc != null)
                            freeFire = (fc.kind == FireballKind.FreeSpins);
                    }
                    item.ShowFire(true, freeFire);    // 火球：FreeSpins 时亮 m_freeFire，否则 m_fire；隐藏 m_image
                }
                else
                {
                    item.ShowFire(false);
                    if (item.m_image != null)
                    {
                        Sprite s = GetSymbol(id);
                        if (s == null) item.m_image.enabled = false;
                        else
                        {
                            item.m_image.enabled = true;
                            item.m_image.sprite = s;
                            var col = item.m_image.color;
                            item.m_image.color = new Color(col.r, col.g, col.b, m_symbolAlpha);
                        }
                    }
                    // ★ 确保 UIImageAnimator 正在播放：ShowFire(false) 恢复了 m_image 显示，
                    //   但如果该格之前因 overlay 接管/SetActive 时序等原因导致 animator 停滞，
                    //   此处兜底重启动画（避免"符号显示了但不呼吸/不浮动"）。
                    var anim = item.GetComponent<UIImageAnimator>();
                    if (anim != null && !anim.IsPlaying) anim.Restart();
                }
                // 非火球：隐藏倍率文字（火球倍率文字由 ShowFireballOverlay 在最上层 overlay 上单独设置）
                if (item.m_text != null && id != m_fireballSymbolId)
                    item.m_text.gameObject.SetActive(false);
            }
            else
            {
                // 无 prefab 回退路径：直接设 m_image
                Image img = (k < st.cellImgs.Count) ? st.cellImgs[k] : null;
                if (img == null) return;
                Sprite s = GetSymbol(id);
                if (s == null) img.enabled = false;
                else { img.enabled = true; img.sprite = s; }
            }
        }

        /// <summary>
        /// 按 symbol ID 加载精灵。
        /// Config paytable ID 与 Icon 资源文件编号一一对应（1-based）：
        ///   id1=icon1(9), id2=icon2(10), ..., id9=icon9(章鱼),
        ///   id10=icon10(百搭Wild), id11=icon11(免费游戏Scatter), id12=icon12(火球)。
        /// 无需偏移，直接用 id 当 iconId。
        /// </summary>
        Sprite GetSymbol(int id)
        {
            int iconId = id;
            if (_symCache.TryGetValue(iconId, out var s) && s != null) return s;
            s = Resources.Load<Sprite>($"Icon/icon{iconId}/icon{iconId}");
            _symCache[iconId] = s;
            return s;
        }

        /// <summary>LCG 随机数（0..32767），RandSymbol/RandInt 共用。</summary>
        uint NextRand()
        {
            _rngState = 1664525u * _rngState + 1013904223u;
            return (_rngState >> 16) & 0x7FFFu;
        }

        /// <summary>随机可见符号（排除火球 id，避免滚动中偶现火球图）。</summary>
        int RandSymbol()
        {
            int s;
            do { s = m_symbolMin + (int)(NextRand() % (uint)(m_symbolMax - m_symbolMin + 1)); }
            while (s == m_fireballSymbolId);
            return s;
        }

        /// <summary>随机普通符号（1..m_symbolMax-1，即排除 Wild(m_symbolMax) 与 火球），用于覆盖火球下面的格子 / 限制百搭。</summary>
        int RandNormalSymbol()
        {
            // m_symbolMax=Wild，取 [m_symbolMin, m_symbolMax-1] → 不含 Wild；该区间不含火球 id(12)
            return m_symbolMin + (int)(NextRand() % (uint)(m_symbolMax - m_symbolMin));
        }

        int RandInt(int minInclusive, int maxExclusive)
        {
            int span = Mathf.Max(1, maxExclusive - minInclusive);
            return minInclusive + (int)(NextRand() % (uint)span);
        }
    }
}
