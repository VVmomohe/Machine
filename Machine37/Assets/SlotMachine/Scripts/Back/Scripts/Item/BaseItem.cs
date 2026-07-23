using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Com.Back
{
    public class BaseItem : MonoBehaviour, IPointerClickHandler
    {

        private RectTransform m_rectTransform;
        public RectTransform RectTransform
        {
            get
            {
                if (m_rectTransform == null)
                {
                    m_rectTransform = GetComponent<RectTransform>();
                }
                return m_rectTransform;
            }
        }

        public int m_key;

        [HideInInspector]
        public Text m_text;
        private Toggle m_toggle;

        // Use this for initialization
        void Start()
        {
            
        }

        void OnEnable()
        {
            if (m_toggle == null)
            {
                m_toggle = GetComponentInChildren<Toggle>();
            }
            if (m_toggle == null)
            {
                m_toggle = transform.parent.GetComponentInChildren<Toggle>();
            }
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OnEnter();
        }

        public virtual void OnEnter()
        {

        }

        public void SetStr(string _str)
        {
            if (m_text == null)
            {
                m_text = GetComponentInChildren<Text>();
            }
            m_text.text = _str;
        }

        public void SetToggle(bool _bo)
        {
            if (m_toggle != null)
            {
                m_toggle.isOn = _bo;
                if (m_text != null && MainView.Instance != null)
                {
                    m_text.color = m_toggle.isOn ? MainView.Instance.m_clickColor : MainView.Instance.m_norColor;
                }
            }
        }

    }

}
