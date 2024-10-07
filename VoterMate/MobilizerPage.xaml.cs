using CsvHelper;
using System.Globalization;
using VoterMate.Database;

namespace VoterMate;

public partial class MobilizerPage : ContentPage
{
    private readonly Mobilizer _mobilizer;
    private readonly MainPage _mainPage;
    private readonly Location _location;
    private readonly IReadOnlyCollection<Voter> _voters;
    private readonly List<Voter> _fetchedVoters = [];
    private int _page;

    public Mobilizer Mobilizer => _mobilizer;

    public MobilizerPage(Location location, Mobilizer? mobilizer, MainPage page)
    {
        InitializeComponent();

        _location = location;

        if (mobilizer != null)
        {
            nameRow.Height = new GridLength(0);
            Title = mobilizer.Name;
        }
        else
            btnEdit.IconImageSource = null;

        _mobilizer = mobilizer ?? new Mobilizer(null, string.Empty, null);
        _mainPage = page;
        _voters = App.Database.GetVoters(location, _mobilizer);
        for (int i = 0; i < 101; i++)
            dgVoters.RowDefinitions.Add(new(GridLength.Auto));

        LoadVoterPage();
    }

    private async void LoadVoterPage()
    {
        while (dgVoters.Children.Count > 2)
            dgVoters.Children.RemoveAt(2);

        int i = 1;
        foreach (var voter in _voters.Skip(_page * 100).Take(100))
        {
            if (i % 10 == 0)
                await Task.Yield();

            Button border = new() { BorderColor = Colors.Black, BorderWidth = .25, BackgroundColor = Colors.White };
            Grid.SetRow(border, i);
            Grid.SetColumnSpan(border, 2);
            dgVoters.Children.Add(border);

            CheckBox checkBox = new() { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, Scale = 1.5 };
            Grid.SetRow(checkBox, i);
            checkBox.CheckedChanged += (s, e) => voter.WillContact = checkBox.IsChecked;
            border.Clicked += (_, _) => checkBox.IsChecked = !checkBox.IsChecked;
            dgVoters.Children.Add(checkBox);

            Label label = new() { Text = voter.NameAgeAddress, Margin = new(3), VerticalOptions = LayoutOptions.Center, FontSize = (double)new FontSizeConverter().ConvertFromString(null, null, "Medium")! };
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 1);
            dgVoters.Children.Add(label);

            _fetchedVoters.Add(voter);

            i++;
        }

        int maxPage = (_voters.Count + 99) / 100;
        Button back = new() { IsEnabled = _page != 0, Text = "← Previous", HorizontalOptions = LayoutOptions.Start, Margin = new(5) };
        back.Clicked += (_, _) => { _page--; LoadVoterPage(); };
        Grid.SetRow(back, 101);
        Grid.SetColumnSpan(back, 2);
        dgVoters.Children.Add(back);
        Label label1 = new() { Text = $"Page {_page + 1} of {maxPage}", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        Grid.SetRow(label1, 101);
        Grid.SetColumnSpan(label1, 2);
        dgVoters.Children.Add(label1);
        Button next = new() { IsEnabled = _page + 1 < maxPage, Text = "Next →", HorizontalOptions = LayoutOptions.End, Margin = new(5) };
        next.Clicked += (_, _) => { _page++; LoadVoterPage(); };
        Grid.SetRow(next, 101);
        Grid.SetColumnSpan(next, 2);
        dgVoters.Children.Add(next);
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        _mobilizer.Name = ((Entry)sender).Text;
    }
    private void Phone_TextChanged(object sender, TextChangedEventArgs e)
    {
        _mobilizer.Phone = ((Entry)sender).Text;
    }

    private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e) => Save();

    private async void Save()
    {
        _mainPage.LogEvent("Closing mobilizer page (Note: Reports household location)", Mobilizer.ID, _location);

        string name = Mobilizer.Name;
        if (string.IsNullOrEmpty(name))
            name = "<No name entered>";

        bool mobilizerContacted = false;
        int friends = 0;

        using (CsvWriter csv = new(new StreamWriter(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments_v2.csv"), true), CultureInfo.InvariantCulture))
            foreach (var voter in _fetchedVoters.Where(v => v.WillContact))
            {
                voter.WillContact = false;
                csv.WriteRecord(new ContactCommitment(_mainPage.Canvasser, _mobilizer.ID ?? name, voter.ID, _location.Latitude, _location.Longitude));
                csv.NextRecord();
                friends++;
                mobilizerContacted = true;
            }

        if (!string.IsNullOrEmpty(_mobilizer.Phone) || Mobilizer.NameChanged)
        {
            App.PhoneNumbers.Append(new PhoneNumber(_mainPage.Canvasser, _mobilizer.ID ?? name, _mobilizer.Phone, _location.Latitude, _location.Longitude, Mobilizer.NameChanged ? name : null));
            mobilizerContacted = true;
        }

        App.Database.SaveShownFriends();

        if (mobilizerContacted)
        {
            int friendCount = Math.Min(10, friends);
            _mainPage.FriendsCommitted += friendCount;
            _mainPage.FriendsCommittedThisHour += friendCount;
            _mainPage.MobilizersContacted++;
            _mainPage.MobilizersContactedThisHour++;
            _mainPage.UpdateProgress();
            await Task.Delay(1000 * 3600);
            _mainPage.FriendsCommittedThisHour -= friendCount;
            _mainPage.MobilizersContactedThisHour--;
            _mainPage.UpdateProgress();
        }
    }

    internal static Task LoadShownFriendsData(FileResult file) => App.Database.LoadShownFriends(file);

    private async void Edit_Clicked(object sender, EventArgs e)
    {
        var newName = await DisplayPromptAsync("Edit name", null, initialValue: Mobilizer.Name);
        if (newName != null)
            Title = Mobilizer.Name = newName;
    }
}
