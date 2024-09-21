﻿using Microsoft.Maui.Devices.Sensors;
using OfficeOpenXml;
using System.Reflection;
using System.Text.RegularExpressions;
using VoterMate.Database;

internal static partial class Program
{
    private static void Main()
    {
        Dictionary<string, List<string>> housemates = [];
        // address -> (Location, List<voterID>)
        Dictionary<string, Household> households = [];
        // voterID -> Voter
        Dictionary<string, Voter> voters = [];
        HashSet<string> priorityVoters = [];

        var gitFolder = new DirectoryInfo(LibGit2Sharp.Repository.Discover(Environment.CurrentDirectory)).Parent!.FullName;
        Directory.SetCurrentDirectory(Path.Combine(Path.GetDirectoryName(gitFolder)!, "VoterMate\\VoterMate\\Database"));

        var files = Directory.GetFiles(".", "*.tsv");
        if (files.Length == 4 && files.Min(f => new FileInfo(f).LastWriteTime) > new FileInfo(typeof(Program).Assembly.Location).LastWriteTime)
        {
            return; // Already up to date;
        }

        ReadExcel(housemates, households, voters, priorityVoters);
        try
        {
            WriteTsv(housemates, households, voters, priorityVoters);
        }
        catch (IOException) { }
    }

    private static void ReadExcel(Dictionary<string, List<string>> housemates, Dictionary<string, Household> households, Dictionary<string, Voter> voters, HashSet<string> priorityVoters)
    {
        HashSet<string> mobilizers = [];

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoterMate.BuildDatabase.schema.xlsx");

        if (stream == null)
        {
            Console.Error.WriteLine("ERROR: Please download the latest voter database (schema.xlsx) and place it in the VoterMate.Database project folder before building.");
            Environment.Exit(-1);
        }
        using var workbook = new ExcelPackage(stream).Workbook;

        // Slurp the data out of the Excel file and into intermediate maps.

        var priorityDB = workbook.Worksheets["all voters we want to put on the list"];
        int rowCount = priorityDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
            priorityVoters.Add(priorityDB.Cells[i, 1].Value.ToString()!);


        var mobilizerDB = workbook.Worksheets["canvass"];
        rowCount = mobilizerDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string id = mobilizerDB.Cells[i, 1].Value.ToString()!;
            mobilizers.Add(id);

            string address = mobilizerDB.Cells[i, 3].Value.ToString()!;

            if (!households.TryGetValue(address, out var household))
            {
                Location location = new(Convert.ToDouble(mobilizerDB.Cells[i, 4].Value), Convert.ToDouble(mobilizerDB.Cells[i, 5].Value));
                household = households[address] = new(address, location, []);
            }
            household.Mobilizers.Add(new(id, string.Empty, null));
        }

        var voterDB = workbook.Worksheets["voterDB"];
        rowCount = voterDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string id = voterDB.Cells[i, 1].Value.ToString()!;
            // We don't need information on voters who are neither mobilizers nor priority.
            if (priorityVoters.Contains(id) || mobilizers.Contains(id))
            {
                _ = DateTime.TryParse(voterDB.Cells[i, 8].Value.ToString(), out var birthDate);
                Location location = new(Convert.ToDouble(voterDB.Cells[i, 4].Value), Convert.ToDouble(voterDB.Cells[i, 5].Value));
                voters[id] = new Voter(id, GetName(voterDB, i), location, birthDate);
            }
        }

        var housemateDB = workbook.Worksheets["households"];
        rowCount = housemateDB.Rows.Count();
        for (int i = 2; i <= rowCount; i++)
        {
            string? housemate1 = housemateDB.Cells[i, 1].Value?.ToString();
            string? housemate2 = housemateDB.Cells[i, 2].Value?.ToString();
            // We don't need information on voters who are neither mobilizers nor priority.
            if (housemate1 == null || housemate2 == null
                || (!priorityVoters.Contains(housemate1) && !mobilizers.Contains(housemate1))
                || (!priorityVoters.Contains(housemate2) && !mobilizers.Contains(housemate2)))
                continue;

            if (!housemates.TryGetValue(housemate1, out var housemates1)) { housemates[housemate1] = housemates1 = []; }
            if (!housemates.TryGetValue(housemate1, out var housemates2)) { housemates[housemate2] = housemates2 = []; }
            housemates1.Add(housemate2);
            housemates2.Add(housemate1);
        }

        static string GetName(ExcelWorksheet sheet, int i) => NameFilter().Match(sheet.Cells[i, 2].Value.ToString()!).Groups[1].Value;
    }

    private static void WriteTsv(Dictionary<string, List<string>> housemates, Dictionary<string, Household> households, Dictionary<string, Voter> voters, HashSet<string> priorityVoters)
    {
        using (StreamWriter housematesSteam = new("housemates.tsv") { NewLine = "\n" })
            foreach (var (key, value) in housemates)
                if (value.Count > 0)
                    housematesSteam.WriteLine(key + "\t" + string.Join('\t', value));

        using (StreamWriter householdsStream = new("households.tsv") { NewLine = "\n" })
            foreach (var household in households.Values)
                household.SaveTo(householdsStream);

        using (StreamWriter votersStream = new("voters.tsv") { NewLine = "\n" })
            foreach (var voter in voters.Values)
                voter.SaveTo(votersStream);

        using (StreamWriter priorityVotersStream = new("priorityVoters.tsv") { NewLine = "\n" })
            foreach (var voter in priorityVoters)
                priorityVotersStream.WriteLine(voter);
    }



    [GeneratedRegex(@"^([^[]*)\s+\[")]
    private static partial Regex NameFilter();
}

