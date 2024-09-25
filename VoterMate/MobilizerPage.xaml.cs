using System.Collections.Concurrent;
using VoterMate.Database;

namespace VoterMate;

public partial class MobilizerPage : ContentPage
{
    private readonly Mobilizer _mobilizer;
    private readonly List<Voter> _voters;
    private static readonly ConcurrentDictionary<string, int> _nextPageToShow = [];
    private int _page;

    public Mobilizer Mobilizer => _mobilizer;

    public MobilizerPage(Location location, Mobilizer? mobilizer)
    {
        InitializeComponent();

        if (mobilizer != null)
        {
            nameRow.Height = new GridLength(0);
            _ = _nextPageToShow.TryGetValue(mobilizer.ID!, out _page);
        }

        _mobilizer = mobilizer ?? new Mobilizer(null, string.Empty, null);

        _voters = App.Database.GetVoters(location, _mobilizer);
        for (int i = 0; i < 101; i++)
            dgVoters.RowDefinitions.Add(new(GridLength.Auto));

        if (_page * 100 > _voters.Count)
            _page = 0; // Go back to the beginning if they've seen everything.

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

            CheckBox checkBox = new() { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
            Grid.SetRow(checkBox, i);
            checkBox.CheckedChanged += (s, e) => voter.WillContact = checkBox.IsChecked;
            border.Clicked += (_, _) => checkBox.IsChecked = !checkBox.IsChecked;
            dgVoters.Children.Add(checkBox);

            Label label = new() { Text = voter.NameAgeAddress, Margin = new(3), VerticalOptions = LayoutOptions.Center };
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 1);
            dgVoters.Children.Add(label);

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

    public void Save()
    {
        using StreamWriter sw = new(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments.csv"), true) { NewLine = "\n" };
        foreach (var voter in _voters.Where(v => v.WillContact))
        {
            voter.WillContact = false;
            string name = '"' + _mobilizer.Name.Replace("\"", "\"\"") + '"';
            sw.WriteLine((_mobilizer.ID ?? name) + ',' + voter.ID);
        }

        if (!string.IsNullOrEmpty(_mobilizer.Phone))
        {
            File.AppendAllLines(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers.csv"), [(_mobilizer.ID ?? _mobilizer.Name) + ',' + _mobilizer.Phone]);
        }

        if (Mobilizer.ID != null)
        {
            _ = _nextPageToShow.TryGetValue(Mobilizer.ID, out var lastPage);
            _nextPageToShow[Mobilizer.ID] = Math.Max(lastPage, _page + 1);
            File.WriteAllLines(Path.Combine(FileSystem.Current.AppDataDirectory, "nextPageToShow.csv"), [.. _nextPageToShow.Select(kvp => kvp.Key + ',' + kvp.Value)]);
        }
    }

    internal static async Task LoadLastSeenData(string file)
    {
        using Stream stream = File.OpenRead(file);
        await LoadLastSeenData(stream);
    }

    internal static async Task LoadLastSeenData(FileResult file)
    {
        using Stream stream = await file.OpenReadAsync();
        await LoadLastSeenData(stream);
    }

    internal static async Task LoadLastSeenData(Stream stream)
    {
        using StreamReader sr = new(stream);
        while (await sr.ReadLineAsync() is string line)
        {
            var parts = line.Split(',');
            if (int.TryParse(parts[1], out int page) && page >= 0)
                _nextPageToShow[parts[0]] = page;
        }
    }
}
