namespace VoterMate.Database;

internal class TsvDatabase : IDatabase
{
    // voterID -> List<voterID>
    private readonly Dictionary<string, List<string>> _housemates = [];
    // address -> (Location, List<voterID>)
    private readonly Dictionary<string, Household> _households = [];
    // voterID -> Voter
    private readonly Dictionary<string, Voter> _voters = [];
    private readonly HashSet<Voter> _priorityVoters = [];

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
                _priorityVoters.Add(_voters[line]);
    }

    public IEnumerable<Household> GetHouseholds() => _households.Values;

    public List<Voter> GetVoters(Location location, Mobilizer mobilizer)
    {
        return [.. _priorityVoters
            .OrderByDescending(RelationshipScore)
            .ThenBy(Distance)];

        double RelationshipScore(Voter voter)
        {
            const double ZeroDistanceScore = 20;
            const double PointsLostPerMile = 40;
            const double ClassmatesScore = 5;
            const double HousematesScore = 10;

            double distanceScore = Math.Min(0, ZeroDistanceScore - PointsLostPerMile * location.CalculateDistance(voter.Location, DistanceUnits.Miles));
            double classmatesScore = (voter.BirthDate - mobilizer.BirthDate.GetValueOrDefault()).Days < 18 * 30 ? ClassmatesScore : 0;
            double housematesScore = _housemates.TryGetValue(voter.ID, out var housemates) && housemates.Contains(mobilizer.ID!) ? HousematesScore : 0;

            return distanceScore + classmatesScore + housematesScore;
        }

        double Distance(Voter voter) => location.CalculateDistance(voter.Location, DistanceUnits.Miles);
    }

    public (Mobilizer, Location)? GetMobilizer(string voterID)
    {
        if (_voters.TryGetValue(voterID, out Voter? voter))
        {
            return (new Mobilizer(voterID, voter.Name, voter.BirthDate), voter.Location);
        }
        return null;
    }
}
