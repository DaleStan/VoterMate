using Microsoft.Maui.Devices.Sensors;
using System.Text.RegularExpressions;
using VoterMate.Database;

namespace VoterMate.BuildDatabase;

internal static partial class Program
{
    private static void SortTurfs(Dictionary<string, Household> households, Dictionary<string, List<string>> turfs)
    {
        foreach (var turf in turfs.Values)
            SortTurf(households, turf);
    }

    private static void SortTurf(Dictionary<string, Household> households, List<string> turf)
    {
        var keyedAddresses = turf.GroupBy(MakeAddressKey).ToDictionary(g => g.Key, g => g.Distinct().Order().ToList());
        Dictionary<Location, (Location, string)> locationPairs = [];

        foreach (var (key, addresses) in keyedAddresses)
        {
            var loc1 = households[addresses[0]].Location;
            var loc2 = households[addresses[^1]].Location;
            locationPairs[loc1] = (loc2, key);
            locationPairs[loc2] = (loc1, key);
        }

        Location startLocation = new(40.7580, -82.5151); // Office location

        List<(Location, Location)> outputOrder = (
#if DEBUG
            System.Diagnostics.Debugger.IsAttached ? Enumerable.Range(0, 1000).Select(selector) :
#endif
            ParallelEnumerable.Range(0, 1000).Select(selector)).MinBy(x => x.testLength).testOrder;

        (double testLength, List<(Location, Location)> testOrder) selector(int _)
        {
            List<(Location, Location)> testOrder = [(new(), startLocation), (startLocation, new())];

            var array = locationPairs.Keys.ToArray();
            Random.Shared.Shuffle(array);
            var locationList = array.ToList();
            while (locationList.Count > 0)
            {
                int bestPosition = 0;
                double bestLength = double.MaxValue;
                for (int j = 1; j < testOrder.Count; j++)
                {
                    if (testOrder[j - 1].Item2.CalculateDistance(locationList[0], DistanceUnits.Kilometers)
                        < testOrder[j - 1].Item2.CalculateDistance(locationPairs[locationList[0]].Item1, DistanceUnits.Kilometers))
                        testOrder.Insert(j, (locationList[0], locationPairs[locationList[0]].Item1));
                    else
                        testOrder.Insert(j, (locationPairs[locationList[0]].Item1, locationList[0]));

                    double newLength = MeasureLength(testOrder);
                    if (newLength < bestLength)
                    {
                        bestLength = newLength;
                        bestPosition = j;
                    }
                    testOrder.RemoveAt(j);
                }
                if (testOrder[bestPosition - 1].Item2.CalculateDistance(locationList[0], DistanceUnits.Kilometers)
                    < testOrder[bestPosition - 1].Item2.CalculateDistance(locationPairs[locationList[0]].Item1, DistanceUnits.Kilometers))
                    testOrder.Insert(bestPosition, (locationList[0], locationPairs[locationList[0]].Item1));
                else
                    testOrder.Insert(bestPosition, (locationPairs[locationList[0]].Item1, locationList[0]));

                if (locationPairs[locationList[0]].Item1 != locationList[0])
                    locationList.Remove(locationPairs[locationList[0]].Item1);
                locationList.Remove(locationList[0]);
            }

            double testLength = MeasureLength(testOrder);
            bool repeat = false;
            while (repeat)
            {
                repeat = false;
                for (int j = 1; j < testOrder.Count - 1; j++)
                {
                    testOrder[j] = (testOrder[j].Item2, testOrder[j].Item1);
                    var newLength = MeasureLength(testOrder);
                    if (newLength < testLength)
                    {
                        repeat = true;
                        testLength = newLength;
                    }
                    else
                        testOrder[j] = (testOrder[j].Item2, testOrder[j].Item1);
                }

                for (int j = 1; j < testOrder.Count - 2; j++)
                {
                    (testOrder[j], testOrder[j + 1]) = (testOrder[j + 1], testOrder[j]);
                    var newLength = MeasureLength(testOrder);
                    if (newLength < testLength)
                    {
                        repeat = true;
                        testLength = newLength;
                    }
                    else
                        (testOrder[j], testOrder[j + 1]) = (testOrder[j + 1], testOrder[j]);
                }
            }

            return (testLength, testOrder);
        }

        turf.Clear();

        foreach (var (start, _) in outputOrder[1..^1])
        {
            var addressKey = locationPairs[start].Item2;
            var addresses = keyedAddresses[addressKey];
            if (households[addresses[0]].Location == start)
                turf.AddRange(addresses);
            else
                turf.AddRange(addresses.AsEnumerable().Reverse());
        }

        static string MakeAddressKey(string address)
        {
            var parts = SplitAddress().Match(address);
            if (parts.Success && int.TryParse(parts.Groups[1].Value, out int number))
            {
                return number / 100 + ((number & 1) == 0 ? "E" : "O") + parts.Groups[3].Value;
            }

            Console.WriteLine($"WARNING: Could not parse address '{address}'");
            return address;
        }
        
        static double MeasureLength(List<(Location, Location)> route)
        {
            var flattened = Flatten(route).ToList();
            return flattened.Zip(flattened[1..]).Sum(x => x.First.CalculateDistance(x.Second, DistanceUnits.Kilometers));

        }

        static IEnumerable<Location> Flatten(List<(Location, Location)> route)
        {
            yield return route[0].Item2;
            for (var i = 1; i < route.Count - 1; i++)
            {
                yield return route[i].Item1;
                yield return route[i].Item2;
            }
            yield return route[^1].Item1;
        }
    }


    [GeneratedRegex(@"(\d+)( 1/2)? ([A-Z0-9 ]{4,}?) ")]
    private static partial Regex SplitAddress();
}
