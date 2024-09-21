using VoterMate.Database;

namespace VoterMate;

public partial class App : Application
{
    // TODO: Create a `class RealDatabase : IDatabase` type that reads and writes the appropriate data.
    // Change StubDatabase to RealDatabase here.
    public static IDatabase Database { get; } = new TsvDatabase();

    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
}
