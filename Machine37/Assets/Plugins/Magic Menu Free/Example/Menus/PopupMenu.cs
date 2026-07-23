using UnityEngine;

namespace CG.MagicMenu.Example
{
    public class PopupMenu : Menu<PopupMenu>
    {
        public void QuitGame()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
            Application.Quit();
        }

        public override void OnBackPressed()
        {
            base.OnBackPressed();
        }
    }
}