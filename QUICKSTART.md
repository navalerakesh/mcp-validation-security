# Quick Start Guide

## 🚀 Get Started in 5 Minutes

This guide shows you how to validate your first AI server in under 5 minutes.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- Windows, Linux, or macOS

## Step 1: Install the Tool

### Option A: Via dotnet tool (Recommended)

```bash
# Will be available after NuGet publish
dotnet tool install --global Mcp.Benchmark.CLI

# Verify installation
mcpval --version
```

### Option B: Build from source

```bash
git clone https://github.com/navalerakesh/mcp-validation-security.git
cd mcp-validation-security
dotnet build
dotnet run --project Mcp.Benchmark.CLI -- --version
```

## Step 2: Choose a Server to Test

### Option A: Test a Public MCP Server

**GitHub Copilot MCP** (requires GitHub authentication):
```bash
mcpval validate --server https://api.githubcopilot.com/mcp/
```

### Option B: Test a Local Example Server

We provide example servers you can run locally:

```bash
# Coming soon: Python example server
cd examples/simple-mcp-server
python server.py

# In another terminal:
mcpval validate --server http://localhost:8000/mcp
```

### Option C: Test Your Own Server

```bash
mcpval validate --server https://your-server.com/mcp --output ./reports
```

## Step 3: Review Results

The tool will:
1. ✅ Check protocol compliance (JSON-RPC, MCP specification)
2. 🔒 Test security posture (authentication, input validation, attacks)
3. 🛠️ Validate tool/resource/prompt implementations
4. ⚡ Measure performance and concurrency handling
5. 📊 Generate a compliance score (0-100)

### Console Output

You'll see real-time progress:
```
🔍 Starting MCP Validation...
✓ Health check passed (2.3s)
✓ Protocol compliance: PASSED (98.5%)
✓ Security assessment: PASSED (95.2%)
✓ Tool validation: PASSED (100%)
⚡ Performance test: PASSED (87.3%)

📊 Overall Score: 95.3% - PASSED
```

### Report Files

Reports are saved to your output directory:
```
./reports/
  mcp-validation-20260115-143022-report.md    # Human-readable summary
  mcp-validation-20260115-143022-result.json  # Machine-readable results
```

## Step 4: Understand Your Score

### Score Ranges
- **90-100**: Excellent - Production ready
- **75-89**: Good - Minor improvements needed
- **60-74**: Fair - Several issues to address
- **Below 60**: Poor - Significant work required

### What Gets Tested

#### ✅ Protocol Compliance (25%)
- JSON-RPC 2.0 format correctness
- MCP specification adherence
- Version negotiation
- Schema validation

#### 🔒 Security Testing (35%)
- Authentication mechanisms
- Authorization checks
- Input validation
- Injection attack resistance
- Error message disclosure

#### 🛠️ Functionality (25%)
- Tool discovery and execution
- Resource access
- Prompt handling
- Capability advertisement

#### ⚡ Performance (15%)
- Response time
- Concurrent request handling
- Rate limiting
- Resource utilization

## Common Validation Scenarios

### Scenario 1: Pre-Deployment Check
```bash
# Run full validation before deploying to production
mcpval validate \
  --server https://staging-server.com/mcp \
  --access authenticated \
  --token $YOUR_TOKEN \
  --output ./pre-deploy-reports \
  --verbose
```

### Scenario 2: CI/CD Integration
```bash
# In your GitHub Actions / Azure Pipeline
mcpval validate \
  --server $SERVER_URL \
  --max-concurrency 2 \
  --output ./test-results

# Fails pipeline if score < 75
```

### Scenario 3: Security Audit
```bash
# Focus on security testing only
mcpval validate \
  --server https://your-server.com/mcp \
  --access authenticated \
  --output ./security-audit \
  --verbose
```

### Scenario 4: Comparing Servers
```bash
# Test multiple servers and compare
mcpval validate --server https://server-a.com/mcp --output ./reports/server-a
mcpval validate --server https://server-b.com/mcp --output ./reports/server-b

# Compare the generated reports
```

## Authentication Options

### Public Server (No Auth)
```bash
mcpval validate --server https://public-server.com/mcp
```

### API Key Authentication
```bash
mcpval validate \
  --server https://api-server.com/mcp \
  --token "sk-abc123..."
```

### OAuth / GitHub Auth
```bash
# Interactive authentication (opens browser)
mcpval validate \
  --server https://api.githubcopilot.com/mcp/ \
  --access authenticated \
  --interactive
```

## Generating HTML Reports

Convert JSON results to shareable HTML:

```bash
# Generate HTML report
mcpval report \
  --input ./reports/mcp-validation-20260115-143022-result.json \
  --format html \
  --output ./reports/validation-report.html

# Open in browser
open ./reports/validation-report.html  # macOS
start ./reports/validation-report.html # Windows
```

## Troubleshooting

### "Server not reachable"
- Check the URL is correct
- Verify the server is running
- Check firewall/network settings

### "Authentication failed"
- Verify your token is valid
- Check --access flag matches server requirements
- Try --interactive for OAuth flows

### "STDIO transport not supported"
- STDIO support is coming soon
- Use HTTP/SSE endpoints for now
- Track progress: [GitHub Issue #X]

### "Validation failed"
- Review the detailed report in output directory
- Check server logs for errors
- File an issue if you think it's a false positive

## Next Steps

### For Users
- ✅ Test servers before using them
- 📊 Compare different AI tools
- 🔒 Verify security claims
- 📝 Share reports with your team

### For Developers
- 🛠️ Fix issues found in validation
- 🔄 Run in CI/CD pipelines
- 📈 Track improvement over time
- 🏆 Aim for 95+ score

### For Contributors
- 🐛 Report bugs or false positives
- 💡 Suggest new security checks
- 🌟 Star the repo if helpful
- 📝 Write about your experience

## Getting Help

- 📖 [Full Documentation](../docs/README.md)
- 🐛 [Report Issues](https://github.com/navalerakesh/mcp-validation-security/issues)
- 💬 [Discussions](https://github.com/navalerakesh/mcp-validation-security/discussions)
- 🔒 [Security Issues](SECURITY.md)

---

**Ready to make AI safer? Start validating! 🚀**
