using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityXML;

namespace Com.Back
{
    [System.Serializable]
    public class SettingData : BaseData
    {

        public int id;

        public int GameSwitch;
        public int coin_rate;
        public int ticket_rate;
        public int ticket_mode;

        public int min_num;
        public int max_num;

        public int bomb_num;
        public int bonus_num;
        public int experience_num;
        public int sound_bg;

        public int qr;
        public int auto;
        public int language;

        public int ClearCount;    // 清账次数 (2字节，大端)
        public long LastClearDate;    // 上次清账日期 (Unix时间戳)

        public byte ClearPermission { get; set; }   // 清账权限

        public override void Init()
        {
            id = 1;

            coin_rate = 9;
            ticket_rate = 9;
            ticket_mode = 0;

            min_num = 3;
            max_num = 5;

            bomb_num = 0;
            bonus_num = 7;
            experience_num = 4;

            sound_bg = 10;

            qr = 1;
            auto = 0;   // ★ 默认关闭自动结算：每局/每轮 respin 都等玩家按确认键推进（避免火球特性自动连滚）
        }

    }

}
