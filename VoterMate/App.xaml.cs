﻿using VoterMate.Database;

namespace VoterMate;

public partial class App : Application
{
    internal static TsvDatabase Database { get; } = new();

    internal static OutputFile<ContactCommitment> ContactCommitments { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "contactCommitments_v2.csv"));
    internal static MultiMapDataFile<DoorKnock> DoorsKnocked { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "doorsKnocked.csv"), nameof(DoorKnock.Address));
    internal static KeyValueDataFile MobilizerNotes { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "mobilizerNotes.csv"), "Mobilizer");
    internal static OutputFile<PhoneNumber> PhoneNumbers { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "phoneNumbers_v2.csv"));
    internal static OutputFile<TravelLog> TravelLog { get; } = new(Path.Combine(FileSystem.Current.AppDataDirectory, "travelLog_v2.csv"));

    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
}
