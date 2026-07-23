using UnityEngine;
using UnityEngine.SceneManagement;

namespace CG.MagicMenu.Example
{
    public class PauseMenu : Menu<PauseMenu>
    {
        public void QuitGame()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
            Application.Quit();
        }

        public void QuitGameRequested()
        {
            PopupMenu.Open();
        }

        public override void OnBackPressed()
        {
            base.OnBackPressed();
            GameMenu.Open();
        }

        public void GoToMainMenu()
        {
            SceneManager.LoadScene("Main_menu");
            MainMenu.Open();
        }
    }
}