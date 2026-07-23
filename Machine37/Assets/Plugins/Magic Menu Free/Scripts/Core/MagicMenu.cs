using UnityEngine;
using System.Collections.Generic;

namespace CG.MagicMenu
{
    public class MagicMenu : MonoBehaviour
    {
        public static event System.Action<Menu> OnMenuChanged;

        private Stack<Menu> menuStack = new Stack<Menu>();        

        private void Awake()
        {
            HashSet<Menu> menuPrefabs = new HashSet<Menu>(Resources.LoadAll<Menu>("MagicMenu/Prefabs"));            

            foreach (var menuPrefab in menuPrefabs)
            {
                menuPrefab.Initialize(this);
                
                var menuInstance = Instantiate(menuPrefab, transform);

                menuStack.Push(menuInstance);

                menuInstance.gameObject.SetActive(false);

                if (menuInstance.ShowFirst)
                    menuInstance.gameObject.SetActive(true);
            }
        }

        public void CloseMenu()
        {
            if (menuStack.Count == 0)
            {
                Debug.LogWarning("Menu Stack is Empty !", gameObject);

                return;
            }

            Menu topMenu = menuStack.Pop();

            topMenu.gameObject.SetActive(false);

            if (menuStack.Count > 0)
            {
                Menu nextMenu = menuStack.Peek();

                nextMenu.gameObject.SetActive(true);
            }
        }
        public void OpenMenu(Menu menuToBeOpened, bool openOnTop = false)
        {
            if (menuToBeOpened == null)
            {
                Debug.LogWarning("Invalid Menu", gameObject);

                return;
            }

            if (menuStack.Count > 0 && openOnTop == false)
            {
                foreach (Menu menu in menuStack)
                {
                    menu.gameObject.SetActive(false);                    
                }
            }

            menuToBeOpened.gameObject.SetActive(true);

            menuStack.Push(menuToBeOpened);

            OnMenuChanged?.Invoke(menuToBeOpened);
        }

        /// <summary>关闭所有菜单并清空栈（退出后台时用，不触发“回到上一层”的重新激活）。</summary>
        public void Reset()
        {
            while (menuStack.Count > 0)
            {
                var m = menuStack.Pop();
                if (m != null && m.gameObject != null) m.gameObject.SetActive(false);
            }
        }
    }
}


