using CsvHelper;
using System.Globalization;

namespace VoterMate;

internal sealed partial class OutputFile<TRecord>
{
    private readonly string path;

    public OutputFile(string path)
    {
        if (!File.Exists(path))
            using (CsvWriter csv = new(new StreamWriter(path), CultureInfo.InvariantCulture))
            {
                csv.WriteHeader<TRecord>();
                csv.NextRecord();
            }

        this.path = path;
    }

    public void Append(TRecord record) => Append([record]);

    public void Append(IEnumerable<TRecord> records)
    {
        using CsvWriter csv = new(new StreamWriter(path, true), CultureInfo.InvariantCulture);
        foreach (TRecord record in records)
        {
            csv.WriteRecord(record);
            csv.NextRecord();
        }
    }
}
