namespace VoterMate.Database
{
    public interface IDatabase
    {
        IEnumerable<Household> GetHouseholds();
    }

    public record Household(double Latitude, double Longitude, List<Voter> Voters);

    public record Voter(string Name, int AgeMonths);
}
