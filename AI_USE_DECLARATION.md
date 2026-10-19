# AI Use Declaration

This project was built with the help of **Claude** (an AI assistant made by
Anthropic), used through Claude Code. This file records how, so that anyone
reading the repository knows what was AI-assisted.

## What Claude did

- **Part 1 (sensor ingestion and telemetry):** built together with Claude in an
  earlier working session.
- **Part 2 (real-time command stream), written by Claude:**
  - the real-time processing code in `src/SmartX.Api/RealTime/`
    (device registry, queues, history log, active-error tracking, command stack
    and undo, simulator, SSE broadcaster, recommendation engine);
  - the API endpoints for Part 2;
  - the Blazor pages and components for the Part 2 dashboard;
  - the 56 automated tests in `tests/SmartX.Tests/`;
  - the README sections, the API example file and `docs/DEMO_SCRIPT.md`;
  - the Harvard in-text citations in the code comments and the reference list.
- **Fixes to Part 1:** the aggregate-total bug and the API error handling on the
  Part 1 pages.
- **Git:** Claude made the original 20 Part 2 commits, which is why they carry a
  `Co-Authored-By: Claude` line.
- **Running and checking:** Claude ran the build, the tests and the application
  to check that the features worked.

## What I did

*(Edit this section so it is true. Describe your own contribution honestly, for
example: wrote the brief and requirements, chose the design, ran and tested the
application myself, reviewed the code, made later changes and commits.)*

- 

## Things I checked myself

*(Tick only what you really did.)*

- [ ] Read through the code and can explain how each data structure is used.
- [ ] Ran the application and tried each feature.
- [ ] Opened every reference link and confirmed it supports the statement it is cited for.

## Notes

- The Part 2 simulator is simulation only. No physical hardware is connected.
- Docker support has not been verified by running the containers.
