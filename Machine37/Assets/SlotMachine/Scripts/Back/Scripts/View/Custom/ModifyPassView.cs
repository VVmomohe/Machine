using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using UnityEngine.UI;
using UnityXML;

namespace Com.Back
{
    public class ModifyPassView : EnterPassView
    {

        private bool m_isSet;
        public Text[] m_passText1;
        public Text[] m_passText2;

        public GameObject c_view;
        public GameObject g_view;

        protected override void Start()
        {
            base.Start();
        }

        protected override void OnEnable()
        {
            m_isSet = false;
            m_passText = m_passText1;
            for (int i = 0; i < m_passText2.Length; i++)
            {
                m_passText2[i].text = "0";
            }

            base.OnEnable();
        }

        // Update is called once per frame
        protected override void Update()
        {
            base.Update();

            // 密码光标
            image.rectTransform.anchoredPosition3D = m_iconDefulatPos - (Vector3.left * index * m_offset.x);
            if (!m_isSet)
            {
                image.rectTransform.anchoredPosition3D = new Vector3(image.rectTransform.anchoredPosition3D.x,
                        m_iconDefulatPos.y, m_iconDefulatPos.z);
            }
            else
            {
                image.rectTransform.anchoredPosition3D = new Vector3(image.rectTransform.anchoredPosition3D.x,
                        m_iconDefulatPos.y - m_offset.y, m_iconDefulatPos.z);
            }
        }

        protected override void OnEnter()
        {
            int newNum = 0;
            for (int i = 0; i < m_passArr.Length; i++)
            {
                int rate = (int)Mathf.Pow(10, m_passArr.Length - i - 1);
                newNum += m_passArr[i] * rate;
            }

            if (!m_isSet)
            {
                // 密码正确
                if (newNum == DataManager.Instance.Pass[1].pass)
                {
                    index = 0;
                    m_isSet = true;
                    m_passArr = new int[8];
                    m_passText = m_passText2;
                }
                else
                {
                    // 密码错误：ErrorText 由 SettingPanel 实例化在最底部，直接取容器引用
                    var et = SettingPanel.Instance?.ErrorText;
                    if (et != null) et.StartIE("密码错误");
                }
            }
            else
            {
                DataManager.Instance.Pass[1].pass = newNum;
                DataHelper.Instance.Modify("Data/Pass.xml", DataManager.Instance.Pass, DataManager.Instance.Pass[1]);

                // 改密成功后直接返回 GameSelection：ChangePSW 是整屏切换进入（GameSelection 已被隐藏），
                // Back() 弹栈即重新激活下一层的 GameSelection，无中间帧、不闪（与 EnterPassView.OnCancel 同路径）。
                if (SettingPanel.Instance != null)
                    SettingPanel.Instance.Back();
            }
        }
    }
}
