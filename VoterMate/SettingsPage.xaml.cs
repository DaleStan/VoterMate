using Syncfusion.Maui.Inputs;

namespace VoterMate;

public partial class SettingsPage : ContentPage
{
    private readonly MainPage _mainPage;

    public SettingsPage(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
        txtCanvasserName.Text = mainPage.Canvasser;
        cboTurf.ItemsSource = typeof(SettingsPage).Assembly.GetManifestResourceNames().Where(n => n.Contains("TurfFiles")).Select(n => n["VoterMate.TurfFiles.".Length..^4]).ToArray();
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
            LoadTurfStream(stream);
        }
    }

    private async void LoadTurfStream(Stream stream)
    {
        var bytes = new byte[stream.Length];
        stream.Read(bytes, 0, bytes.Length);
        File.WriteAllBytes(Path.Combine(FileSystem.Current.AppDataDirectory, "turf.txt"), bytes);
        App.Database.LoadTurfList(Path.Combine(FileSystem.Current.AppDataDirectory, "turf.txt"));
        txtSuccess.IsVisible = true;
        await Task.Delay(10000);
        txtSuccess.IsVisible = false;
    }

    private void cboTurf_SelectionChanged(object sender, Syncfusion.Maui.Inputs.SelectionChangedEventArgs e)
    {
        if (e.AddedItems?.Count > 0)
        {
            using var stream = typeof(SettingsPage).Assembly.GetManifestResourceStream("VoterMate.TurfFiles." + e.AddedItems[0] + ".txt");
            if (stream != null)
                LoadTurfStream(stream);
        }
    }
}