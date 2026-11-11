using SmartX.Api.Topology;
using SmartX.Shared.Dto;

namespace SmartX.Api.Endpoints;

/// <summary>Part 3: the sensor mesh, routing and historical readings.</summary>
public static class TopologyEndpoints
{
    public static RouteGroupBuilder MapTopologyEndpoints(this RouteGroupBuilder group)
    {
        // the whole network: nodes, links and who can reach the gateway
        group.MapGet("/", (MeshTopology topology) => Results.Ok(topology.GetTopology()));

        // the route one sensor uses to reach the gateway
        group.MapGet("/route/{id}", (string id, MeshTopology topology) =>
            topology.GetRoute(id) is { } route
                ? Results.Ok(route)
                : Results.NotFound(new { message = $"Unknown node '{id}'." }));

        // minimum spanning tree (Prim's algorithm), calculated from the current network
        group.MapGet("/mst", (MeshTopology topology) => Results.Ok(topology.GetMinimumSpanningTree()));

        // breadth-first search from a node (the gateway if "from" is left out)
        group.MapGet("/bfs", (string? from, MeshTopology topology) =>
            topology.RunBfs(from) is { } result
                ? Results.Ok(result)
                : Results.NotFound(new { message = $"Unknown node '{from}'." }));

        // switch a link off or on, e.g. POST /api/topology/links/up?a=...&b=...&isUp=false
        group.MapPost("/links/up", (string a, string b, bool isUp, MeshTopology topology) =>
            topology.SetLink(a, b, isUp) is { } link
                ? Results.Ok(link)
                : Results.NotFound(new { message = "Those two nodes are not connected." }));

        // historical readings for one sensor between two times, found with the AVL tree
        group.MapGet("/readings/{id}", (string id, DateTimeOffset? from, DateTimeOffset? to,
            MeshTopology topology, ReadingHistory history, TimeProvider clock) =>
        {
            var name = topology.GetNodeName(id);
            if (name is null || id == MeshTopology.GatewayId)
            {
                return Results.NotFound(new { message = $"Unknown sensor '{id}'." });
            }

            var end = to ?? clock.GetUtcNow();
            var start = from ?? end.AddMinutes(-15);
            if (start > end)
            {
                return Results.BadRequest(new { message = "The 'from' time must be before the 'to' time." });
            }

            var (readings, visited, total) = history.Query(id, start, end);

            return Results.Ok(new ReadingQueryDto
            {
                Id = id,
                Name = name,
                From = start,
                To = end,
                Readings = readings
                    .Select(r => new ReadingDto { Timestamp = r.Timestamp, SensorType = r.SensorType, Value = r.Value })
                    .ToList(),
                NodesVisited = visited,
                TotalStored = total
            });
        });

        return group;
    }
}
