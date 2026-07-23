using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using UnityXML;

namespace Com.Back
{
    public class InfoView : MoveView
    {

        public TimeView m_time;
        public Text[] m_titleTexts;

        // Start is called before the first frame update
        protected override void Start()
        {
            base.Start();  
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            m_texts[9].SetStr("版本号");
            m_texts[10].SetStr("设备号");

            m_titleTexts[0].text = string.Format("{0} 1:{1}", DataManager.Instance.Language[16].GetStr,
                DataManager.Instance.coin_rate_Arr[DataManager.Instance.Setting[1].coin_rate]);
            m_titleTexts[1].text = string.Format("{0} 1:{1}", DataManager.Instance.Language[15].GetStr,
                DataManager.Instance.ticket_rate_Arr[DataManager.Instance.Setting[1].ticket_rate]);
        }

        // Update is called once per frame
        protected override void Update()
        {
            base.Update();

            textArray[0].text = string.Format("{0}{1}", DataManager.Instance.Account[1].all_profit, DataManager.Instance.Language[26].GetStr);
            //textArray[1].text = string.Format("{0} 币", DataManager.Instance.Account[1].previous_profit);
            //textArray[2].text = string.Format("{0} 币", DataManager.Instance.Account[1].current_profit);
            textArray[3].text = string.Format("{0}{1}", DataManager.Instance.Account[1].coin, DataManager.Instance.Language[26].GetStr);
            textArray[4].text = string.Format("{0}{1}", DataManager.Instance.Account[1].ticket, DataManager.Instance.Language[25].GetStr);

            textArray[5].text = string.Format("{0}{1}", DataManager.Instance.Setting[1].ClearCount, DataManager.Instance.Language[27].GetStr);

            // 修改：添加时间戳有效性检查
            long lastClearDate = DataManager.Instance.Setting[1].LastClearDate;
            DateTime dt;

            if (lastClearDate >= 0 && lastClearDate <= 253402300799) // Unix 时间戳有效范围（1970-9999年）
            {
                dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(lastClearDate).ToLocalTime();
            }
            else
            {
                dt = DateTime.Now;
            }

            textArray[6].text = dt.ToString("yyyy/MM/dd");
            textArray[9].text = string.Format("{0}", dt.ToLongTimeString());
        }

        protected override void OnEnter()
        {
            // opt0 → 清帐（密码正确才执行 SaveDave 清帐，期间 Acount 保持可见）
            if (index == 0) { SettingPanel.Instance.OpenPasswordGate("Acount", SaveDave); return; }
            if (index == 1) { SettingPanel.Instance.OpenScreen("Menus"); return; }
            base.OnEnter();
        }

        protected override void OnCancel()
        {
            base.OnCancel();
            SettingPanel.Instance.Back(); // 返回主界面 Menus
        }

        public override void SaveDave()
        {
            // 设置
            DataManager.Instance.Setting[1].ClearCount++;
            if (m_time == null)
                m_time = FindObjectOfType<TimeView>();
            if (m_time != null && m_time.m_dt >= new DateTime(1970, 1, 1))
            {
                long timestamp = new DateTimeOffset(m_time.m_dt, TimeZoneInfo.Local.GetUtcOffset(m_time.m_dt)).ToUnixTimeSeconds();
                DataManager.Instance.Setting[1].LastClearDate = timestamp >= 0 ? timestamp : DateTimeOffset.Now.ToUnixTimeSeconds();
            }
            else
            {
                DataManager.Instance.Setting[1].LastClearDate = DateTimeOffset.Now.ToUnixTimeSeconds();
            }
            DataHelper.Instance.Modify("Data/Setting.xml", DataManager.Instance.Setting, DataManager.Instance.Setting[1]);

            DataManager.Instance.Account[1].Init();
            DataManager.Instance.Player[1].Init();
            DataManager.Instance.SaveData();
        }
    }
}
