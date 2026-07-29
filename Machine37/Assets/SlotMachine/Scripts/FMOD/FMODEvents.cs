using UnityEngine;


/// <summary>
/// FMOD事件路径常量
/// 集中管理所有FMOD事件路径，避免硬编码字符串
/// 注意：这些路径需要与FMOD Studio中创建的Event路径完全一致
/// </summary>
public static class FMODEvents
{
    // ==================== BGM（背景音乐） ====================
    public static class BGM
    {
        // 注意：事件路径需要与FMOD Studio中的实际路径一致
        public const string Lobby = "event:/Common/BGM";  // 大厅BGM
        public const string Fruit = "event:/Fruit/BGM";
        public const string FruitWin = "event:/Fruit/Win_BGM";
        public const string Fish = "event:/Fish/BGM";
        public const string Mahjong = "event:/Mahjong/BGM";
    }

    // ==================== Common（通用音效） ====================
    public static class Common
    {
        public const string Score = "event:/Common/Score";
        public const string CoinLoop1 = "event:/Common/Coin_Loop1";
        public const string CoinLoop2 = "event:/Common/Coin_Loop2";
        public const string InsertCoin = "event:/Common/Insert_Coin";
        public const string GameSwitch = "event:/Common/GameSwitch";
        public const string CaiJin = "event:/Common/caijin";
        public const string MegaWin = "event:/Common/MegaWin";
        public const string BigWin = "event:/Common/BigWin";
        public const string Jackpot = "event:/Common/Jackpot";

        /// <summary>押分音：Key1～8 与 Common/DownMoney1～8 同号；9～11 为 Fish/DownMoney9～11（水果机与金鲨银鲨一致）。</summary>
        public static string GetDownMoneySound(int keyIndex)
        {
            return GetFishDownMoneySound(keyIndex);
        }

        /// <summary>金鲨银鲨押分音：Key1～8 与 DownMoney1～8 同号；9～11 为 Fish/DownMoney9～11。</summary>
        public static string GetFishDownMoneySound(int keyIndex)
        {
            if (keyIndex >= 9 && keyIndex <= 11)
                return $"event:/Fish/DownMoney{keyIndex}";
            return $"event:/Common/DownMoney{keyIndex}";
        }
    }

    // ==================== Fruit（水果游戏音效） ====================
    public static class Fruit
    {
        public const string BGM = "event:/Fruit/BGM";
        public const string WinBGM = "event:/Fruit/Win_BGM";
        public const string OpenGame = "event:/Fruit/OpenGame";
        public const string Train = "event:/Fruit/Train";
        public const string TrainStart = "event:/Fruit/train_start";
        public const string TrainStop = "event:/Fruit/train_stop";
        public const string Spotlight = "event:/Fruit/Spotlight";
        public const string WinHint = "event:/Fruit/Win_Hint";
        public const string Score = "event:/Fruit/Score";
        public const string Tick = "event:/Fruit/Tick";
        public const string RunLoop = "event:/Fruit/RunLoop";
        public const string RunEnd = "event:/Fruit/Finish";
        public const string BaoziStop = "event:/Fruit/baozi_stop";
        public const string StarRun1 = "event:/Fruit/StarRun1";
        public const string StarRun2 = "event:/Fruit/StarRun2";

        public static string GetStarRunSound(int index)
        {
            return index == 1 ? StarRun1 : StarRun2;
        }

        // OpenGame系列（带数字后缀）
        public static string GetOpenGameSound(int number)
        {
            return $"event:/Fruit/OpenGame{number}";
        }

        // Bonus音效系列
        public static string GetBonusSound(string soundName)
        {
            return $"event:/Fruit/{soundName}";
        }

        /// <summary>开奖语音播报：Id=10→voice_train，Id=22→voice_all_star，否则→voice_xx（xx 为倍率配置中的门 ID，gateIndex=GateId-1，故 xx=gateIndex+1，范围 1-8）</summary>
        public static string GetVoiceSound(int gameId, int gateIndex)
        {
            if (gameId == 10) return "event:/Fruit/voice_train";
            if (gameId == 22) return "event:/Fruit/voice_all_star";
            int gateId = UnityEngine.Mathf.Clamp(gateIndex + 1, 1, 8); // 门 ID 1-8（菠萝=8 对应 voice_8）
            return $"event:/Fruit/voice_{gateId}";
        }
    }

    // ==================== Fish（捕鱼游戏音效） ====================
    public static class Fish
    {
        public const string BGM = "event:/Fish/BGM";
        public const string Run = "event:/Fish/Run";
        /// <summary>跑灯结束段音效（倒数第 N 格时停 Run 改播此音效）</summary>
        public const string RunEnd = "event:/Fish/RunEnd";
        public const string Spotlight = "event:/Fish/Spotlight";
        public const string Score = "event:/Fish/Score";

        // 动态音效路径（根据配置）
        public static string GetAnimSound(string soundName)
        {
            return $"event:/Fish/{soundName}";
        }

        // Bonus音效系列
        public static string GetBonusSound(string soundName)
        {
            return $"event:/Fish/{soundName}";
        }
    }

    // ==================== Mahjong（麻将游戏音效） ====================
    public static class Mahjong
    {
        public const string BGM = "event:/Mahjong/BGM";
        public const string Shuffle = "event:/Mahjong/Shuffle";
        public const string Break = "event:/Mahjong/Break";
        public const string CoinPop = "event:/Mahjong/Coin_Pop";
        /// <summary>构成消除逐张点亮时</summary>
        public const string Selected = "event:/Mahjong/Selected";
        public const string Score = "event:/Mahjong/Score";
        /// <summary>大彩金音效</summary>
        public const string God = "event:/Mahjong/God";
        /// <summary>中彩金音效</summary>
        public const string GoldenPig = "event:/Mahjong/GoldenPig";
        /// <summary>小彩金音效</summary>
        public const string FortuneRat = "event:/Mahjong/FortuneRat";
        /// <summary>押分键（加强键）音效</summary>
        public const string DownMoney12 = "event:/Mahjong/DownMoney12";
        /// <summary>12 连「碰」消除时</summary>
        public const string RevealPeng = "event:/Mahjong/Reveal_Peng";
        /// <summary>13 连「吃」消除时</summary>
        public const string RevealEat = "event:/Mahjong/Reveal_Eat";
        /// <summary>14 连「杠」消除时</summary>
        public const string RevealGang = "event:/Mahjong/Reveal_Gang";
        /// <summary>15 连「听」消除时</summary>
        public const string RevealListen = "event:/Mahjong/Reveal_Listen";
        /// <summary>16 连及以上「胡」消除时</summary>
        public const string RevealHu = "event:/Mahjong/Reveal_Hu";

        // Bonus音效系列
        public static string GetBonusSound(string soundName)
        {
            return $"event:/Mahjong/{soundName}";
        }
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 将旧的Unity Audio路径转换为FMOD事件路径
    /// 用于快速迁移代码
    /// </summary>
    /// <param name="oldPath">旧的路径，例如："Lobby/BGM"</param>
    /// <returns>FMOD事件路径，例如："event:/BGM/Lobby_BGM"</returns>
    public static string ConvertFromOldPath(string oldPath)
    {
        if (string.IsNullOrEmpty(oldPath)) return string.Empty;

        // 移除可能的扩展名
        oldPath = oldPath.Replace(".mp3", "").Replace(".wav", "").Replace(".ogg", "");

        // 路径映射表
        var pathMap = new System.Collections.Generic.Dictionary<string, string>()
        {
            // BGM
            { "Lobby/BGM", BGM.Lobby },
            { "Fruit/Sound/BGM", BGM.Fruit },
            { "Fruit/Sound/中奖背景音乐", BGM.FruitWin },
            { "Fish/Sound/BGM", BGM.Fish },
            { "Mahjong/Sound/BGM", BGM.Mahjong },

            // Common
            { "Common/得分音效", Common.Score },
            { "Common/金币持续声音", Common.CoinLoop1 },
            { "Common/金币持续声音2", Common.CoinLoop2 },

            // Fruit
            { "Fruit/Sound/OpenGame", Fruit.OpenGame },
            { "Fruit/Sound/跑火车", Fruit.Train },
            { "Fruit/Sound/射灯", Fruit.Spotlight },
            { "Fruit/Sound/中奖提示音", Fruit.WinHint },
            { "Fruit/Sound/得分音效", Fruit.Score },

            // Fish
            { "Fish/Sound/run", Fish.Run },
            { "Fish/Sound/射灯", Fish.Spotlight },
            { "Fish/Sound/得分音效2", Fish.Score },

            // Mahjong
            { "Mahjong/Sound/洗麻将牌", Mahjong.Shuffle },
            { "Mahjong/Sound/麻将碎裂音效", Mahjong.Break },
            { "Mahjong/Sound/金币弹出", Mahjong.CoinPop },
            { "Mahjong/Sound/得分音效", Mahjong.Score },
        };

        if (pathMap.TryGetValue(oldPath, out string fmodPath))
        {
            return fmodPath;
        }

        // 如果没有找到映射，尝试自动转换
        Debug.LogWarning($"未找到路径映射: {oldPath}，尝试自动转换");
        return $"event:/{oldPath}";
    }
}
