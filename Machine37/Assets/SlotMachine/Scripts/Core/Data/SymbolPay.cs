using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SlotMachine.Core
{
    /// <summary>单个符号的赔付定义（与 JSON paytable 对应）。</summary>
    [Serializable]
    public class SymbolPay
    {
        [JsonProperty("id")]
        public int symbolId;
        public string name;
        public bool wild;
        public bool scatter;
        public bool fireball;                    // 火球：锁定触发 Hold & Spin，携带倍率
        public bool firelink;                    // FireLink 大奖符(已废弃)
        public int minMatch = 0;                 // 独立最低连数；0=用全局 minMatch。章鱼=2(2连即中)。
        public List<float> pays = new List<float>(); // [minMatch连, minMatch+1连, ...] 倍数(×betPerLine)
    }

    public enum WinEvalType
    {
        Paylines = 0,
        Ways = 1,
        Rows = 2          // 逐行匹配：每横排独立统计相同符号数，>=minMatch 即中
    }

    /// <summary>单条中奖记录。lineIndex=-1 表示 Ways 模式。</summary>
    [Serializable]
    public class Win
    {
        public int lineIndex = -1;
        public int symbolId;
        public int count;
        public int ways;                       // 连线模式为 0
        public float payout;
        public List<int> positions = new List<int>(); // reel*100+row，供高亮
    }

}
