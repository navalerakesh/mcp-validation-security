# MCP Validator Component Design

This document focuses on the runtime components, their responsibilities, and how control flows between them during validation and reporting runs.

## Component Map

```mermaid
flowchart LR
    subgraph Host[CLI host]
        Program[Program and DI bootstrap]
        Commands[Validate, health-check, discover, report commands]
        Console[Console output, next-step guidance, and GitHub Actions reporter]
        SessionCtx[CliSessionContext and session artifact store]
    end

    subgraph Orchestration[Execution orchestration]
        Governance[Execution governance service]
        Session[ValidationSessionBuilder]
        Applicability[Applicability resolver]
        Service[McpValidatorService]
    end

    subgraph Engines[Infrastructure engines]
        Transport[Transport clients]
        Auth[Authentication strategies]
        Validators[Validation engines]
        Assembly[Assessment and evidence assembly]
        Reporting[Report generators and renderers]
        ModelEval[Model evaluation executor]
    end

    subgraph Assets[Shared assets]
        Core[Core models and contracts]
        Schemas[Schema registry and version resolver]
        Packs[Protocol feature, rule, and scenario packs]
        Profiles[Client profile evaluator]
    end

    Target[(Target MCP server)]
    Artifacts[(Artifacts and session data)]

    Program --> Commands
    Commands --> Governance
    Commands --> Console
    Commands --> SessionCtx
    Governance --> Session
    Session --> Auth
    Session --> Transport
    Transport --> Target
    Session --> Applicability
    Session --> Service
    Core --> Governance
    Core --> Session
    Core --> Service
    Schemas --> Applicability
    Packs --> Applicability
    Applicability --> Service
    Service --> Validators
    Validators --> Assembly
    Assembly --> Profiles
    Assembly --> Reporting
    Assembly --> ModelEval
    Profiles --> Reporting
    Reporting --> Artifacts
    ModelEval --> Artifacts
    SessionCtx --> Artifacts
    Console --> Artifacts
```

## Responsibility Matrix

| Component | Owns | Does not own |
| --- | --- | --- |
| `Program` and DI bootstrap | Service registration, command wiring, startup composition | Validation logic, rule decisions |
| Command handlers | Translate CLI input into config, call shared services, choose output paths, and emit host summaries | Transport implementation, scoring logic, client-profile rules |
| Execution governance and session services | Validate execution plans, route dry runs, persist operational session artifacts | Validator rules, compatibility interpretation |
| `ValidationSessionBuilder` | Transport detection, bootstrap state, initialize handshake capture, auth preparation, capability snapshot | Report rendering, CLI formatting |
| `McpValidatorService` | Validator orchestration, applicability resolution, result assembly, layer and coverage population, scenario-pack execution | Argument parsing, host-specific presentation |
| Validators | Category-specific evidence collection and rule evaluation | Policy thresholds, client-specific compatibility mapping |
| Trust, verdict, and policy layers | Convert neutral evidence into trust level, release posture, and exit behavior | Raw evidence collection |
| Client profile evaluator | Interpret completed evidence against documented host expectations | Network calls or validator execution |
| Report renderers | Produce Markdown, HTML, XML, SARIF, and JUnit from saved results | Live validation or transport activity |

## Interaction Notes

- `validate` and `health-check` share the same bootstrap concepts even though they produce different depths of evidence.
- Capability snapshots captured during bootstrap are reused by tool, prompt, and resource validation so the validator does not re-bootstrap the same target unnecessarily.
- The canonical record of a run is the saved JSON result. Human-facing reports and client-compatibility rollups are renderings of that evidence.
- `validate` emits explicit output artifacts and may also persist session-scoped JSON artifacts through the session artifact store when that execution mode is enabled.
- `report` is intentionally offline. It reads saved artifacts instead of contacting the target again.

## Change Guidance

- Add a new validator when you need new evidence collection or new rule coverage.
- Add a new scenario pack or rule pack when behavior should be gated by applicability instead of hardcoded global branching.
- Add a new client profile when a host has documented compatibility expectations that can be derived from existing evidence.
- Add a new report format in the shared reporting layer, then expose it through the CLI host.
- Add a new transport behavior in infrastructure, not by branching command handlers.
