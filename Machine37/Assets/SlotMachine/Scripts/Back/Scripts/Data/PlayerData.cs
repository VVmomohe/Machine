using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityXML;

namespace Com.Back
{
    [System.Serializable]
    public class PlayerData : BaseData
    {

        public int id;

        public long score;
        public long bet;
        public long win;


        public override void Init()
        {
            score = 0;
            bet = 0;
            win = 0;
        }
    }
}
