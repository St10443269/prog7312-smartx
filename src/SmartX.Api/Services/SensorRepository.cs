using System.Collections.Concurrent;
using SmartX.Api.RealTime;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// In-memory sensor store for the simulation environment described in the
/// brief. A singleton service (registered in <c>Program.cs</c>) rather than
/// a database, since Part 1 focuses on the ingestion API and dashboard, not
/// persistence.
/// </summary>
public sealed class SensorRepository : ISensorRepository
{
    private readonly ConcurrentDictionary<Guid, SensorRegistration> _sensors = new();

    // MAC address (in the standard format) -> sensor id. Checking whether a MAC
    // is already registered is then one lookup, instead of going through every
    // sensor each time someone registers a device.
    private readonly ConcurrentDictionary<string, Guid> _idByMac = new();

    public SensorRegistration Add(SensorRegistration sensor)
    {
        _sensors[sensor.Id] = sensor;
        _idByMac[DeviceRegistry.NormaliseMac(sensor.MacAddress)] = sensor.Id;
        return sensor;
    }

    public IReadOnlyList<SensorRegistration> GetAll() =>
        _sensors.Values.OrderByDescending(s => s.RegisteredAtUtc).ToList();

    public SensorRegistration? GetById(Guid id) =>
        _sensors.GetValueOrDefault(id);

    public bool MacAddressExists(string macAddress) =>
        _idByMac.ContainsKey(DeviceRegistry.NormaliseMac(macAddress));

    public SensorAttachment? AddAttachment(Guid sensorId, SensorAttachment attachment)
    {
        if (!_sensors.TryGetValue(sensorId, out var sensor))
        {
            return null;
        }

        // Attachments list belongs to a single sensor at a time, so a lock
        // around this one mutation is enough to keep concurrent uploads safe.
        lock (sensor.Attachments)
        {
            sensor.Attachments.Add(attachment);
        }

        return attachment;
    }
}
