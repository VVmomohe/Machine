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

            // ★ Mini 持久 overlay 模式（v2）：卷轴内仅抑制"老火球"(上一轮已锁定)符号——它们由持久 overlay 固定显示，
            //   避免与滚动中的卷轴符号重影；新火球(不在 m_preLockedFireRows)照常渲染、随卷轴自然滚入，不再突兀。
            //   shownSym 仍记 12，不影响逻辑/对齐判定。
            if (m_persistentFireOverlays && id == m_fireballSymbolId)
            {
                int row = k - m_buf;
                int key = CellKey.Encode(st.reelIdx, row);
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

            // ★ 定格同步 m_id：syncId=true（结果 List 算定、定格时）或 id=火球时都把 id 写回 m_id。
            //   火球格特殊处理：只要视觉呈现火球，m_id 就必须是 12，避免 Inspector 出现"id=3 但显示火球"
            //   的误导性状态（滚动中/减速期 displayStrip 经过火球位置时会暂时显示火球）。非火球仍遵循
            //   "滚动中不变"，仅在定格/创建时定值。
            if (syncId || id == m_fireballSymbolId)
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
                //   ② 定格时（syncId=true：SnapFinal 基础旋转 / 收集盘推进收尾）。
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
                        if (_baseFireMults.TryGetValue(CellKey.Encode(st.reelIdx, row), out var fc) && fc != null)
                        {
                            // ★ A 模式基础轮不应出现 FreeSpins 火球；若数据层泄漏进来，已由 StartBaseSpin 过滤（A 模式跳过 FreeSpins 细胞）不再渲染为 m_freeFire。
                            //   此处仅在真正处于免费游戏(m_inFreeSpins)或 B 模式基础轮合法 FREE 火球时点亮 m_freeFire。
                            freeFire = (fc.kind == FireballKind.FreeSpins);
                        }
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

        /// <summary>停轮后把基础卷轴格同步到权威数据网格（r.baseGrid）：逻辑 id=火球(12) 的格屏幕上确实显示火球，
        /// 使"数据显示火球"与"视觉火球"严格一致——不依赖 ShowFeatureState 的 overlay 去覆盖底层普通符号。
        /// 普通符号位置幂等不变（SetCell 内部会跳过未变化的格）。overlay 仍负责倍率/彩金文字，叠在最上层。
        /// ★ 同时按 _baseFireMults 重写底层火球格的 m_text(倍率/彩金文字)，保证底层格"x3"等文字一定可见——即使
        ///   overlay 因任何原因（prefab 子物体顺序、字体缺失、Canvas 层级）未显示文字，底层格也能兜底显示。</summary>
        public void SyncBoardFromGrid(int[][] grid)
        {
            if (grid == null) return;
            for (int r = 0; r < _reels.Count && r < grid.Length; r++)
            {
                var st = _reels[r];
                if (st == null || st.cellItems == null) continue;
                int rows = grid[r].Length;
                for (int row = 0; row < rows; row++)
                {
                    int k = m_buf + row;
                    if (k < 0 || k >= st.cellItems.Count) continue;
                    int id = grid[r][row];
                    // ★ syncId=true：把 m_id 也对齐到权威网格（避免"逻辑 id=12 但 m_id 是底层 spun 符号"的误读），
                    //   并触发 ShowFire(true) 让火球格显示 m_fire、隐藏 m_image。
                    SetCell(st, k, id, syncId: true);
                    // ★ 火球：兜底写回 m_text 文字（即使 overlay 没显示，底层格也确保"x3"等可见）
                    if (id == m_fireballSymbolId && _baseFireMults != null)
                    {
                        if (_baseFireMults.TryGetValue(CellKey.Encode(st.reelIdx, row), out var cell) && cell != null)
                            SetCellFireballMult(st, k, cell);
                    }
                }
            }
        }

        /// <summary>取 (reel,row) 位置的 ReelItem（底层卷轴格组件），供诊断/外部查询使用。
        /// 越界或卷轴未初始化时返回 null。</summary>
        public ReelItem GetReelItem(int reel, int row)
        {
            if (reel < 0 || reel >= _reels.Count) return null;
            var st = _reels[reel];
            if (st == null || st.cellItems == null) return null;
            int k = m_buf + row;
            if (k < 0 || k >= st.cellItems.Count) return null;
            return st.cellItems[k];
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
