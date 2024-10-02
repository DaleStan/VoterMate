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

    private async void LoadTurf_Clicked(object sender, EventArgs e)
    {
        FilePickerFileType customFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.iOS] = ["public.text"], // UTType values
            [DevicePlatform.Android] = ["text/*"], // MIME type
            [DevicePlatform.WinUI] = [".txt"], // file extension
            [DevicePlatform.Tizen] = ["*/*"],
            [DevicePlatform.macOS] = ["turf"], // UTType values
        });

        var file = await FilePicker.PickAsync(new() { FileTypes = customFileType });
        if (file != null)
        {
            using var stream = await file.OpenReadAsync();
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(FileSystem.Current.AppDataDirectory, "turf.txt"), bytes);
            App.Database.LoadTurfList(Path.Combine(FileSystem.Current.AppDataDirectory, "turf.txt"));
        }
    }
}