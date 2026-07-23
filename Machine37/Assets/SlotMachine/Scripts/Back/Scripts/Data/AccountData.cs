using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

using UnityXML;

namespace Com.Back
{
    [System.Serializable]
    public class AccountData : BaseData
    {

        public int id;
        public BigInteger roundNum;

        public BigInteger all_profit;
        public BigInteger previous_profit;
        public BigInteger current_profit;

        public BigInteger coin;
        public BigInteger ticket;

        public BigInteger TotalInvest;          // 总投入
        public BigInteger TotalReward;       // 总获得

        public override void Init()
        {
            all_profit = 0;
            previous_profit = 0;
            current_profit = 0;
            coin = 0;
            ticket = 0;
            TotalInvest = 0;
            TotalReward = 0;
        }
    }
}
