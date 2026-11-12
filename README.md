# Smart-X Gateway - Parts 1, 2 & 3

A hybrid IoT gateway API and dashboard for the Smart-X ecosystem (hydroponic
farms, utility trackers and smart grid installations).

- **Part 1 - Sensor Data Ingestion & Telemetry:** device registration, file
  attachments, telemetry ingestion, the live pulse feed and grid heatmap, and
  recursive deployment-tree validation.
- **Part 2 - Real-Time Command Stream & History:** a live device dashboard fed
  by Server-Sent Events, manual override commands with undo, priority alert
  processing, a chronological history log, search and filtering, and a
  recommendation engine ("Suggested Actions").
- **Part 3 - Network Topology & Mesh Routing:** an interactive diagram of the
  sensor mesh. Sensors that are too far from the gateway send their data
  through other sensors. The page shows each sensor's route to the gateway, a
  minimum spanning tree of the network, a breadth-first search, and each
  sensor's past readings, which are stored in an AVL tree.

> **Simulation notice.** No physical hardware is connected. A built-in device
> simulator in the API plays the part of nine ESP32-based devices (moisture
> sensors, pumps, smart plugs, a relay, a power meter and a controller). Every
> simulated device is labelled as simulated in the UI. Simulated packets go
> through exactly the same validation, queues and processing as packets posted
> to the API from outside. In Part 3 the device positions and the link costs
> on the topology page are also simulated (see
> [Network topology and routing](#network-topology-and-routing-part-3)).

## Contents

1. [Architecture](#architecture)
2. [Requirements and setup](#requirements-and-setup)
3. [Running the application](#running-the-application)
4. [Configuration](#configuration)
5. [Using the application](#using-the-application)
6. [Part 2 data structures - where and why](#part-2-data-structures---where-and-why)
7. [Real-time updates (Server-Sent Events)](#real-time-updates-server-sent-events)
8. [Recommendation engine](#recommendation-engine)
9. [Part 3 data structures and algorithms - where and why](#part-3-data-structures-and-algorithms---where-and-why)
10. [Network topology and routing (Part 3)](#network-topology-and-routing-part-3)
11. [Testing each feature by hand](#testing-each-feature-by-hand)
12. [API reference](#api-reference)
13. [Automated tests](#automated-tests)
14. [Troubleshooting](#troubleshooting)
15. [Limitations and assumptions](#limitations-and-assumptions)
16. [Part 1 C# concepts](#part-1-c-concepts)
17. [References](#references)
18. [Third-party assets](#third-party-assets)

## Architecture

**Web-first**: an ASP.NET Core Minimal API backend and a Blazor WebAssembly
frontend that share one class library, all on .NET 10. The browser talks to the
API over normal HTTP requests (reads, commands, searches). The API pushes live
updates to the browser over one long-lived Server-Sent Events (SSE) connection.

```
Smart-X/
├── SmartX.slnx                      solution file (API, client, shared library, tests)
├── docker-compose.yml
├── src/
│   ├── SmartX.Shared/               class library used by both API and client
│   │   ├── Enums/                   SensorCategory, DeviceKind, DeviceStatus, CommandAction, ...
│   │   ├── Generics/                TelemetryPacket<T>                         (Part 1)
│   │   ├── Aggregation/             MeterReading (overloaded operators)        (Part 1)
│   │   ├── Models/                  SensorRegistration, DeploymentNode, ...    (Part 1)
│   │   ├── Validation/              DeploymentTreeValidator (recursive)        (Part 1)
│   │   ├── Search/                  DeviceQuery - search/filter rules          (Part 2)
│   │   └── Dto/                     request/response and live-update contracts
│   ├── SmartX.Api/                  ASP.NET Core Minimal API
│   │   ├── Endpoints/               one file per route group
│   │   ├── Services/                Part 1 in-memory repositories and telemetry store
│   │   ├── RealTime/                Part 2: registry, queues, history, errors, commands,
│   │   │                            simulator, SSE broadcaster, recommendation engine
│   │   ├── Topology/                Part 3: AVL tree, reading history, mesh graph,
│   │   │                            BFS and Prim's algorithm, simulated layout
│   │   ├── SmartX.Api.http          ready-made example requests
│   │   └── appsettings.json         Part 2 thresholds and timings ("RealTime" section)
│   └── SmartX.Client/               Blazor WebAssembly dashboard
│       ├── Layout/                  sidebar navigation (three pillars) + live indicator
│       ├── Pages/                   Part 1 pages, Part 2 Stream* pages, Topology page
│       ├── Components/              DeviceCard, SuggestedActionsPanel, ApiErrorBanner, ...
│       └── Services/                SmartXApiClient (HTTP) and LiveDashboardState (SSE)
└── tests/
    └── SmartX.Tests/                xUnit tests for the Part 2 and Part 3 logic
```

### How a telemetry packet flows (Part 2)

```
device / simulator ──POST /api/stream/telemetry──▶ TelemetryProcessor.Enqueue
    validate (MAC format, Dictionary lookup, sensor type, value range)
    classify severity ──▶ Info: Queue<T>      Warning/Critical: PriorityQueue
                                    │
ProcessingWorker (every 300 ms) ───▶ dequeue: priority first (with starvation guard)
    update device state ─ add to HistoryLog (SortedDictionary)
    raise/clear active errors (HashSet) ─ forward to the Part 1 pulse feed and heatmap
    publish to LiveUpdateBroadcaster ──SSE /api/stream/live──▶ dashboard
```

### How the topology page gets its data (Part 3)

```
Part 2 device list (DeviceRegistry) ──▶ MeshTopology.Sync()
    one node per device + the gateway, online/offline from the device status
    links between nodes within radio range (positions from SimulatedLayout)
                                    │
topology page ──GET /api/topology, /route, /mst, /bfs──▶ MeshGraph + MeshAlgorithms
    BFS (Queue) for reachability and fewest-hops routes
    Prim (PriorityQueue) for the minimum spanning tree
                                    │
TelemetryProcessor ──every processed reading──▶ ReadingHistory (one AVL tree per sensor)
topology page ──GET /api/topology/readings/{id}?from=&to=──▶ tree range search
```

All data is held **in memory** (no database), as in Part 1. Restarting the API
resets devices, history, commands, searches and the stored readings.

## Requirements and setup

| Software | Version | Needed for |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** (`dotnet --version` should show 10.x) | building, running and testing |
| A modern browser | Chrome, Edge or Firefox | the dashboard (needs streaming `fetch` support for SSE) |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | optional | running both parts in containers |

Dependencies are NuGet packages that restore automatically. No environment
variables are needed for a local run.

```bash
git clone https://github.com/St10443269/prog7312-smartx.git
cd prog7312-smartx
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```

## Running the application

The API and the dashboard are two separate processes. Start both.

**Terminal 1 - API** (http://localhost:5190):

```bash
dotnet run --project src/SmartX.Api --urls http://localhost:5190
```

**Terminal 2 - dashboard** (http://localhost:5122):

```bash
dotnet run --project src/SmartX.Client --urls http://localhost:5122
```

Open **http://localhost:5122**. The sidebar should show
**"Live stream connected"** within a second or two, and the Live Dashboard
should start showing readings. If the API is not running, the pages show an
"API problem" banner and the sidebar shows "Reconnecting...". The client keeps
retrying and recovers by itself when the API comes back.

In **Visual Studio**, you can instead right-click the solution, choose
*Configure Startup Projects* -> *Multiple startup projects*, and set both
`SmartX.Api` and `SmartX.Client` to *Start*. Their launch profiles already use
ports 5190 and 5122.

### Running with Docker

Start Docker Desktop first, then:

```bash
docker compose up --build
```

- API: http://localhost:5190
- Dashboard: http://localhost:8080

The client is a static Blazor WebAssembly build served by nginx. The API runs
in its own container. The browser calls the API (and its SSE stream) directly
on port 5190, and `ClientOrigins__0` in `docker-compose.yml` allows the
dashboard's origin through CORS. Stop with `Ctrl+C`, then `docker compose down`.

## Configuration

| Setting | File | Purpose |
|---|---|---|
| `ApiBaseUrl` | `src/SmartX.Client/wwwroot/appsettings.json` | Where the dashboard finds the API (default `http://localhost:5190`). |
| `ClientOrigins` | API `appsettings.json` or env var `ClientOrigins__0` | Browser origins allowed by CORS (defaults to `http://localhost:5122` and `https://localhost:7163`). |
| `RealTime:Thresholds` | `src/SmartX.Api/appsettings.json` | Alert thresholds: moisture critical < 20 %, warning < 30 %; power spike > 1500 W, warning > 1000 W; controller overheat > 40 °C, warning > 35 °C; disconnected after 8 s of silence. |
| `RealTime:Processing` | same | Worker interval (300 ms), batch size (50), starvation guard `MaxConsecutivePriority` (5), history cap (5000 entries), and `MaxReadingsPerSensor` (5000 readings kept per sensor in the Part 3 AVL tree). |
| `RealTime:Simulator` | same | Start on launch, tick interval (1000 ms), random dropout chance. |
| `RealTime:Recommendations` | same | Pattern window (5 min), minimum occurrences (2), minimum confidence (0.5), minimum history (3), recurring-fault threshold (3), lookback (60 min). |

Any of these can also be overridden with environment variables, e.g.
`RealTime__Thresholds__MoistureCriticalPct=15`.

The Part 3 topology settings are not in `appsettings.json`. They are the
simulated layout, so they live in code, in
[`SimulatedLayout.cs`](src/SmartX.Api/Topology/SimulatedLayout.cs): the
radio range (30 m) and where each device sits on the diagram. The link cost
formula is in [`MeshTopology.cs`](src/SmartX.Api/Topology/MeshTopology.cs)
(`BuildCost`).

## Using the application

The sidebar groups the pages by pillar. Navigation is client-side routing, so
moving between pages never reloads the app. The live SSE connection, the
latest device states, your search filters and the selected history device all
carry over between pages.

| Pillar | Page | What it does |
|---|---|---|
| 1 | **Telemetry** | Part 1 pulse feed, heatmap and gauge. Now also shows the live simulated readings. |
| 1 | **Sensors** | Register a device (every field is checked, and each wrong field shows its own message) and attach files. Each device has a *Files* panel with an upload box, a list of its files and a download link for each. A newly registered device also joins the Part 2 network. |
| 1 | **Deployment Tree** | Recursive deployment-tree validation. |
| 2 | **Live Dashboard** (`/stream`) | Device cards with live readings, status, alerts and ON/OFF buttons; search and filters; active errors; live event stream; top suggestions; simulator on/off. |
| 2 | **Commands & Undo** (`/stream/commands`) | Send commands, see the undo stack, undo, simulate an outage, full command history. |
| 2 | **Queues & Alerts** (`/stream/processing`) | Queue depths, pause/resume, the priority demo burst, the processing order, active errors with duplicate counts, send your own packets. |
| 2 | **History Log** (`/stream/history`) | The chronological log with a chart, filters and two ordering demos. |
| 2 | **Suggested Actions** (`/stream/insights`) | All recommendations with their reasons, how they are generated, and recent searches. |
| 3 | **Network Topology** (`/topology`) | Diagram of the sensor mesh. Click a sensor for its details, its two routes to the gateway and its past readings; click a line to switch a link off; show the spanning tree; run a breadth-first search; take a sensor offline. |

### Simulated devices

| Device | MAC | Kind | Zone | Reports |
|---|---|---|---|---|
| Moisture Sensor B | `24:6F:28:B1:00:01` | MoistureSensor | Zone 1 | `SoilMoisturePct` (dries fast - triggers alerts soon after start-up) |
| Pump A | `24:6F:28:A1:00:01` | WaterPump | Zone 1 | `FlowLitresPerMin`. When ON, Zone 1 moisture rises. |
| ESP32 Controller Z1 | `24:6F:28:C1:00:01` | Esp32Controller | Zone 1 | `TemperatureC` (occasional overheat) |
| Moisture Sensor D | `24:6F:28:B2:00:01` | MoistureSensor | Zone 2 | `SoilMoisturePct` |
| Pump C | `24:6F:28:A2:00:01` | WaterPump | Zone 2 | `FlowLitresPerMin` (starts ON) |
| Smart Plug 1 / 2 | `24:6F:28:D3:00:01` / `:02` | SmartPlug | Zone 3 | `PowerWatts` (occasional spike; ~0 W when OFF) |
| Relay R1 | `24:6F:28:E3:00:01` | Relay | Zone 3 | `RelayClosed` (0/1) |
| Power Meter M1 | `24:6F:28:F3:00:01` | PowerMeter | Zone 3 | `PowerWatts` (sum of the Zone 3 plugs + base load) |

Readings depend on the real device state, so commands have visible effects:
turn Pump A on and Moisture Sensor B rises, turn a plug off and its draw drops
to ~0 W.

## Part 2 data structures - where and why

| Structure | Where | What it does and why it fits |
|---|---|---|
| **`Queue<T>`** | [`TelemetryProcessor`](src/SmartX.Api/RealTime/TelemetryProcessor.cs) `_normalQueue` | Normal (Info) telemetry. FIFO means readings are processed in the order they arrived, so each device's values are applied in sequence. Enqueue and dequeue are O(1). |
| **`PriorityQueue<TElement, TPriority>`** | [`TelemetryProcessor`](src/SmartX.Api/RealTime/TelemetryProcessor.cs) `_priorityQueue` | Warnings and critical alerts. Priority = `(level, arrival number)`, where level 0 = Critical (power spike > 1500 W, moisture < 20 %, overheat > 40 °C) and 1 = Warning. It always dequeues the smallest, so Critical comes before Warning, and the arrival number keeps equal priorities first-come-first-served. The worker serves this queue before the normal one. |
| *Starvation guard* | `TryDequeueNext` | After `MaxConsecutivePriority` (default 5) alerts in a row, one waiting normal message is processed before alerts continue, so a flood of alerts can never block normal telemetry forever. |
| **`Stack<T>`** | [`CommandService`](src/SmartX.Api/RealTime/CommandService.cs) `_undoStack` | Every accepted command that changed a device is pushed. Undo pops the most recent (LIFO) and restores the device's previous state. Push, Peek and Pop are O(1). |
| **`Dictionary<TKey, TValue>`** | [`DeviceRegistry`](src/SmartX.Api/RealTime/DeviceRegistry.cs) `_devicesByMac` | Registered devices keyed by normalised MAC address. Every incoming packet is matched to its device with an O(1) average hash lookup instead of scanning a list. Unknown MACs return `false` (HTTP 404), never an exception. |
| **`SortedDictionary<TKey, TValue>`** | [`HistoryLog`](src/SmartX.Api/RealTime/HistoryLog.cs) `_entries` | Readings, commands, alerts and recoveries, kept sorted by key on every insert (balanced tree, O(log n)). The key is [`HistoryKey(Timestamp, Sequence)`](src/SmartX.Api/RealTime/HistoryLog.cs): it compares by time, then by arrival sequence. Readings with **identical timestamps are both kept** (different sequence numbers), and late or buffered readings sort into the right place. The log is read front-to-back for timelines and charts without re-sorting. |
| **`HashSet<T>`** | [`ActiveErrorTracker`](src/SmartX.Api/RealTime/ActiveErrorTracker.cs) `_active` | The currently active errors as `(MAC, code)` pairs, e.g. `(Smart Plug 1, POWER_SPIKE)`. `Add` returns `false` for an error that is already active, so repeats are counted as suppressed duplicates instead of raising new alerts. On recovery the entry is `Remove`d, so the set only ever means "active now". Past alerts stay in the history log. |

### Undo - rules and limitations

- Undo reverts the **top** command only if the device is still in the state
  that command left it in. If a newer reading shows the device changed since
  (e.g. a plug switched back on locally), the undo is **refused** and that
  stale entry is discarded. Restoring it would overwrite a newer real change.
- If the device is **disconnected**, undo **fails** and the entry stays on the
  stack so it can be retried when the device reconnects.
- An undo is not pushed onto the stack, so pressing Undo twice reverts the last
  two commands (it does not "redo").
- A command that changes nothing (e.g. turning on a device that is already
  on) is accepted but not pushed, because there is nothing to undo.
- Readings captured **before** a command cannot overwrite the state that
  command set. They were already queued when the switch happened.

## Real-time updates (Server-Sent Events)

- **Why SSE:** updates only flow API -> browser. Commands go the other way as
  normal HTTP POSTs. SSE is plain HTTP, is built into ASP.NET Core 10
  (`TypedResults.ServerSentEvents`), and needs no extra library. The client
  reads it with `HttpClient` and browser response streaming, so SignalR was
  not needed.
- **Server** ([`StreamEndpoints`](src/SmartX.Api/Endpoints/StreamEndpoints.cs),
  [`LiveUpdateBroadcaster`](src/SmartX.Api/RealTime/LiveUpdateBroadcaster.cs)):
  - Each connection gets its own bounded channel that drops its oldest update
    when full, so a slow browser can't hold up the API.
  - Every new connection starts with a full snapshot of devices, errors and
    queues.
  - A heartbeat is sent after 15 s of silence.
  - When the browser disconnects, the request is cancelled and the connection
    is unsubscribed.
- **Client** ([`LiveDashboardState`](src/SmartX.Client/Services/LiveDashboardState.cs)):
  - There is one connection per browser tab, shared by every page.
  - If the stream drops, or nothing arrives for 40 s, it reconnects with a
    backoff of 1, 2, 4 ... up to 10 s, and the snapshot then brings it up to
    date.
  - Incoming updates only mark the data as changed. Pages re-render at most
    every 250 ms, so a burst of readings doesn't cause a burst of renders.

## Recommendation engine

[`RecommendationEngine`](src/SmartX.Api/RealTime/RecommendationEngine.cs)
(`GET /api/insights`) uses only data the app has recorded. Each suggestion
shows the device, the issue, **why** it was generated, the supporting data, and
a suggested action. It is **never executed automatically**. If a suggestion
includes a command, it only runs when the user presses its button.

1. **Behaviour patterns (alert -> response).** For every alert in the last 60
   minutes, the user's commands and searches made **while that alert was
   active** count as responses to it. "While active" means until the device
   recovered, capped at 5 minutes. Pairs of
   (trigger device + error, response device + action) are tallied in a
   Dictionary, and a HashSet per alert stops double counting. A pair becomes a
   pattern when it has:
   - **support ≥ 2:** it was seen at least twice;
   - **confidence ≥ 50 %:** it followed at least half of that trigger's alerts.

   This is the support/confidence idea from association-rule mining. Example:
   *"Moisture Sensor B raised MOISTURE_LOW 3 times; 2 of those times (67 %)
   you switched Pump A ON."* If the trigger is active again right now, the
   suggestion offers that command.
2. **Recurring faults.** At least 3 alerts of the same type for one device in
   the lookback window.
3. **Out-of-range readings.** Each connected device's latest reading checked
   against the thresholds (e.g. low moisture -> turn on the pump in that zone).
4. **Disconnected devices.** Devices with no telemetry within the timeout.

**Score** (used to sort the list): patterns score
`50 + 40 × confidence (+30 if the trigger is active now) + min(count, 10)`.
Recurring faults score `40 + 5 × count`, critical out-of-range readings 45,
disconnected devices 35, and warning-level readings 25.

With fewer than 3 recorded searches and commands, the page says there is not
enough history to detect patterns yet, but still shows the fault-based
insights. Every threshold is configurable under `RealTime:Recommendations`.

## Part 3 data structures and algorithms - where and why

| Structure / algorithm | Where | What it does and why it fits |
|---|---|---|
| **AVL tree** | [`AvlTree<TKey, TValue>`](src/SmartX.Api/Topology/AvlTree.cs), used by [`ReadingHistory`](src/SmartX.Api/Topology/ReadingHistory.cs) | Every processed reading is stored in a tree for its sensor, keyed by timestamp. After each insert the tree rotates itself so the two sides of every node differ in height by at most 1, so it stays shallow even though timestamps arrive in order (a plain binary search tree would turn into a long chain). A search for "Sensor A between 14:00 and 16:00" only walks the part of the tree inside that range: O(log n + number of matches). |
| *Duplicate timestamps* | `AvlTree.Insert` | If two readings have the same timestamp, the second is added to the list on the same node, so neither is lost. |
| **Graph (adjacency list)** | [`MeshGraph`](src/SmartX.Api/Topology/MeshGraph.cs) | Each node (the gateway or a sensor) is a vertex, each wireless link is an edge. It is a `Dictionary` from a node's id to its neighbours, so finding the neighbours of a node does not mean checking every link. It can add nodes and links, list the neighbours of a node, and switch a node or link on or off. |
| **Breadth-first search (BFS)** | [`MeshAlgorithms.Bfs`](src/SmartX.Api/Topology/MeshAlgorithms.cs) | Uses a `Queue<T>` to visit the start node, then everything 1 hop away, then 2 hops, and so on. Because it goes level by level, the first time it reaches a node is by the route with the fewest hops. Nodes it never reaches have no route. Used for the reachable and unreachable lists, the fewest-hops route and the "Run search" button. |
| **Prim's algorithm** | [`MeshAlgorithms.Prim`](src/SmartX.Api/Topology/MeshAlgorithms.cs) | Builds the minimum spanning tree: start at the gateway and keep adding the cheapest link that reaches a new node. |
| **`PriorityQueue` (min-heap)** | inside `Prim` | The links waiting to be considered go into .NET's built-in `PriorityQueue`, which is a binary min-heap, so the cheapest link comes out first without re-sorting every time. I used the built-in one because it is already used for alerts in Part 2, and it is O(log n) per add and remove. |
| **`HashSet<T>`** | `Bfs` and `Prim` | Remembers which nodes are already visited or already in the tree. |
| **`LinkCost` (operator overloading)** | [`LinkCost`](src/SmartX.Api/Topology/LinkCost.cs) | A small struct with `+` and `<` (like `MeterReading` in Part 1). Costs are added up with `+`, and the priority queue orders links with `<`. |

**Why an AVL tree instead of a list or the `SortedDictionary` from Part 2?** A
`List` has to be checked item by item for a time range. A `SortedDictionary`
keeps things in order, but it has no way to jump straight to a range. The AVL
tree can skip whole branches, and the page shows this: with a 30-second range it
reports how many readings it found and how many stored readings the tree
actually looked at (for example 35 of 203).

**The core of the range search** (from `AvlTree.cs`):

```csharp
visited++;

// only go left if smaller keys could still be in range
if (from.CompareTo(node.Key) < 0)
    Collect(node.Left, from, to, items, ref visited);

if (from.CompareTo(node.Key) <= 0 && node.Key.CompareTo(to) <= 0)
    items.AddRange(node.Values);

// only go right if larger keys could still be in range
if (to.CompareTo(node.Key) > 0)
    Collect(node.Right, from, to, items, ref visited);
```

**The core of Prim's algorithm** (from `MeshAlgorithms.cs`):

```csharp
while (waiting.TryDequeue(out var cheapest, out var cost))
{
    // the node may have joined the tree through a cheaper link already
    if (inTree.Contains(cheapest.To))
        continue;

    inTree.Add(cheapest.To);
    treeLinks.Add(cheapest.Link);
    total += cost;
    AddLinksFrom(graph, cheapest.To, inTree, waiting);
}
```

### The spanning tree is not the shortest route

A minimum spanning tree keeps the **total cost of the whole network** as low as
possible while still connecting every sensor that can reach the gateway. It does
**not** give each sensor its own cheapest or shortest route. In the default
layout, Moisture Sensor B reaches the gateway in 2 hops (cost about 8.3) through
the full network, but the spanning-tree route is 3 hops (cost about 9.5). The
tree as a whole is much cheaper, though: about 31.7 for 9 links, against about
69.7 for all 15 links. The page shows both routes for each sensor so the
difference is easy to see.

## Network topology and routing (Part 3)

The **Network Topology** page draws the sensor mesh and everything on it comes
from the graph on the server. The page asks the API for the nodes, links, routes
and spanning tree, then draws what comes back.

- **Nodes.** The gateway plus one node per device in the Part 2 registry. A
  device registered later on the Sensors page appears on the diagram by itself.
  A node is **online** or **offline** from the device status Part 2 already
  tracks, so a device that stops reporting goes red here as well.
- **Links.** Two nodes are linked when they are within the radio range
  (30 m). Some sensors are too far from the gateway, so they can only reach it by
  going through other sensors. That is what makes it a mesh.
- **Link cost.** An estimate of the energy needed to send a message over the
  link. Radio transmit energy grows roughly with the square of the distance
  (Heinzelman, Chandrakasan and Balakrishnan, 2000), so
  `cost = distance² / 100`. Short links are cheap and long links are expensive.
- **Simulated.** There are no real radios, so the positions on the diagram
  are fixed in [`SimulatedLayout.cs`](src/SmartX.Api/Topology/SimulatedLayout.cs)
  and nothing is random. The page says so at the top. To use real signal data
  later, only that file and the cost formula need to change.
- **A link works only if** it is switched on **and** both of its ends are
  online. Routes, BFS and the spanning tree only use working links.

**What the colours mean:**

| On the diagram | Meaning |
|---|---|
| Green circle | Online and can reach the gateway |
| Red circle | Offline |
| Amber circle | Online but **cut off**: every route to the gateway is broken |
| Purple square | The gateway |
| Cyan line | A working link |
| Thick green line | A link in the spanning tree (after pressing *Show spanning tree*) |
| Thick amber line | The route chosen for the selected sensor |
| Red dotted line | A link that has been switched off |
| Grey dashed line | A link that is on, but one end is offline |

**What happens when something fails:**

- If a device goes offline or a link is switched off, the routes and the
  spanning tree are worked out again on the next request, using only what is
  still working. Another route is used if one exists.
- If there is no other route, the sensor is shown as **cut off**, its routes say
  "No route", BFS lists it under "Cannot be reached", and the spanning tree
  leaves it out. The page never draws an invalid route.
- An empty network (only the gateway), or one with a single device, works too:
  the spanning tree is empty or has one link.

## Testing each feature by hand

Every request below is also in
[`src/SmartX.Api/SmartX.Api.http`](src/SmartX.Api/SmartX.Api.http). Open it in
Visual Studio and click *Send request*. The curl examples are for Git Bash.
In PowerShell, use the `.http` file instead.

**Simulate telemetry packets.** The simulator sends one reading per device
every second automatically. To send your own:

```bash
curl -X POST http://localhost:5190/api/stream/telemetry -H "Content-Type: application/json" \
     -d '{"macAddress":"24:6F:28:B1:00:01","sensorType":"SoilMoisturePct","value":45}'
```

Or use **Queues & Alerts -> Send a telemetry packet**.

**Trigger a high-priority alert.** Send moisture `12` (critical drop) or plug
power `1900` (severe spike). The response says
`Accepted into the priority queue (priority 0)`.

**Queue + PriorityQueue.** On **Queues & Alerts**, click *Pause processing*,
then *Inject priority demo burst*, then *Resume processing*. The
*Priority demo result* table shows that the critical alert, sent 10th of 10,
was processed **1st**, the warning **2nd**, and the eight normal readings
after them in the order they were sent. The same is available through the API:
`POST /api/stream/processing/pause`, `POST /api/stream/demo/priority-burst`,
`POST /api/stream/processing/resume`, then `GET /api/stream/processing`.

**Stack and undo.** On **Commands & Undo**:

1. Turn Pump A on, then turn Smart Plug 1 off. The stack shows the plug on top.
2. *Undo last command* restores the plug.
3. Undo again to restore the pump.
4. Undo once more: it is refused with "stack is empty".
5. To see the disconnect rule, send a command, click *Simulate 20s outage*,
   wait about 8 s, and try Undo. It **fails** and the entry stays on the stack.
   Once the device reconnects, Undo works.

**Dictionary.** `GET /api/devices/24-6f-28-a1-00-01` finds Pump A even with a
different case and separator. Unknown MACs get 404 in telemetry and commands.

**SortedDictionary.** On **History Log**, pick a device, then:

- *Send 2 readings with the same timestamp*: both rows appear with the same
  time and consecutive sequence numbers, labelled "same timestamp - kept".
- *Send a late reading*: a reading captured 30 s ago is inserted 30 s back in
  the timeline, not at the end.

**HashSet.** Wait for Moisture Sensor B to drop below 20 % (about 15 s after
start-up), or send moisture `12` twice with *Send twice*. **Queues & Alerts ->
Active errors** shows one `MOISTURE_LOW` entry whose *duplicates suppressed*
count keeps rising. The history log has only one Alert. Turn Pump A on: once
moisture is back above 30 % the error disappears and a Recovery entry is
logged.

**Disconnected devices.** Click *Simulate 20s outage* (Commands page), or
`POST /api/simulator/devices/{mac}/disconnect?seconds=20`. After 8 s the device
shows **Disconnected**, gets a `DISCONNECTED` error, commands to it fail, and it
recovers on its own.

**Search and filter.** On the **Live Dashboard**, type `pump` and press
*Search*, or pick Kind / Category / Status / *Alerts only*. The search runs on
the API (and is recorded for recommendations) and is re-applied live: e.g.
with Status = Disconnected, a device appears in the list the moment it drops
out. Filters are kept when you navigate away and back.

**Recommendations.** Wait for Moisture Sensor B to raise `MOISTURE_LOW`, then
search for `Pump A` and turn Pump A on. When it recovers, turn the pump off.
Repeat once more. On the third drop, **Suggested Actions** shows *"Repeated
response pattern: MOISTURE_LOW on Moisture Sensor B -> switched Pump A ON"*
with a *Turn on Pump A* button. Before any history exists it reports "Not
enough history to detect behaviour patterns yet".

**Network diagram (graph).** Open **Network Topology**. You should see the
gateway (purple square) with the nine devices around it. The lines are the
links. Click a line to see its two ends and its cost.

**BFS.** Press *Run search from gateway*. Numbers appear on the nodes in the
order the search visited them, and the *Search (BFS)* panel lists each node with
how many hops away it is. Select a sensor and press *Run search from here* to
see what that sensor can reach.

**Route to the gateway.** Click **Moisture Sensor B**. The *Route to the gateway*
panel shows the fewest-hops route (2 hops) and the route through the spanning
tree (3 hops). *Show on diagram* draws either one in amber.

**Minimum spanning tree (Prim).** Press *Show spanning tree*. The links chosen
by the algorithm turn thick green, the others fade, and the *Spanning tree*
panel compares the two networks: all links (15 links, cost about 69.7) against
the tree (9 links, cost about 31.7).

**Rerouting.** Click a link on a sensor's route and press *Switch this link
off*. The route changes to another way round, and the spanning tree is
recalculated. Switch every link of a sensor off and it turns amber (cut off),
its routes say "No route", and BFS lists it under "Cannot be reached". Press
*Switch this link on* to restore it.

**Offline devices.** Select a sensor and press *Simulate 20 s outage*. After
about 8 seconds it turns red, the routes of sensors that used it change, and it
comes back by itself after the outage.

**AVL tree (historical readings).** Select a sensor, then use the *Historical
readings* panel. Pick a time range (or press *Last 30 sec*) and press *Search*.
The panel lists the readings and says how many the tree looked at out of the
total stored, for example "found 30 readings, the tree looked at 35 of the 203
stored readings". The API does the same:
`GET /api/topology/readings/{mac}?from=...&to=...`.

**Error handling.** Stop the API while the dashboard is open: pages show an
"API problem" banner, the sidebar shows *Reconnecting...*, and nothing
crashes. Start the API again and the stream reconnects by itself.

## API reference

Base URL `http://localhost:5190`. Enums are sent as strings
(e.g. `"TurnOn"`). OpenAPI JSON is at `/openapi/v1.json` in Development.

### Part 3

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/topology` | The whole network: nodes, links, who is online and who can reach the gateway |
| GET | `/api/topology/route/{id}` | One node's route to the gateway: fewest hops and through the spanning tree, with costs |
| GET | `/api/topology/mst` | Minimum spanning tree (Prim's algorithm) with its total cost compared to all links |
| GET | `/api/topology/bfs?from={id}` | Breadth-first search from a node (the gateway if `from` is left out) |
| POST | `/api/topology/links/up?a={id}&b={id}&isUp=true\|false` | Switch a link on or off |
| GET | `/api/topology/readings/{id}?from=&to=` | Readings for one sensor in a time range (AVL tree search). Defaults to the last 15 minutes. |

`{id}` is the device's MAC address (e.g. `24:6F:28:B1:00:01`) or `GATEWAY`.
Unknown nodes return 404, and a `from` later than `to` returns 400.

### Part 2

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/devices?search=&kind=&category=&status=&alertsOnly=&record=` | Search and filter devices (`record=false` skips search history) |
| GET | `/api/devices/{mac}` | One device by MAC |
| GET | `/api/devices/searches?take=` | Recent searches |
| POST | `/api/stream/telemetry` | Submit a packet `{ macAddress, sensorType, value, capturedAtUtc? }` -> 202 / 400 / 404 |
| GET | `/api/stream/live` | **SSE** stream of live updates (`text/event-stream`) |
| GET | `/api/stream/processing` | Queue depths and processing order |
| POST | `/api/stream/processing/pause` / `resume` | Pause or resume the processing worker |
| POST | `/api/stream/demo/priority-burst` | Queue 8 normal + 1 warning + 1 critical at once |
| POST | `/api/commands` | Command `{ macAddress, action: "TurnOn"\|"TurnOff" }` -> 200 accepted / 400 rejected / 404 unknown / 503 failed |
| POST | `/api/commands/undo` | Undo the most recent command -> 200 / 409 / 503 |
| GET | `/api/commands?take=` | Command and undo history, newest first |
| GET | `/api/commands/stack` | Undo stack, top first |
| GET | `/api/history?mac=&kind=&minutes=&take=` | Chronological history (oldest first) |
| GET | `/api/errors` | Active errors with duplicate counts |
| GET | `/api/insights` | Suggested actions report |
| GET | `/api/simulator` | Simulator status |
| POST | `/api/simulator/start` / `stop` | Start or stop the simulator |
| POST | `/api/simulator/devices/{mac}/disconnect?seconds=` | Simulate a device outage |

Every packet sensor type: `SoilMoisturePct` (0-100), `PowerWatts` (≥ 0),
`FlowLitresPerMin` (≥ 0), `RelayClosed` (0 or 1), `TemperatureC` (-40 to 125).

### Part 1

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/sensors` | List registered sensors |
| POST | `/api/sensors` | Register a sensor (also joins the Part 2 registry). The MAC must be six hex pairs with one kind of separator and is stored as `AA:BB:CC:DD:EE:FF`. Bad fields give 400 with a message for each, a MAC that is already registered gives 409. |
| GET | `/api/sensors/{id}/attachments` | List the files attached to a sensor, newest first |
| POST | `/api/sensors/{id}/attachments` | Upload a file attachment (multipart). Allowed types: `.json .yaml .yml .txt .log .cfg .conf .png .jpg .jpeg .pdf`, up to 25 MB. |
| GET | `/api/sensors/{id}/attachments/{attachmentId}` | Download an attachment |
| POST | `/api/telemetry` | Ingest a single reading |
| POST | `/api/telemetry/batch` | Ingest a jagged per-device sample batch |
| POST | `/api/telemetry/seed` | Generate mock readings for registered sensors |
| GET | `/api/telemetry/feed` | Recent telemetry pulse feed |
| GET | `/api/telemetry/aggregate/{nodeId}` | Aggregated power load for a node |
| GET | `/api/telemetry/grid` | Zone-load heatmap snapshot |
| POST | `/api/deployment/validate` | Recursively validate a deployment tree |
| GET | `/api/deployment/sample` | A ready-made sample deployment tree |

## Automated tests

```bash
dotnet test tests/SmartX.Tests
```

> On Windows, **stop the API first**. A running API locks its DLLs, and the
> test build then fails with "file is being used by another process".

148 xUnit tests run against the real services (56 for Part 2, 54 for Part 3
and 38 for the Part 1 sensor rules, file rules and sensor store), wired up with a fake clock and no
web server. The Part 2 tests cover:

- FIFO order, critical-before-normal, priority ties and the starvation guard
  ([`TelemetryProcessorTests`](tests/SmartX.Tests/TelemetryProcessorTests.cs)).
- Malformed packets and unknown devices.
- MAC lookups and duplicate registration
  ([`DeviceRegistryTests`](tests/SmartX.Tests/DeviceRegistryTests.cs)).
- Chronological history, equal timestamps, late packets and the cap
  ([`HistoryLogTests`](tests/SmartX.Tests/HistoryLogTests.cs)).
- Duplicate suppression, recovery and disconnects
  ([`ActiveErrorTrackerTests`](tests/SmartX.Tests/ActiveErrorTrackerTests.cs)).
- Commands, LIFO undo and every undo refusal case
  ([`CommandServiceTests`](tests/SmartX.Tests/CommandServiceTests.cs)).
- Search and filter rules ([`DeviceQueryTests`](tests/SmartX.Tests/DeviceQueryTests.cs)).
- Patterns, thresholds, "not enough data", and the guarantee that nothing is
  auto-executed ([`RecommendationEngineTests`](tests/SmartX.Tests/RecommendationEngineTests.cs)).

The Part 3 tests cover:

- The AVL tree: all four rotation cases, 1000 keys inserted in order staying
  balanced, duplicate keys, range searches (inclusive ends, empty and backwards
  ranges, and a big tree where only a few nodes are visited), removing the
  oldest, and the empty tree
  ([`AvlTreeTests`](tests/SmartX.Tests/AvlTreeTests.cs)).
- Historical readings: one sensor's readings between two times, same
  timestamps, late arrivals, the size limit, unknown sensors, and a processed
  packet ending up in the tree (`ReadingHistoryTests`, same file).
- The graph, BFS and Prim's algorithm on small networks worked out by hand:
  bad additions, offline nodes and switched-off links, BFS levels and
  unreachable nodes, fewest-hops routes, the spanning tree total (7 on the
  example network), the same total from any start node, the tree route being
  longer than the direct route, recalculating when a link fails, leaving out
  offline nodes, and an empty network or a single node
  ([`MeshGraphTests`](tests/SmartX.Tests/MeshGraphTests.cs)).
- The topology service on the real device list: routes through another node,
  a sensor being cut off when its relay goes offline and coming back, switching
  links off and on, spanning tree changes, BFS lists, devices added later, and
  networks with no devices or one device
  ([`MeshTopologyTests`](tests/SmartX.Tests/MeshTopologyTests.cs)).

The Part 1 sensor and file rules are covered by
[`SensorInputTests`](tests/SmartX.Tests/SensorInputTests.cs): good and bad MAC
addresses (wrong length, not hex, no separators, mixed separators), blank and
too-long fields, an unknown category, which file types are allowed, the size
limit, and how file sizes are shown. [`SensorRepositoryTests`](tests/SmartX.Tests/SensorRepositoryTests.cs) checks the MAC lookup in the Part 1 sensor store.

The Blazor pages are not covered by automated UI tests. They were checked by
hand in the browser.

## Troubleshooting

| Problem | Cause and fix |
|---|---|
| `The provided file path does not exist: src/SmartX.Api` | The terminal is in the wrong folder. Run the commands from the project folder (the one containing `SmartX.slnx`), e.g. `cd prog7312-smartx`. |
| `address already in use` / the API or dashboard won't start | Something is already running on port 5190 (API) or 5122 (dashboard), often an earlier copy. Close it (Ctrl+C in its terminal) or choose other ports with `--urls`. If you change the API port, change `ApiBaseUrl` in `src/SmartX.Client/wwwroot/appsettings.json` too. |
| `dotnet test` or `dotnet build` fails with "file is being used by another process" | The API is running and has locked its DLLs. Stop the API first, then build or test. |
| The pages show an "API problem" banner and the sidebar says *Reconnecting* | The dashboard can't reach the API. Check the API is running on port 5190. It reconnects by itself once the API is back. |
| The browser blocks requests to the API (CORS error in the console) | The dashboard is running on an address the API doesn't allow. Add it to `ClientOrigins` in the API's `appsettings.json` (or set the `ClientOrigins__0` environment variable). |
| `docker compose up --build` fails to connect to Docker | Docker Desktop isn't started. Start it, wait until it says it is running, then try again. |
| A device on the topology page is red even though nobody switched it off | The simulator drops a device at random now and then, to test offline handling. It comes back by itself. To turn this off, set `RealTime:Simulator:RandomDisconnectChance` to `0` in `appsettings.json`. |
| Moisture Sensor B keeps showing an alert | It is set up to dry out quickly so alerts appear soon after start-up. Turn Pump A on to raise its moisture. |
| The readings table is empty | The API has only just started, or the range doesn't include any readings. Press *Last 30 sec*. Readings are kept in memory, so they are lost when the API restarts. |

## Limitations and assumptions

- **No hardware and no persistence.** All devices are simulated, and all state
  is in memory and lost when the API restarts.
- **Single API instance.** The queues, stack and sets live in one process.
  Running several API instances would need a shared store or message broker.
- **Severity rules are threshold-based** per sensor type. Part 1's
  delta-based severity on the Telemetry page is unchanged and separate.
- **Errors clear only when fully back to normal.** An active error clears on
  an Info-level reading, so moisture must rise above 30 %, not just above
  20 %. This stops alerts flickering around the threshold.
- **Global undo stack.** There is one undo stack shared by all users, and no
  authentication. Anyone with the dashboard can send commands.
- **Searches only count for patterns when specific.** A search contributes to
  pattern detection only if it matched 1-3 devices. A search for everything
  says nothing about a specific device.
- **The mesh is simulated.** Device positions are fixed in `SimulatedLayout.cs`,
  links come from a made-up 30 m radio range, and link costs are an estimate
  from distance. They are not measurements from real radios. Real signal data
  would replace `SimulatedLayout.cs` and the cost formula.
- **One gateway, links decided once.** There is a single gateway. A device's
  links are worked out when it first appears, so links are not recalculated if
  positions change.
- **Switched-off links are not saved.** A link you switch off on the topology
  page stays off until you switch it on or the API restarts.
- **Routes use hops or the spanning tree, not the cheapest path per sensor.**
  The page shows the fewest-hops route (BFS) and the route through the minimum
  spanning tree (Prim). It does not work out the cheapest route for each sensor
  separately (that would need Dijkstra's algorithm).
- **Readings are kept in memory.** At most 5000 readings are kept per sensor in
  the AVL tree (the oldest are removed beyond that), and all of them are lost
  when the API restarts.

## Part 1 C# concepts

| Requirement | Location |
|---|---|
| **Generics** | [`TelemetryPacket<T>`](src/SmartX.Shared/Generics/TelemetryPacket.cs): a single `struct`-constrained generic class handles float/int/bool telemetry uniformly without boxing. |
| **Operator overloading** | [`MeterReading`](src/SmartX.Shared/Aggregation/MeterReading.cs): overloads `+`, `-`, `==`, `!=`, `>`, `<`, `>=`, `<=` so power readings aggregate and compare directly (`Meter3 = Meter1 + Meter2`). Used for real in [`TelemetryStore`](src/SmartX.Api/Services/TelemetryStore.cs) to drive severity and the per-node running total. |
| **Advanced arrays/lists** | Jagged `float[][]` batch ingestion in [`TelemetryStore.IngestFloatBatch`](src/SmartX.Api/Services/TelemetryStore.cs), and a rectangular `double[,]` zone-load matrix behind the grid heatmap. |
| **Recursion** | [`DeploymentTreeValidator`](src/SmartX.Shared/Validation/DeploymentTreeValidator.cs): walks Facility → Zone → Sub-Zone → Device trees of unknown depth. |

The Part 1 **Telemetry** page's *Live Telemetry Pulse* (a severity-coded feed,
zone heatmap and SVG gauge) also displays the Part 2 simulated readings,
because processed Part 2 packets are forwarded into the Part 1 store.

## References

In-text citations in the source code (e.g. `(Microsoft, n.d.c)`) refer to the
list below. Where one author has several sources in the same year, they are
lettered a, b, c ... in the order used in this list.

Adelson-Velsky, G.M. and Landis, E.M. (1962) 'An algorithm for the organization of information', *Doklady Akademii Nauk SSSR*, 146(2), pp. 263-266.

Agrawal, R., Imieliński, T. and Swami, A. (1993) 'Mining association rules between sets of items in large databases', *Proceedings of the 1993 ACM SIGMOD International Conference on Management of Data*, Washington DC, 25-28 May. New York: ACM, pp. 207-216. doi: 10.1145/170035.170072.

Cormen, T.H., Leiserson, C.E., Rivest, R.L. and Stein, C. (2022) *Introduction to algorithms*. 4th edn. Cambridge, MA: MIT Press.

Espressif Systems (n.d.) *ESP32 Wi-Fi & Bluetooth SoC*, viewed 13 September 2026, <https://www.espressif.com/en/products/socs/esp32>.

Heinzelman, W.R., Chandrakasan, A. and Balakrishnan, H. (2000) 'Energy-efficient communication protocol for wireless microsensor networks', *Proceedings of the 33rd Annual Hawaii International Conference on System Sciences*, Maui, 4-7 January. Los Alamitos: IEEE Computer Society, pp. 1-10. doi: 10.1109/HICSS.2000.926982.

Internet Engineering Task Force (2014) *RFC 7252: The Constrained Application Protocol (CoAP)*, viewed 13 September 2026, <https://datatracker.ietf.org/doc/html/rfc7252>.

Microsoft (2025a) *Boxing and Unboxing (C# Programming Guide)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing>.

Microsoft (2025b) *ASP.NET Core Blazor Hosting Models*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models>.

Microsoft (2026) *Generic Types and Methods (C#)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/generics>.

Microsoft (n.d.a) *Minimal APIs Quick Reference*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>.

Microsoft (n.d.b) *Queue<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1>.

Microsoft (n.d.c) *PriorityQueue<TElement,TPriority> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2>.

Microsoft (n.d.d) *Stack<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.stack-1>.

Microsoft (n.d.e) *Dictionary<TKey,TValue> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2>.

Microsoft (n.d.f) *SortedDictionary<TKey,TValue> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2>.

Microsoft (n.d.g) *HashSet<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1>.

Microsoft (n.d.h) *Channels*, .NET documentation, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>.

OASIS (2019) *MQTT Version 5.0*, OASIS Standard, viewed 13 September 2026, <https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html>.

OWASP Foundation (n.d.) *File Upload Cheat Sheet*, OWASP Cheat Sheet Series, viewed 13 September 2026, <https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html>.

Prim, R.C. (1957) 'Shortest connection networks and some generalizations', *Bell System Technical Journal*, 36(6), pp. 1389-1401. doi: 10.1002/j.1538-7305.1957.tb01515.x.

WHATWG (n.d.) *HTML Living Standard: Server-sent events*, viewed 19 October 2026, <https://html.spec.whatwg.org/multipage/server-sent-events.html>.

## Third-party assets

- **Bootstrap v5.3.3** (The Bootstrap Authors, MIT licence), in
  `src/SmartX.Client/wwwroot/lib/bootstrap`. It came with the Blazor
  WebAssembly project template.
- **Fonts:** Orbitron, Rajdhani and Share Tech Mono, loaded from Google Fonts
  (SIL Open Font Licence) in `src/SmartX.Client/wwwroot/index.html`.
