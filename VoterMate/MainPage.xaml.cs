using Microsoft.Maui.Devices.Sensors;
using VoterMate.Database;

namespace VoterMate;

public partial class MainPage : ContentPage
{
    // Approximate distance in miles between degrees of longitude at equator, or between degrees of latitude. Actual value ranges apparently ranges from 68.7 to 69.4.
    private const double MilesPerDegree = 69;
    private Household? _nearestHousehold;
    private Location? _location;

    public MainPage()
    {
        InitializeComponent();
        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments.csv")).Close();
        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers.csv")).Close();
    }

    private async void SetHousehold(Location location, Household? value)
    {
        if (_nearestHousehold != value || namesPanel.Children.Count == 0)
        {
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync();

            namesPanel.Children.Clear();

            namesPanel.Children.Add(new Label
            {
                Text = (value != null) ? "You are at " + value.Address : "No mobilizer houses nearby",
                Margin = new(3),
                HorizontalTextAlignment = TextAlignment.Center
            });

            Button button;

            foreach (var mobilizer in value?.Mobilizers ?? [])
            {
                string age = mobilizer.BirthDate.HasValue ? $"({(int)((DateTime.Now - mobilizer.BirthDate.Value).TotalDays / 365.24)})" : "(unknown age)";
                button = new Button { Text = $"{mobilizer.Name} {age}", Margin = new Thickness(3) };
                button.Clicked += (s, e) => (s as Button)!.Navigation.PushAsync(new MobilizerPage(location, mobilizer));
                namesPanel.Add(button);
            }

            _nearestHousehold = value;
            _location = location;
        }
    }

    private async void Copy_Clicked(object? sender, EventArgs e)
    {
        var button = (Button)sender!;
        if (button.Opacity == 0)
        {
            button.Opacity = 1;
            await Task.Delay(5000);
            button.Opacity = 0;
        }
        else if (button.Opacity == 1)
        {
            try
            {
                List<ShareFile> files = [new(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments.csv")),
                    new(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers.csv"))];

                await Share.Default.RequestAsync(new ShareMultipleFilesRequest { Files = files });
            }
            catch { }
        }
    }

    private void Geolocation_LocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        double squareFilterRange = 0.5; // 1/2 mile
        double squareFilterLongitude = squareFilterRange / MilesPerDegree;
        double squareFilterLatitude = squareFilterRange / MilesPerDegree / Math.Cos(e.Location.Longitude);

        var household = App.Database.GetHouseholds()
            .Where(SquareFilter) // Premature optimization? Don't consider houses more than squareFilterRange away in either lat or lon.
            .OrderBy(DistanceTo).FirstOrDefault();

        Dispatcher.Dispatch(() => SetHousehold(e.Location, household));

        bool SquareFilter(Household h)
        {
            return Math.Abs(h.Location.Latitude - e.Location.Latitude) < squareFilterLatitude
                && Math.Abs(h.Location.Longitude - e.Location.Longitude) < squareFilterLongitude;
        }

        double DistanceTo(Household h) => h.Location.CalculateDistance(e.Location, DistanceUnits.Miles);
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
        await Navigation.PushAsync(new MobilizerPage(location, mobilizer));
    }
}
