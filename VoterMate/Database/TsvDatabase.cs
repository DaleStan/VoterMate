namespace VoterMate.Database;

internal class TsvDatabase : IDatabase
{
    // voterID -> List<voterID>
    private readonly Dictionary<string, List<string>> _housemates = [];
    // address -> (Location, List<voterID>)
    private readonly Dictionary<string, Household> _households = [];
    // voterID -> Voter
    private readonly Dictionary<string, Voter> _voters = [];
    private readonly HashSet<string> _priorityVoters = [];

    public TsvDatabase()
    {
        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.housemates.tsv")!))
            while (sr.ReadLine() is string line)
            {
                var parts = line.Split('\t');
                _housemates[parts[0]] = [.. parts[1..]];
            }

        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.voters.tsv")!))
            while (Voter.LoadFrom(sr) is Voter voter)
                _voters[voter.ID] = voter;

        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.households.tsv")!))
            while (Household.LoadFrom(sr, _voters) is Household household)
                _households[household.Address] = household;

        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.priorityVoters.tsv")!))
            while (sr.ReadLine() is string line)
                _priorityVoters.Add(line);
    }

    public IEnumerable<Household> GetHouseholds() => _households.Values;

    public List<Voter> GetVoters(Location location, Mobilizer mobilizer)
    {
        return _priorityVoters.Select(v => _voters[v]).OrderByDescending(RelationshipScore).ToList();

        int RelationshipScore(Voter voter)
        {
            int ZeroDistanceScore = 20;
            double pointsLostPerMile = .25;
            int ClassmatesScore = 5;
            int HousematesScore = 10;

            int distanceScore = ZeroDistanceScore - (int)(pointsLostPerMile * location.CalculateDistance(voter.Location, DistanceUnits.Miles));
            int ageScore = (voter.BirthDate - mobilizer.BirthDate.GetValueOrDefault()).Days < 18 * 30 ? ClassmatesScore : 0;
            int classmatesScore = _housemates.TryGetValue(voter.ID, out var housemates) && housemates.Contains(mobilizer.ID!) ? HousematesScore : 0;

            return distanceScore + ageScore + classmatesScore;
        }
    }

    public (Mobilizer, Location)? GetMobilizer(string voterID)
    {
        var household = _households.Values.FirstOrDefault(h => h.Mobilizers.Any(m => m.ID == voterID));
        if (household == null)
            return null;
        return (household.Mobilizers.First(m => m.ID == voterID), household.Location);
    }
}
