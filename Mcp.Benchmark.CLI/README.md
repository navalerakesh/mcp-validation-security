# Mcp.Benchmark.CLI

This project hosts the `mcpval` command-line application. It is the composition root for command binding, dependency injection, console UX, artifact writing, and GitHub Actions integration.

If you are looking for installation or end-user usage guidance, start with the repository [README](../README.md) and [QUICKSTART](../QUICKSTART.md). This document focuses on what this source project owns inside the solution.

> **Breaking changes:** For 3.0 adoption, use the [upgrade checklist](../docs/SchemaCompatibility.md#upgrading-to-30). Package release versions, artifact schemas and assurance-policy revisions are separate contracts.

## Responsibilities

| Concern | Owned here | Lives elsewhere |
| --- | --- | --- |
| Command parsing and option validation | Yes | No |
| Console output, next-step hints, and session-log guidance | Yes | No |
| Dependency injection and runtime composition | Yes | No |
| Transport clients, validators, scoring, and reporting engines | No | `Mcp.Benchmark.Infrastructure` |
| Neutral domain models and abstraction contracts | No | `Mcp.Benchmark.Core` |
| Client-specific compatibility expectations | No | `Mcp.Benchmark.ClientProfiles` |
| Vendored protocol schemas and version catalog | No | `Mcp.Compliance.Spec` |

## Commands Hosted Here

| Command | Role |
| --- | --- |
| `validate` | Runs the full validation suite and writes the standard artifact set |
| `health-check` | Performs a fast connectivity and initialization probe |
| `discover` | Captures remote capability snapshots for documentation or debugging |
| `report` | Renders saved validation results into additional offline formats |

Transport support is intentionally command-specific: `validate` and `health-check` support HTTP and STDIO targets today, `discover` is HTTP-first, and `report` is offline only.

`validate --assurance comprehensive|inventory|ci|provider` selects the evidence-first assurance contract. The default is `comprehensive`, policy revision `2026-11-24.2`: execution requires explicit standard or elevated admission, pinned target-bound official conformance evidence, and the signed identity baseline described below. Missing prerequisites are rejected before target contact; missing required assessment evidence cannot earn a successful exit. Safe execution remains the default mode, so use `--assurance inventory` explicitly for discovery/static assessment. A dry run is planning only, not assessment acceptance. General per-check completeness and a fully evidenced comprehensive owned run are still under qualification; see the [recovery tracker](../docs/EnterpriseRecoveryProgress.md).

Default performance scope is client-observable catalog latency, concurrent requests and throughput, not an application workload SLA or capacity-limit guarantee. Target memory, buffer-overflow and resource-exhaustion qualification are explicit additional requirements (`testMemoryUsage`, `testBufferOverflow`, `testResourceExhaustion` default to false). Selecting them in a comprehensive request currently fails closed because the required operator-owned observer or isolated stress harness is unavailable. Generic load measurement never selects and invokes an advertised application tool.

`validate --conformance-results <wrapper.json>` imports pinned official conformance evidence through the Core importer contract. Provider assurance additionally requires `--provider-harness <manifest.json>`, `--provider-authorization-key <public-key.pem>`, and `--sign-attestation`; the P-256-signed authorization and prior-run harness, target, protocol revision, dry-run execution-plan digest, repeated receipts, and internally produced current run must all verify. Repeatable `--extension <id>` selections flow through the existing applicability resolver; they do not create extension-specific top-level commands. Core, security, operations, extensions, clients, and external conformance remain independent typed assessments.

## Comprehensive Admission

The current comprehensive policy revision is `2026-11-24.2`. Its mandatory identity baseline covers actor binding, scope enforcement and actor/action/object authorization through the existing signed-observer matrix. These controls do not earn MCP core-conformance credit or prove universal security.

### Choose Report Detail And Assessment Scope Separately

| Goal | Selection | What it establishes |
| --- | --- | --- |
| Inspect all recorded detail | `--report-detail full` or offline `report` | Renders existing evidence; does not execute missing checks |
| First assessment of a public target | `--assurance inventory --mode safe` | Discovery and static analysis; no active conformance, tool execution or load assurance |
| Active checks on an owned test target | `--assurance ci --mode standard` | Admitted active checks and measurements; failures and unavailable evidence remain visible |
| Comprehensive assurance | `--assurance comprehensive --mode standard` plus the prerequisites below | Evaluates the selected required evidence; starting the run does not guarantee acceptance |

Public endpoint availability, a PAT, or permission to call a read-only tool does not establish authorization for malformed requests, writes or load. Before active testing, agree on permitted methods/arguments, target data, credentials, request/concurrency limits and cleanup. Do not run an unrestricted active scan against Microsoft Learn, GitHub MCP or a production wrapper merely to eliminate skipped rows.

### Prepare The Evidence

1. Select an owned or explicitly authorized endpoint, supported protocol revision and execution limits. Review the dry-run plan before contact.
2. Collect pinned official conformance evidence with the [existing companion](../docs/OfficialConformanceAndExtensions.md#official-conformance-import). Its current runner targets the pinned `2026-07-28` HTTP requirement set; do not relabel it for another protocol or STDIO. The companion strips ambient credentials and has no credential-forwarding CLI option; protected targets need an appropriate authorized setup, not a token embedded in the URL.
3. Reserve the validation ID independently before collecting identity observations. Have an operator-selected observer measure a valid positive control and the required actor-binding, scope and actor/action/object denial cases on the same target/run/route.
4. Produce the bounded signed identity evidence and selection described below, using the independently trusted observer public key. The repository supplies evidence contracts/import verification, **not a turnkey identity collector for arbitrary servers**. A locally generated signing key or copied example cannot prove an external server enforced authorization.
5. Supply any required tool/prompt arguments privately through the existing explicit-input configuration. The arguments do not grant execution permission. Keep target-memory and isolated-stress requirements unselected unless their actual observer/harness exists.
6. Run comprehensive validation, then inspect the selected policy, required coverage and recommended exit. Retain the canonical result and audit bundle. Missing/blocked prerequisites must remain incomplete; never edit observations or weaken thresholds to force acceptance.

If you do not control the target or its observer, request the required evidence from its operator or use an honestly scoped inventory/CI assessment. Offline rendering cannot fill the gap.

Reserve the validation ID independently before collecting the controlled observations. `--identity-baseline` reads a bounded JSON selection containing `contract`, `admission`, `signedEvidenceJson`, `trustedObserverPublicKeyPem`, `actorBindingCaseId`, `scopeEnforcementCaseId`, and `authorizationCaseId`. It uses the existing identity contract and original signed envelope, not a second observation format. The three selected denial cases must declare the corresponding signed challenges and reference successful positive controls. Supply only nonsecret run-local aliases and an independently trusted public key, never a private key or credentials.

```bash
mcpval validate --server https://example.test/mcp \
  --assurance comprehensive --mode standard \
  --validation-id "$RESERVED_RUN_ID" \
  --identity-baseline ./identity-baseline.json \
  --conformance-results ./official-conformance.json \
  --output ./mcp-reports
```

The CLI verifies target, independently planned run ID, signature, observer, case selection and freshness before transport setup. Evidence cannot choose its own run ID. Use a fresh reserved ID for a new independent assessment; replaying the same evidence is not a new observation or repeated-run certificate. Correlated library callers reserve `ValidationRunRequest`, collect the approved observations, then attach the selection with `WithIdentityControls`. Canonical results retain verified selection for signature-checked replay; rejected or quarantined raw envelopes are not retained as baseline inputs. Missing or failed observations never gain credit.

### Reproduce Owned Examples

From a repository checkout with the [contributor prerequisites](../docs/ContributorEnvironment.md) installed, build the solution and wrapper before running examples. Use a fresh private output directory; exported evidence is not committed.

The existing local fixture permits active diagnostic testing without probing a third-party service:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server "node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-compliant.cjs" \
  --assurance ci --mode standard --access public \
  --protocol-era legacy --mcpspec 2025-11-25 \
  --max-requests 200 --max-concurrency 2 --timeout 10 \
  --report-detail full --output ./artifacts/owned-active
```

This is an active **diagnostic** example, not a comprehensive-success fixture. The owned run exercises real tool calls, prompt retrieval, resource reads and bounded catalog load. Native category success can still produce incomplete exit `2` because official core-conformance and identity-control evidence were not supplied. Preserve that distinction; fixture naming does not guarantee that the entire selected assessment passes. Safe reports and category-level tests are not substitutes for a complete comprehensive end-to-end result.

For reproducible identity evidence, the owned HTTP pilot exercises real issued fixture credentials and a separately credentialed observer:

```bash
MCPVAL_IDENTITY_PILOT_OUTPUT="$PWD/artifacts/identity-pilot" \
  dotnet test Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj \
  --filter 'FullyQualifiedName~OwnedHttpIdentityPilot_ShouldObserveIssuedPrincipalsAndPolicy'

dotnet run --project Mcp.Benchmark.CLI -- report \
  --input ./artifacts/identity-pilot/correct-policy.canonical.json \
  --format html --report-detail full \
  --output ./artifacts/identity-pilot/correct-policy.html
```

The test also exports `broken-policy`, `observer-unavailable` and `counter-drift` variants. Render their `.canonical.json` files the same way. They demonstrate verified identity controls, known failures and incomplete observation separately; they do not contain complete protocol/performance evidence or qualify an independent production observer. The ephemeral fixture stops after collection. Do not reuse its signed envelope, run ID or target digest as prerequisites for a different target or a later live run.

## Explicit Positive Tool Inputs

Interactive authentication requires explicit `--interactive` on the invocation, even if a configuration file enables `AllowInteractive`. Without that invocation authority the command remains noninteractive and preserves configured noninteractive credentials. The GitHub Action never admits interactive authentication.

Supply positive arguments through the existing configuration record and `--config`. Names match the observed catalog exactly and case-sensitively; an absent or ambiguous match remains unexecuted evidence.

```json
{
  "validation": {
    "categories": {
      "tools": {
        "CustomScenarios": [
          { "ToolName": "owned-tool", "Parameters": { "kind": "phone", "phone": "fixture-value" } }
        ]
      }
    }
  }
}
```

Arguments are not execution permission: Safe mode remains non-executing. Standard/elevated policy and all selected assurance prerequisites still apply. One positive scenario per tool is supported, with at most 64 scenarios, 64 JSON values and 16 nesting levels per argument object, 64 KiB serialized arguments per scenario and 1 MiB in aggregate. Values must be JSON-compatible. Unsupported or schema-invalid explicit inputs never fall back to synthetic arguments. Bounded conditional schemas and supported local references are evaluated against the original schema; regex-dependent, cyclic, external and ambiguous-resource evaluation remains unavailable.

Non-null `ExpectedResult`, `ExpectError: true`, unavailable/null `Parameters`, duplicate names, and combining custom inputs with `SpecificTools` are rejected. Custom performance scenarios remain unsupported. Positive success requires a completed, schema-valid, non-error response; protocol-valid errors do not satisfy a positive scenario. Unplanned client input requests remain incomplete.

For authorized modern (`2026-07-28`) targets, an explicit scenario may include `ClientInputRounds`:

```json
{
  "ToolName": "owned-tool",
  "Parameters": {},
  "ClientInputRounds": [{
    "ExpectedRequests": { "roots": { "method": "roots/list" } },
    "Responses": { "roots": { "roots": [] } }
  }]
}
```

Each round must exactly match the observed input-request map, including server-assigned IDs, methods and parameters. Only operator-supplied roots, form elicitation and synthetic sampling results are supported. Sampling results must use model identifier `mcpval-synthetic-fixture`; no model is invoked. Sampling requests containing `tools` or `toolChoice` are rejected because the synthetic client does not advertise sampling-tool support. Elicitation acceptance/decline/cancellation is explicit fixture data, not automatic user consent. Root URIs are supplied data; the validator does not enumerate or read the filesystem. Empty request/response maps admit a state-only continuation.

Limits are eight rounds per tool, eight inputs per round, 128 characters per input ID, 1,024 JSON values per round, 64 KiB serialized data per round and 1 MiB across captured arguments/rounds. Each interaction has a 30-second deadline and a 1 MiB aggregate response budget, subordinate to caller cancellation and transport budgets. Only required client capabilities are declared, on this tool's requests alone. Opaque state is echoed unchanged, never interpreted, and removed on the next continuation if absent from its response. Every continuation has a fresh JSON-RPC ID. Unexpected requests, extra/missing rounds and exhausted limits never earn completed execution credit. Legacy server-initiated callbacks remain unsupported.

Use nonsecret fixture inputs where possible. Public configuration/result clones omit argument values, client-input rounds, opaque request state and expected-result text; redacted `Parameters: null` cannot be replayed as an empty argument object. Target response content and value-bearing exception diagnostics are not retained for explicit positive scenarios. Keep the operator configuration private.

## Explicit Prompt Inputs And Run Budgets

Prompt scenarios use `validation.categories.prompts.CustomScenarios`, with `PromptName`, string-valued `Arguments` and optional `ClientInputRounds`. Rounds use the same exact request matching, finite limits, synthetic model marker and private capture as explicit tool scenarios. The collector completes bounded catalog enumeration before dispatching an explicit prompt, rejects ambiguous names, retains missing/disabled obligations, and does not synthesize missing required arguments. Modern HTTP and STDIO share the same continuation implementation. Legacy callbacks and automatic elicitation consent remain unsupported.

```json
{
  "validation": {
    "categories": {
      "prompts": {
        "CustomScenarios": [{
          "PromptName": "owned-prompt",
          "Arguments": {},
          "ClientInputRounds": [{
            "ExpectedRequests": { "roots": { "method": "roots/list" } },
            "Responses": { "roots": { "roots": [] } }
          }]
        }]
      }
    }
  },
  "testExecution": { "GlobalTimeoutMinutes": 10 },
  "execution": { "maxCatalogPayloadCharacters": 16777216 }
}
```

`GlobalTimeoutMinutes` is one total engine deadline, including bootstrap and execution; it does not restart for a category or retry. Cancellation retains collected evidence and returns the timeout/cancellation contract. It does not impose a deadline on an independent external conformance runner.

`maxCatalogPayloadCharacters` bounds cumulative native catalog payload admitted for parsing across tool, resource, resource-template and prompt collectors. The default reuses the existing 16 Mi-character per-catalog ceiling as a shared run ceiling; the configurable maximum is 64 Mi characters (four native catalog ceilings). This is a conservative retained-payload work bound, not a byte-accurate whole-process memory guarantee. Per-response, item/page/depth and actual outbound-attempt limits still apply. Exhaustion retains earlier evidence and declares incomplete catalog coverage.

Execution plans and audits distinguish stage-level known request demand from unknown target-dependent expansion and the enforced actual-attempt limit. Explicit argument/reply rounds provide known work; auth, pagination, retries, extension reads and load generation are not assigned invented totals.

`config-schema --commands` emits versioned command metadata from the same definitions used by execution and help, without reading target configuration or constructing a host. It separates root-only and inherited options, includes argument arity and multiple-values-per-token behavior, and never exports secret defaults. The parser remains authoritative; see the [command manifest schema](../docs/Schemas/mcpval-command-manifest.schema.json).

## Local Development

Run the CLI host directly from source:

```bash
dotnet run --project Mcp.Benchmark.CLI -- --help
```

Example commands:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate --server https://example.test/mcp --access public --output ./reports
dotnet run --project Mcp.Benchmark.CLI -- health-check --server https://example.test/mcp --access public
dotnet run --project Mcp.Benchmark.CLI -- discover --server https://example.test/mcp --format json
dotnet run --project Mcp.Benchmark.CLI -- config-schema --commands
dotnet run --project Mcp.Benchmark.CLI -- report --input "./reports/<run-directory>/<base-name>-result.json" --format junit
```

## Execution Governance

`validate`, `health-check`, and `discover` now apply a safe-by-default execution contract before contacting a target.

- Default persistence is ephemeral. Session logs and session-state artifacts are only created when `--persistence-mode session` is selected.
- Explicit `validate --output` reports are published as one per-run directory after all reports and the audit manifest are complete. Use the emitted artifact paths; ignore `.pending-*` staging directories. Existing bundles are never overwritten. See [artifact migration](../docs/SchemaCompatibility.md) for recorded timestamps, legacy flat results and interruption limits.
- `--dry-run` renders the execution plan and exits without sending requests.
- `--mode elevated` requires `--confirm-elevated-risk` before the run starts.
- `--allow-host`, `--allow-origin`, `--allow-private-addresses`, `--max-requests`, and `--timeout` define the outbound execution budget instead of leaving it implicit.
- Experimental model evaluation stays outside the canonical validation result and is emitted as a companion artifact only when a supported provider is configured.

Current model-evaluation support:

- `--enable-model-eval` without `evaluation.modelEvaluation.provider` now fails fast instead of emitting a placeholder `Skipped` artifact.
- This build currently ships the deterministic companion provider `builtin-rubric` for experimental side-lane summaries.

Typical governance-first invocation:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server https://example.test/mcp \
  --mode safe \
  --dry-run \
  --allow-host example.test
```

The same contract applies to quick probes:

```bash
dotnet run --project Mcp.Benchmark.CLI -- health-check \
  --server https://example.test/mcp \
  --dry-run \
  --allow-host example.test

dotnet run --project Mcp.Benchmark.CLI -- discover \
  --server https://example.test/mcp \
  --format json \
  --dry-run \
  --allow-host example.test
```

## Configuration Files

An explicitly supplied configuration file must exist, be a regular non-symlink file, and fit the 1 MiB input limit. Configuration JSON must be an object with at most 64 levels of nesting. Malformed JSON, unknown fields, duplicate fields (including casing aliases), and null required sections fail before target contact; omitting the file still uses defaults.

CLI binding and configuration normalization occur before host construction. Host transport selection and the selected command consume one invocation-owned normalized input; a later file change does not replace that input. Undefined enum values, including collection items, are rejected. Zero-length inputs are rejected before opening, preventing the observed FIFO blocking case. These checks are not a sandbox against hostile filesystem path replacement; keep configuration files and their parent directories under trusted ownership.

Configuration field names accept casing aliases, including `server`/`Server` and `headers`/`Headers`. This does not lowercase dictionary keys: environment variables and arbitrary parameter names retain their case. Duplicate HTTP header names are rejected case-insensitively. Nullable settings remain nullable; non-nullable settings and string dictionary values must not explicitly be null.

This is stricter than 2.0.1, which could silently ignore unknown fields or a missing file. Remove misspelled or duplicated fields instead of relying on last-value-wins behavior. The same reader is used by validation, health/discovery configuration and offline report configuration. Diagnostics do not include configuration values or unrecognized key text.

Configuration documents may declare `"schemaVersion": "1.0.0"` at the root. Files without this property retain legacy interpretation; adding the current version does not change normalized settings. Unsupported versions, null or non-string versions, and duplicate version aliases fail preflight. This version identifies the configuration format, not the MCP protocol revision or canonical result contract. UTF-8 files with one leading byte-order mark are accepted; invalid UTF-8 is a usage error.

A minimal versioned configuration:

```json
{
  "schemaVersion": "1.0.0",
  "server": {
    "endpoint": "https://example.invalid/mcp",
    "transport": "http"
  },
  "execution": {
    "mode": "safe",
    "timeoutSeconds": 30
  }
}
```

The [configuration schema](../docs/Schemas/mcpval-configuration-v1.schema.json) is generated from the same serializer metadata as the parser. Emit it without target contact using:

```bash
mcpval config-schema
```

For migration, first remove unknown or duplicate fields and explicit nulls rejected by the parser, then add `"schemaVersion": "1.0.0"`. Do not relabel an unsupported future version as current without reviewing its settings. The schema describes structural input shape; byte/depth limits, duplicate aliases, case-insensitive header collisions and runtime numeric representation checks remain parser responsibilities. The checked-in schema has a drift test and must be regenerated deliberately when the contract changes.

Validation, health and discovery plans and audit manifests record value-free setting origins: default, file, profile, CLI or governance. A same-value explicit CLI setting still records the override. Dictionary/collection settings are tracked as units without recording arbitrary keys or values. Offline-report session receipts track only their effective reporting settings.

For health and discovery, an explicit `--timeout` (milliseconds) wins; otherwise the configured `execution.timeoutSeconds` wins over legacy `server.timeoutMs`. If neither is supplied, health retains its 30-second default and discovery its 120-second default. Milliseconds are rounded up to whole seconds for the execution budget. Non-positive explicit timeouts fail with usage exit 64. Safe-mode restrictions and derived budgets are recorded after command overrides.

The full entrypoint qualification matrix remains in progress; see the [implementation status](../docs/EnterpriseRecoveryProgress.md). Schema generation uses the locked System.Text.Json exporter described by the [official documentation revision](https://github.com/dotnet/docs/blob/700169ff5b8d4936652749c196f76eb31a512bd1/docs/standard/serialization/system-text-json/extract-schema.md).

## Internal Windows Supervision

The local MCP adapter uses a versioned internal CLI channel on Windows to own CLI workers and their descendants in an unnamed Job Object. It is not a user-facing command. The worker is created suspended with creation-time job assignment and an explicit three-handle stdio inheritance list; the job handle is not inherited. Parent control and bounded output use owned overlapped pipes.

The protocol requires compatible capability negotiation and a post-creation ready/resume exchange. Deadlines include creation and admission. Cancellation, parent loss, output failure or worker exit trigger job cleanup; only observed zero active job processes and drained output can produce a confirmed terminal receipt. Kill-on-close is a safety mechanism, not completion evidence. Windows 10 / Server 2016 or later is required; nested-job restrictions fail closed without breakaway flags.

Native Windows CI is a release dependency and rejects missing, failed or skipped native evidence. Portable compilation and mock tests do not establish Windows acceptance. Job Objects are not a sandbox; externally brokered processes are outside the containment claim. See [implementation status](../docs/EnterpriseRecoveryProgress.md).

## Design Guardrails

- Keep this project thin. Command handlers should translate CLI input into configuration and delegate real work to shared services.
- Keep protocol rules, transport logic, auth strategies, scoring, and report generation out of the CLI host.
- Keep client-specific compatibility logic out of Core and out of command handlers; it belongs in `Mcp.Benchmark.ClientProfiles`.
- Add new report surfaces by extending the shared reporting layer and wiring them here, not by duplicating rendering logic per command.

## Useful References

- [../README.md](../README.md) for installation, commands, and artifact behavior
- [../docs/FeatureMatrix.md](../docs/FeatureMatrix.md) for current command and transport support
- [../docs/Design/Architecture.md](../docs/Design/Architecture.md) for solution boundaries and runtime flow
- [../docs/Design/TechnicalArchitecture.md](../docs/Design/TechnicalArchitecture.md) for extension points and internal lifecycle details
