using CommunityToolkit.Maui.Views;
using CsvHelper;
using CsvHelper.Configuration;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using VoterMate.Database;

namespace VoterMate;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    // Approximate distance in miles between degrees of longitude at equator, or between degrees of latitude. Actual value ranges apparently ranges from 68.7 to 69.4.
    private const double MilesPerDegree = 69;
    private Location? _location;
    private bool hideDistantHouses = true;
    private int selectedSort;
    private int selectedFilter = 3;

    internal string Canvasser { get; private set; }

    private const double locationFilterRange = 0.1; // 100 m

    public bool HideDistantHouses
    {
        get => hideDistantHouses;
        set
        {
            if (hideDistantHouses != value)
            {
                hideDistantHouses = value;
                OnPropertyChanged();
                SetDisplayDescription();
            }
        }
    }

    public int SelectedSort
    {
        get => selectedSort;
        set
        {
            selectedSort = value;
            OnPropertyChanged();
            SetDisplayDescription();
        }
    }

    public int SelectedFilter
    {
        get => selectedFilter;
        set
        {
            selectedFilter = value;
            OnPropertyChanged();
            SetDisplayDescription();
        }
    }

    public int FriendsCommitted { get; set; }
    public int FriendsCommittedThisHour { get; set; }
    public int MobilizersContacted { get; set; }
    public int MobilizersContactedThisHour { get; set; }
    public int DoorsKnocked { get; set; }
    public int DoorsKnockedThisHour { get; set; }
    public Location? Location => _location;

    public MainPage()
    {
        InitializeComponent();
        using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.voterDataDate.tsv")!);
        lblBuildInfo.Text = GetBuildInfo() + "\n" + sr.ReadToEnd();
        DoorsKnocked = App.DoorsKnocked.Keys.Count;

        try
        {
            Canvasser = File.ReadAllText(Path.Combine(FileSystem.Current.AppDataDirectory, "canvasser.txt"));
        }
        catch { Canvasser = null!; }

        try
        {
            App.Database.LoadTurfList(Path.Combine(FileSystem.Current.AppDataDirectory, "turf.txt"));
        }
        catch { }
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

    private async void SetHousehold(Location location, IReadOnlyList<Household> households)
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

        foreach (var household in households.Take(15))
        {
            Expander expander = new()
            {
                Header = new Button { Text = household.Address, FontAttributes = FontAttributes.Bold },
                Margin = 3
            };

            expander.ExpandedChanged += Expander_ExpandedChanged;

            if (App.DoorsKnocked.ContainsKey(household.Address))
                ((Button)expander.Header).BackgroundColor = Colors.ForestGreen;

            Grid grid = [.. household.Mobilizers.SelectMany(MakeButtons)];
            grid.RowDefinitions = [.. household.Mobilizers.Select(_ => new RowDefinition())];
            grid.ColumnDefinitions = [new(), new(GridLength.Auto)];
            grid.Margin = new(20, 0);

            grid.RowDefinitions.Add(new());
            Button noResponse = new() { Text = "Other mobilizer", Margin = 3, BackgroundColor = Colors.BlueViolet };
            noResponse.Clicked += (s, e) =>
            {
                App.DoorsKnocked.AddValue(new(Canvasser, household.Address, "Other mobilizer"));
                NotListed_Clicked(s, e);
                DoorKnocked((Button)expander.Header);
            };
            grid.AddWithSpan(noResponse, grid.RowDefinitions.Count - 1, columnSpan: 2);

            grid.RowDefinitions.Add(new());
            noResponse = new() { Text = "No response", Margin = 3, BackgroundColor = Colors.PaleVioletRed };
            noResponse.Clicked += (_, _) =>
            {
                expander.IsExpanded = false;
                App.DoorsKnocked.AddValue(new(Canvasser, household.Address, "No response"));
                DoorKnocked((Button)expander.Header);
            };
            grid.AddWithSpan(noResponse, grid.RowDefinitions.Count - 1, columnSpan: 2);

            expander.Content = grid;

            Button[] MakeButtons(Mobilizer mobilizer, int i)
            {
                string age = mobilizer.BirthDate.HasValue ? $"({(int)((DateTime.Now - mobilizer.BirthDate.Value).TotalDays / 365.24)})" : "(unknown age)";
                Button button = new() { Text = $"{mobilizer.Name} {age}", Margin = 3, BackgroundColor = Colors.BlueViolet };
                button.Clicked += Clicked;
                Grid.SetRow(button, i);

                Button note = new() { Text = App.MobilizerNotes.ContainsKey(mobilizer.ID!) ? "View/edit notes" : "Add note", Margin = 3, Background = Colors.BlueViolet };
                Grid.SetRow(note, i);
                Grid.SetColumn(note, 1);
                note.Clicked += AddNote;

                return [button, note];

                async void Clicked(object? sender, EventArgs e)
                {
                    LogEvent("Opening mobilizer page (selected)", mobilizer.ID, _location);
                    App.DoorsKnocked.AddValue(new(Canvasser, household.Address, mobilizer.ID!));
                    await Navigation.PushAsync(new MobilizerPage(household.Location, mobilizer, await App.Database.GetPriorityVotersAsync(location, mobilizer), this));
                    DoorKnocked((Button)expander.Header);
                }
                async void AddNote(object? sender, EventArgs e)
                {
                    _ = App.MobilizerNotes.TryGetValue(mobilizer.ID!, out var notes);
                    notes = await DisplayPromptAsync("Notes", null, placeholder: "Add notes about " + mobilizer.Name, initialValue: notes);
                    if (notes != null)
                    {
                        App.MobilizerNotes[mobilizer.ID!] = notes;
                        ((Button)sender!).Text = "View/edit notes";
                    }
                }
            }

            expander.IsExpanded = households.Count == 1;
            namesPanel.Add(expander);
        }
    }

    private async void Expander_ExpandedChanged(object? sender, CommunityToolkit.Maui.Core.ExpandedChangedEventArgs e)
    {
        var expander = (Expander)sender!;
        if (expander.IsExpanded)
        {
            await Task.Delay(10);
            double initialScroll = sv.ScrollY;
            await sv.ScrollToAsync(expander, ScrollToPosition.End, false);
            double endScroll = sv.ScrollY;
            await sv.ScrollToAsync(0, initialScroll, false);
            if (endScroll > initialScroll)
                await sv.ScrollToAsync(0, endScroll, true);
        }
    }

    private async void DoorKnocked(Button button)
    {
        if (button.BackgroundColor != Colors.ForestGreen)
        {
            button.BackgroundColor = Colors.ForestGreen;
            DoorsKnocked++;
            DoorsKnockedThisHour++;
            UpdateProgress();
            await Task.Delay(1000 * 3600);
            DoorsKnockedThisHour--;
            UpdateProgress();
        }
    }

    private void Geolocation_LocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        LogEvent("Moving", null, e.Location);

        LocationChanged(e.Location);
    }

    private async void LocationChanged(Location location)
    {
        double squareFilterLongitude = locationFilterRange / 1.609 / MilesPerDegree;
        double squareFilterLatitude = locationFilterRange / 1.609 / MilesPerDegree / Math.Cos(location.Longitude);

        var households = await App.Database.GetHouseholdsAsync();

        if (households.Count == 0)
        {
            if (Navigation.NavigationStack.Count == 1)
            {
                await Navigation.PushAsync(new SettingsPage(this));
            }
            return;
        }

        households = households
            .Where(SquareFilter) // Premature optimization? Filter to a square before doing accurate distance calculations.
            .Where(h => !hideDistantHouses || (DistanceTo(h) < locationFilterRange))
            .Where(FilterNumber)
            .ToList();

        switch (SelectedSort)
        {
            case 0:
                households = [.. households.OrderBy(DistanceTo)];
                break;
            case 2:
                ((List<Household>)households).Reverse();
                break;
            case 3:
                households = [.. households.OrderBy(AddressKey)];
                break;
        }

        Dispatcher.Dispatch(() => SetHousehold(location, households));

        bool SquareFilter(Household h)
        {
            return !hideDistantHouses || (Math.Abs(h.Location.Latitude - location.Latitude) < squareFilterLatitude
                && Math.Abs(h.Location.Longitude - location.Longitude) < squareFilterLongitude);
        }

        double DistanceTo(Household h) => h.Location.CalculateDistance(location, DistanceUnits.Kilometers);

        bool FilterNumber(Household household)
        {
            var parts = household.Address.Split(' ', 2);
            if (int.TryParse(parts[0], out int number))
            {
                return (SelectedFilter & (1 << (number & 1))) != 0;
            }
            return true;
        }

        static string AddressKey(Household household)
        {
            var parts = SplitAddress().Match(household.Address);
            if (parts.Success && int.TryParse(parts.Groups[1].Value, out int number))
            {
                return parts.Groups[3].Value + number / 100 + ((number & 1) == 0 ? "E" : "O");
            }

            return household.Address;
        }
    }

    private async void SetDisplayDescription()
    {
        if (lblDisplay == null) return;

        StringBuilder description = new("Showing up to 15");
        if (HideDistantHouses)
            description.Append(" nearby");
        switch (SelectedFilter)
        {
            case 1:
                description.Append(" even");
                break;
            case 2:
                description.Append(" odd");
                break;
        }
        description.Append(" houses, ");
        switch (SelectedSort)
        {
            case 0:
                description.Append("closest first");
                break;
            case 1:
                description.Append("in walk order");
                break;
            case 2:
                description.Append("in reverse walk order");
                break;
            case 3:
                description.Append("by address");
                break;
        }
        lblDisplay.Text = description.ToString();

        _location ??= await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best));
        if (_location != null)
            LocationChanged(_location);
    }

    private async void MainPage_Loaded(object sender, EventArgs e)
    {
        if (Canvasser == null)
            await Navigation.PushAsync(new SettingsPage(this));

        Geolocation.LocationChanged += Geolocation_LocationChanged;
        Geolocation.ListeningFailed += Geolocation_ListeningFailed;

        await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        try
        {
            await Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best));
        }
        catch (InvalidOperationException) { /* probably already listening */ }

        Window.Resumed += Window_Resumed;
        Window.Deactivated += Window_Deactivated;

        SetDisplayDescription();

        LogEvent("Started", null, _location ?? await Geolocation.GetLocationAsync());
    }

    private async void Window_Deactivated(object? sender, EventArgs e) => LogEvent("Deactivated", null, _location ?? await Geolocation.GetLastKnownLocationAsync());
    private async void Window_Resumed(object? sender, EventArgs e) => LogEvent("Resumed", null, await Geolocation.GetLocationAsync());

    private void Geolocation_ListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        // Failed also stops listening. Start listening again.
        Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best));
    }

    private async void NotListed_Clicked(object? sender, EventArgs e)
    {
        if (_location != null)
        {
            LogEvent("Opening mobilizer page (not listed)", null, _location);
            var mobilizer = new Mobilizer(null, string.Empty, null);
            await Navigation.PushAsync(new MobilizerPage(_location, mobilizer, await App.Database.GetPriorityVotersAsync(_location, mobilizer), this));
        }
        else
            await DisplayAlert("Unknown location", $"Friend lists cannot be displayed without a voter ID or location information.", "OK");
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
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(@internal);
        else
            @internal();

        void @internal() => App.TravelLog.Append(new TravelLog(Canvasser, @event, data, location?.Speed, location?.Latitude, location?.Longitude));
    }

    private void Settings_Clicked(object sender, EventArgs e) => Navigation.PushAsync(new SettingsPage(this));
    private void Search_Clicked(object sender, EventArgs e) => Navigation.PushAsync(new LookupPage(this));

    internal async void UpdateSettings(string canvasser)
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

        _location ??= await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best));
        if (_location != null)
            LocationChanged(_location);
    }

    internal void UpdateProgress()
    {
        lblProgress.Text = $"Knocked on {DoorsKnocked} doors, talked to {MobilizersContacted} mobilizers, and got {FriendsCommitted} commitments. " +
            $"({DoorsKnockedThisHour}, {MobilizersContactedThisHour}, and {FriendsCommittedThisHour} in the last 60 minutes.)";
    }

    private static readonly CsvConfiguration _csvConfiguration = new(CultureInfo.InvariantCulture) { HasHeaderRecord = false };

    private void UpdateTravelLog()
    {
        string oldLogPath = Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog.csv");

        App.TravelLog.Append(ReadOldRecords());

        File.Delete(oldLogPath);

        IEnumerable<TravelLog> ReadOldRecords()
        {
            using CsvDataReader dr = new(new(File.OpenText(oldLogPath), _csvConfiguration));
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

        App.ContactCommitments.Append(ReadOldRecords());

        File.Delete(oldCommitmentsPath);

        IEnumerable<ContactCommitment> ReadOldRecords()
        {
            using CsvDataReader dr = new(new CsvReader(File.OpenText(oldCommitmentsPath), _csvConfiguration));
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

        App.PhoneNumbers.Append(ReadOldRecords());

        File.Delete(oldPhonePath);

        IEnumerable<PhoneNumber> ReadOldRecords()
        {
            using CsvDataReader dr = new(new CsvReader(File.OpenText(oldPhonePath), _csvConfiguration));
            while (dr.Read())
            {
                int count = dr.FieldCount;
                string[] values = new string[count];
                dr.GetValues(values);
                yield return new PhoneNumber(Canvasser, values[0], values[1], values[2], values[3], values[4], values[5]);
            }
        }
    }

    private async void MainPage_SizeChanged(object sender, EventArgs e)
    {
        // Wait for the post-expansion render pass to complete, but only if this was triggered by an expander mode change
        if (((RadioButton)grdNumberFilter[0]).Height == -1 && sender is MainPage) return;
        while (((RadioButton)grdNumberFilter[0]).Height == -1)
            await Task.Delay(50);

        // Once the first post-expansion render completes, we no longer need to know about expander state changes
        if (sender is Expander expander)
            expander.ExpandedChanged -= MainPage_SizeChanged!;

        double visibleWidth = Width - 6;

        if (grdSelectedSort.Sum(x => ((RadioButton)x).Width) > visibleWidth)
        {
            grdSelectedSort.SetRow(grdSelectedSort[2], 1);
            grdSelectedSort.SetColumn(grdSelectedSort[2], 0);
            grdSelectedSort.SetRow(grdSelectedSort[3], 1);
            grdSelectedSort.SetColumn(grdSelectedSort[3], 1);

            double narrowWidth = grdSelectedSort.Skip(2).Sum(x => ((RadioButton)x).Width);
            double narrowHeight = grdSelectedSort.Take(2).Sum(x => ((RadioButton)x).Height);
            if (narrowWidth > visibleWidth)
            {
                grdSelectedSort.Scale = visibleWidth / narrowWidth;
                double TranslationX = (visibleWidth - narrowWidth) / 2;
                double TranslationY = TranslationX * narrowHeight / narrowWidth;
                grdSelectedSort.Margin = new(TranslationX, TranslationY);
            }
            else
            {
                grdSelectedSort.Scale = 1;
                grdSelectedSort.Margin = 0;
            }
        }
        else
        {
            grdSelectedSort.SetRow(grdSelectedSort[2], 0);
            grdSelectedSort.SetColumn(grdSelectedSort[2], 2);
            grdSelectedSort.SetRow(grdSelectedSort[3], 0);
            grdSelectedSort.SetColumn(grdSelectedSort[3], 3);
        }

        double numberWidth = grdNumberFilter.Sum(x => ((RadioButton)x).Width);
        double numberHeight = ((RadioButton)grdNumberFilter[0]).Height;
        if (numberWidth > visibleWidth)
        {
            grdNumberFilter.Scale = visibleWidth / numberWidth;
            double TranslationX = (visibleWidth - numberWidth) / 2;
            double TranslationY = TranslationX * numberHeight / numberWidth;
            grdNumberFilter.Margin = new(TranslationX, TranslationY);
        }
        else
        {
            grdNumberFilter.Scale = 1;
            grdNumberFilter.Margin = 0;
        }
    }

    [GeneratedRegex(@"(\d+)( 1/2)? ([A-Z0-9 ]{4,}?) ")]
    private static partial Regex SplitAddress();
}
