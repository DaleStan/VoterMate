using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;

namespace VoterMate.Database;

public interface IDatabase
{
    IEnumerable<Household> GetHouseholds();
    (Mobilizer, Location)? GetMobilizer(string voterID);
    IReadOnlyCollection<Voter> GetVoters(Location location, Mobilizer mobilizer);
    Task LoadShownFriends(FileResult file);
    void SaveShownFriends();
}

public record Household(string Address, Location Location, List<Mobilizer> Mobilizers)
{
    public void SaveTo(StreamWriter stream)
        => stream.WriteLine(string.Join('\t', Mobilizers.Select(m => m.ID).Prepend($"{Address}\t{Location.Latitude:0.####}\t{Location.Longitude:0.####}")));

    public static Household? LoadFrom(StreamReader stream, Dictionary<string, Voter> voters)
    {
        if (stream.ReadLine() is string line)
        {
            var parts = line.Split('\t');
            return new(parts[0], new Location(double.Parse(parts[1]), double.Parse(parts[2])),
                [.. parts[3..].Select(p => new Mobilizer(p, voters[p].Name, voters[p].BirthDate))]);
        }
        return null;
    }
}

public record Mobilizer(string? ID, string Name, DateTime? BirthDate)
{
    public string Name { get; set; } = Name;
    public string? Phone { get; set; }
}

public record Voter(string ID, string Name, string NameAgeAddress, Location Location, DateTime BirthDate)
{
    public bool WillContact { get; set; }

    public void SaveTo(StreamWriter stream) => stream.WriteLine($"{ID}\t{Name}\t{NameAgeAddress}\t{Location.Latitude:0.####}\t{Location.Longitude:0.####}\t{BirthDate:yyyyMMdd}");

    public static Voter? LoadFrom(StreamReader stream)
    {
        if (stream.ReadLine() is string line)
        {
            var parts = line.Split('\t');
            return new(parts[0], parts[1], parts[2], new Location(double.Parse(parts[3]), double.Parse(parts[4])), DateTime.ParseExact(parts[5], "yyyyMMdd", null));
        }
        return null;
    }
}

