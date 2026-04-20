# Security Policy

## Reporting Security Vulnerabilities

If you discover a security vulnerability in MCP Validator itself, we appreciate your responsible disclosure.

### \ud83d\udea8 How to Report

**DO NOT** create a public GitHub issue for security vulnerabilities.

Instead:

1. **Email the maintainer** directly (see GitHub profile)
2. **Include** the following information:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

3. **Response timeline**:
   - Initial response: Within 48 hours
   - Status update: Within 7 days
   - Fix timeline: Within 90 days (depending on severity)

### \ud83d\udd12 Disclosure Policy

- We follow **coordinated vulnerability disclosure**
- We will acknowledge your contribution in release notes (unless you prefer anonymity)
- Please allow us **90 days** to fix critical issues before public disclosure
- We will credit security researchers who report responsibly

### \u2705 Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.1.x   | \u2705 Yes             |
| < 1.1   | \u274c No              |

### \ud83d\udee1\ufe0f Security Best Practices for Users

When using this tool to validate MCP servers for AI agent consumption:

- **Run in isolated environments** first (don't test production directly)
- **Review reports** before sharing (they may contain sensitive info like OAuth metadata)
- **Use authentication** (`--token` or `--interactive`) when validating authenticated servers
- **Respect rate limits** (`--max-concurrency`) to avoid DoS on target servers
- **Follow responsible disclosure** when finding vulnerabilities in tested servers
- **Review Trust Level findings** — L1/L2 servers should NOT be used by AI agents without remediation
- **Check AI Boundary Findings** for destructive tool exposure and data exfiltration risks

### ⚠️ AI Safety Considerations

This tool assesses MCP servers specifically for **AI agent safety**:

- **LLM-Friendliness scoring** grades whether error responses help AI agents self-correct (Pro-LLM vs Anti-LLM)
- **Injection reflection detection** identifies tools that echo back malicious payloads, which AI agents could then process
- **Destructive tool detection** flags tools that could cause harm when invoked by AI without human confirmation
- **Schema quality scoring** measures hallucination risk — poor schemas cause AI to generate incorrect tool calls

Servers rated below L3 (Acceptable) should be reviewed before granting AI agent access.

### \ud83d\udcdd Out of Scope

The following are **not** security vulnerabilities in this tool:

- Vulnerabilities found in **target servers** being tested (report those to server owners)
- False positives/negatives in security scans (report as bugs)
- Performance issues (report as bugs)
- Missing features (report as feature requests)

---

**Thank you for helping keep this project and the AI ecosystem secure!**
