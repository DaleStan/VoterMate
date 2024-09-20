using Maui.DataGrid;
using VoterMate.Database;

namespace VoterMate;

public partial class MobilizerPage : ContentPage
{
    private readonly Mobilizer _mobilizer;

    public MobilizerPage(Location location, Mobilizer? mobilizer)
    {
        InitializeComponent();

        if (mobilizer != null)
            nameRow.Height = new GridLength(0);

        _mobilizer = mobilizer ?? new Mobilizer(string.Empty, null, null);

        dgData.ItemsSource = App.Database.GetVoters(location, _mobilizer);
    }

    private void Name_Completed(object sender, EventArgs e)
    {
        _mobilizer.Name = ((Entry)sender).Text;
    }
    private void Phone_Completed(object sender, EventArgs e)
    {
        _mobilizer.Phone = ((Entry)sender).Text;
    }

    private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e) => Save();

    private void dgData_Refreshing(object sender, EventArgs e) => dgData.IsRefreshing = false;

    public void Save()
    {
    }
}