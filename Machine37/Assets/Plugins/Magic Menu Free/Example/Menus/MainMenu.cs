using UnityEngine.SceneManagement;

namespace CG.MagicMenu.Example
{
    public class MainMenu : Menu<MainMenu>
    {
        public void SettingsPressed()
        {
            SettingsMenu.Open();
        }

        public override void OnBackPressed() => PopupMenu.OpenOnTop();

        public void StartGame()
        {
            SceneManager.LoadScene("scene_gamePlay");
            GameMenu.Open();
        }
    }
}