using SmartX.Api.RealTime;

namespace SmartX.Api.Topology;

/// <summary>One historical sensor reading.</summary>
public sealed record TelemetryReading(string MacAddress, string SensorType, DateTimeOffset Timestamp, double Value);

/// <summary>
/// Historical readings, one AVL tree per sensor, keyed by timestamp. Asking
/// for "Sensor A between 14:00 and 16:00" only walks that part of Sensor A's
/// tree instead of scanning every reading of every sensor.
/// </summary>
public sealed class ReadingHistory(RealTimeOptions options)
{
    private readonly Dictionary<string, AvlTree<DateTimeOffset, TelemetryReading>> _bySensor = new();
    private readonly object _sync = new();

    public void Add(TelemetryReading reading)
    {
        var mac = DeviceRegistry.NormaliseMac(reading.MacAddress);

        lock (_sync)
        {
            if (!_bySensor.TryGetValue(mac, out var tree))
            {
                tree = new AvlTree<DateTimeOffset, TelemetryReading>();
                _bySensor[mac] = tree;
            }

            tree.Insert(reading.Timestamp, reading);

            // keep memory under control by dropping the oldest readings
            while (tree.Count > options.Processing.MaxReadingsPerSensor && tree.RemoveSmallest())
            {
            }
        }
    }

    /// <summary>Readings for one sensor between two times (inclusive), oldest first.</summary>
    public (List<TelemetryReading> Readings, int NodesVisited, int TotalStored) Query(string macAddress, DateTimeOffset from, DateTimeOffset to)
    {
        var mac = DeviceRegistry.NormaliseMac(macAddress);

        lock (_sync)
        {
            if (!_bySensor.TryGetValue(mac, out var tree))
            {
                return ([], 0, 0);
            }

            var result = tree.RangeSearch(from, to);
            return (result.Items, result.NodesVisited, tree.Count);
        }
    }

    public int CountFor(string macAddress)
    {
        lock (_sync)
        {
            return _bySensor.TryGetValue(DeviceRegistry.NormaliseMac(macAddress), out var tree) ? tree.Count : 0;
        }
    }
}
