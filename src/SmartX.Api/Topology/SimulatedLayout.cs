namespace SmartX.Api.Topology;

/// <summary>
/// SIMULATED DATA. There are no real ESP32 radios in this project, so nothing
/// here is a measurement. This file only decides where each device sits on the
/// diagram (in metres). The rest of the topology code works from these
/// positions: two nodes are linked when they are within radio range, and the
/// cost of a link is worked out from the distance between them. To use real
/// signal data later, only this file and MeshTopology.BuildCost need to change.
/// </summary>
public static class SimulatedLayout
{
    public const string GatewayId = "GATEWAY";

    /// <summary>Farthest two nodes can be and still link up (metres).</summary>
    public const double RadioRangeMetres = 30;

    public static (double X, double Y) GatewayPosition => (50, 35);

    // Fixed positions for the demo devices (MAC address -> position).
    // Laid out so some devices are too far from the gateway and must go
    // through other nodes: Moisture Sensor B, Moisture Sensor D, Smart Plug 2
    // and Relay R1 can only reach the gateway by relaying.
    private static readonly Dictionary<string, (double X, double Y)> DemoPositions = new()
    {
        ["24:6F:28:C1:00:01"] = (32, 38),   // ESP32 Controller Z1
        ["24:6F:28:A1:00:01"] = (30, 22),   // Pump A
        ["24:6F:28:B1:00:01"] = (12, 28),   // Moisture Sensor B
        ["24:6F:28:A2:00:01"] = (70, 22),   // Pump C
        ["24:6F:28:B2:00:01"] = (78, 12),   // Moisture Sensor D
        ["24:6F:28:D3:00:01"] = (60, 55),   // Smart Plug 1
        ["24:6F:28:D3:00:02"] = (80, 52),   // Smart Plug 2
        ["24:6F:28:E3:00:01"] = (92, 38),   // Relay R1
        ["24:6F:28:F3:00:01"] = (45, 58)    // Power Meter M1
    };

    /// <summary>
    /// Position for a device. Demo devices have fixed spots. Any other device
    /// (for example one registered on the Sensors page) is placed on a circle
    /// around the gateway, at an angle taken from its MAC address, so it
    /// always lands in the same place and within range of the gateway.
    /// </summary>
    public static (double X, double Y) PositionFor(string macAddress)
    {
        if (DemoPositions.TryGetValue(macAddress, out var fixedPosition))
        {
            return fixedPosition;
        }

        var sum = 0;
        foreach (var letter in macAddress)
        {
            sum += letter;
        }

        var angle = sum % 360 * Math.PI / 180;
        const double radius = 24;
        var (gx, gy) = GatewayPosition;
        return (gx + radius * Math.Cos(angle), gy + radius * Math.Sin(angle));
    }
}
