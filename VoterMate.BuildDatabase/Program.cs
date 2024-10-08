﻿using Microsoft.Maui.Devices.Sensors;
using OfficeOpenXml;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using VoterMate.Database;

namespace VoterMate.BuildDatabase;

internal static partial class Program
{
    private static string[] args = null!;

    private static void Main(string[] args)
    {
        Program.args = args;

        if (args.Length > 0 && File.Exists(args[0]))
            args[0] = Path.Combine(Environment.CurrentDirectory, args[0]);

        Dictionary<string, HashSet<string>> housemates = [];
        // address -> (Location, List<voterID>)
        Dictionary<string, Household> households = [];
        Dictionary<string, List<string>> turfs = [];
        // voterID -> Voter
        Dictionary<string, Voter> voters = [];
        Dictionary<string, List<string>> nicknames = new(StringComparer.InvariantCultureIgnoreCase);
        HashSet<string> priorityVoters = [];

        var dotGitFolder = LibGit2Sharp.Repository.Discover(Environment.CurrentDirectory);
        var rootFolder = dotGitFolder == null ? "." : new DirectoryInfo(dotGitFolder).Parent!.FullName;

        if (ReadExcel(housemates, households, voters, turfs, priorityVoters, nicknames) && dotGitFolder != null)
        {
            Directory.SetCurrentDirectory(Path.Combine(rootFolder, "VoterMate\\Database"));
            WriteTsv(housemates, voters, priorityVoters, nicknames);
            File.WriteAllText("voterDataDate.tsv", GetBuildInfo() ?? "Voter data date/time unknown");
        }

        Directory.CreateDirectory(Path.Combine(rootFolder, "VoterMate\\TurfFiles"));
        Directory.SetCurrentDirectory(Path.Combine(rootFolder, "VoterMate\\TurfFiles"));
        SortTurfs(households, turfs);
        WriteTurfs(turfs);
    }

    public static string? GetBuildInfo()
    {
        const string BuildVersionMetadataPrefix = "+build";

        var attribute = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attribute?.InformationalVersion != null)
        {
            var value = attribute.InformationalVersion;
            var index = value.IndexOf(BuildVersionMetadataPrefix);
            if (index > 0)
            {
                value = value[(index + BuildVersionMetadataPrefix.Length)..];
                return value;
            }
        }
        return default;
    }


    private static bool ReadExcel(Dictionary<string, HashSet<string>> housemates, Dictionary<string, Household> households, Dictionary<string, Voter> voters, Dictionary<string, List<string>> turfs, HashSet<string> priorityVoters, Dictionary<string, List<string>> nicknames)
    {
        bool result = true;
        HashSet<string> mobilizers = [];

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoterMate.BuildDatabase.schema.xlsx");

        if (args.Length > 0 && File.Exists(args[0]))
        {
            stream?.Dispose();
            stream = File.Open(args[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            result = false;
        }

        if (stream == null)
        {
            Console.Error.WriteLine("ERROR: Please download the latest voter database (schema.xlsx) and place it in the VoterMate.BuildDatabase project folder before building.");
            Environment.Exit(-1);
        }
        using var _1 = stream;
        using var workbook = new ExcelPackage(stream).Workbook;

        // Slurp the data out of the Excel file and into intermediate maps.

        var mobilizerDB = workbook.Worksheets["canvass"];
        int rowCount = mobilizerDB.Dimension.Rows;
        for (int i = 2; i <= rowCount; i++)
        {
            string id = mobilizerDB.Cells[i, 1].Value.ToString()!;
            mobilizers.Add(id);

            string address = mobilizerDB.Cells[i, 3].Value.ToString()!;

            if (!households.TryGetValue(address, out var household))
            {
                Location location = new(Convert.ToDouble(mobilizerDB.Cells[i, 6].Value), Convert.ToDouble(mobilizerDB.Cells[i, 7].Value));
                household = households[address] = new(address, location, []);
            }
            household.Mobilizers.Add(new(id, string.Empty, null));

            string turfID = mobilizerDB.Cells[i, 8].Value?.ToString() ?? "";
            if (!turfs.TryGetValue(turfID, out var turf))
                turf = turfs[turfID] = [];
            turf.Add(address);
        }

        var voterDB = workbook.Worksheets["voterDB"];
        rowCount = voterDB.Dimension.Rows;
        for (int i = 2; i <= rowCount; i++)
        {
            string? id = voterDB.Cells[i, 1].Value?.ToString();
            if (id == null) continue;
            _ = DateTime.TryParse(voterDB.Cells[i, 8].Value.ToString(), out var birthDate);
            Location location = new(Convert.ToDouble(voterDB.Cells[i, 4].Value), Convert.ToDouble(voterDB.Cells[i, 5].Value));
            voters[id] = new Voter(id, GetName(voterDB, i), voterDB.Cells[i, 2].Value.ToString()!.Trim(), location, birthDate, voterDB.Cells[i, 3].Value.ToString()!.Trim());
        }

        var housemateDB = workbook.Worksheets["households"];
        rowCount = housemateDB.Dimension.Rows;
        for (int i = 2; i <= rowCount; i++)
        {
            string? housemate1 = housemateDB.Cells[i, 1].Value?.ToString();
            string? housemate2 = housemateDB.Cells[i, 2].Value?.ToString();
            if (housemate1 == null || housemate2 == null)
                continue;

            if (!housemates.TryGetValue(housemate1, out var housemates1)) { housemates[housemate1] = housemates1 = []; }
            if (!housemates.TryGetValue(housemate2, out var housemates2)) { housemates[housemate2] = housemates2 = []; }
            housemates1.Add(housemate2);
            housemates2.Add(housemate1);
        }

        var priorityDB = workbook.Worksheets["all voters we want to put on the list"];
        rowCount = priorityDB.Dimension.Rows;
        for (int i = 2; i <= rowCount; i++)
        {
            var id = priorityDB.Cells[i, 1].Value?.ToString();
            if (id != null && voters.ContainsKey(id))
                priorityVoters.Add(id);
            else if (id != null)
                Console.WriteLine($"WARNING: Skipping priority voter {id} ({priorityDB.Cells[i, 2].Value}) because they do not appear on the 'voterDB' tab.");
        }

        TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
        var nicknameDB = workbook.Worksheets["allNicknames"];
        rowCount = nicknameDB.Dimension.Rows;
        for (int i = 2; i <= rowCount; i++)
            if (nicknameDB.Cells[i, 1].Value != null && nicknameDB.Cells[i, 2].Value != null)
            {
                string name = textInfo.ToTitleCase(nicknameDB.Cells[i, 2].Value.ToString()!.Trim().ToLower());
                string alternate = textInfo.ToTitleCase(nicknameDB.Cells[i, 1].Value.ToString()!.Trim().ToLower());
                if (!nicknames.TryGetValue(name!, out var alternates))
                    nicknames[name!] = alternates = [name];
                alternates.Add(alternate);
            }

        return result;

        static string GetName(ExcelWorksheet sheet, int i) => NameFilter().Match(sheet.Cells[i, 2].Value.ToString()!).Groups[1].Value;
    }

    private static void WriteTsv(Dictionary<string, HashSet<string>> housemates, Dictionary<string, Voter> voters, HashSet<string> priorityVoters, Dictionary<string, List<string>> nicknames)
    {
        using (StreamWriter housematesSteam = new("housemates.tsv") { NewLine = "\n" })
            foreach (var (key, value) in housemates)
                if (value.Count > 0)
                    housematesSteam.WriteLine(key + "\t" + string.Join('\t', value));

        using (StreamWriter votersStream = new("voters.tsv") { NewLine = "\n" })
            foreach (var voter in voters.Values)
                voter.SaveTo(votersStream);

        using (StreamWriter priorityVotersStream = new("priorityVoters.tsv") { NewLine = "\n" })
            foreach (var voter in priorityVoters)
                priorityVotersStream.WriteLine(voter);

        Dictionary<string, HashSet<string>> lookupDb = new(StringComparer.InvariantCultureIgnoreCase);
        HashSet<string> suffixes = new(["Jr", "Sr", "Ii", "Iv"], StringComparer.InvariantCultureIgnoreCase);
        HashSet<string> exclusions = new(["ave", "cir", "blvd", "st", "rd", "dr", "Apt"], StringComparer.InvariantCultureIgnoreCase);

        foreach (var voter in voters.Values)
        {
            var match = NameAgeAddressSplit().Match(voter.NameAgeAddress);
            if (match.Success)
            {
                List<string> parts = [.. match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), .. match.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

                if (parts[0].Length < 3)
                    parts[0] = parts[0] + ' ' + parts[1];
                if (match.Groups[3].Value.Contains("st rt")) parts.Add("state route");

                var stringParts = parts.Where(p => p.Length >= 2).Except(suffixes).Except(exclusions).ToList();
                var suffixParts = suffixes.Intersect(parts, StringComparer.InvariantCultureIgnoreCase).ToList();
                var numberParts = parts.Where(p => int.TryParse(p, out int num) && num > 9).ToList();
                numberParts.Add(match.Groups[2].Value[1..^1]);

                foreach (var item1 in stringParts.Union(suffixParts).Union(numberParts))
                {
                    if (!nicknames.TryGetValue(item1, out var alternates))
                        alternates = [item1];

                    foreach (var item in alternates)
                    {
                        string cleaned = item;
                        while (cleaned[0] is '0' or ' ' or '#')
                            cleaned = cleaned[1..];
                        if (cleaned.Length < 2) continue;

                        if (!lookupDb.TryGetValue(cleaned, out var idList)) lookupDb[item] = idList = [];
                        idList.Add(voter.ID);
                    }
                }
            }
        }

        using (StreamWriter lookupDbStream = new("lookupDb.tsv") { NewLine = "\n" })
            foreach (var (key, value) in lookupDb.OrderBy(kvp => kvp.Key.Length).ThenBy(kvp => kvp.Key))
                lookupDbStream.WriteLine(key + "\t" + string.Join('\t', value));
    }

    private static void WriteTurfs(Dictionary<string, List<string>> turfs)
    {
        foreach (var (id, turf) in turfs)
        {
            try
            {
                File.WriteAllLines(id + ".txt", [.. turf]);
            }
            catch (IOException)
            {
                Console.WriteLine($"ERROR: Could not create turf file for turf ID '{id}'.");
            }
        }
    }

    [GeneratedRegex(@"^([^[]*?)\s+\[")]
    private static partial Regex NameFilter();

    [GeneratedRegex(@"^(.*?)\s+(\[\d+])\s+(.*)$")]
    private static partial Regex NameAgeAddressSplit();
}

