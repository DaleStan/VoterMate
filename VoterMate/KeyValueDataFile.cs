using CsvHelper;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace VoterMate;

internal sealed class KeyValueDataFile : IDictionary<string, string>
{
    private readonly string _path, _keyName;
    private readonly Dictionary<string, string> _data = [];

    public KeyValueDataFile(string path, string keyName)
    {
        using (File.AppendText(path)) { }

        using var csv = new CsvReader(File.OpenText(path), CultureInfo.InvariantCulture);
        if (csv.Read())
        {
            csv.ReadHeader();
            int keyIndex = csv.GetFieldIndex(keyName);
            int valueIndex = csv.GetFieldIndex("Value");
            while (csv.Read())
                _data[csv.GetField(keyIndex)!] = (string)(object)csv.GetField(valueIndex)!;
        }

        _path = path;
        _keyName = keyName;
    }

    private void Save()
    {
        using CsvWriter csv = new(File.CreateText(_path), CultureInfo.InvariantCulture);
        csv.WriteField(_keyName);
        csv.WriteField("Value");
        csv.NextRecord();

        foreach (var (key, value) in _data)
        {
            csv.WriteField(key);
            csv.WriteField(value);
            csv.NextRecord();
        }
    }

    public string this[string key]
    {
        get => ((IDictionary<string, string>)_data)[key];
        set
        {
            ((IDictionary<string, string>)_data)[key] = value;
            Save();
        }
    }

    public ICollection<string> Keys => ((IDictionary<string, string>)_data).Keys;

    public ICollection<string> Values => ((IDictionary<string, string>)_data).Values;

    public int Count => ((ICollection<KeyValuePair<string, string>>)_data).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<string, string>>)_data).IsReadOnly;

    public void Add(string key, string value)
    {
        ((IDictionary<string, string>)_data).Add(key, value);
        Save();
    }

    public void Add(KeyValuePair<string, string> item)
    {
        ((ICollection<KeyValuePair<string, string>>)_data).Add(item);
        Save();
    }

    public void Clear()
    {
        ((ICollection<KeyValuePair<string, string>>)_data).Clear();
        Save();
    }

    public bool Contains(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)_data).Contains(item);

    public bool ContainsKey(string key) => ((IDictionary<string, string>)_data).ContainsKey(key);

    public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, string>>)_data).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, string>>)_data).GetEnumerator();

    public bool Remove(string key)
    {
        bool result = ((IDictionary<string, string>)_data).Remove(key);
        Save();
        return result;
    }

    public bool Remove(KeyValuePair<string, string> item)
    {
        bool result = ((ICollection<KeyValuePair<string, string>>)_data).Remove(item);
        Save();
        return result;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => ((IDictionary<string, string>)_data).TryGetValue(key, out value);

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_data).GetEnumerator();
}
