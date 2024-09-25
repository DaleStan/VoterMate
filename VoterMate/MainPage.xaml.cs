using CommunityToolkit.Maui.Views;
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
        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments.csv")).Close();
        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers.csv")).Close();
    }

    private async void SetHousehold(Location location, List<Household> households)
    {
        if (Navigation.NavigationStack[^1] is MobilizerPage mp)
        {
            if (_location?.CalculateDistance(location, DistanceUnits.Kilometers) < locationFilterRange)
                return;

            if (!households.SelectMany(h => h.Mobilizers).Contains(mp.Mobilizer))
                await Navigation.PopAsync();
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
                    button.Clicked += (s, e) => (s as Button)!.Navigation.PushAsync(new MobilizerPage(household.Location, mobilizer));
                    return button;
                }
            }

            expander.IsExpanded = households.Count == 1;
            namesPanel.Add(expander);
        }
    }

    private void Geolocation_LocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
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
    }

    private void Geolocation_ListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        // Failed also stops listening. Start listening again.
        Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best));
    }

    private void NotListed_Clicked(object sender, EventArgs e)
    {
        if (_location != null)
            Navigation.PushAsync(new MobilizerPage(_location, null));
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
}
