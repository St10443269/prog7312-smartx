# Technology recommendations for Smart-X

These are tools that could improve Smart-X in the future. This page is split
into what is **already in the project** and what is **only a recommendation**,
so nobody thinks something exists when it does not.

## What is in the project now

| Technology | Status |
|---|---|
| .NET 10, ASP.NET Core Minimal API, Blazor WebAssembly | **Used.** The whole application. |
| Server-Sent Events for live updates | **Used.** Part 2 and Part 3 pages. |
| xUnit tests | **Used.** 148 tests. |
| OpenAPI (API description) | **Used** in development (`/openapi/v1.json`). |
| Docker (a Dockerfile for the API, one for the client, and `docker-compose.yml`) | **Files are in the project, but they have not been tested.** |
| GitHub Actions | **Not used.** There are no workflow files. |
| Cloud hosting or a database | **Not used.** Everything is kept in memory. |
| Home Assistant | **Not used.** |
| An AI service | **Not used.** The "Suggested Actions" page works from fixed rules and counted patterns, not from AI. |

## Recommendations

### 1. GitHub Actions - build and test automatically (recommended first)

- **What it does.** GitHub runs a short script every time code is pushed. For Smart-X the script would build the solution and run the 148 tests (GitHub, n.d.).
- **How it helps Smart-X.**
  - A broken build or a failing test is found straight away, not when someone next runs it
  - It runs on a different computer (Linux), so it also shows that the project builds on more than the developer's own PC
  - The repository can show a "passing" badge
- **Why it fits a .NET project.** GitHub provides a ready-made step that installs the .NET SDK (GitHub Actions, n.d.). The same two commands used by hand, `dotnet build` and `dotnet test`, are used in the script.
- **Extra work.** Very little: one small file in `.github/workflows/`. Actions is free for public repositories.

Example only (not in the project):

```yaml
name: build-and-test
on: [push]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet build SmartX.slnx
      - run: dotnet test tests/SmartX.Tests
```

### 2. Docker - finish and test what is already there

- **What it does.** Packs the API and the dashboard into containers, so they run the same way on any computer (Microsoft, n.d.a).
- **How it helps Smart-X.** Someone can start everything with one command (`docker compose up --build`) without installing the .NET SDK. It also makes it easier to put the app on a server later.
- **Why it fits a .NET project.** Microsoft publishes official .NET images, and the project's Dockerfiles already use them.
- **Where it stands.** The files are written but **have not been run or tested**, so this should not be described as working. The next steps would be to run it, add a way to keep uploaded files outside the container, and add a health check.

### 3. A managed database for telemetry

- **What it does.** Stores the readings, history and registered devices in a database that is hosted and looked after by a cloud provider, instead of in memory.
- **How it helps Smart-X.**
  - Right now everything is lost when the API restarts, and only the latest 5000 readings per sensor are kept
  - A database keeps the data for good and can hold far more history
  - More than one copy of the API could share the same data
- **Why it fits a .NET project.** Entity Framework Core is Microsoft's standard way to talk to a database from .NET, and it has providers for common databases such as PostgreSQL and SQL Server (Microsoft, n.d.b). The in-memory classes could be swapped one at a time.
- **What would change.** The AVL tree would still make sense as a fast in-memory copy of recent readings, with the database holding older ones. Free tiers and prices differ between providers, so that needs checking when choosing.

### 4. Home Assistant - connect real devices

- **What it does.** Home Assistant is open-source software that collects data from smart devices. ESP32 boards can join it using ESPHome, and it can share data over MQTT (ESPHome, n.d.; Home Assistant, n.d.a).
- **How it helps Smart-X.** The simulator could be replaced with real sensor readings, which is what the simulated devices stand in for. A comment in `Program.cs` already explains why the project uses plain HTTP and not MQTT for now.
- **Why it fits a .NET project.** Two ways in:
  - Read device states from Home Assistant's REST API (Home Assistant, n.d.b)
  - Listen to MQTT messages using MQTTnet, a .NET library (MQTTnet, n.d.)
- **What would change.** Readings would arrive through the same checking and queues that the simulator uses now, so most of the existing code would stay the same. The device positions and link costs on the topology page would still need real signal data.

### 5. An AI service to summarise alerts

- **What it does.** Sends the recent alerts to an AI service over the internet, and gets back a short plain-English summary such as "Moisture Sensor B has dropped below 20 % three times in the last hour."
- **How it helps Smart-X.** A person reads one sentence instead of a long list of alerts.
- **Why it fits a .NET project.** It is a normal HTTPS call that `HttpClient` can make from the API. The key must stay on the server (in configuration or user secrets, as described in Microsoft, n.d.c), never in the Blazor client, because anything in the client can be read by users.
- **Things to watch.**
  - It costs money per request
  - Alert text would be sent to another company, so private data should not be included
  - The summary can be wrong, so it should be marked as AI-generated and should never run commands by itself
- **Not the same as what is built.** The current "Suggested Actions" page does not use AI.

## Suggested order

1. **GitHub Actions** (small effort, protects every later change)
2. **Test Docker** (the files already exist)
3. **A managed database** (fixes losing data on restart)
4. **Home Assistant** (needs real hardware)
5. **AI summaries** (nice to have)

## Sources

ESPHome (n.d.) *ESPHome*, viewed 12 November 2026, <https://esphome.io/>.

GitHub (n.d.) *GitHub Actions documentation*, viewed 12 November 2026, <https://docs.github.com/en/actions>.

GitHub Actions (n.d.) *setup-dotnet*, viewed 12 November 2026, <https://github.com/actions/setup-dotnet>.

Home Assistant (n.d.a) *MQTT integration*, viewed 12 November 2026, <https://www.home-assistant.io/integrations/mqtt/>.

Home Assistant (n.d.b) *REST API*, viewed 12 November 2026, <https://developers.home-assistant.io/docs/api/rest/>.

Microsoft (n.d.a) *Containerize a .NET app: introduction*, Microsoft Learn, viewed 12 November 2026, <https://learn.microsoft.com/en-us/dotnet/core/docker/introduction>.

Microsoft (n.d.b) *Entity Framework Core*, Microsoft Learn, viewed 12 November 2026, <https://learn.microsoft.com/en-us/ef/core/>.

Microsoft (n.d.c) *Safe storage of app secrets in development in ASP.NET Core*, Microsoft Learn, viewed 12 November 2026, <https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets>.

MQTTnet (n.d.) *MQTTnet*, GitHub, viewed 12 November 2026, <https://github.com/dotnet/MQTTnet>.
