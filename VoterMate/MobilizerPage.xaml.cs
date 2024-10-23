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

    public MobilizerPage(Location location, Mobilizer mobilizer, IReadOnlyCollection<Voter> voters, MainPage mainPage)
    {
        InitializeComponent();

        _location = location;

        if (mobilizer.ID != null)
        {
            nameRow.Height = new GridLength(0);
            Title = mobilizer.Name;
        }
        else
            btnEdit.IconImageSource = null;

        _mobilizer = mobilizer;
        _mainPage = mainPage;
        _voters = voters;
        for (int i = 0; i < 101; i++)
            dgVoters.RowDefinitions.Add(new(GridLength.Auto));

        LoadVoterPage();
    }

    private async void LoadVoterPage()
    {
        var savePreviousPage = SavePage();

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

        await savePreviousPage;

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

    private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e) => SaveMobilizer();

    bool mobilizerContacted = false;
    int friends = 0;

    private Task SavePage()
    {
        string name = Mobilizer.Name;
        if (string.IsNullOrEmpty(name))
            name = "<No name entered>";

        foreach (var voter in _fetchedVoters.TakeLast(100))
        {
            if (voter.WillContact)
            {
                App.ContactCommitments.Append(new ContactCommitment(_mainPage.Canvasser, _mobilizer.ID ?? name, voter.ID, _location.Latitude, _location.Longitude));
                friends++;
                mobilizerContacted = true;
            }
        }
        return App.Database.SaveShownFriendsAsync();
    }

    private async void SaveMobilizer()
    {
        _mainPage.LogEvent("Closing mobilizer page (Note: Reports household location)", Mobilizer.ID, _location);

        await SavePage();

        string name = Mobilizer.Name;
        if (string.IsNullOrEmpty(name))
            name = "<No name entered>";

        foreach (var voter in _fetchedVoters)
        {
            voter.WillContact = false;
        }

        if (!string.IsNullOrEmpty(_mobilizer.Phone) || Mobilizer.NameChanged)
        {
            App.PhoneNumbers.Append(new PhoneNumber(_mainPage.Canvasser, _mobilizer.ID ?? name, _mobilizer.Phone, _location.Latitude, _location.Longitude, Mobilizer.NameChanged ? name : null));
            mobilizerContacted = true;
        }

        await App.Database.SaveShownFriendsAsync();

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

    internal static Task LoadShownFriendsData(FileResult file) => App.Database.LoadShownFriendsAsync(file);

    private async void Edit_Clicked(object sender, EventArgs e)
    {
        var newName = await DisplayPromptAsync("Edit name", null, initialValue: Mobilizer.Name);
        if (newName != null)
            Title = Mobilizer.Name = newName;
    }
}
