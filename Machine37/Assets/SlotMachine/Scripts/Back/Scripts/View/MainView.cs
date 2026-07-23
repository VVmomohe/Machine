using UnityEngine;

namespace Com.Back
{
    public class MainView : MoveView
    {
        private static MainView _instance;
        public static MainView Instance
        {
            get
            {
                return _instance;
            }
        }

        public Color m_norColor;
        public Color m_clickColor;

        private void Awake()
        {
            // 单例保护：仅当尚未设置时才绑定，避免场景里误放的重复 SettingPanel 子实例
            // （加载时创建、随后被销毁）把自己的 _instance 覆盖成已销毁对象。
            if (_instance == null) _instance = this;
            if (DataManager.Instance.Account == null)
                DataManager.Instance.LoadData();
        }

        /// <summary>Menus 选项确认：
        ///   opt0 → Acount（直接，无需密码）
        ///   opt1 → 密码校验网关（密码正确后才整屏切到 GameSelection）
        ///   其它选项（如 DateTime）走基类默认处理。</summary>
        protected override void OnEnter()
        {
            if (index == 0) { SettingPanel.Instance.OpenScreen("Acount"); return; }
            if (index == 1) { SettingPanel.Instance.OpenPasswordGate("Menus", () => SettingPanel.Instance.OpenScreen("GameSelection")); return; }
            base.OnEnter();
        }

        protected override void OnCancel()
        {
            base.OnCancel();
            DataManager.Instance.SaveData();
            // 主界面按取消 = 退出整个后台，关闭全部界面（含常驻的 DateTime）
            SettingPanel.Instance.CloseAll();
        }
    }
}
