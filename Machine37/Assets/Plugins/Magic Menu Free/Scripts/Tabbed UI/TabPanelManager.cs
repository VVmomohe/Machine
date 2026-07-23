using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CG.MagicMenu
{
    public class TabPanelManager : MonoBehaviour
    {
        [SerializeField] List<TabButton> buttons;
        [SerializeField] List<TabPanel> panels;

        [Header("Button States")]
        [Space]
        [SerializeField] Sprite defaultImage;
        [SerializeField] Sprite hoverImage;
        [SerializeField] Sprite activeImage;

        TabButton activeButton;

        private void Start()
        {
            panels.AddRange(GetComponentsInChildren<TabPanel>(true));
            buttons.AddRange(GetComponentsInChildren<TabButton>());
            SetStartingPanel();
        }

        public void OnButtonHover(TabButton button)
        {
            ResetButtonApperence();
            if (activeButton != null && button != activeButton)
                button.BackgroundImage.sprite = hoverImage;
        }
        public void OnButtonClicked(TabButton button)
        {
            if (activeButton != null) activeButton.OnDeselected?.Invoke();
            activeButton = button;
            activeButton.OnSelected?.Invoke();

            ResetButtonApperence();
            button.BackgroundImage.sprite = activeImage;

            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].gameObject.SetActive(panels[i] == button.Panel);
            }
        }
        public void OnButtonExit(TabButton button) => ResetButtonApperence();

        private void SetStartingPanel()
        {
            activeButton = buttons[0];
            buttons[0].Panel.gameObject.SetActive(true);
            buttons[0].BackgroundImage.sprite = activeImage;
        }

        private void ResetButtonApperence()
        {
            if (defaultImage == null || hoverImage == null || activeImage == null) return;

            foreach (TabButton button in buttons)
            {
                if (activeButton != null && activeButton == button) continue;
                button.BackgroundImage.sprite = defaultImage;
            }
        }
    }
}
