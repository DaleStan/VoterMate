using CsvHelper;
using System.Globalization;
using System.Reflection;

namespace VoterMate;

internal class MultiMapDataFile<TRecord>
{
    private readonly Dictionary<string, List<TRecord>> _data = [];
    private readonly string _path;
    private readonly PropertyInfo _keyProperty;

    public MultiMapDataFile(string path, string keyPropertyName)
    {
        _path = path;
        _keyProperty = typeof(TRecord).GetProperty(keyPropertyName)!;
        if (_keyProperty?.PropertyType != typeof(string))
            throw new ArgumentException($"Could not find string-valued property '{keyPropertyName}' in {typeof(TRecord).Name}.", nameof(keyPropertyName));

        try
        {
            using var csv = new CsvReader(File.OpenText(path), CultureInfo.InvariantCulture);
            if (csv.Read())
            {
                csv.ReadHeader();
                foreach (var record in csv.GetRecords<TRecord>())
                    AddValueInternal(record);
            }
        }
        catch
        {
            using CsvWriter csv = new(File.CreateText(_path), CultureInfo.InvariantCulture);
            csv.WriteHeader<TRecord>();
            csv.NextRecord();
        }
    }

    public bool ContainsKey(string key) => _data.ContainsKey(key);

    public void AddValue(TRecord record)
    {
        AddValueInternal(record);
        using CsvWriter csv = new(File.AppendText(_path), CultureInfo.InvariantCulture);
        csv.WriteRecord(record);
        csv.NextRecord();
    }

    private void AddValueInternal(TRecord record)
    {
        string key = _keyProperty.GetValue(record) as string ?? throw new ArgumentException("New record must not contain a null key.", nameof(record));
        if (!_data.TryGetValue(key, out var values))
            _data[key] = values = [];
        values.Add(record);
    }
}