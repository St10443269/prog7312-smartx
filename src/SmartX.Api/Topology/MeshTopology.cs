using SmartX.Api.RealTime;
using SmartX.Shared.Dto;

namespace SmartX.Api.Topology;

/// <summary>
/// Keeps the mesh graph in step with the real device list from Part 2 and
/// answers the topology questions (routes, BFS, spanning tree). Everything is
/// worked out from the graph, so the diagram on the page always shows what the
/// data structure holds.
/// </summary>
public sealed class MeshTopology(DeviceRegistry registry)
{
    public const string GatewayId = SimulatedLayout.GatewayId;

    private readonly MeshGraph _graph = new();
    private readonly object _sync = new();

    // ------------------------------------------------------------ graph upkeep

    /// <summary>
    /// Brings the graph up to date: adds a node (and its links) for any new
    /// device, and marks each node online or offline from the device status
    /// that Part 2 already tracks. Caller must hold the lock.
    /// </summary>
    private void Sync()
    {
        if (!_graph.HasNode(GatewayId))
        {
            var (gx, gy) = SimulatedLayout.GatewayPosition;
            _graph.AddNode(new MeshNode { Id = GatewayId, Name = "Gateway", IsGateway = true, X = gx, Y = gy });
        }

        foreach (var device in registry.GetAll())
        {
            if (!_graph.HasNode(device.MacAddress))
            {
                AddDeviceNode(device);
            }

            _graph.SetNodeOnline(device.MacAddress, device.IsConnected);
        }
    }

    private void AddDeviceNode(SimulatedDevice device)
    {
        var (x, y) = SimulatedLayout.PositionFor(device.MacAddress);
        var node = new MeshNode { Id = device.MacAddress, Name = device.Name, X = x, Y = y };
        _graph.AddNode(node);

        // link the new node to every node that is within radio range
        foreach (var other in _graph.Nodes.Where(n => n.Id != node.Id).ToList())
        {
            var distance = DistanceBetween(node, other);
            if (distance <= SimulatedLayout.RadioRangeMetres)
            {
                _graph.AddLink(node.Id, other.Id, BuildCost(distance));
            }
        }
    }

    private static double DistanceBetween(MeshNode a, MeshNode b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>
    /// Estimated energy cost of a link. The energy a radio needs to transmit
    /// grows roughly with the square of the distance (Heinzelman, Chandrakasan
    /// and Balakrishnan, 2000), so cost = distance squared / 100. The units
    /// are simulated, not measured.
    /// </summary>
    private static LinkCost BuildCost(double distanceMetres) => new(distanceMetres * distanceMetres / 100.0);

    // ------------------------------------------------------------------ queries

    public TopologyDto GetTopology()
    {
        lock (_sync)
        {
            Sync();
            var toGateway = MeshAlgorithms.Bfs(_graph, GatewayId);
            var levels = toGateway.Steps.ToDictionary(step => step.NodeId, step => step.Level);

            var nodes = _graph.Nodes
                .Select(node => new TopologyNodeDto
                {
                    Id = node.Id,
                    Name = node.Name,
                    IsGateway = node.IsGateway,
                    X = node.X,
                    Y = node.Y,
                    IsOnline = node.IsOnline,
                    CanReachGateway = levels.ContainsKey(node.Id),
                    HopsToGateway = levels.TryGetValue(node.Id, out var hops) ? hops : null
                })
                .OrderBy(node => !node.IsGateway)
                .ThenBy(node => node.Name)
                .ToList();

            var devices = nodes.Where(node => !node.IsGateway).ToList();

            return new TopologyDto
            {
                Nodes = nodes,
                Links = _graph.Links.Select(ToDto).OrderBy(link => link.A).ThenBy(link => link.B).ToList(),
                OnlineCount = devices.Count(node => node.IsOnline),
                OfflineCount = devices.Count(node => !node.IsOnline),
                CutOffCount = devices.Count(node => node.IsOnline && !node.CanReachGateway),
                RadioRangeMetres = SimulatedLayout.RadioRangeMetres,
                IsSimulated = true
            };
        }
    }

    public RouteDto? GetRoute(string id)
    {
        lock (_sync)
        {
            Sync();
            var node = _graph.GetNode(id);
            if (node is null)
            {
                return null;
            }

            var hopsPath = MeshAlgorithms.FindFewestHopsPath(_graph, id, GatewayId);
            var tree = MeshAlgorithms.Prim(_graph, GatewayId);
            var treePath = MeshAlgorithms.FindPathInTree(tree.Links, id, GatewayId);

            string message;
            if (node.IsGateway)
            {
                message = "This is the gateway itself.";
            }
            else if (!node.IsOnline)
            {
                message = $"{node.Name} is offline, so it has no route to the gateway.";
            }
            else if (hopsPath is null)
            {
                message = $"{node.Name} is online but every link that leads to the gateway is down or offline, so it has no route.";
            }
            else
            {
                message = $"{node.Name} reaches the gateway in {hopsPath.Count - 1} hop(s).";
            }

            return new RouteDto
            {
                Id = node.Id,
                Name = node.Name,
                CanReachGateway = hopsPath is not null,
                FewestHopsPath = hopsPath ?? [],
                FewestHopsCost = hopsPath is null ? 0 : MeshAlgorithms.PathCost(_graph, hopsPath).Value,
                TreePath = treePath ?? [],
                TreePathCost = treePath is null ? 0 : MeshAlgorithms.PathCost(_graph, treePath).Value,
                Message = message
            };
        }
    }

    public MstDto GetMinimumSpanningTree()
    {
        lock (_sync)
        {
            Sync();
            var tree = MeshAlgorithms.Prim(_graph, GatewayId);

            var usableLinks = _graph.Links.Where(_graph.IsUsable).ToList();
            var allCost = usableLinks.Aggregate(LinkCost.Zero, (sum, link) => sum + link.Cost);

            var notInTree = _graph.Nodes
                .Where(node => !tree.NodeIds.Contains(node.Id))
                .Select(node => node.Id)
                .ToList();

            var message = tree.Links.Count == 0
                ? "No links to connect: the gateway has no usable connections."
                : $"The spanning tree uses {tree.Links.Count} of {usableLinks.Count} usable links and costs {tree.TotalCost}, compared with {allCost} for all of them.";

            return new MstDto
            {
                Links = tree.Links.Select(ToDto).ToList(),
                TotalCost = tree.TotalCost.Value,
                AllLinksCost = allCost.Value,
                AllLinksCount = usableLinks.Count,
                NotInTree = notInTree,
                Message = message
            };
        }
    }

    /// <summary>Runs BFS from the given node (the gateway if none is given) and lists who it can and cannot reach.</summary>
    public BfsDto? RunBfs(string? startId)
    {
        lock (_sync)
        {
            Sync();
            var start = string.IsNullOrWhiteSpace(startId) ? GatewayId : startId;
            var startNode = _graph.GetNode(start);
            if (startNode is null)
            {
                return null;
            }

            var bfs = MeshAlgorithms.Bfs(_graph, start);
            var reached = bfs.Steps.Select(step => step.NodeId).ToHashSet();

            var steps = bfs.Steps
                .Select((step, index) => new BfsStepDto
                {
                    Order = index + 1,
                    NodeId = step.NodeId,
                    Name = _graph.GetNode(step.NodeId)!.Name,
                    Level = step.Level,
                    FromId = step.FromId
                })
                .ToList();

            var unreachable = _graph.Nodes
                .Where(node => !reached.Contains(node.Id))
                .Select(node => node.Id)
                .ToList();

            var message = !startNode.IsOnline
                ? $"{startNode.Name} is offline, so the search cannot start."
                : $"Starting at {startNode.Name}, BFS reached {reached.Count - 1} other node(s); {unreachable.Count} could not be reached.";

            return new BfsDto
            {
                StartId = start,
                Steps = steps,
                ReachableIds = reached.ToList(),
                UnreachableIds = unreachable,
                Message = message
            };
        }
    }

    // ------------------------------------------------------------------ changes

    /// <summary>Switches a link on or off. Returns the link, or null if the two nodes are not connected.</summary>
    public TopologyLinkDto? SetLink(string a, string b, bool isUp)
    {
        lock (_sync)
        {
            Sync();
            if (!_graph.SetLinkUp(a, b, isUp))
            {
                return null;
            }

            return ToDto(_graph.GetLink(a, b)!);
        }
    }

    public string? GetNodeName(string id)
    {
        lock (_sync)
        {
            Sync();
            return _graph.GetNode(id)?.Name;
        }
    }

    private TopologyLinkDto ToDto(MeshLink link) => new()
    {
        A = link.A,
        B = link.B,
        Cost = Math.Round(link.Cost.Value, 2),
        IsUp = link.IsUp,
        IsUsable = _graph.IsUsable(link)
    };
}
