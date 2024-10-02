namespace VoterMate;

public partial class SettingsPage : ContentPage
{
    private readonly MainPage _mainPage;

    public SettingsPage(MainPage mainPage)
	{
		InitializeComponent();
        _mainPage = mainPage;
        txtCanvasserName.Text = mainPage.Canvasser;
    }

    private void SettingsPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
    {
        _mainPage.UpdateSettings(txtCanvasserName.Text);
    }
}