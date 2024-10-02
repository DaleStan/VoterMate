using CommunityToolkit.Maui.Views;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using System.Reflection;
using VoterMate.Database;

namespace VoterMate;

public partial class MainPage : ContentPage
{
    // Approximate distance in miles between degrees of longitude at equator, or between degrees of latitude. Actual value ranges apparently ranges from 68.7 to 69.4.
    private const double MilesPerDegree = 69;
    private readonly Dictionary<Household, Expander> expanders = [];
    private Location? _location;
    internal string Canvasser { get; private set; }

    const double locationFilterRange = 0.1; // 100 m

    public MainPage()
    {
        InitializeComponent();
        using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.voterDataDate.tsv")!);
        lblBuildInfo.Text = GetBuildInfo() + "\n" + sr.ReadToEnd();

        try
        {
            Canvasser = File.ReadAllText(Path.Combine(FileSystem.Current.AppDataDirectory, "canvasser.txt"));
        }
        catch { Canvasser = null!; }
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
                        (sender as Button)!.Navigation.PushAsync(new MobilizerPage(household.Location, mobilizer, this));
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

        if (Canvasser == null)
            await Navigation.PushAsync(new SettingsPage(this));
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
            Navigation.PushAsync(new MobilizerPage(_location, null, this));
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
        await Navigation.PushAsync(new MobilizerPage(location, mobilizer, this));
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

    internal void LogEvent(string @event, string? data, Location? location)
    {
        TravelLog line = new(Canvasser, @event, data, location?.Speed, location?.Latitude, location?.Longitude);

        using CsvWriter csv = new(new StreamWriter(Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog_v2.csv"), true), CultureInfo.InvariantCulture);
        csv.WriteRecord(line);
        csv.NextRecord();
    }

    private void Settings_Clicked(object sender, EventArgs e) => Navigation.PushAsync(new SettingsPage(this));

    internal void UpdateSettings(string canvasser)
    {
        Canvasser = canvasser;
        foreach (var path in Directory.GetFiles(FileSystem.Current.AppDataDirectory, "*.csv"))
        {
            switch (Path.GetFileNameWithoutExtension(path))
            {
                case "travelLog":
                    UpdateTravelLog();
                    break;
                case "contactCommitments":
                    UpdateContactCommitments();
                    break;
                case "phoneNumbers":
                    UpdatePhoneNumbers();
                    break;
            }
        }

        File.WriteAllText(Path.Combine(FileSystem.Current.AppDataDirectory, "canvasser.txt"), Canvasser);
    }

    private void UpdateTravelLog()
    {
        string oldLogPath = Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog.csv");
        string newLogPath = Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog_v2.csv");

        using CsvWriter csv = new(new StreamWriter(newLogPath), CultureInfo.InvariantCulture);
        csv.WriteRecords(ReadOldRecords());

        File.Delete(oldLogPath);

        IEnumerable<TravelLog> ReadOldRecords()
        {
            using CsvDataReader dr = new(new(new StreamReader(oldLogPath), new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false }));
            while (dr.Read())
            {
                int count = dr.FieldCount;
                string[] values = new string[count];
                dr.GetValues(values);
                yield return new TravelLog(Canvasser, values[0], values[1], values[2], values[3], values[4], values[5]);
            }
        }
    }

    private void UpdateContactCommitments()
    {
        string oldCommitmentsPath = Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments.csv");
        string newCommitmentsPath = Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments_v2.csv");

        using CsvWriter csv = new(new StreamWriter(newCommitmentsPath), CultureInfo.InvariantCulture);
        csv.WriteRecords(ReadOldRecords());

        File.Delete(oldCommitmentsPath);

        IEnumerable<object> ReadOldRecords()
        {
            using CsvDataReader dr = new(new CsvReader(new StreamReader(oldCommitmentsPath), new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false }));
            while (dr.Read())
            {
                int count = dr.FieldCount;
                string[] values = new string[count];
                dr.GetValues(values);
                if (DateTime.TryParseExact(values[2], "MMM dd HH:mm:ss", null, DateTimeStyles.None, out DateTime result))
                    values[2] = result.ToString("MM/dd HH:mm:ss");
                yield return new ContactCommitment(Canvasser, values[0], values[1], values[2], values[3], values[4]);
            }
        }
    }

    private void UpdatePhoneNumbers()
    {
        string oldPhonePath = Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers.csv");
        string newPhonePath = Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers_v2.csv");

        using CsvWriter csv = new(new StreamWriter(newPhonePath), CultureInfo.InvariantCulture);
        csv.WriteRecords(ReadOldRecords());

        File.Delete(oldPhonePath);

        IEnumerable<object> ReadOldRecords()
        {
            using CsvDataReader dr = new(new CsvReader(new StreamReader(oldPhonePath), new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false }));
            while (dr.Read())
            {
                int count = dr.FieldCount;
                string[] values = new string[count];
                dr.GetValues(values);
                yield return new PhoneNumber(Canvasser, values[0], values[1], values[2], values[3], values[4], values[5]);
            }
        }
    }
}
