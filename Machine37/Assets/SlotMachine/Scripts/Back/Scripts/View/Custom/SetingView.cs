using Com.Controller;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityXML;


namespace Com.Back
{
    public class SetingView : MoveView
    {

        public GameObject m_hint;
        public float m_hintTime = 0;

        public RectTransform m_nodel;
        public Text[] tagArray;

        // 当前编辑的设置数据（工作副本）
        private SettingData currentSetting;

        protected override void Start()
        {
            base.Start();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            Load();
            m_hint.SetActive(false);
        }

        // Update is called once per frame
        protected override void Update()
        {
            base.Update();

            if (index <= 10)
            {
                m_nodel.anchoredPosition3D = new Vector3(0, index * 30, 0);
            }

            textArray[0].text = string.Format("1{1}{0}{2}", DataManager.Instance.coin_rate_Arr[currentSetting.coin_rate], DataManager.Instance.Language[26].GetStr, DataManager.Instance.Language[45].GetStr);
            textArray[1].text = string.Format("1{1}{0}{2}", DataManager.Instance.ticket_rate_Arr[currentSetting.ticket_rate], DataManager.Instance.Language[25].GetStr, DataManager.Instance.Language[45].GetStr);
            textArray[2].text = string.Format("{0}", DataManager.Instance.bomb_num_Arr[currentSetting.bomb_num] > 0 ? DataManager.Instance.bomb_num_Arr[currentSetting.bomb_num].ToString() : "不爆机");
            textArray[3].text = string.Format("{0}{1}", DataManager.Instance.Language[60].GetStr, NumberToChinese(currentSetting.experience_num + 1));

            textArray[4].text = string.Format("{0}{1}", DataManager.Instance.min_num_Arr[currentSetting.min_num], DataManager.Instance.Language[45].GetStr);
            textArray[5].text = string.Format("{0}{1}", DataManager.Instance.max_num_Arr[currentSetting.max_num], DataManager.Instance.Language[45].GetStr);

            int[] lanArrT = new int[4] { 31, 32, 57, 58 };
            int tm = currentSetting.ticket_mode;
            tm = tm < 0 ? 0 : (tm > 3 ? 3 : tm);   // 防御：ticket_mode 越界时钳制到 [0,3]，避免 lanArrT 下标越界
            textArray[6].text = string.Format("{0}", DataManager.Instance.Language[lanArrT[tm]].GetStr);
            textArray[7].text = string.Format("{0}", currentSetting.sound_bg > 0 ? currentSetting.sound_bg.ToString() : DataManager.Instance.Language[48].GetStr);
            textArray[8].text = string.Format("{0}", currentSetting.qr > 0 ? DataManager.Instance.Language[53].GetStr : DataManager.Instance.Language[48].GetStr);

            textArray[9].text = string.Format("{0}", currentSetting.auto > 0 ? DataManager.Instance.Language[53].GetStr : DataManager.Instance.Language[48].GetStr);
            textArray[10].text = string.Format("{0}", DataManager.Instance.bonus_num_Arr[currentSetting.bonus_num]);

            var s1 = DataManager.Instance.Setting[1];
            tagArray[0].text = currentSetting.coin_rate == s1.coin_rate ? "" : "!";
            tagArray[1].text = currentSetting.ticket_rate == s1.ticket_rate ? "" : "!";
            tagArray[2].text = currentSetting.bomb_num == s1.bomb_num ? "" : "!";
            tagArray[3].text = currentSetting.experience_num == s1.experience_num ? "" : "!";

            tagArray[4].text = currentSetting.min_num == s1.min_num ? "" : "!";
            tagArray[5].text = currentSetting.max_num == s1.max_num ? "" : "!";

            tagArray[6].text = currentSetting.ticket_mode == s1.ticket_mode ? "" : "!";
            tagArray[7].text = currentSetting.sound_bg == s1.sound_bg ? "" : "!";
            tagArray[8].text = currentSetting.qr == s1.qr ? "" : "!";

            tagArray[9].text = currentSetting.auto == s1.auto ? "" : "!";
            tagArray[10].text = currentSetting.bonus_num == s1.bonus_num ? "" : "!";

            if (m_hint.activeSelf)
            {
                m_hintTime += Time.deltaTime;
                if (m_hintTime > 3)
                {
                    m_hintTime = 0;
                    m_hint.SetActive(false);
                }
            }
        }

        public override void SelectItem()
        {
            base.SelectItem();
            for (int i = 0; i < textArray.Length; i++)
            {
                if (textArray != null && MainView.Instance != null)
                {
                    textArray[i].color =  tabelArray[i].m_text.color;
                }
            }
            for (int i = 0; i < tagArray.Length; i++)
            {
                if (tagArray != null && MainView.Instance != null)
                {
                    tagArray[i].color = tabelArray[i].m_text.color;
                }
            }
        }

        protected override void OnEnter()
        {
            base.OnEnter();

            // opt11 → 初始化（密码正确才 InitData）
            if (index == 11) { SettingPanel.Instance.OpenPasswordGate("GameSeting", InitData); return; }
            // opt12 → 保存（密码正确才 SaveDave）
            if (index == 12) { SettingPanel.Instance.OpenPasswordGate("GameSeting", SaveDave); return; }
            // opt13 → 返回上一个界面（GameSelection）
            if (index == 13) { SettingPanel.Instance.Back(); return; }
        }

        protected override void OnCancel()
        {
            base.OnCancel();
            SettingPanel.Instance.Back(); // 返回上一个界面（GameSelection）
        }

        protected override void LeftAndRight(int num)
        {
            if (index == 0)
                UpdateNum(num, 0, DataManager.Instance.coin_rate_Arr.Length - 1, ref currentSetting.coin_rate);
            else if (index == 1)
                UpdateNum(num, 0, DataManager.Instance.ticket_rate_Arr.Length - 1, ref currentSetting.ticket_rate);
            else if (index == 2)
                UpdateNum(num, 0, DataManager.Instance.bomb_num_Arr.Length - 1, ref currentSetting.bomb_num);
            else if (index == 3)
                UpdateNum(num, 0, 4, ref currentSetting.experience_num);

            else if (index == 4)
            {
                UpdateNum(num, 0, DataManager.Instance.min_num_Arr.Length - 1, ref currentSetting.min_num);
                // 最小押分不能大于最大押分
                while (DataManager.Instance.min_num_Arr[currentSetting.min_num] > DataManager.Instance.max_num_Arr[currentSetting.max_num])
                    currentSetting.max_num++;
            }
            else if (index == 5)
            {
                UpdateNum(num, 0, DataManager.Instance.max_num_Arr.Length - 1, ref currentSetting.max_num);
                // 最大押分不能大于最小押分
                while (DataManager.Instance.max_num_Arr[currentSetting.max_num] < DataManager.Instance.min_num_Arr[currentSetting.min_num])
                    currentSetting.min_num--;
            }

            else if (index == 6)
                UpdateNum(num, 0, 3, ref currentSetting.ticket_mode);

            else if (index == 7)
                UpdateNum(num, 0, 20, ref currentSetting.sound_bg);

            else if (index == 8)
                UpdateNum(num, 0, 1, ref currentSetting.qr);

            else if (index == 9)
                UpdateNum(num, 0, 1, ref currentSetting.auto);

            else if (index == 10)
                UpdateNum(num, 0, DataManager.Instance.bonus_num_Arr.Length - 1, ref currentSetting.bonus_num);
        }

        public void Load()
        {
            var original = DataManager.Instance.Setting[1];
            currentSetting = JsonUtility.FromJson<SettingData>(JsonUtility.ToJson(original));
        }

        public override void InitData()
        {
            if (currentSetting != null)
                currentSetting.Init();
        }

        /// <summary>账户数据(Account[1])的数值字段是否全部为 0（Init 重置的那些：利润/币/票/总投入产出）。
        /// 不含 id / roundNum（计数器，Init 不重置，不反映“是否还有账目”）。</summary>
        private bool IsAccountDataEmpty()
        {
            var a = DataManager.Instance.Account[1];
            return a.all_profit == 0 && a.previous_profit == 0 && a.current_profit == 0 &&
                   a.coin == 0 && a.ticket == 0 && a.TotalInvest == 0 && a.TotalReward == 0;
        }

        public override void SaveDave()
        {
            var target = DataManager.Instance.Setting[1];

            // 仅当本次改动涉及 coin_rate 或 ticket_rate 时，才强制要求先清帐再保存
            // （汇率变动会影响账目换算，必须先清帐避免账目错乱）；修改其它参数无需清帐。
            bool changedRate = currentSetting.coin_rate != target.coin_rate ||
                               currentSetting.ticket_rate != target.ticket_rate;
            if (changedRate && !IsAccountDataEmpty())
            {
                m_hint.SetActive(true);
                return;
            }

            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(currentSetting), target);

            DataHelper.Instance.Modify("Data/Setting.xml", DataManager.Instance.Setting, target);
        }

        private string NumberToChinese(int num)
        {
            string[] chineseNumbers = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
            if (num >= 0 && num <= 10)
                return chineseNumbers[num];
            // 如果需要支持更大数字，可以扩展；否则返回数字字符串
            return num.ToString();
        }
    }
}
