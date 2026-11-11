namespace SmartX.Api.Topology;

/// <summary>One node visited by BFS: how many hops from the start, and which node it was reached from.</summary>
public sealed record BfsStep(string NodeId, int Level, string? FromId);

public sealed record BfsResult(string StartId, List<BfsStep> Steps)
{
    public bool WasReached(string id) => Steps.Any(step => step.NodeId == id);
}

public sealed record MstResult(List<MeshLink> Links, LinkCost TotalCost, HashSet<string> NodeIds);

/// <summary>
/// The graph algorithms for the mesh. They only use links that are usable
/// (link up and both ends online), so offline devices and broken links are
/// skipped automatically.
/// </summary>
public static class MeshAlgorithms
{
    /// <summary>
    /// Breadth-first search. It visits the start node, then everything 1 hop
    /// away, then everything 2 hops away, and so on, using a queue
    /// (first in, first out). Because it works level by level, the first time
    /// it reaches a node is by the route with the fewest hops (Cormen et al.,
    /// 2022). Nodes that never get visited cannot be reached from the start.
    /// </summary>
    public static BfsResult Bfs(MeshGraph graph, string startId)
    {
        var steps = new List<BfsStep>();
        var start = graph.GetNode(startId);
        if (start is null || !start.IsOnline)
        {
            return new BfsResult(startId, steps);
        }

        var visited = new HashSet<string> { startId };
        var queue = new Queue<(string Id, int Level)>();
        queue.Enqueue((startId, 0));
        steps.Add(new BfsStep(startId, 0, null));

        while (queue.Count > 0)
        {
            var (current, level) = queue.Dequeue();

            foreach (var (neighbour, _) in graph.GetUsableNeighbours(current))
            {
                if (visited.Add(neighbour.Id))
                {
                    steps.Add(new BfsStep(neighbour.Id, level + 1, current));
                    queue.Enqueue((neighbour.Id, level + 1));
                }
            }
        }

        return new BfsResult(startId, steps);
    }

    /// <summary>
    /// The route with the fewest hops from one node to another, as a list of
    /// node ids (including both ends), or null if there is no route.
    /// </summary>
    public static List<string>? FindFewestHopsPath(MeshGraph graph, string fromId, string toId)
    {
        var bfs = Bfs(graph, fromId);
        if (!bfs.WasReached(toId))
        {
            return null;
        }

        // walk back from the destination using "reached from" until the start
        var cameFrom = bfs.Steps.ToDictionary(step => step.NodeId, step => step.FromId);
        var path = new List<string>();
        for (string? id = toId; id is not null; id = cameFrom[id])
        {
            path.Add(id);
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Prim's algorithm for a minimum spanning tree. It starts at one node and
    /// repeatedly adds the cheapest link that connects a new node to the tree.
    /// A priority queue (a min-heap) hands back the cheapest waiting link each
    /// time, so the links never have to be re-sorted (Prim, 1957; Cormen et
    /// al., 2022).
    ///
    /// It only covers the nodes reachable from the start. The result has the
    /// lowest possible total cost for connecting them, but it does not give
    /// every node its own cheapest route back to the start.
    /// </summary>
    public static MstResult Prim(MeshGraph graph, string startId)
    {
        var treeLinks = new List<MeshLink>();
        var inTree = new HashSet<string>();
        var total = LinkCost.Zero;

        var start = graph.GetNode(startId);
        if (start is null || !start.IsOnline)
        {
            return new MstResult(treeLinks, total, inTree);
        }

        // the queue holds (node the link leads to, the link) ordered by cost
        var waiting = new PriorityQueue<(string To, MeshLink Link), LinkCost>();

        inTree.Add(startId);
        AddLinksFrom(graph, startId, inTree, waiting);

        while (waiting.TryDequeue(out var cheapest, out var cost))
        {
            // the node may have joined the tree through a cheaper link already
            if (inTree.Contains(cheapest.To))
            {
                continue;
            }

            inTree.Add(cheapest.To);
            treeLinks.Add(cheapest.Link);
            total += cost;
            AddLinksFrom(graph, cheapest.To, inTree, waiting);
        }

        return new MstResult(treeLinks, total, inTree);
    }

    private static void AddLinksFrom(MeshGraph graph, string nodeId, HashSet<string> inTree, PriorityQueue<(string To, MeshLink Link), LinkCost> waiting)
    {
        foreach (var (neighbour, link) in graph.GetUsableNeighbours(nodeId))
        {
            if (!inTree.Contains(neighbour.Id))
            {
                waiting.Enqueue((neighbour.Id, link), link.Cost);
            }
        }
    }

    /// <summary>
    /// The route between two nodes using only the links of a spanning tree.
    /// In a tree there is exactly one route between any two nodes. Returns
    /// null if either node is not in the tree.
    /// </summary>
    public static List<string>? FindPathInTree(IEnumerable<MeshLink> treeLinks, string fromId, string toId)
    {
        var neighbours = new Dictionary<string, List<string>>();
        foreach (var link in treeLinks)
        {
            if (!neighbours.TryGetValue(link.A, out var listA)) neighbours[link.A] = listA = [];
            if (!neighbours.TryGetValue(link.B, out var listB)) neighbours[link.B] = listB = [];
            listA.Add(link.B);
            listB.Add(link.A);
        }

        if (fromId == toId)
        {
            return [fromId];
        }

        if (!neighbours.ContainsKey(fromId) || !neighbours.ContainsKey(toId))
        {
            return null;
        }

        // simple breadth-first walk over the tree, remembering where we came from
        var cameFrom = new Dictionary<string, string?> { [fromId] = null };
        var queue = new Queue<string>();
        queue.Enqueue(fromId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == toId)
            {
                break;
            }

            foreach (var next in neighbours[current])
            {
                if (cameFrom.TryAdd(next, current))
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (!cameFrom.ContainsKey(toId))
        {
            return null;
        }

        var path = new List<string>();
        for (string? id = toId; id is not null; id = cameFrom[id])
        {
            path.Add(id);
        }

        path.Reverse();
        return path;
    }

    /// <summary>Adds up the cost of the links along a path of node ids.</summary>
    public static LinkCost PathCost(MeshGraph graph, IReadOnlyList<string> path)
    {
        var total = LinkCost.Zero;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var link = graph.GetLink(path[i], path[i + 1]);
            if (link is not null)
            {
                total += link.Cost;
            }
        }

        return total;
    }
}
