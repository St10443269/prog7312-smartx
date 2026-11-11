using SmartX.Api.RealTime;
using SmartX.Api.Topology;

namespace SmartX.Tests;

/// <summary>The AVL tree: inserting, balancing, range searches and duplicate keys.</summary>
public class AvlTreeTests
{
    private static AvlTree<int, string> TreeWith(params int[] keys)
    {
        var tree = new AvlTree<int, string>();
        foreach (var key in keys)
        {
            tree.Insert(key, $"v{key}");
        }

        return tree;
    }

    [Theory]
    [InlineData(1, 2, 3)]   // right-right: left rotation
    [InlineData(3, 2, 1)]   // left-left: right rotation
    [InlineData(3, 1, 2)]   // left-right: double rotation
    [InlineData(1, 3, 2)]   // right-left: double rotation
    public void Three_keys_in_any_awkward_order_rotate_into_a_balanced_tree(int first, int second, int third)
    {
        var tree = TreeWith(first, second, third);

        Assert.Equal(2, tree.Height);
        Assert.True(tree.IsBalanced());
        Assert.Equal(["v1", "v2", "v3"], tree.ToList());
    }

    [Fact]
    public void Keys_inserted_in_order_still_give_a_balanced_tree()
    {
        // an ordinary binary search tree would turn into a 1000-long chain here
        var tree = TreeWith(Enumerable.Range(1, 1000).ToArray());

        Assert.True(tree.IsBalanced());
        Assert.True(tree.Height <= 14, $"height was {tree.Height}"); // AVL height is at most about 1.44 * log2(n)
        Assert.Equal(1000, tree.Count);
        Assert.Equal(Enumerable.Range(1, 1000).Select(n => $"v{n}"), tree.ToList());
    }

    [Fact]
    public void Descending_and_shuffled_inserts_stay_balanced_and_sorted()
    {
        var descending = TreeWith(Enumerable.Range(1, 500).Reverse().ToArray());
        Assert.True(descending.IsBalanced());

        var random = new Random(42);
        var shuffled = Enumerable.Range(1, 500).OrderBy(_ => random.Next()).ToArray();
        var tree = TreeWith(shuffled);

        Assert.True(tree.IsBalanced());
        Assert.Equal(Enumerable.Range(1, 500).Select(n => $"v{n}"), tree.ToList());
    }

    [Fact]
    public void Same_key_inserted_twice_keeps_both_values()
    {
        var tree = new AvlTree<int, string>();
        tree.Insert(5, "first");
        tree.Insert(5, "second");
        tree.Insert(3, "other");

        Assert.Equal(3, tree.Count);
        Assert.Equal(["first", "second"], tree.Find(5));
        Assert.Equal(["other", "first", "second"], tree.ToList());
        Assert.True(tree.IsBalanced());
    }

    [Fact]
    public void Range_search_returns_only_keys_inside_the_range_in_order()
    {
        var tree = TreeWith(10, 20, 30, 40, 50, 60, 70);

        var result = tree.RangeSearch(25, 60);

        Assert.Equal(["v30", "v40", "v50", "v60"], result.Items);
    }

    [Fact]
    public void Range_search_includes_both_ends_and_all_duplicates()
    {
        var tree = TreeWith(10, 20, 30);
        tree.Insert(20, "again");

        var result = tree.RangeSearch(20, 30);

        Assert.Equal(["v20", "again", "v30"], result.Items);
    }

    [Fact]
    public void Range_search_with_nothing_in_range_or_a_backwards_range_is_empty()
    {
        var tree = TreeWith(10, 20, 30);

        Assert.Empty(tree.RangeSearch(11, 19).Items);
        Assert.Empty(tree.RangeSearch(100, 200).Items);
        Assert.Empty(tree.RangeSearch(30, 10).Items);
    }

    [Fact]
    public void Range_search_only_looks_at_a_small_part_of_a_big_tree()
    {
        var tree = TreeWith(Enumerable.Range(1, 10_000).ToArray());

        var result = tree.RangeSearch(5000, 5009);

        Assert.Equal(10, result.Items.Count);
        // 10 matches plus the path down the tree - nowhere near 10,000
        Assert.True(result.NodesVisited < 60, $"visited {result.NodesVisited} nodes");
    }

    [Fact]
    public void Empty_tree_is_safe_to_search_and_remove_from()
    {
        var tree = new AvlTree<int, string>();

        Assert.Equal(0, tree.Count);
        Assert.Equal(0, tree.Height);
        Assert.Empty(tree.Find(1));
        Assert.Empty(tree.RangeSearch(0, 100).Items);
        Assert.False(tree.RemoveSmallest());
        Assert.True(tree.IsBalanced());
    }

    [Fact]
    public void Removing_the_smallest_key_removes_its_duplicates_and_keeps_the_tree_balanced()
    {
        var tree = TreeWith(Enumerable.Range(1, 200).ToArray());
        tree.Insert(1, "duplicate of 1");

        Assert.True(tree.RemoveSmallest());

        Assert.Equal(199, tree.Count);
        Assert.Empty(tree.Find(1));
        Assert.Equal("v2", tree.ToList()[0]);
        Assert.True(tree.IsBalanced());
    }

    [Fact]
    public void Removing_many_smallest_keys_keeps_the_tree_balanced()
    {
        var tree = TreeWith(Enumerable.Range(1, 300).ToArray());

        for (var i = 0; i < 250; i++)
        {
            tree.RemoveSmallest();
        }

        Assert.Equal(50, tree.Count);
        Assert.True(tree.IsBalanced());
        Assert.Equal(Enumerable.Range(251, 50).Select(n => $"v{n}"), tree.ToList());
    }
}

/// <summary>Historical readings stored per sensor in AVL trees, searched by time.</summary>
public class ReadingHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);
    private const string SensorA = "24:6F:28:B1:00:01";
    private const string SensorB = "24:6F:28:A1:00:01";

    private static TelemetryReading Reading(string mac, int minutes, double value) =>
        new(mac, "SoilMoisturePct", T0.AddMinutes(minutes), value);

    [Fact]
    public void Query_returns_only_that_sensors_readings_between_the_two_times()
    {
        var history = new ReadingHistory(new RealTimeOptions());
        for (var minute = 0; minute <= 240; minute += 30)
        {
            history.Add(Reading(SensorA, minute, minute));
            history.Add(Reading(SensorB, minute, -minute));
        }

        // 14:00 + 60 min = 15:00 up to 14:00 + 120 min = 16:00
        var (readings, _, total) = history.Query(SensorA, T0.AddMinutes(60), T0.AddMinutes(120));

        Assert.Equal([60.0, 90.0, 120.0], readings.Select(r => r.Value));
        Assert.All(readings, r => Assert.Equal(SensorA, r.MacAddress));
        Assert.Equal(9, total);
    }

    [Fact]
    public void Readings_with_the_same_timestamp_are_all_kept()
    {
        var history = new ReadingHistory(new RealTimeOptions());
        history.Add(Reading(SensorA, 10, 1));
        history.Add(Reading(SensorA, 10, 2));
        history.Add(Reading(SensorA, 10, 3));

        var (readings, _, _) = history.Query(SensorA, T0.AddMinutes(10), T0.AddMinutes(10));

        Assert.Equal([1.0, 2.0, 3.0], readings.Select(r => r.Value));
    }

    [Fact]
    public void Readings_that_arrive_late_are_returned_in_time_order()
    {
        var history = new ReadingHistory(new RealTimeOptions());
        history.Add(Reading(SensorA, 30, 3));
        history.Add(Reading(SensorA, 10, 1));
        history.Add(Reading(SensorA, 20, 2));

        var (readings, _, _) = history.Query(SensorA, T0, T0.AddHours(1));

        Assert.Equal([1.0, 2.0, 3.0], readings.Select(r => r.Value));
    }

    [Fact]
    public void Mac_address_format_does_not_matter_when_searching()
    {
        var history = new ReadingHistory(new RealTimeOptions());
        history.Add(Reading(SensorA, 0, 5));

        var (readings, _, _) = history.Query("24-6f-28-b1-00-01", T0, T0.AddHours(1));

        Assert.Single(readings);
    }

    [Fact]
    public void Unknown_sensor_gives_an_empty_result()
    {
        var history = new ReadingHistory(new RealTimeOptions());

        var (readings, visited, total) = history.Query("24:6F:28:00:00:99", T0, T0.AddHours(1));

        Assert.Empty(readings);
        Assert.Equal(0, visited);
        Assert.Equal(0, total);
    }

    [Fact]
    public void Oldest_readings_are_dropped_when_the_limit_is_reached()
    {
        var options = new RealTimeOptions();
        options.Processing.MaxReadingsPerSensor = 3;
        var history = new ReadingHistory(options);

        for (var minute = 0; minute < 6; minute++)
        {
            history.Add(Reading(SensorA, minute, minute));
        }

        var (readings, _, total) = history.Query(SensorA, T0, T0.AddHours(1));

        Assert.Equal(3, total);
        Assert.Equal([3.0, 4.0, 5.0], readings.Select(r => r.Value));
    }

    [Fact]
    public void A_processed_telemetry_packet_ends_up_in_the_tree()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 42.5);

        var (readings, _, _) = net.Readings.Query(TestNetwork.MoistureMac, net.Clock.Now.AddMinutes(-1), net.Clock.Now.AddMinutes(1));
        var reading = Assert.Single(readings);
        Assert.Equal(42.5, reading.Value);
        Assert.Equal("SoilMoisturePct", reading.SensorType);
    }
}
