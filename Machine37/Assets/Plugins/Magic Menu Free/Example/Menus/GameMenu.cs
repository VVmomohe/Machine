namespace CG.MagicMenu.Example
{
    public class GameMenu : Menu<GameMenu>
    {
        public override void OnBackPressed()
        {
            PauseMenu.Open();
        }
    }
}


