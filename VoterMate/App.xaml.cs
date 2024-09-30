using VoterMate.Database;

namespace VoterMate;

public partial class App : Application
{
    public static IDatabase Database { get; } = new TsvDatabase();

    internal static OutputFile<ContactCommitment> ContactCommitments { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments_v2.csv"));
    internal static OutputFile<PhoneNumber> PhoneNumbers { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers_v2.csv"));
    internal static OutputFile<TravelLog> TravelLog { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog_v2.csv"));

    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
}
