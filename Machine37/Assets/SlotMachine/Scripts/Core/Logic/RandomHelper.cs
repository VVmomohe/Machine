using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>
    /// 通用随机工具（逻辑层，不依赖 UnityEngine）。
    /// </summary>
    public static class RandomHelper
    {
        /// <summary> Fisher–Yates 洗牌（respin 百搭定点 / 垂直聚类随机用）。</summary>
        public static void Shuffle<T>(List<T> list, ISlotRng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
