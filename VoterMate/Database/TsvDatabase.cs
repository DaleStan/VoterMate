using System.Collections;

namespace VoterMate.Database;

internal class TsvDatabase
{
    // voterID -> List<voterID>
    private readonly Task<Dictionary<string, HashSet<string>>> _housemates;
    private Task<List<Household>>? _households;
    // voterID -> Voter
    private readonly Task<Dictionary<string, Voter>> _voters;
    private readonly Task<Dictionary<string, List<Voter>>> _voterAddresses;
    private readonly Task<HashSet<Voter>> _priorityVoters;
    private readonly Dictionary<string, HashSet<string>> _friendsShown = [];
    private readonly Task<Dictionary<string, List<Voter>>> _voterNames;
    private readonly Task<Dictionary<string, List<Voter>>> _voterNameParts;
    private readonly Task<Dictionary<DateTime, List<Voter>>> _voterBirth;

    public TsvDatabase()
    {
        _housemates = Task.Run(() =>
        {
            Dictionary<string, HashSet<string>> housemates = [];
            using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.housemates.tsv")!);
            while (sr.ReadLine() is string line)
            {
                var parts = line.Split('\t');
                housemates[parts[0]] = [.. parts[1..]];
            }
            return housemates;
        });

        _voters = Task.Run(() =>
        {
            Dictionary<string, Voter> voters = [];
            using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.voters.tsv")!);
            while (Voter.LoadFrom(sr) is Voter voter)
                voters[voter.ID] = voter;
            return voters;
        });

        _voterAddresses = Task.Run(async () =>
        {
            Dictionary<string, List<Voter>> voterAddresses = [];
            foreach (var voter in (await _voters).Values)
            {
                if (!voterAddresses.TryGetValue(voter.Address, out var household))
                    voterAddresses[voter.Address] = household = [];
                household.Add(voter);
            }
            return voterAddresses;
        });

        _voterNames = Task.Run(async () =>
        {
            Dictionary<string, List<Voter>> voterNames = new(StringComparer.InvariantCultureIgnoreCase);
            foreach (var voter in (await _voters).Values)
            {
                string name = voter.Name.Replace("  ", " ");
                if (!voterNames.TryGetValue(name, out var voters)) voterNames[name] = voters = [];
                voters.Add(voter);
            }
            return voterNames;
        });

        _voterBirth = Task.Run(async () =>
        {
            Dictionary<DateTime, List<Voter>> voterBirth = [];
            foreach (var voter in (await _voters).Values)
            {
                if (!voterBirth.TryGetValue(voter.BirthDate, out var voters)) voterBirth[voter.BirthDate] = voters = [];
                voters.Add(voter);
            }
            return voterBirth;
        });

        _priorityVoters = Task.Run(async () =>
        {
            var voters = await _voters;
            HashSet<Voter> priorityVoters = [];
            using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.priorityVoters.tsv")!);
            while (sr.ReadLine() is string line)
                priorityVoters.Add(voters[line]);
            return priorityVoters;
        });


        _voterNameParts = Task.Run(async () =>
        {
            var voters = await _voters;
            Dictionary<string, List<Voter>> voterNameParts = new(StringComparer.InvariantCultureIgnoreCase);
            using StreamReader sr = new(typeof(TsvDatabase).Assembly.GetManifestResourceStream("VoterMate.Database.lookupDb.tsv")!);
            while (sr.ReadLine() is string line)
            {
                var parts = line.Split('\t');
                voterNameParts[parts[0]] = [.. parts[1..].Select(v => voters[v])];
            }
            return voterNameParts;
        });

        File.OpenWrite(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv")).Close();
        using (StreamReader sr = new(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv")))
            LoadShownFriends(sr);
    }

    public void LoadTurfList(string path)
    {
        _households = Task.Run(async () =>
        {
            List<Household> households = [];
            HashSet<string> addresses = [];
            File.OpenWrite(path).Close();
            using StreamReader sr = new(path);
            while (Household.LoadFrom(sr, await _voterAddresses) is Household household)
                if (addresses.Add(household.Address))
                    households.Add(household);
            return households;
        });
    }

    public async Task<IReadOnlyList<Household>> GetHouseholdsAsync() => (await (_households ?? Task.FromResult(new List<Household>()))).AsReadOnly();

    public async Task<IReadOnlyCollection<Voter>> GetPriorityVotersAsync(Location location, Mobilizer mobilizer)
        => new ReadOnlyCollection(await _housemates, await _priorityVoters, _friendsShown, location, mobilizer);

    public async Task<(Mobilizer, Location)?> GetMobilizerAsync(string voterID)
    {
        if ((await _voters).TryGetValue(voterID, out Voter? voter))
        {
            return (new Mobilizer(voterID, voter.Name, voter.BirthDate), voter.Location);
        }
        return null;
    }

    public Task SaveShownFriendsAsync()
    {
        return File.WriteAllLinesAsync(Path.Combine(FileSystem.Current.AppDataDirectory, "friendsShown.csv"),
            [.. _friendsShown.SelectMany(kvp => kvp.Value.Select(val => kvp.Key + ',' + val))]);
    }

    public async Task LoadShownFriendsAsync(FileResult file)
    {
        using Stream stream = await file.OpenReadAsync();
        using StreamReader sr = new(stream);
        LoadShownFriends(sr);
    }

    public async Task<IReadOnlyList<Voter>> GetVotersByNameAsync(string name)
    {
        _ = (await _voterNames).TryGetValue(name, out var voters);
        return voters ?? [];
    }

    public async Task<IReadOnlyCollection<string>> GetNamePartsAsync() => (await _voterNameParts).Keys;

    public async Task<IReadOnlyCollection<Voter>> GetVotersAsync(string namePart)
    {
        _ = (await _voterNameParts).TryGetValue(namePart, out var voters);
        return voters ?? [];
    }

    public async Task<IReadOnlyList<Voter>> GetVotersByBirthdateAsync(DateTime date)
    {
        _ = (await _voterBirth).TryGetValue(date, out var voters);
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

        public ReadOnlyCollection(Dictionary<string, HashSet<string>> housemates, HashSet<Voter> priorityVoters, Dictionary<string, HashSet<string>> friendsShown, Location location, Mobilizer mobilizer)
        {
            _location = location;
            _mobilizer = mobilizer;
            if (mobilizer.ID == null || !housemates.TryGetValue(mobilizer.ID, out _housemates!))
                _housemates = [];

            if (mobilizer.ID != null)
            {
                if (!friendsShown.TryGetValue(mobilizer.ID, out _viewedFriends!))
                    _viewedFriends = friendsShown[mobilizer.ID] = [];
            }
            else
                _viewedFriends = [];

            _initialViewedFriends = [.. _viewedFriends];

            _voters = priorityVoters.Where(v => v.ID != mobilizer.ID).ToList();
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
            double HousematesScore = 4 + 16 * Random.Shared.NextDouble();// 20*rand_between(0.2,1) == rand_between(4,20) == 4+rand_between(0,16) == 4+16*rand_between(0,1)

            double distanceScore = Math.Max(0, ZeroDistanceScore - PointsLostPerMile * _location.CalculateDistance(voter.Location, DistanceUnits.Miles));
            double classmatesScore = (voter.BirthDate - _mobilizer.BirthDate.GetValueOrDefault()).Days < 18 * 30 ? ClassmatesScore : 0;
            double housematesScore = _housemates.Contains(voter.ID!) ? HousematesScore : 0;
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
