using SmartX.Api.RealTime;
using SmartX.Api.Topology;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>
/// The topology service on top of the real device list from Part 2.
/// With the four test devices and the demo layout: Pump A, Smart Plug 1 and
/// Power Meter M1 are in range of the gateway, but Moisture Sensor B is too
/// far away and can only reach the gateway through Pump A.
/// </summary>
public class MeshTopologyTests
{
    private const string Gateway = MeshTopology.GatewayId;

    /// <summary>Sends one reading from every device except Pump A, then checks for silent devices, so only Pump A goes offline.</summary>
    private static void TakePumpOffline(TestNetwork net)
    {
        net.Clock.Advance(TimeSpan.FromSeconds(net.Options.Thresholds.DisconnectAfterSeconds + 1));
        net.SendAndProcess(TestNetwork.MoistureMac, 45);
        net.SendAndProcess(TestNetwork.PlugMac, 150);
        net.SendAndProcess(TestNetwork.MeterMac, 400);
        net.Processor.SweepDisconnected();
        Assert.False(net.Device(TestNetwork.PumpMac).IsConnected);
    }

    [Fact]
    public void Topology_has_the_gateway_and_one_node_per_device()
    {
        var net = new TestNetwork().WithDemoDevices();

        var topology = net.Topology.GetTopology();

        Assert.Equal(5, topology.Nodes.Count);
        Assert.True(topology.Nodes[0].IsGateway);
        Assert.Equal(4, topology.OnlineCount);
        Assert.Equal(0, topology.OfflineCount);
        Assert.Equal(0, topology.CutOffCount);
        Assert.True(topology.IsSimulated);
        Assert.All(topology.Nodes, n => Assert.True(n.CanReachGateway));
    }

    [Fact]
    public void Links_come_from_radio_range_and_costs_grow_with_distance()
    {
        var net = new TestNetwork().WithDemoDevices();

        var topology = net.Topology.GetTopology();

        // no link may be longer than the radio range, and none to a node out of range
        Assert.DoesNotContain(topology.Links, l => l.A == TestNetwork.MoistureMac && l.B == Gateway);
        Assert.DoesNotContain(topology.Links, l => l.B == TestNetwork.MoistureMac && l.A == Gateway);
        Assert.Contains(topology.Links, l => new[] { l.A, l.B }.Contains(TestNetwork.MoistureMac) && new[] { l.A, l.B }.Contains(TestNetwork.PumpMac));
        Assert.All(topology.Links, l => Assert.True(l.Cost > 0));

        // the meter is farther from the gateway than the plug, so its link costs more
        double CostOf(string device) => topology.Links.Single(l => new[] { l.A, l.B }.Contains(device) && new[] { l.A, l.B }.Contains(Gateway)).Cost;
        Assert.True(CostOf(TestNetwork.MeterMac) > CostOf(TestNetwork.PlugMac));
    }

    [Fact]
    public void A_far_away_sensor_reaches_the_gateway_through_another_node()
    {
        var net = new TestNetwork().WithDemoDevices();

        var route = net.Topology.GetRoute(TestNetwork.MoistureMac)!;

        Assert.True(route.CanReachGateway);
        Assert.Equal([TestNetwork.MoistureMac, TestNetwork.PumpMac, Gateway], route.FewestHopsPath);
        Assert.Contains("2 hop", route.Message);
        Assert.Equal(2, net.Topology.GetTopology().Nodes.Single(n => n.Id == TestNetwork.MoistureMac).HopsToGateway);
    }

    [Fact]
    public void When_the_relay_goes_offline_the_sensor_behind_it_is_cut_off_with_a_clear_message()
    {
        var net = new TestNetwork().WithDemoDevices();
        TakePumpOffline(net);

        var topology = net.Topology.GetTopology();
        var route = net.Topology.GetRoute(TestNetwork.MoistureMac)!;

        Assert.Equal(1, topology.OfflineCount);
        Assert.Equal(1, topology.CutOffCount);   // Moisture Sensor B: online, but no way through
        Assert.False(route.CanReachGateway);
        Assert.Empty(route.FewestHopsPath);
        Assert.Empty(route.TreePath);
        Assert.Contains("no route", route.Message);

        var pumpRoute = net.Topology.GetRoute(TestNetwork.PumpMac)!;
        Assert.False(pumpRoute.CanReachGateway);
        Assert.Contains("offline", pumpRoute.Message);
    }

    [Fact]
    public void The_route_comes_back_when_the_device_reconnects()
    {
        var net = new TestNetwork().WithDemoDevices();
        TakePumpOffline(net);

        net.SendAndProcess(TestNetwork.PumpMac, 0); // the pump reports again

        Assert.True(net.Topology.GetRoute(TestNetwork.MoistureMac)!.CanReachGateway);
        Assert.Equal(0, net.Topology.GetTopology().CutOffCount);
    }

    [Fact]
    public void Switching_off_a_link_changes_the_routes_and_switching_it_on_restores_them()
    {
        var net = new TestNetwork().WithDemoDevices();

        var link = net.Topology.SetLink(Gateway, TestNetwork.PumpMac, false);

        Assert.NotNull(link);
        Assert.False(link!.IsUp);
        Assert.False(link.IsUsable);
        // Pump A's only way to the gateway was that link, and B went through Pump A
        Assert.Equal(2, net.Topology.GetTopology().CutOffCount);

        net.Topology.SetLink(TestNetwork.PumpMac, Gateway, true);
        Assert.Equal(0, net.Topology.GetTopology().CutOffCount);

        Assert.Null(net.Topology.SetLink(TestNetwork.MoistureMac, Gateway, false)); // no such link
    }

    [Fact]
    public void Spanning_tree_never_costs_more_than_all_the_links_together_and_changes_when_a_link_fails()
    {
        var net = new TestNetwork().WithDemoDevices();

        var mst = net.Topology.GetMinimumSpanningTree();

        // gateway + 4 devices = 5 nodes, so the tree has 4 links
        Assert.Equal(4, mst.Links.Count);
        Assert.Equal(mst.Links.Sum(l => l.Cost), mst.TotalCost, 1);
        Assert.True(mst.TotalCost <= mst.AllLinksCost);
        Assert.Empty(mst.NotInTree);

        net.Topology.SetLink(Gateway, TestNetwork.PlugMac, false);
        var afterFailure = net.Topology.GetMinimumSpanningTree();

        // the plug is now reached through the meter, so the tree changes but still covers everything
        Assert.Equal(4, afterFailure.Links.Count);
        Assert.NotEqual(mst.TotalCost, afterFailure.TotalCost);
        Assert.Empty(afterFailure.NotInTree);
    }

    [Fact]
    public void Spanning_tree_lists_the_nodes_it_could_not_include()
    {
        var net = new TestNetwork().WithDemoDevices();
        TakePumpOffline(net);

        var mst = net.Topology.GetMinimumSpanningTree();

        Assert.Contains(TestNetwork.PumpMac, mst.NotInTree);
        Assert.Contains(TestNetwork.MoistureMac, mst.NotInTree);
        Assert.Equal(2, mst.Links.Count);   // gateway-plug and gateway-meter or plug-meter
    }

    [Fact]
    public void Bfs_from_the_gateway_lists_reachable_and_unreachable_devices()
    {
        var net = new TestNetwork().WithDemoDevices();
        TakePumpOffline(net);

        var bfs = net.Topology.RunBfs(null)!;

        Assert.Equal(Gateway, bfs.StartId);
        Assert.Equal(Gateway, bfs.Steps[0].NodeId);
        Assert.Contains(TestNetwork.PlugMac, bfs.ReachableIds);
        Assert.Contains(TestNetwork.PumpMac, bfs.UnreachableIds);
        Assert.Contains(TestNetwork.MoistureMac, bfs.UnreachableIds);
        Assert.Equal(bfs.Steps.Count, bfs.ReachableIds.Count);
        Assert.Null(net.Topology.RunBfs("24:6F:28:00:00:99"));
    }

    [Fact]
    public void Bfs_from_a_sensor_shows_what_that_sensor_can_reach()
    {
        var net = new TestNetwork().WithDemoDevices();

        var bfs = net.Topology.RunBfs(TestNetwork.MoistureMac)!;

        Assert.Equal(TestNetwork.MoistureMac, bfs.StartId);
        Assert.Contains(Gateway, bfs.ReachableIds);
        Assert.Equal(1, bfs.Steps.Single(s => s.NodeId == TestNetwork.PumpMac).Level);
        Assert.Equal(2, bfs.Steps.Single(s => s.NodeId == Gateway).Level);
    }

    [Fact]
    public void A_device_registered_later_appears_in_the_network_and_gets_linked()
    {
        var net = new TestNetwork().WithDemoDevices();
        Assert.Equal(5, net.Topology.GetTopology().Nodes.Count);

        net.AddDevice("24:6F:28:99:00:01", "New Sensor", DeviceKind.MoistureSensor, "Zone 4");
        var topology = net.Topology.GetTopology();

        Assert.Equal(6, topology.Nodes.Count);
        Assert.Contains(topology.Nodes, n => n.Name == "New Sensor");
        var route = net.Topology.GetRoute("24:6F:28:99:00:01")!;
        Assert.True(route.CanReachGateway); // new devices are placed within range of the gateway
    }

    [Fact]
    public void Network_with_no_devices_only_has_the_gateway()
    {
        var net = new TestNetwork();

        var topology = net.Topology.GetTopology();
        var mst = net.Topology.GetMinimumSpanningTree();
        var bfs = net.Topology.RunBfs(null)!;

        Assert.Single(topology.Nodes);
        Assert.Empty(topology.Links);
        Assert.Equal(0, topology.CutOffCount);
        Assert.Empty(mst.Links);
        Assert.Equal(0, mst.TotalCost);
        Assert.Single(bfs.Steps);
        Assert.Contains("No links", mst.Message);
    }

    [Fact]
    public void Network_with_one_device_works()
    {
        var net = new TestNetwork();
        net.AddDevice(TestNetwork.PlugMac, "Smart Plug 1", DeviceKind.SmartPlug, "Zone 3");

        var topology = net.Topology.GetTopology();
        var route = net.Topology.GetRoute(TestNetwork.PlugMac)!;
        var mst = net.Topology.GetMinimumSpanningTree();

        Assert.Equal(2, topology.Nodes.Count);
        Assert.Equal([TestNetwork.PlugMac, Gateway], route.FewestHopsPath);
        Assert.Single(mst.Links);
    }

    [Fact]
    public void Asking_for_the_route_of_an_unknown_node_returns_nothing()
    {
        var net = new TestNetwork().WithDemoDevices();

        Assert.Null(net.Topology.GetRoute("24:6F:28:00:00:99"));
        Assert.Equal("This is the gateway itself.", net.Topology.GetRoute(Gateway)!.Message);
    }
}
