using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;

namespace VoterMate.Database;

public interface IDatabase
{
    Task<IReadOnlyList<Household>> GetHouseholdsAsync();
    Task<(Mobilizer, Location)?> GetMobilizerAsync(string voterID);
    Task<IReadOnlyCollection<Voter>> GetPriorityVotersAsync(Location location, Mobilizer mobilizer);
    Task LoadShownFriendsAsync(FileResult file);
    void LoadTurfList(string path);
    Task SaveShownFriendsAsync();
    Task<IReadOnlyCollection<string>> GetNamePartsAsync();
    Task<IReadOnlyCollection<Voter>> GetVotersAsync(string namePart);
    Task<IReadOnlyList<Voter>> GetVotersByNameAsync(string name);
    Task<IReadOnlyList<Voter>> GetVotersByBirthdateAsync(DateTime date);
}

public record Household(string Address, Location Location, List<Mobilizer> Mobilizers)
{
    public static Household? LoadFrom(StreamReader stream, Dictionary<string, List<Voter>> voters)
    {
        while (stream.ReadLine() is string line)
            if (voters.TryGetValue(line, out var mobilizers))
                return new(line, mobilizers[0].Location, [.. mobilizers.Select(v => new Mobilizer(v.ID, v.Name, v.BirthDate))]);
        return null;
    }
}

public record Mobilizer(string? ID, string Name, DateTime? BirthDate)
{
    private string _name = Name;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                NameChanged = true;
            }
        }
    }
    // Note: This must be declared after Name, so the constructor will re-initialize it to false.
    public bool NameChanged { get; private set; } = false;
    public string? Phone { get; set; }
}

public record Voter(string ID, string Name, string NameAgeAddress, Location Location, DateTime BirthDate, string Address)
{
    public bool WillContact { get; set; }

    public void SaveTo(StreamWriter stream) => stream.WriteLine($"{ID}\t{Name}\t{NameAgeAddress}\t{Location.Latitude:0.####}\t{Location.Longitude:0.####}\t{BirthDate:yyyyMMdd}\t{Address}");

    public static Voter? LoadFrom(StreamReader stream)
    {
        if (stream.ReadLine() is string line)
        {
            var parts = line.Split('\t');
            return new(parts[0], parts[1], parts[2], new Location(double.Parse(parts[3]), double.Parse(parts[4])), DateTime.ParseExact(parts[5], "yyyyMMdd", null), parts[6]);
        }
        return null;
    }
}

