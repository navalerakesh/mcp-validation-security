# MCP Benchmark – Architecture & Design

## System Overview

MCP Benchmark is a Clean-Architecture .NET solution that stress-tests Model Context Protocol servers end to end: transport negotiation, JSON-RPC framing, tool/resource/prompt schemas, authentication, security attacks, load testing, scoring, and reporting. The CLI is the composition root and packages the entire validator and benchmarking stack into a single command.

## Design Principles & Practices

This repo is intentionally structured so that the design stays easy to reason about today and flexible for future MCP profiles and servers:

- **Clean Architecture & layering** – domain models and contracts live in a core layer; concrete HTTP clients, validators, and scoring strategies sit in an infrastructure layer; the CLI is a thin composition root. This keeps business rules independent from transport or hosting details.
- **Dependency Injection by default** – all services (validators, clients, scoring, auth) are wired through DI in the CLI entrypoint. Code depends on interfaces, which makes behavior easy to swap in tests and when adding new implementations.
- **Testable units with clear seams** – validators, scoring, and orchestration are designed as small units with explicit dependencies, covered by fast unit tests, integration tests against a mock MCP server, and architecture tests that enforce dependency direction.
- **Configuration-driven behavior** – profiles, concurrency limits, and output options are expressed as configuration rather than baked into code, so new MCP profiles or scoring modes can be introduced without rewriting flows.
- **Extensibility as a first-class concern** – new rules, validators, transports, and auth strategies can be added by plugging into existing interfaces and DI registration, instead of editing core control flow.
- **Safety and observability** – session artifacts (logs, JSON, Markdown, HTML) are produced in a consistent way so that every run is explainable and debuggable, which is essential when validating security and compliance.

## Design Pillars Overview

```mermaid
graph TD
	subgraph Pillars["MCP Benchmark Pillars"]
		SPEC[Spec Alignment and MCP Profiles & Schemas]
		SEC[Security and Adversarial Testing]
		PERF[Performance and Concurrency]
		DX[Developer Experience - CLI + Local MCP]
		OBS[Observability and Session Traceability]
		EXT[Extensibility and Pluggable Validators]
		RAI[Guardrails and Safe Defaults]
	end
	SPEC --> SEC
	SPEC --> EXT
	SEC --> RAI
	PERF --> RAI
	EXT --> DX
	DX --> OBS
	OBS --> RAI
```

## End-to-End Component Architecture

```mermaid
flowchart LR
	%% ===================== CLIENTS =====================
	subgraph Clients
		U[User / Engineer]
		CI[CI Pipelines]
		MCPClient[Local MCP / Copilot]
	end

	%% ===================== CLI & COMMANDS =====================
	subgraph CLI["Mcp.Benchmark.CLI - mcpval CLI"]
		Root[Program & System.CommandLine]
		VCmd[ValidateCommand]
		RCmd[ReportCommand]
	end

	%% ===================== ORCHESTRATION =====================
		subgraph Orchestrator["Validation Orchestrator"]
		Session[ValidationSessionBuilder - health auth capabilities]
		Service[McpValidatorService]
	end

	%% ===================== VALIDATION PIPELINE =====================
	subgraph Validators["Validators (Infrastructure)"]
		Proto[ProtocolComplianceValidator]
		Sec[SecurityValidator]
		Tools[ToolValidator]
		Res[ResourceValidator]
		Prompts[PromptValidator]
		Perf[PerformanceValidator]
	end

	%% ===================== TRANSPORT & SERVER =====================
	subgraph Transport["MCP HTTP Client"]
		Http[McpHttpClient / IMcpHttpClient]
	end

	Server[(Target MCP Server)]

	%% ===================== ARTIFACTS =====================
	subgraph Artifacts["Artifacts & Reports"]
		Json[JSON validation result mcp-validation-*-result.json]
		Md[Markdown summary mcp-validation-*-report.md]
		Html[HTML or XML reports]
		Logs[Session logs at %LOCALAPPDATA%/McpCli/Sessions]
	end

	%% CLIENT ENTRY POINTS
	U -->|runs mcpval validate/report| Root
	CI -->|dotnet tool or exe| Root
	MCPClient -->|STDIO wrapper mcpval_stdio| Root

	%% COMMAND DISPATCH
	Root --> VCmd
	Root --> RCmd

	%% VALIDATE FLOW
	VCmd -->|build config + auth + output| Service
	Service --> Session
	Session -->|health, initialize, capabilities| Http
	Http -->|JSON-RPC| Server
	Server --> Http
	Session -->|ValidationSessionContext| Service

	%% PARALLEL VALIDATORS
	Service --> Proto
	Service --> Sec
	Service --> Tools
	Service --> Res
	Service --> Prompts
	Service --> Perf

	Proto --> Http
	Sec --> Http
	Tools --> Http
	Res --> Http
	Prompts --> Http
	Perf --> Http
	Http --> Server
	Server --> Http

	%% SCORING & ARTIFACTS
	Service -->|aggregate results| Json
	Service --> Md
	Service --> Logs

	%% REPORT FLOW
	RCmd -->|load JSON/Markdown| Json
	RCmd --> Md
	Json --> Html
	Md --> Html

	%% OUTPUT TO USERS
	Html --> U
	Md --> U
	Json --> U
	Logs --> U
```

## Validate & Report Command Flows

```mermaid
flowchart TD
		Start([User runs mcpval validate]) --> Args[Parse CLI args and config file]
		Args --> Profile[Resolve MCP spec profile]
		Profile --> DI[Bootstrap DI container]
		DI --> Health{Health check passed?}

		Health -- "No" --> Fail[Emit failure summary and non-zero exit]
		Health -- "Yes" --> Auth{Is authentication required?}

		Auth -- "Yes" --> AuthFlow[Run auth strategy using token or interactive login]
		AuthFlow --> AuthOk{Authentication succeeded?}
		AuthOk -- "No" --> Fail

		Auth -- "No" --> FanOut[Fan out validators]
		AuthOk -- "Yes" --> FanOut

		FanOut --> V1[Protocol compliance validator]
		FanOut --> V2[Security and attack validators]
		FanOut --> V3[Tools, resources, and prompts validators]
		FanOut --> V4[Performance and load validators]

		V1 --> Collect[Collect category results]
		V2 --> Collect
		V3 --> Collect
		V4 --> Collect

		Collect --> Score[Aggregate scores and overall status]
		Score --> Artifacts[Write JSON and Markdown artifacts under --output]
		Artifacts --> Console[Print console summary and recommendations]
		Console --> End([Exit with status from results])
```

```mermaid
flowchart TD
		StartR([User runs mcpval report]) --> RArgs[Parse CLI args with input and format options]
		RArgs --> Load[Load saved JSON or Markdown file]
		Load --> ValidateInput{Input readable?}

		ValidateInput -- "No" --> RFail[Emit error and non-zero exit]
		ValidateInput -- "Yes" --> Render[Render HTML or XML from results]

		Render --> RArtifacts[Write report file to output or derived path]
		RArtifacts --> Done([Exit 0 and print report path])
```

## Session Traceability & Lineage

```mermaid
graph LR
	subgraph Flow["CLI Run to Artifacts to Report"]
		Cmd[CLI Command validate or report]
		Cfg[McpValidatorConfiguration]
		Sess[ValidationSessionContext]
		Res[ValidationResult]
		JsonFile[mcp-validation-*-result.json]
		MdFile[mcp-validation-*-report.md]
		HtmlFile[HTML / XML report]
		Logs[Session logs and advisor hints]
	end

	Cmd -->|args + config| Cfg
	Cfg -->|session bootstrap| Sess
	Sess -->|effective server, protocol, and capabilities| Res
	Res -->|serialized snapshot| JsonFile
	Res -->|Markdown renderer| MdFile
	JsonFile -->|offline report input| HtmlFile
	MdFile -->|resolve sibling *-result.json| HtmlFile
	Res -->|recommendations + scores| Logs
	HtmlFile --> Cmd
	MdFile --> Cmd
	JsonFile --> Cmd
	Logs --> Cmd
```

## Where to Go Next

If you want a deeper, code-level view of layers, services, and extension points, see:

- [Technical Architecture Details](TechnicalArchitecture.md)

If you want to see a concrete example of the report generated by a real `mcpval validate` run and then rendered via `mcpval report`
