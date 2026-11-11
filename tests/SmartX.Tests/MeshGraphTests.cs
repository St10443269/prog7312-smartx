using SmartX.Api.Topology;

namespace SmartX.Tests;

/// <summary>The graph, breadth-first search and Prim's spanning tree, on small networks where the answers can be worked out by hand.</summary>
public class MeshGraphTests
{
    private static MeshNode Node(string id) => new() { Id = id, Name = id };

    private static MeshGraph GraphOf(params string[] ids)
    {
        var graph = new MeshGraph();
        foreach (var id in ids)
        {
            graph.AddNode(Node(id));
        }

        return graph;
    }

    private static void Link(MeshGraph graph, string a, string b, double cost) =>
        graph.AddLink(a, b, new LinkCost(cost));

    /// <summary>
    ///      G --1-- A --2-- B
    ///      |       |       |\
    ///      |       5       1 6
    ///      4       |       | |
    ///      +-------C---3---D-+   (and G-B costs 4)
    /// Worked out by hand: the cheapest way to connect everything is
    /// G-A (1), B-C (1), A-B (2), C-D (3) = 7.
    /// </summary>
    private static MeshGraph ExampleNetwork()
    {
        var graph = GraphOf("G", "A", "B", "C", "D");
        Link(graph, "G", "A", 1);
        Link(graph, "G", "B", 4);
        Link(graph, "A", "B", 2);
        Link(graph, "A", "C", 5);
        Link(graph, "B", "C", 1);
        Link(graph, "C", "D", 3);
        Link(graph, "B", "D", 6);
        return graph;
    }

    // ---------------------------------------------------------------- the graph

    [Fact]
    public void Nodes_and_links_can_be_added_and_the_neighbours_found()
    {
        var graph = GraphOf("G", "A", "B");
        Link(graph, "G", "A", 1);
        Link(graph, "A", "B", 1);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Links.Count());
        Assert.Equal(["A"], graph.GetConnectedNodes("G").Select(n => n.Id));
        Assert.Equal(["B", "G"], graph.GetConnectedNodes("A").Select(n => n.Id).Order());
    }

    [Fact]
    public void Bad_additions_are_refused()
    {
        var graph = GraphOf("G", "A");
        Link(graph, "G", "A", 1);

        Assert.False(graph.AddNode(Node("G")));                          // duplicate node
        Assert.Null(graph.AddLink("G", "A", new LinkCost(9)));           // duplicate link
        Assert.Null(graph.AddLink("A", "G", new LinkCost(9)));           // same link the other way round
        Assert.Null(graph.AddLink("G", "G", new LinkCost(1)));           // node to itself
        Assert.Null(graph.AddLink("G", "missing", new LinkCost(1)));     // node that does not exist
        Assert.Single(graph.Links);
    }

    [Fact]
    public void A_link_is_unusable_when_switched_off_or_when_an_end_is_offline()
    {
        var graph = GraphOf("G", "A", "B");
        Link(graph, "G", "A", 1);
        Link(graph, "A", "B", 1);
        var gToA = graph.GetLink("G", "A")!;

        Assert.True(graph.IsUsable(gToA));

        graph.SetLinkUp("A", "G", false);
        Assert.False(graph.IsUsable(gToA));

        graph.SetLinkUp("G", "A", true);
        graph.SetNodeOnline("A", false);
        Assert.False(graph.IsUsable(gToA));
        Assert.Empty(graph.GetUsableNeighbours("G"));
        Assert.False(graph.SetNodeOnline("nobody", false));
        Assert.False(graph.SetLinkUp("G", "B", false)); // not connected
    }

    [Fact]
    public void Link_costs_can_be_added_and_compared_with_operators()
    {
        var a = new LinkCost(1.5);
        var b = new LinkCost(2);

        Assert.Equal(new LinkCost(3.5), a + b);
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= new LinkCost(1.5));
        Assert.True(a != b);
        Assert.Equal(LinkCost.Zero, new LinkCost(0));
    }

    // ---------------------------------------------------------------------- BFS

    [Fact]
    public void Bfs_visits_nodes_level_by_level_from_the_start()
    {
        var graph = ExampleNetwork();

        var result = MeshAlgorithms.Bfs(graph, "G");

        Assert.Equal("G", result.Steps[0].NodeId);
        Assert.Equal(0, result.Steps[0].Level);
        Assert.Equal(5, result.Steps.Count);

        var levels = result.Steps.ToDictionary(s => s.NodeId, s => s.Level);
        Assert.Equal(1, levels["A"]);
        Assert.Equal(1, levels["B"]);
        Assert.Equal(2, levels["C"]);
        Assert.Equal(2, levels["D"]);

        // levels never go down as the search goes on
        Assert.Equal(result.Steps.Select(s => s.Level).Order(), result.Steps.Select(s => s.Level));
    }

    [Fact]
    public void Bfs_does_not_reach_nodes_with_no_route()
    {
        var graph = GraphOf("G", "A", "Island");
        Link(graph, "G", "A", 1);

        var result = MeshAlgorithms.Bfs(graph, "G");

        Assert.True(result.WasReached("A"));
        Assert.False(result.WasReached("Island"));
    }

    [Fact]
    public void Bfs_goes_around_an_offline_node_when_another_route_exists_and_stops_when_none_does()
    {
        var graph = GraphOf("G", "A", "B", "C");
        Link(graph, "G", "A", 1);
        Link(graph, "G", "B", 1);
        Link(graph, "A", "C", 1);
        Link(graph, "B", "C", 1);

        graph.SetNodeOnline("A", false);
        Assert.True(MeshAlgorithms.Bfs(graph, "G").WasReached("C"));   // still reachable through B

        graph.SetNodeOnline("B", false);
        Assert.False(MeshAlgorithms.Bfs(graph, "G").WasReached("C"));  // both ways blocked
    }

    [Fact]
    public void Bfs_with_a_missing_or_offline_start_visits_nothing()
    {
        var graph = GraphOf("G", "A");
        Link(graph, "G", "A", 1);
        graph.SetNodeOnline("G", false);

        Assert.Empty(MeshAlgorithms.Bfs(graph, "G").Steps);
        Assert.Empty(MeshAlgorithms.Bfs(graph, "nobody").Steps);
        Assert.Empty(MeshAlgorithms.Bfs(new MeshGraph(), "G").Steps);
    }

    [Fact]
    public void Bfs_on_a_single_node_visits_just_that_node()
    {
        var graph = GraphOf("G");

        var result = MeshAlgorithms.Bfs(graph, "G");

        Assert.Single(result.Steps);
    }

    [Fact]
    public void Fewest_hops_path_takes_the_shortcut_even_if_it_costs_more()
    {
        // G-A-B-D is 3 hops and cheap; G-D is 1 hop but expensive
        var graph = GraphOf("G", "A", "B", "D");
        Link(graph, "G", "A", 1);
        Link(graph, "A", "B", 1);
        Link(graph, "B", "D", 1);
        Link(graph, "G", "D", 50);

        var path = MeshAlgorithms.FindFewestHopsPath(graph, "D", "G");

        Assert.Equal(["D", "G"], path);
    }

    [Fact]
    public void Fewest_hops_path_is_null_when_there_is_no_route()
    {
        var graph = GraphOf("G", "A", "Island");
        Link(graph, "G", "A", 1);

        Assert.Null(MeshAlgorithms.FindFewestHopsPath(graph, "Island", "G"));
        Assert.Equal(["G"], MeshAlgorithms.FindFewestHopsPath(graph, "G", "G"));
    }

    // -------------------------------------------------------------------- Prim

    [Fact]
    public void Prim_finds_the_cheapest_way_to_connect_the_network()
    {
        var graph = ExampleNetwork();

        var mst = MeshAlgorithms.Prim(graph, "G");

        Assert.Equal(new LinkCost(7), mst.TotalCost);
        Assert.Equal(4, mst.Links.Count);   // 5 nodes need exactly 4 links
        Assert.Equal(5, mst.NodeIds.Count);

        var pairs = mst.Links.Select(l => string.Join("-", new[] { l.A, l.B }.Order())).Order();
        Assert.Equal(["A-B", "A-G", "B-C", "C-D"], pairs);
    }

    [Fact]
    public void Prim_gives_the_same_total_whichever_node_it_starts_from()
    {
        var graph = ExampleNetwork();

        foreach (var start in new[] { "G", "A", "B", "C", "D" })
        {
            Assert.Equal(new LinkCost(7), MeshAlgorithms.Prim(graph, start).TotalCost);
        }
    }

    [Fact]
    public void Spanning_tree_route_can_be_longer_than_the_direct_route()
    {
        // G-A 1, A-B 1, G-B 1.5: the tree uses G-A and A-B (total 2), so B goes
        // through A (cost 2) even though B has its own link to G (cost 1.5)
        var graph = GraphOf("G", "A", "B");
        Link(graph, "G", "A", 1);
        Link(graph, "A", "B", 1);
        Link(graph, "G", "B", 1.5);

        var mst = MeshAlgorithms.Prim(graph, "G");
        var treePath = MeshAlgorithms.FindPathInTree(mst.Links, "B", "G")!;
        var directPath = MeshAlgorithms.FindFewestHopsPath(graph, "B", "G")!;

        Assert.Equal(new LinkCost(2), mst.TotalCost);
        Assert.Equal(["B", "A", "G"], treePath);
        Assert.Equal(new LinkCost(2), MeshAlgorithms.PathCost(graph, treePath));
        Assert.Equal(["B", "G"], directPath);
        Assert.Equal(new LinkCost(1.5), MeshAlgorithms.PathCost(graph, directPath));
    }

    [Fact]
    public void Prim_recalculates_when_a_link_goes_down()
    {
        var graph = ExampleNetwork();
        Assert.Equal(new LinkCost(7), MeshAlgorithms.Prim(graph, "G").TotalCost);

        graph.SetLinkUp("B", "C", false); // the cheap B-C link fails

        // now G-A 1, A-B 2, A-C 5, C-D 3 = 11
        var mst = MeshAlgorithms.Prim(graph, "G");
        Assert.Equal(new LinkCost(11), mst.TotalCost);
        Assert.Equal(4, mst.Links.Count);
        Assert.DoesNotContain(mst.Links, l => graph.GetLink("B", "C") == l);
    }

    [Fact]
    public void Prim_leaves_out_offline_and_cut_off_nodes()
    {
        var graph = ExampleNetwork();
        graph.SetNodeOnline("C", false);

        // C is out, so D is only reachable through B-D (6): G-A 1, A-B 2, B-D 6 = 9
        var mst = MeshAlgorithms.Prim(graph, "G");

        Assert.Equal(new LinkCost(9), mst.TotalCost);
        Assert.DoesNotContain("C", mst.NodeIds);
        Assert.Contains("D", mst.NodeIds);

        graph.SetLinkUp("B", "D", false); // now D has no usable link at all
        var cutOff = MeshAlgorithms.Prim(graph, "G");
        Assert.DoesNotContain("D", cutOff.NodeIds);
        Assert.Equal(new LinkCost(3), cutOff.TotalCost);
    }

    [Fact]
    public void Prim_handles_an_empty_network_and_a_single_node()
    {
        var empty = MeshAlgorithms.Prim(new MeshGraph(), "G");
        Assert.Empty(empty.Links);
        Assert.Equal(LinkCost.Zero, empty.TotalCost);

        var single = MeshAlgorithms.Prim(GraphOf("G"), "G");
        Assert.Empty(single.Links);
        Assert.Equal(LinkCost.Zero, single.TotalCost);
        Assert.Equal(["G"], single.NodeIds);
    }

    [Fact]
    public void Prim_only_covers_the_part_of_the_network_the_start_can_reach()
    {
        var graph = GraphOf("G", "A", "X", "Y");
        Link(graph, "G", "A", 2);
        Link(graph, "X", "Y", 1); // a separate pair with no link to G

        var mst = MeshAlgorithms.Prim(graph, "G");

        Assert.Equal(new LinkCost(2), mst.TotalCost);
        Assert.DoesNotContain("X", mst.NodeIds);
        Assert.DoesNotContain("Y", mst.NodeIds);
    }

    [Fact]
    public void Path_in_tree_is_null_for_nodes_that_are_not_in_the_tree()
    {
        var graph = GraphOf("G", "A", "Island");
        Link(graph, "G", "A", 1);
        var mst = MeshAlgorithms.Prim(graph, "G");

        Assert.Null(MeshAlgorithms.FindPathInTree(mst.Links, "Island", "G"));
        Assert.Equal(["A", "G"], MeshAlgorithms.FindPathInTree(mst.Links, "A", "G"));
    }
}
