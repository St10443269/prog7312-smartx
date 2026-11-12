using SmartX.Api.Services;
using SmartX.Shared.Enums;
using SmartX.Shared.Models;

namespace SmartX.Tests;

/// <summary>The Part 1 sensor store: finding a registered MAC address with one dictionary lookup.</summary>
public class SensorRepositoryTests
{
    private static SensorRegistration Sensor(string mac, string node = "Node") => new()
    {
        MacAddress = mac,
        FacilityName = "Facility A",
        ZoneName = "Zone 1",
        NodeId = node,
        Category = SensorCategory.Environmental
    };

    [Fact]
    public void A_registered_mac_is_found_whatever_the_case_or_separator()
    {
        var repo = new SensorRepository();
        repo.Add(Sensor("24:6F:28:AA:11:01"));

        Assert.True(repo.MacAddressExists("24:6F:28:AA:11:01"));
        Assert.True(repo.MacAddressExists("24:6f:28:aa:11:01"));
        Assert.True(repo.MacAddressExists("24-6F-28-AA-11-01"));
    }

    [Fact]
    public void An_unknown_mac_is_not_found()
    {
        var repo = new SensorRepository();
        repo.Add(Sensor("24:6F:28:AA:11:01"));

        Assert.False(repo.MacAddressExists("24:6F:28:AA:11:02"));
        Assert.False(new SensorRepository().MacAddressExists("24:6F:28:AA:11:01"));
    }

    [Fact]
    public void Lookups_still_work_with_many_sensors_registered()
    {
        var repo = new SensorRepository();
        for (var i = 0; i < 2000; i++)
        {
            repo.Add(Sensor($"AA:BB:CC:DD:{i / 256:X2}:{i % 256:X2}", $"Node {i}"));
        }

        Assert.True(repo.MacAddressExists("AA:BB:CC:DD:07:CF")); // the 1999th one
        Assert.False(repo.MacAddressExists("AA:BB:CC:DD:08:00")); // one past the end
        Assert.Equal(2000, repo.GetAll().Count);
    }

    [Fact]
    public void A_sensor_can_still_be_found_by_its_id()
    {
        var repo = new SensorRepository();
        var added = repo.Add(Sensor("24:6F:28:AA:11:01"));

        Assert.Same(added, repo.GetById(added.Id));
        Assert.Null(repo.GetById(Guid.NewGuid()));
    }
}
