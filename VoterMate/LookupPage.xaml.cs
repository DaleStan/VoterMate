using Syncfusion.Maui.Inputs;
using VoterMate.Database;

namespace VoterMate;

public partial class LookupPage : ContentPage
{
    private static readonly Task<List<string>> _nameParts;

    static LookupPage()
    {
        _nameParts = Task.Run(async () =>
        {
            var nameParts = (await App.Database.GetNamePartsAsync()).ToList();
            nameParts.Sort(StringComparer.InvariantCultureIgnoreCase);
            return nameParts;
        });
    }

    private readonly MainPage _mainPage;

    public LookupPage(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
        acVoterName.FilterBehavior = new FilterBehavior(_nameParts);
    }

    private async void ContentPage_Loaded(object sender, EventArgs e)
    {
        if (!_nameParts.Wait(100))
            await _nameParts;

        acVoterName.ItemsSource = _nameParts.Result;
        acVoterName.IsEnabled = true;
        acVoterName.Placeholder = "Enter name, age, and/or address";
    }

    private async void Lookup_Clicked(object sender, EventArgs e)
    {
        var info = await App.Database.GetMobilizerAsync("OH" + txtVoterID.Text);
        if (info == null)
        {
            await DisplayAlert("Not Found", $"The voter with ID OH{txtVoterID.Text} could not be found.", "OK");
            return;
        }

        var (mobilizer, location) = info.Value;
        _mainPage.LogEvent("Opening mobilizer page (ID lookup)", mobilizer.ID, _mainPage.Location);
        await txtVoterID.HideSoftInputAsync(new CancellationTokenSource().Token);
        await Navigation.PushAsync(new MobilizerPage(location, mobilizer, await App.Database.GetPriorityVotersAsync(location, mobilizer), _mainPage));
    }

    private async void acVoterName_SelectionChanged(object sender, Syncfusion.Maui.Inputs.SelectionChangedEventArgs e)
    {
        lblWarning.IsVisible = false;
        var taskLists = acVoterName.SelectedItems?.Cast<string>().Select(App.Database.GetVotersAsync).ToList() ?? [];
        await Task.WhenAll(taskLists);
        var lists = taskLists.Select(t => t.Result).ToList();

        if (lists.Count == 0)
        {
            ConfigureVoterSelection([]);
            cboVoterName.Text = "No search parameters";
        }
        else
        {
            var voters = lists.Aggregate((IEnumerable<Voter>)lists[0], (a, b) => a.Intersect(b)).ToList();
            if (voters.Count == 0 && lists.Count > 1)
            {
                for (int i = 0; i < lists.Count; i++)
                    voters.AddRange(lists.Except([lists[i]]).Aggregate((IEnumerable<Voter>)lists.Except([lists[i]]).First(), (a, b) => a.Intersect(b)));
                voters = voters.Distinct().ToList();
                lblWarning.IsVisible = true;
            }
            ConfigureVoterSelection(voters);
        }
    }

    private async void DatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        if (acVoterName.SelectedItems?.Count > 0)
        {
            acVoterName.SelectedItems?.Clear();
        }

        ConfigureVoterSelection(await App.Database.GetVotersByBirthdateAsync(e.NewDate));
    }


    private void ConfigureVoterSelection(IReadOnlyList<Voter> voters)
    {
        cboVoterName.IsDropDownOpen = false;
        cboVoterName.IsVisible = true;
        cboVoterName.IsEnabled = false;
        btnVoterName.IsVisible = false;

        if (voters.Count == 0)
        {
            cboVoterName.Text = "No voters match supplied filters";
        }
        else if (voters.Count == 1)
        {
            btnVoterName.Text = "Look up " + voters[0].NameAgeAddress;
            btnVoterName.IsVisible = true;
            cboVoterName.IsVisible = false;
            cboVoterName.ItemsSource = voters;
            cboVoterName.SelectedItem = voters[0];
        }
        else if (voters.Count <= 20)
        {
            cboVoterName.ItemsSource = voters;
            cboVoterName.SelectedItem = null;
            cboVoterName.Text = $"Select one of {voters.Count} matches";
            cboVoterName.IsEnabled = true;
        }
        else
        {
            cboVoterName.Text = $"{voters.Count} matches; please add more filters";
        }
    }

    private void cboVoterName_SelectionChanged(object sender, EventArgs e)
    {
        var element = (VisualElement)sender;
        if (element.IsVisible && element.IsEnabled && cboVoterName.SelectedItem != null)
        {
            var voter = (Voter)cboVoterName.SelectedItem;
            txtVoterID.Text = voter.ID[2..];
            Lookup_Clicked(sender, e);
        }
    }

    private void ContentPage_NavigatedTo(object sender, NavigatedToEventArgs e)
    {
        acVoterName.SelectedItems?.Clear();
    }
}

internal class FilterBehavior(Task<List<string>> nameParts) : IAutocompleteFilterBehavior
{
    public async Task<object?> GetMatchingItemsAsync(SfAutocomplete source, AutocompleteFilterInfo filterInfo)
    {
        if (filterInfo.Text?.Length > 1)
        {
            var startIdx = (await nameParts).BinarySearch(filterInfo.Text, StringComparer.InvariantCultureIgnoreCase);
            if (startIdx < 0) startIdx = ~startIdx;
            return nameParts.Result.Skip(startIdx).TakeWhile(v => v.StartsWith(filterInfo.Text, StringComparison.InvariantCultureIgnoreCase)).ToList();
        }
        return new string[] { "Enter at least 2 characters" };
    }
}