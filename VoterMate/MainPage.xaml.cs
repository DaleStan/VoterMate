using CommunityToolkit.Maui.Views;
using System.Reflection;
using VoterMate.Database;

namespace VoterMate;

public partial class MainPage : ContentPage
{
    // Approximate distance in miles between degrees of longitude at equator, or between degrees of latitude. Actual value ranges apparently ranges from 68.7 to 69.4.
    private const double MilesPerDegree = 69;
    private readonly Dictionary<Household, Expander> expanders = [];
    private Location? _location;

    const double locationFilterRange = 0.1; // 100 m

    public MainPage()
    {
        InitializeComponent();
        using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.voterDataDate.tsv")!);
        lblBuildInfo.Text = GetBuildInfo() + "\n" + sr.ReadToEnd();
    }

    public static string? GetBuildInfo()
    {
        const string BuildVersionMetadataPrefix = "+build";

        var attribute = typeof(MainPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attribute?.InformationalVersion != null)
        {
            var value = attribute.InformationalVersion;
            var index = value.IndexOf(BuildVersionMetadataPrefix);
            if (index > 0)
            {
                value = value[(index + BuildVersionMetadataPrefix.Length)..];
                return value;
            }
        }
        return default;
    }

    private async void SetHousehold(Location location, List<Household> households)
    {
        if (Navigation.NavigationStack[^1] is MobilizerPage mp)
        {
            if (_location?.CalculateDistance(location, DistanceUnits.Kilometers) < locationFilterRange)
                return;

            if (!households.SelectMany(h => h.Mobilizers).Contains(mp.Mobilizer))
            {
                await Navigation.PopAsync();
                LogEvent("Auto-closed mobilizer page", mp.Mobilizer.ID, location);
            }
        }

        _location = location;

        while (namesPanel.Count > 0)
            namesPanel.RemoveAt(0);

        namesPanel.Add(new Label
        {
            Text = households.Count switch
            {
                0 => "No mobilizer houses nearby",
                1 => "You are at " + households[0].Address,
                _ => "Select the house you are knocking on"
            },
            Margin = new(3),
            HorizontalTextAlignment = TextAlignment.Center
        });

        foreach (var household in households)
        {
            if (!expanders.TryGetValue(household, out var expander))
            {
                expander = new Expander
                {
                    Header = new Button { Text = household.Address, FontAttributes = FontAttributes.Bold },
                    Content = (VerticalStackLayout)([.. household.Mobilizers.Select(MakeButton)]),
                    Margin = 3
                };

                Button MakeButton(Mobilizer mobilizer)
                {
                    string age = mobilizer.BirthDate.HasValue ? $"({(int)((DateTime.Now - mobilizer.BirthDate.Value).TotalDays / 365.24)})" : "(unknown age)";
                    Button button = new() { Text = $"{mobilizer.Name} {age}", Margin = new Thickness(23, 3), BackgroundColor = Colors.BlueViolet };
                    button.Clicked += Clicked;
                    return button;

                    void Clicked(object? sender, EventArgs e)
                    {
                        LogEvent("Opening mobilizer page (selected)", mobilizer.ID, location);
                        (sender as Button)!.Navigation.PushAsync(new MobilizerPage(household.Location, mobilizer));
                    }
                }
            }

            expander.IsExpanded = households.Count == 1;
            namesPanel.Add(expander);
        }
    }

    private void Geolocation_LocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        LogEvent("Moving", null, e.Location);

        double squareFilterLongitude = locationFilterRange / 1.609 / MilesPerDegree;
        double squareFilterLatitude = locationFilterRange / 1.609 / MilesPerDegree / Math.Cos(e.Location.Longitude);

        var households = App.Database.GetHouseholds()
            .Where(SquareFilter) // Premature optimization? Filter to a square before doing accurate distance calculations.
            .OrderBy(DistanceTo)
            .TakeWhile(h => DistanceTo(h) < locationFilterRange)
            .ToList();

        Dispatcher.Dispatch(() => SetHousehold(e.Location, households));

        bool SquareFilter(Household h)
        {
            return Math.Abs(h.Location.Latitude - e.Location.Latitude) < squareFilterLatitude
                && Math.Abs(h.Location.Longitude - e.Location.Longitude) < squareFilterLongitude;
        }

        double DistanceTo(Household h) => h.Location.CalculateDistance(e.Location, DistanceUnits.Kilometers);
    }

    private async void MainPage_Loaded(object sender, EventArgs e)
    {
        Geolocation.LocationChanged += Geolocation_LocationChanged;
        Geolocation.ListeningFailed += Geolocation_ListeningFailed;
        if (!await Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best)))
        {
            Application.Current!.Quit();
        }

        Window.Resumed += Window_Resumed;
        Window.Deactivated += Window_Deactivated;

        LogEvent("Started", null, _location ?? await Geolocation.GetLastKnownLocationAsync());
    }

    private async void Window_Deactivated(object? sender, EventArgs e) => LogEvent("Deactivated", null, _location ?? await Geolocation.GetLastKnownLocationAsync());
    private async void Window_Resumed(object? sender, EventArgs e) => LogEvent("Resumed", null, await Geolocation.GetLocationAsync());

    private void Geolocation_ListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        // Failed also stops listening. Start listening again.
        Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best));
    }

    private void NotListed_Clicked(object sender, EventArgs e)
    {
        if (_location != null)
        {
            LogEvent("Opening mobilizer page (not listed)", null, _location);
            Navigation.PushAsync(new MobilizerPage(_location, null));
        }
        else
            DisplayAlert("Unknown location", $"Friend lists cannot be displayed without a voter ID or location information.", "OK");
    }

    private async void Lookup_Clicked(object sender, EventArgs e)
    {
        var info = App.Database.GetMobilizer("OH" + txtVoterID.Text);
        if (info == null)
        {
            await DisplayAlert("Not Found", $"The voter with ID OH{txtVoterID.Text} could not be found.", "OK");
            return;
        }

        var (mobilizer, location) = info.Value;
        LogEvent("Opening mobilizer page (ID lookup)", mobilizer.ID, _location);
        await txtVoterID.HideSoftInputAsync(new CancellationTokenSource().Token);
        await Navigation.PushAsync(new MobilizerPage(location, mobilizer));
    }

    private async void Copy_Clicked(object? sender, EventArgs e)
    {
        var grid = (Grid)((Button)sender!).Parent;
        if (grid.Opacity == 0)
        {
            grid.Opacity = 1;
            await Task.Delay(5000);
            grid.Opacity = 0;
            return;
        }

        try
        {
            List<ShareFile> files = [.. Directory.GetFiles(FileSystem.Current.AppDataDirectory, "*.csv").Select(f => new ShareFile(f))];
            await Share.Default.RequestAsync(new ShareMultipleFilesRequest { Files = files });
        }
        catch { }
    }

    private async void Import_Clicked(object sender, EventArgs e)
    {
        var grid = (Grid)((Button)sender!).Parent;
        if (grid.Opacity == 0)
        {
            grid.Opacity = 1;
            await Task.Delay(5000);
            grid.Opacity = 0;
            return;
        }

        FilePickerFileType customFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.iOS] = ["public.comma-separated-values-text"], // UTType values
            [DevicePlatform.Android] = ["text/*"], // MIME type
            [DevicePlatform.WinUI] = [".csv"], // file extension
            [DevicePlatform.Tizen] = ["*/*"],
            [DevicePlatform.macOS] = ["csv"], // UTType values
        });

        var file = await FilePicker.PickAsync(new() { FileTypes = customFileType });
        if (file != null)
            await MobilizerPage.LoadShownFriendsData(file);
    }

    internal static void LogEvent(string @event, string? data, Location? location)
    {
        string line;
        if (location == null)
            line = $"{@event},{data},{DateTime.Now:MM/dd HH:mm:ss},,Unknown,Unknown";
        else if (location.Speed == null)
            line = $"{@event},{data},{DateTime.Now:MM/dd HH:mm:ss},,{location.Latitude:0.####},{location.Longitude:0.####}";
        else
            line = $"{@event},{data},{DateTime.Now:MM/dd HH:mm:ss},{location.Speed:0.##m/s},{location.Latitude:0.####},{location.Longitude:0.####}";

        File.AppendAllLines(Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog.csv"), [line]);
    }
}
