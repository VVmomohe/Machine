using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Back
{
    public class EnterPassView : MoveView
    {

        protected Vector3 m_iconDefulatPos;

        public int[] m_passArr = new int[8];
        public Text[] m_passText;

        protected override void Start()
        {
            m_iconDefulatPos = image.rectTransform.anchoredPosition3D;
            base.Start();
        }

        protected override void OnEnable()
        {
            index = 0;
            m_passArr = new int[8];
            base.OnEnable();
        }

        // Update is called once per frame
        protected override void Update()
        {
            base.Update();

            // 密码光标
            image.rectTransform.anchoredPosition3D = m_iconDefulatPos - (Vector3.left * index * m_offset.x);

            // 密码文本
            for (int i = 0; i < m_passArr.Length; i++)
            {
                m_passText[i].text = m_passArr[i].ToString();
            }
        }

        protected override void UpAndDown(int num)
        {
            m_passArr[index] -= num;
            m_passArr[index] = m_passArr[index] > 9 ? 9 : m_passArr[index];
            m_passArr[index] = m_passArr[index] < 0 ? 0 : m_passArr[index];
        }

        protected override void LeftAndRight(int num)
        {
            index += num;
            index = index > m_passArr.Length - 1 ? m_passArr.Length - 1 : index;
            index = index < 0 ? 0 : index;
        }

        protected override void OnEnter()
        {
            int newNum = 0;
            for (int i = 0; i < m_passArr.Length; i++)
            {
                int rate = (int)Mathf.Pow(10, m_passArr.Length - i - 1);
                newNum += m_passArr[i] * rate;
            }

            // 密码正确：交给密码网关结算（成功回调由来源屏决定：进 GameSelection / 清帐 / 初始化 / 保存）
            if (newNum == DataManager.Instance.Pass[1].pass)
            {
                SettingPanel.Instance.ResolvePasswordGate(true);
            }
            else
            {
                // 密码错误：显示提示。ErrorText 由 SettingPanel 实例化在最底部作为共享提示，
                // 直接取容器引用即可（无需 Inspector 绑定，也不用 GameObject.Find 按名查找）。
                var et = SettingPanel.Instance?.ErrorText;
                if (et != null)
                    et.StartIE("密码错误");
                else
                    Debug.LogWarning("[EnterPassView] SettingPanel.ErrorText 未就绪，无法显示密码错误提示");
            }
        }

        protected override void OnCancel()
        {
            base.OnCancel();
            // 处于密码网关中 → 取消回到来源屏；否则（ChangePSW 等独立屏）直接返回上一层
            if (SettingPanel.Instance.IsGateOpen)
                SettingPanel.Instance.ResolvePasswordGate(false);
            else
                SettingPanel.Instance.Back();
        }

    }
}
