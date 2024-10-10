using System.Collections;

namespace VoterMate.Database;

internal class TsvDatabase : IDatabase
{
    // voterID -> List<voterID>
    private readonly Dictionary<string, HashSet<string>> _housemates = [];
    // address -> (Location, List<voterID>)
    private readonly List<Household> _households = [];
    // voterID -> Voter
    private readonly Dictionary<string, Voter> _voters = [];
    private readonly Dictionary<string, List<Voter>> _voterAddresses = [];
    private readonly HashSet<Voter> _priorityVoters = [];
    private readonly Dictionary<string, HashSet<string>> _friendsShown = [];
    private readonly Dictionary<string, List<Voter>> _voterNames = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, List<Voter>> _voterNameParts = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<DateTime, List<Voter>> _voterBirth = new();

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
            {
                _voters[voter.ID] = voter;
                if (!_voterAddresses.TryGetValue(voter.Address, out var household))
                    _voterAddresses[voter.Address] = household = [];
                household.Add(voter);
                string name = voter.Name.Replace("  ", " ");
                if (!_voterNames.TryGetValue(name, out var names)) _voterNames[name] = names = [];
                names.Add(voter);
                if (!_voterBirth.TryGetValue(voter.BirthDate, out names)) _voterBirth[voter.BirthDate] = names = [];
                names.Add(voter);
            }

        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.priorityVoters.tsv")!))
            while (sr.ReadLine() is string line)
                _priorityVoters.Add(_voters[line]);

        using (StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.lookupDb.tsv")!))
            while (sr.ReadLine() is string line)
            {
                var parts = line.Split('\t');
                _voterNameParts[parts[0]] = [.. parts[1..].Select(v => _voters[v])];
            }

        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv")).Close();
        using (StreamReader sr = new(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv")))
            LoadShownFriends(sr);
    }

    public void LoadTurfList(string path)
    {
        _households.Clear();
        HashSet<string> addresses = [];
        using StreamReader sr = new(path);
        while (Household.LoadFrom(sr, _voterAddresses) is Household household)
            if (addresses.Add(household.Address))
                _households.Add(household);
    }

    public IReadOnlyList<Household> GetHouseholds() => _households.AsReadOnly();

    public IReadOnlyCollection<Voter> GetPriorityVoters(Location location, Mobilizer mobilizer) => new ReadOnlyCollection(this, location, mobilizer);

    public (Mobilizer, Location)? GetMobilizer(string voterID)
    {
        if (_voters.TryGetValue(voterID, out Voter? voter))
        {
            return (new Mobilizer(voterID, voter.Name, voter.BirthDate), voter.Location);
        }
        return null;
    }

    public void SaveShownFriends()
    {
        File.WriteAllLines(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv"),
            [.. _friendsShown.SelectMany(kvp => kvp.Value.Select(val => kvp.Key + ',' + val))]);
    }

    public async Task LoadShownFriends(FileResult file)
    {
        using Stream stream = await file.OpenReadAsync();
        using StreamReader sr = new(stream);
        LoadShownFriends(sr);
    }

    public IReadOnlyList<Voter> GetVotersByName(string name)
    {
        _ = _voterNames.TryGetValue(name, out var voters);
        return voters ?? [];
    }

    public IReadOnlyCollection<string> GetNameParts() => _voterNameParts.Keys;

    public IReadOnlyCollection<Voter> GetVoters(string namePart)
    {
        _ = _voterNameParts.TryGetValue(namePart, out var voters);
        return voters ?? [];
    }

    public IReadOnlyList<Voter> GetVotersByBirthdate(DateTime date)
    {
        _ = _voterBirth.TryGetValue(date, out var voters);
        return voters ?? [];
    }

    private void LoadShownFriends(StreamReader sr)
    {
        while (sr.ReadLine() is string line)
        {
            var parts = line.Split(',');
            if (!_friendsShown.TryGetValue(parts[0], out var shown))
                _friendsShown[parts[0]] = [.. parts[1..]];
            else
                foreach (var friend in parts[1..])
                    shown.Add(friend);
        }
    }

    private sealed class ReadOnlyCollection : IReadOnlyCollection<Voter>
    {
        private readonly HashSet<string> _housemates;
        private readonly Location _location;
        private readonly Mobilizer _mobilizer;
        private readonly HashSet<string> _viewedFriends;
        private readonly HashSet<string> _initialViewedFriends;
        private readonly List<Voter> _voters;

        public int Count => _voters.Count;

        public ReadOnlyCollection(TsvDatabase tsvDatabase, Location location, Mobilizer mobilizer)
        {
            _location = location;
            _mobilizer = mobilizer;
            if (mobilizer.ID == null || !tsvDatabase._housemates.TryGetValue(mobilizer.ID, out _housemates!))
                _housemates = [];

            if (mobilizer.ID != null)
            {
                if (!tsvDatabase._friendsShown.TryGetValue(mobilizer.ID, out _viewedFriends!))
                    _viewedFriends = tsvDatabase._friendsShown[mobilizer.ID] = [];
            }
            else
                _viewedFriends = [];

            _initialViewedFriends = [.. _viewedFriends];

            _voters = tsvDatabase._priorityVoters.Where(v => v.ID != mobilizer.ID).ToList();
        }

        public IEnumerator<Voter> GetEnumerator()
        {
            return _voters
                .OrderByDescending(RelationshipScore)
                .ThenBy(Distance)
                .Select(VoterFetched)
                .GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private double RelationshipScore(Voter voter)
        {
            const double ZeroDistanceScore = 20;
            const double PointsLostPerMile = 40;
            const double ClassmatesScore = 5;
            const double HousematesScore = 10;

            double distanceScore = Math.Min(0, ZeroDistanceScore - PointsLostPerMile * _location.CalculateDistance(voter.Location, DistanceUnits.Miles));
            double classmatesScore = (voter.BirthDate - _mobilizer.BirthDate.GetValueOrDefault()).Days < 18 * 30 ? ClassmatesScore : 0;
            double housematesScore = _housemates.Contains(_mobilizer.ID!) ? HousematesScore : 0;
            double alreadyViewedPenalty = _initialViewedFriends.Contains(voter.ID) ? -100 : 0;

            return distanceScore + classmatesScore + housematesScore + alreadyViewedPenalty;
        }

        private double Distance(Voter voter) => _location.CalculateDistance(voter.Location, DistanceUnits.Miles);

        private Voter VoterFetched(Voter voter) // HACK: depending on the .Skip().Take() behavior of our caller, and using this method for its side effects.
        {
            _viewedFriends.Add(voter.ID);
            return voter;
        }
    }
}
