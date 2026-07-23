using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CG.MagicMenu
{
    [RequireComponent(typeof(Image))]
    public class TabButton : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerExitHandler
    {

        [SerializeField] TabPanel panel;

        [Header("Callback Events --> (Optional)")]
        [Space]
        public UnityEvent OnSelected;
        public UnityEvent OnDeselected;

        Image backGroundImage;
        TabPanelManager panelManager;

        public TabPanel Panel => panel;
        public Image BackgroundImage => backGroundImage;

        private void Awake()
        {
            panelManager = GetComponentInParent<TabPanelManager>();
            backGroundImage = GetComponent<Image>();
        }

        public void OnPointerClick(PointerEventData eventData) => panelManager.OnButtonClicked(this);
        public void OnPointerEnter(PointerEventData eventData) => panelManager.OnButtonHover(this);
        public void OnPointerExit(PointerEventData eventData) => panelManager.OnButtonExit(this);
    }
}