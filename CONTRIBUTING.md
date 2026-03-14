# Contributing to AI Security & Compliance Scanner

Thank you for your interest in contributing! This project aims to make AI integrations safer and more trustworthy for everyone.

## \ud83c\udfaf Our Mission

Help users and organizations **evaluate the security and compliance** of AI tools, LLM servers, and Model Context Protocol implementations through automated testing.

## \ud83d\udc65 How to Contribute

### For Security Researchers
- Add new attack vectors and security test scenarios
- Improve vulnerability detection accuracy
- Report security findings in AI protocols

### For Developers
- Implement missing features (see [Issues](https://github.com/navalerakesh/mcp-validation-security/issues))
- Improve test coverage
- Add support for new protocols and standards
- Enhance documentation

### For AI Users
- Report false positives/negatives
- Share real-world validation scenarios
- Suggest new compliance checks
- Provide feedback on reports

## \ud83d\udee0\ufe0f Development Setup

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git
- Your favorite IDE (VS Code, Visual Studio, Rider)

### Getting Started

1. **Fork the repository**
   ```bash
   # Click "Fork" on GitHub, then:
   git clone https://github.com/YOUR-USERNAME/mcp-validation-security.git
   cd mcp-validation-security
   ```

2. **Create a feature branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

3. **Build and test**
   ```bash
   dotnet build
   dotnet test
   ```

4. **Run the tool locally**
   ```bash
   dotnet run --project Mcp.Benchmark.CLI -- validate --help
   ```

## \ud83d\udcdd Pull Request Process

1. **Write clear commit messages**
   ```
   Add SQL injection detection for tool parameters
   
   - Implement pattern matching for common SQL injection attempts
   - Add test cases for false positive reduction
   - Update security validator documentation
   ```

2. **Update tests**
   - Add unit tests for new functionality
   - Add integration tests for end-to-end scenarios
   - Ensure all tests pass: `dotnet test`

3. **Update documentation**
   - Update README.md if adding new features
   - Add XML documentation to public APIs
   - Update architecture docs if changing design

4. **Submit PR**
   - Push to your fork
   - Open a Pull Request against `master`
   - Fill out the PR template
   - Link to related issues

## \ud83d\udcca Code Standards

### Architecture
- Follow Clean Architecture principles
- Core domain logic in `Mcp.Benchmark.Core`
- Infrastructure in `Mcp.Benchmark.Infrastructure`
- Keep dependencies pointing inward

### Code Style
- Follow C# conventions
- Use nullable reference types
- Add XML documentation to public APIs
- TreatWarningsAsErrors is enabled

### Testing
- Unit tests for business logic
- Integration tests for validators
- Architecture tests for dependency rules
- Aim for >80% coverage on new code

## \ud83d\udd12 Security Disclosures

If you discover a security vulnerability in this tool itself, please:

1. **DO NOT** open a public issue
2. Email the maintainer privately (see GitHub profile)
3. Include steps to reproduce
4. Allow 90 days for fix before public disclosure

## \ud83c\udf93 Learning Resources

New to AI security testing? Check out:

- [OWASP LLM Top 10](https://owasp.org/www-project-top-10-for-large-language-model-applications/)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- [OAuth 2.1 Security Best Practices](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics)

## \u2753 Questions?

- Open a [GitHub Discussion](https://github.com/navalerakesh/mcp-validation-security/discussions)
- Check existing [Issues](https://github.com/navalerakesh/mcp-validation-security/issues)
- Read the [Documentation](docs/README.md)

## \ud83d\udcdc License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

**Thank you for helping make AI safer for everyone! \ud83d\ude80**
