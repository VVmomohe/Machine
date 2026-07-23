using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityXML;
using UnitySound;

namespace Com.Back
{
    public class DataManager
    {

        private static DataManager _instance;
        public static DataManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DataManager();
                }
                return _instance;
            }
        }

        public bool IsWindow;
        public bool IsInit;
        public string saveTag = "1.02";

        public Dictionary<int, LanguageData> Language;

        public Dictionary<int, AccountData> Account;
        public Dictionary<int, SettingData> Setting;
        public Dictionary<int, PassData> Pass;

        public Dictionary<int, PlayerData> Player;

        // 一票几币和一币几分
        public int[] ticket_rate_Arr = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };
        public int[] coin_rate_Arr = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };

        // 最小押分和最大押分
        public int[] min_num_Arr = new int[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
        public int[] max_num_Arr = new int[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };

        // 爆机
        public int[] bomb_num_Arr = new int[] { 0, 50000, 100000, 200000, 300000, 500000, 1000000 };
        // 彩金
        public int[] bonus_num_Arr = new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };


        public void SetPath()
        {
            DataHelper.Instance.Source_Path = Application.streamingAssetsPath + "/Copy";
            
            // 测试数据
            if (Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                DataHelper.Instance.Copy_Path = Application.persistentDataPath + "/Load";
            }
            // 安卓
            else if (Application.platform == RuntimePlatform.Android)
            {
                //DataHelper.Instance.Copy_Path = "/storage/emulated/0/";
                DataHelper.Instance.Copy_Path = Application.persistentDataPath + "/Load";
            }
        }

        public void InitData()
        {
            if (IsInit)
                return;

            SetPath();
            // 读取数据
            DataHelper.Instance.Init("Data/Language.xml");
            if (PlayerPrefs.GetString("saveTag") != saveTag)
            {
                DataHelper.Instance.Init("Data/Account.xml");
                DataHelper.Instance.Init("Data/Setting.xml");
                DataHelper.Instance.Init("Data/Player.xml");
                //DataHelper.Instance.Init("Data/Pass.xml");
                Debug.Log("Init date");

                PlayerPrefs.SetString("saveTag", saveTag);
                PlayerPrefs.Save();
            }

            IsInit = true;
        }

        public void LoadData()
        {
            SetPath();
            DataHelper.Instance.Load("Data/Language.xml", ref Language);

            DataHelper.Instance.Load("Data/Account.xml", ref Account);
            DataHelper.Instance.Load("Data/Setting.xml", ref Setting);
            DataHelper.Instance.Load("Data/Pass.xml", ref Pass);

            DataHelper.Instance.Load("Data/Player.xml", ref Player);
        }

        // 每一秒执行一次，同时acc_runTime += Time.deltime;
        /// <summary>
        /// 保存数据到本地。saveTag 之后默认把当前账目同步到 MCU；
        /// 退彩票流程在 Processing 阶段传 false 跳过（避免每次退票心跳都刷 MCU），仅在 Complete 时单独同步。
        /// </summary>
        public void SaveData(bool syncAccountToMcu = true)
        {
            // 保存数据
            DataHelper.Instance.Modify("Data/Account.xml", Account, Account[1]);
            DataHelper.Instance.Modify("Data/Player.xml", Player, Player[1]);
        }

        public void Start()
        {

        }

        public void Update()
        {

        }
    }
}
