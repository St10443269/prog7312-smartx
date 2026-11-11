namespace SmartX.Api.Topology;

/// <summary>One vertex in the mesh: the gateway or a sensor node.</summary>
public sealed class MeshNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsGateway { get; init; }

    /// <summary>Position on the diagram in metres (simulated).</summary>
    public double X { get; init; }
    public double Y { get; init; }

    public bool IsOnline { get; set; } = true;
}

/// <summary>One wireless connection between two nodes. It works in both directions.</summary>
public sealed class MeshLink
{
    public required string A { get; init; }
    public required string B { get; init; }
    public required LinkCost Cost { get; init; }

    /// <summary>False when this connection has been switched off or has failed.</summary>
    public bool IsUp { get; set; } = true;

    public string Other(string id) => id == A ? B : A;
}

/// <summary>
/// The sensor mesh as an undirected graph. Each node is a vertex and each
/// wireless connection is an edge. It is stored as an adjacency list: a
/// dictionary from each node id to its neighbours, so finding the neighbours
/// of a node does not mean checking every link in the network (Cormen et al.,
/// 2022).
/// </summary>
public sealed class MeshGraph
{
    private readonly Dictionary<string, MeshNode> _nodes = new();

    // node id -> (neighbour id -> the link between them)
    private readonly Dictionary<string, Dictionary<string, MeshLink>> _neighbours = new();

    public IReadOnlyCollection<MeshNode> Nodes => _nodes.Values;

    /// <summary>Every link once (not once per direction).</summary>
    public IEnumerable<MeshLink> Links =>
        _neighbours.SelectMany(pair => pair.Value.Values).Distinct();

    public MeshNode? GetNode(string id) => _nodes.GetValueOrDefault(id);

    public bool HasNode(string id) => _nodes.ContainsKey(id);

    /// <summary>Adds a node. Returns false if a node with that id already exists.</summary>
    public bool AddNode(MeshNode node)
    {
        if (!_nodes.TryAdd(node.Id, node))
        {
            return false;
        }

        _neighbours[node.Id] = new Dictionary<string, MeshLink>();
        return true;
    }

    /// <summary>
    /// Connects two existing nodes. Returns null if either node is missing, the
    /// nodes are the same, or they are already connected.
    /// </summary>
    public MeshLink? AddLink(string a, string b, LinkCost cost)
    {
        if (a == b || !_nodes.ContainsKey(a) || !_nodes.ContainsKey(b) || _neighbours[a].ContainsKey(b))
        {
            return null;
        }

        var link = new MeshLink { A = a, B = b, Cost = cost };
        _neighbours[a][b] = link;
        _neighbours[b][a] = link;
        return link;
    }

    public MeshLink? GetLink(string a, string b) =>
        _neighbours.TryGetValue(a, out var row) ? row.GetValueOrDefault(b) : null;

    public bool SetNodeOnline(string id, bool isOnline)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return false;
        }

        node.IsOnline = isOnline;
        return true;
    }

    public bool SetLinkUp(string a, string b, bool isUp)
    {
        var link = GetLink(a, b);
        if (link is null)
        {
            return false;
        }

        link.IsUp = isUp;
        return true;
    }

    /// <summary>All neighbours of a node, whether or not they can currently be reached.</summary>
    public IEnumerable<MeshNode> GetConnectedNodes(string id) =>
        _neighbours.TryGetValue(id, out var row) ? row.Keys.Select(key => _nodes[key]) : [];

    /// <summary>
    /// A link can carry a message only if the link is up and both ends are
    /// online. Searches (BFS, Prim) only use links like this.
    /// </summary>
    public bool IsUsable(MeshLink link) =>
        link.IsUp && _nodes[link.A].IsOnline && _nodes[link.B].IsOnline;

    /// <summary>Neighbours that can be reached right now, with the link used to get there.</summary>
    public IEnumerable<(MeshNode Node, MeshLink Link)> GetUsableNeighbours(string id)
    {
        if (!_neighbours.TryGetValue(id, out var row))
        {
            yield break;
        }

        foreach (var (neighbourId, link) in row)
        {
            if (IsUsable(link))
            {
                yield return (_nodes[neighbourId], link);
            }
        }
    }
}
