namespace CG.MagicMenu.Example
{
    public class SettingsMenu : Menu<SettingsMenu>
    {
        public override void OnBackPressed()
        {
            base.OnBackPressed();
            MainMenu.Open();
        }
    }
}