using Microsoft.Maui.Devices.Sensors;

namespace VoterMate.Database;

public interface IDatabase
{
    IEnumerable<Household> GetHouseholds();
    IEnumerable<Voter> GetVoters(Location location, Mobilizer mobilizer);
}

public record Household(string Address, Location Location, List<Mobilizer> Mobilizers)
{
    public void SaveTo(StreamWriter stream)
        => stream.WriteLine(string.Join('\t', new object[] { Address, Location.Latitude, Location.Longitude }.Concat(Mobilizers.Select(m => m.ID))));

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

public record Voter(string ID, string Name, Location Location, DateTime BirthDate)
{
    public bool WillContact { get; set; }

    public void SaveTo(StreamWriter stream) => stream.WriteLine($"{ID}\t{Name}\t{Location.Latitude}\t{Location.Longitude}\t{BirthDate:yyyy-MM-dd}");

    public static Voter? LoadFrom(StreamReader stream)
    {
        if (stream.ReadLine() is string line)
        {
            var parts = line.Split('\t');
            return new(parts[0], parts[1], new Location(double.Parse(parts[2]), double.Parse(parts[3])), DateTime.Parse(parts[4]));
        }
        return null;
    }
}

