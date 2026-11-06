# Official Conformance And Extensions

MCP Validator treats official conformance, MCP core validation, and extension readiness as separate typed evidence lanes. External or optional evidence never earns unrelated pass credit or changes a neutral assessment outside its applicability.

## Official Conformance Import

Use `validate --conformance-results <wrapper.json>` with a document conforming to [`Schemas/mcpval-official-conformance-evidence.schema.json`](Schemas/mcpval-official-conformance-evidence.schema.json).

Generate the wrapper with the isolated repository companion:

```bash
node scripts/run-official-conformance.mjs \
  --url https://provider.example.com/mcp \
  --output /secure/path/official-conformance.json \
  --results-dir /secure/path/official-results

dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server https://provider.example.com/mcp \
  --conformance-results /secure/path/official-conformance.json
```

The companion is never invoked by `validate`. It downloads the exact pinned npm tarball into an isolated temporary directory, verifies the tarball and requirement-set SHA-256 values before execution, installs the exact committed transitive dependency lock with lifecycle scripts disabled, strips ambient credentials, bounds per-scenario and whole-run time, rejects stale result directories, and writes the wrapper atomically. Use it only against a target you own or are explicitly authorized to test.

The current trusted pin is:

| Input | Pinned value |
| --- | --- |
| Package | `@modelcontextprotocol/conformance@0.2.0-alpha.12` |
| Source commit | `f44482ba17df816d3176962a11cdf36aec9bda00` |
| Package SHA-256 | `6cd489231fe6a90de07ce940b1945ffef7d11063d17558b0a4c12a00f0e6f41f` |
| Requirement set | `2026-07-28` |
| Requirement-set SHA-256 | `ae2f4f6210fd729e2e318edd5bbfa31a43cee0bc608e48052fa26dbf1d939b57` |

The machine-readable pin is owned by `Mcp.Benchmark.Core/Resources/official-conformance-pin.json`; .NET verification and the companion consume that same manifest. The wrapper supplies provenance that the official per-scenario `checks.json` format does not carry itself. Every scenario embeds the exact bounded `checks.json` bytes as base64 and records their raw SHA-256. The importer verifies those bytes before parsing them and rejects malformed contracts, unsupported document majors, duplicate scenario/check IDs, invalid hashes, tampered payloads, target mismatches, protocol mismatches, configured bounds violations, and missing, unexpected, or misclassified frozen scenarios. A completed `2026-07-28` server run must contain all 37 required and 13 not-scored server scenarios.

An unknown future runner or requirement-set pin remains `Unverified` and receives no pass credit. A non-completed run remains `Incomplete`. If an official success contradicts a native review/reject decision with the same stable rule/check ID, the report records category `EvidenceConflict` with gate `ReviewRequired`; neither source silently overrides the other.

Raw official `details`, `metadata`, and `errorMessage` values are not copied into canonical results. Reports retain structured status, specification references, an error-presence marker, and an error-detail digest. The audit manifest binds the exact imported wrapper bytes.

## Extension Applicability

Modern `server/discover` populates extension IDs and bounded known settings. Packs resolve through the existing validation pack registry when an extension is advertised or explicitly selected. IDs use ordinal matching.

Every extension assessment records:

- extension ID and lifecycle stage;
- immutable specification revision and SHA-256;
- separate verdict and evidence coverage;
- structured findings excluded from aggregate core scoring.

Experimental extensions are observation-only. Beta extensions remain Preview and non-blocking. Stable extensions can produce a separate reject verdict when their own specification fails. Promotion to core requires a dated core revision; configuration cannot promote Beta evidence into core conformance.

## MCP Apps

MCP Apps uses extension ID `io.modelcontextprotocol/ui`, Stable revision `2026-01-26`.

Safe mode validates extension MIME settings, typed `_meta.ui.resourceUri` and visibility, deprecated metadata, `ui://` linkage, fallback descriptions, and already observed resource MIME types.

Standard/elevated mode reads only explicitly linked UI resources through the governed MCP transport. Analysis is bounded and uses AngleSharp for HTML5 parsing. It verifies URI/MIME/content representation, records content SHA-256, validates CSP origins with structured URI parsing, inventories camera/microphone/geolocation/clipboard requests, compares static external references with declared CSP origins, and detects dangerous schemes, nested-frame exposure, and executable object content.

The validator never renders Apps HTML. It does not claim host sandboxing, CSP headers, Permission Policy, postMessage validation, approval prompts, visibility filtering, or teardown behavior.

## MCP Skills Preview

Skills uses extension ID `io.modelcontextprotocol/skills` and remains Beta.

Passive analysis calls bounded paginated `skills/list` and validates result/cache metadata, skill URI/frontmatter identity, complete or dynamic manifests, SHA-256 syntax, byte sizes, in-root membership, per-skill limits, duplicates, name collisions, and risk-bearing frontmatter. Dynamic manifests and collisions remain review evidence.

SKILL.md content reads require both standard/elevated mode and explicit `--extension io.modelcontextprotocol/skills`. The validator reads at most 16 content-bound SKILL.md documents, verifies manifest size/digest, parses bounded YAML frontmatter with YamlDotNet, compares it field-for-field with the catalog entry, and checks verified instruction text for prompt-injection, secret-request, and code-execution risk.

The validator never executes scripts or skill instructions and never treats digests as a trust anchor. Server-only validation does not prove host approval, activation, origin tagging, cache isolation, content-bound reapproval, or cross-server read enforcement behavior.
