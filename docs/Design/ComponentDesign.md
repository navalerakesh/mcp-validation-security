# MCP Validator Component Design

This document focuses on the runtime components, their responsibilities, and how control flows between them during validation and reporting runs.

## Component Map

```mermaid
flowchart LR
    subgraph Host[CLI host]
        Program[Program and DI bootstrap]
        Commands[Validate, health-check, discover, report commands]
        Console[Console output and next-step guidance]
    end

    subgraph Orchestration[Execution orchestration]
        Session[ValidationSessionBuilder]
        Service[McpValidatorService]
    end

    subgraph Engines[Infrastructure engines]
        Transport[Transport clients]
        Auth[Authentication strategies]
        Validators[Validation engines]
        Scoring[Trust, policy, and report generation]
    end

    subgraph Interpretation[Host interpretation]
        Profiles[Client profile evaluator]
    end

    subgraph Assets[Shared assets]
        Core[Core models and contracts]
        Schemas[Schema registry]
    end

    Target[(Target MCP server)]
    Artifacts[(Artifacts and session data)]

    Program --> Commands
    Commands --> Session
    Commands --> Console
    Session --> Auth
    Session --> Transport
    Session --> Service
    Transport --> Target
    Service --> Validators
    Schemas --> Validators
    Core --> Session
    Core --> Service
    Validators --> Scoring
    Scoring --> Profiles
    Scoring --> Artifacts
    Profiles --> Artifacts
```

## Responsibility Matrix

| Component | Owns | Does not own |
| --- | --- | --- |
| `Program` and DI bootstrap | Service registration, command wiring, startup composition | Validation logic, rule decisions |
| Command handlers | Translate CLI input into config, call shared services, choose output paths | Transport implementation, scoring logic, client-profile rules |
| `ValidationSessionBuilder` | Transport detection, bootstrap state, auth preparation, shared session context | Report rendering, CLI formatting |
| `McpValidatorService` | Validator orchestration, result assembly, category coordination | Argument parsing, host-specific presentation |
| Validators | Category-specific evidence collection and rule evaluation | Policy thresholds, client-specific compatibility mapping |
| Trust and policy layers | Convert neutral evidence into trust level and exit behavior | Raw evidence collection |
| Client profile evaluator | Interpret completed evidence against documented host expectations | Network calls or validator execution |
| Report renderers | Produce Markdown, HTML, XML, SARIF, and JUnit from saved results | Live validation or transport activity |

## Interaction Notes

- `validate` and `health-check` share the same bootstrap concepts even though they produce different depths of evidence.
- The canonical record of a run is the saved JSON result. Human-facing reports are renderings of that evidence.
- Client profile interpretation is additive. It explains host compatibility without changing the evidence model that validators produced.
- `report` is intentionally offline. It reads saved artifacts instead of contacting the target again.

## Change Guidance

- Add a new validator when you need new evidence collection or new rule coverage.
- Add a new client profile when a host has documented compatibility expectations that can be derived from existing evidence.
- Add a new report format in the shared reporting layer, then expose it through the CLI host.
- Add a new transport behavior in infrastructure, not by branching command handlers.