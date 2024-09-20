namespace VoterMate.Database;

public interface IDatabase
{
    IEnumerable<Household> GetHouseholds();
    IEnumerable<Voter> GetVoters(Location location, Mobilizer mobilizer);
}

public record Household(Location Location, string Address, List<Mobilizer> Mobilizers);

public record Mobilizer(string Name, string? ID, DateTime? BirthDate)
{
    public string Name { get; set; } = Name;
    public string? Phone { get; set; }
}

public record Voter(string Name, string ID, Location Location, DateTime BirthDate)
{
    public bool WillContact { get; set; }
}

