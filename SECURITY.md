# Security Policy

## Reporting A Vulnerability

If you discover a security issue in MCP Validator itself, do not open a public GitHub issue.

Report it privately through GitHub private vulnerability reporting:

[Submit a private vulnerability report](https://github.com/navalerakesh/mcp-validation-security/security/advisories/new)

If GitHub private vulnerability reporting is unavailable, contact the maintainer by email using the contact details listed on the GitHub profile.

Include the following information:

- A clear description of the issue
- Steps to reproduce it
- The expected versus actual behavior
- The potential impact
- Any suggested mitigation or fix, if you already have one

## Response Expectations

- Initial acknowledgement within 48 hours
- Status update within 7 days
- Fix target within 90 days, depending on severity and exploitability

## Disclosure Policy

- This project follows coordinated vulnerability disclosure.
- Please allow time for investigation and remediation before making the issue public.
- Contributors who report issues responsibly can be credited in release notes unless they prefer to remain anonymous.

## Supported Versions

| Version | Supported |
| --- | --- |
| `1.1.x` | Yes |
| `< 1.1` | No |

## Safe Use Guidance

When using MCP Validator against third-party or production-adjacent systems:

- Start with `mcpval validate --dry-run` so the execution plan is explicit before network traffic begins.
- Use the same dry-run-first workflow for `mcpval health-check` and `mcpval discover` when probing third-party targets.
- Prefer `--mode safe` unless you intentionally need a broader execution surface.
- Use `--allow-host` to pin the outbound host set when validating remote systems.
- Do not use `--mode elevated` without an explicit review and `--confirm-elevated-risk`.
- Run first against a non-production environment whenever possible.
- Review generated reports before sharing them externally because they may contain operational metadata.
- Use `--token` or `--interactive` only when the target is expected to require authentication.
- Set `--max-concurrency` conservatively for shared or rate-limited services.
- Use `--persistence-mode ephemeral` or `explicit-output` when you do not want session logs on disk.
- Treat `--enable-model-eval` as an experimental companion lane. It now requires an explicit supported provider and never changes the deterministic baseline verdict.
- Treat low-trust results as a signal for review, not as a reason to probe a server more aggressively.
- Follow responsible disclosure practices when the validator surfaces weaknesses in a target server.

## AI Safety Scope

MCP Validator evaluates more than transport and protocol mechanics. It also examines AI-agent-facing risks such as:

- Destructive tool exposure without adequate confirmation signals
- Prompt injection or instruction-reflection surface area
- Data exfiltration opportunities exposed through tools or resources
- Error quality and schema clarity that influence AI self-correction behavior

Servers rated below `L3` should be reviewed before being granted autonomous agent access.

## Out Of Scope

The following should not be reported as security vulnerabilities in this repository:

- Security issues in target servers being tested by the validator
- False positives or false negatives in validation results
- General performance issues
- Feature requests or missing integrations
