using VoterMate.Database;

namespace VoterMate;

public partial class MainPage : ContentPage
{
    // Approximate distance in miles between degrees of longitude at equator, or between degrees of latitude. Actual value ranges apparently ranges from 68.7 to 69.4.
    private const double MilesPerDegree = 69;
    private Household? _nearestHousehold;

    private async Task SetHousehold(Household? value)
    {
        if (_nearestHousehold != value)
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

            button = new Button { Text = "Mobilizer not listed", Margin = new Thickness(3) };
            button.Clicked += (s, e) => (s as Button)!.Navigation.PushAsync(new MobilizerPage(location, null));
            namesPanel.Add(button);

            _nearestHousehold = value;
        }
    }

    public MainPage()
    {
        InitializeComponent();
        Geolocation.LocationChanged += Geolocation_LocationChanged;
        Geolocation.ListeningFailed += Geolocation_ListeningFailed;
    }

    private async void Geolocation_LocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        double squareFilterRange = 0.5; // 1/2 mile
        double squareFilterLongitude = squareFilterRange / MilesPerDegree;
        double squareFilterLatitude = squareFilterRange / MilesPerDegree / Math.Cos(e.Location.Longitude);

        await SetHousehold(App.Database.GetHouseholds()
            .Where(SquareFilter) // Premature optimization? Don't consider houses more than squareFilterRange away in either lat or lon.
            .OrderBy(DistanceTo).FirstOrDefault());


        bool SquareFilter(Household h)
        {
            return Math.Abs(h.Latitude - e.Location.Latitude) < squareFilterLatitude && Math.Abs(h.Longitude - e.Location.Longitude) < squareFilterLongitude;
        }

        double DistanceTo(Household h)
        {
            return Location.CalculateDistance(h.Latitude, h.Longitude, e.Location, DistanceUnits.Miles);
        }
    }

    private async void MainPage_Loaded(object sender, EventArgs e)
    {
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
}
