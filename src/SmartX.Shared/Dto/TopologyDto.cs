namespace SmartX.Shared.Dto;

/// <summary>One node in the mesh diagram (the gateway or a sensor device).</summary>
public sealed class TopologyNodeDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsGateway { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required bool IsOnline { get; init; }

    /// <summary>True if a message from this node can get to the gateway right now.</summary>
    public required bool CanReachGateway { get; init; }

    /// <summary>Fewest hops to the gateway, or null if it can't reach it.</summary>
    public int? HopsToGateway { get; init; }
}

/// <summary>One wireless connection between two nodes.</summary>
public sealed class TopologyLinkDto
{
    public required string A { get; init; }
    public required string B { get; init; }

    /// <summary>Estimated energy cost of the link (simulated).</summary>
    public required double Cost { get; init; }

    /// <summary>False if the link has been switched off.</summary>
    public required bool IsUp { get; init; }

    /// <summary>True if the link is up and both ends are online.</summary>
    public required bool IsUsable { get; init; }
}

public sealed class TopologyDto
{
    public required List<TopologyNodeDto> Nodes { get; init; }
    public required List<TopologyLinkDto> Links { get; init; }
    public required int OnlineCount { get; init; }
    public required int OfflineCount { get; init; }

    /// <summary>Online devices that have no route to the gateway.</summary>
    public required int CutOffCount { get; init; }
    public required double RadioRangeMetres { get; init; }

    /// <summary>Always true for now: positions and link costs come from SimulatedLayout.</summary>
    public required bool IsSimulated { get; init; }
}

/// <summary>The route a sensor uses to reach the gateway, two ways.</summary>
public sealed class RouteDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool CanReachGateway { get; init; }

    /// <summary>Node ids from the sensor to the gateway using the fewest hops.</summary>
    public required List<string> FewestHopsPath { get; init; }
    public required double FewestHopsCost { get; init; }

    /// <summary>Node ids from the sensor to the gateway using only spanning tree links.</summary>
    public required List<string> TreePath { get; init; }
    public required double TreePathCost { get; init; }

    /// <summary>Plain-English explanation, e.g. why there is no route.</summary>
    public required string Message { get; init; }
}

public sealed class MstDto
{
    /// <summary>The links chosen by the algorithm.</summary>
    public required List<TopologyLinkDto> Links { get; init; }
    public required double TotalCost { get; init; }

    /// <summary>Total cost of every usable link in the normal network, for comparison.</summary>
    public required double AllLinksCost { get; init; }
    public required int AllLinksCount { get; init; }

    /// <summary>Nodes the tree could not include (offline or cut off).</summary>
    public required List<string> NotInTree { get; init; }
    public required string Message { get; init; }
}

public sealed class BfsStepDto
{
    public required int Order { get; init; }
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public string? FromId { get; init; }
}

public sealed class BfsDto
{
    public required string StartId { get; init; }
    public required List<BfsStepDto> Steps { get; init; }
    public required List<string> ReachableIds { get; init; }
    public required List<string> UnreachableIds { get; init; }
    public required string Message { get; init; }
}

public sealed class ReadingDto
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string SensorType { get; init; }
    public required double Value { get; init; }
}

public sealed class ReadingQueryDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required List<ReadingDto> Readings { get; init; }

    /// <summary>How many tree nodes the search looked at.</summary>
    public required int NodesVisited { get; init; }

    /// <summary>How many readings are stored for this sensor in total.</summary>
    public required int TotalStored { get; init; }
}
