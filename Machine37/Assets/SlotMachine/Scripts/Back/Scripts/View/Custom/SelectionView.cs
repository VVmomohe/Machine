using Com.Back;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Com.Back
{
    public class SelectionView : MoveView
    {

        public GameObject m_initPassText;

        protected override void Start()
        {
            base.Start();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (m_initPassText != null) m_initPassText.SetActive(false);
        }

        // Update is called once per frame
        protected override void Update()
        {
            base.Update();
        }

        protected override void OnEnter()
        {
            base.OnEnter();
            if (index == 0) { SettingPanel.Instance.OpenScreen("GameSeting"); return; }
            if (index == 1) { SettingPanel.Instance.OpenScreen("ChangePSW"); return; }
        }

        // 取消返回主界面 Menus（避免死胡同；用户规格未明确，按常规父级返回处理）
        protected override void OnCancel()
        {
            base.OnCancel();
            SettingPanel.Instance.Back();
        }
    }
}
