using VoterMate.Database;

namespace VoterMate;

public partial class LookupPage : ContentPage
{
    private readonly MainPage _mainPage;

    public LookupPage(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    private void ContentPage_Loaded(object sender, EventArgs e)
    {
        acVoterName.ItemsSource = App.Database.GetNameParts();
        txtVoterName.Focus();
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
        _mainPage.LogEvent("Opening mobilizer page (ID lookup)", mobilizer.ID, _mainPage.Location);
        await txtVoterID.HideSoftInputAsync(new CancellationTokenSource().Token);
        await Navigation.PushAsync(new MobilizerPage(location, mobilizer, _mainPage));
    }

    private void acVoterName_SelectionChanged(object sender, Syncfusion.Maui.Inputs.SelectionChangedEventArgs e)
    {
        if (acVoterName.SelectedItems?.Count > 0 && txtVoterName.Text != "")
        {
            txtVoterName.Text = "";
        }

        var lists = acVoterName.SelectedItems?.Cast<string>().Select(App.Database.GetVoters).ToList() ?? [];
        cboVoterName.IsDropDownOpen = false;
        cboVoterName.IsVisible = true;
        cboVoterName.IsEnabled = false;
        btnVoterName.IsVisible = false;
        lblWarning.IsVisible = false;

        if (lists.Count == 0)
        {
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

    private async void txtVoterName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (txtVoterName.Text != "" && acVoterName.SelectedItems?.Count > 0)
        {
            acVoterName.SelectedItems?.Clear();
            do
            {
                await Task.Delay(20);
                txtVoterName.Focus();
            } while (!txtVoterName.IsFocused);
        }

        cboVoterName.IsDropDownOpen = false;
        cboVoterName.IsVisible = true;
        cboVoterName.IsEnabled = false;
        btnVoterName.IsVisible = false;
        lblWarning.IsVisible = false;
        ConfigureVoterSelection(App.Database.GetVotersByName(txtVoterName.Text));
    }

    private void ConfigureVoterSelection(IReadOnlyList<Voter> voters)
    {
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

    private async void cboVoterName_SelectionChanged(object sender, EventArgs e)
    {
        var element = (VisualElement)sender;
        if (element.IsVisible && element.IsEnabled && cboVoterName.SelectedItem != null)
        {
            var voter = (Voter)cboVoterName.SelectedItem;
            txtVoterID.Text = voter.ID[2..];
            Lookup_Clicked(sender, e);
            acVoterName.SelectedItems?.Clear();
            await txtVoterID.HideSoftInputAsync(new CancellationTokenSource().Token);
        }
    }
}