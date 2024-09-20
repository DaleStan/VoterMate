using OfficeOpenXml;
using System.Text.RegularExpressions;

namespace VoterMate.Database;

internal partial class ExcelDatabase : IDatabase
{
    // voterID -> List<voterID>
    private readonly Dictionary<string, List<string>> _housemates = [];
    // address -> (Location, List<voterID>)
    private readonly Dictionary<string, Household> _households = [];
    // voterID -> Mobilizer
    private readonly Dictionary<string, Mobilizer> _mobilizers = [];
    // voterID -> Voter
    private readonly Dictionary<string, Voter> _voters = [];
    private readonly HashSet<string> _priorityVoters = [];

    public ExcelDatabase()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using Stream stream = typeof(ExcelDatabase).Assembly.GetManifestResourceStream("VoterMate.Resources.schema.xlsx")!;
        using var workbook = new ExcelPackage(stream).Workbook;

        // Slurp the data out of the excel file and into intermediate maps.

        var housemateDB = workbook.Worksheets["households"];
        int rowCount = housemateDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string? housemate1 = housemateDB.Cells[i, 1].Value?.ToString();
            string? housemate2 = housemateDB.Cells[i, 2].Value?.ToString();
            if (housemate1 == null || housemate2 == null)
                continue;

            if (!_housemates.TryGetValue(housemate1, out var housemates1)) { _housemates[housemate1] = housemates1 = []; }
            if (!_housemates.TryGetValue(housemate1, out var housemates2)) { _housemates[housemate2] = housemates2 = []; }
            housemates1.Add(housemate2);
            housemates2.Add(housemate1);
        }

        var voterDB = workbook.Worksheets["voterDB"];
        rowCount = voterDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string id = voterDB.Cells[i, 1].Value.ToString()!;
            _ = DateTime.TryParse(voterDB.Cells[i, 8].Value.ToString(), out var birthDate);
            Location location = new(Convert.ToDouble(voterDB.Cells[i, 4].Value), Convert.ToDouble(voterDB.Cells[i, 5].Value));
            _voters[id] = new Voter(GetName(voterDB, i), id, location, birthDate);
        }

        var priorityDB = workbook.Worksheets["all voters we want to put on the list"];
        rowCount = priorityDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
            _priorityVoters.Add(priorityDB.Cells[i, 1].Value.ToString()!);

        var mobilizerDB = workbook.Worksheets["canvass"];
        rowCount = mobilizerDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string id = mobilizerDB.Cells[i, 1].Value.ToString()!;
            _mobilizers[id] = new Mobilizer(_voters[id].Name, id, _voters[id].BirthDate);

            string address = mobilizerDB.Cells[i, 3].Value.ToString()!;

            if (!_households.TryGetValue(address, out var household))
            {
                Location location = new(Convert.ToDouble(mobilizerDB.Cells[i, 4].Value), Convert.ToDouble(mobilizerDB.Cells[i, 5].Value));
                household = _households[address] = new(location, address, []);
            }
            household.Mobilizers.Add(_mobilizers[id]);
        }

        static string GetName(ExcelWorksheet sheet, int i) => NameFilter().Match(sheet.Cells[i, 2].Value.ToString()!).Groups[1].Value;
    }

    public IEnumerable<Household> GetHouseholds() => _households.Values;

    public IEnumerable<Voter> GetVoters(Location location, Mobilizer mobilizer)
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

    [GeneratedRegex(@"^([^[]*)\s+\[")]
    private static partial Regex NameFilter();
}
