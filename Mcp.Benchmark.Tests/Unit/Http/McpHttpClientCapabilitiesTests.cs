using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Exceptions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using McpServerCapabilities = ModelContextProtocol.Protocol.ServerCapabilities;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Unit.Http;

public class McpHttpClientCapabilitiesTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(12, false)]
    public void SuppliedArgumentInspection_ShouldBoundRepeatedAcyclicApplicationWork(int levels, bool admitted)
    {
        var definitions = new System.Text.Json.Nodes.JsonObject { [$"level{levels}"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" } };
        for (var level = levels - 1; level >= 0; level--)
        {
            var reference = new System.Text.Json.Nodes.JsonObject { ["$ref"] = $"#/$defs/level{level + 1}" };
            definitions[$"level{level}"] = new System.Text.Json.Nodes.JsonObject
            {
                ["properties"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["next"] = new System.Text.Json.Nodes.JsonObject
                    { ["allOf"] = new System.Text.Json.Nodes.JsonArray(reference, reference.DeepClone()) }
                }
            };
        }
        var schema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object", ["$defs"] = definitions, ["$ref"] = "#/$defs/level0" };
        using var document = JsonDocument.Parse(schema.ToJsonString());
        new SchemaInspectionContext(document.RootElement).CanEvaluateSuppliedArguments().Should().Be(admitted);
    }

    [Fact]
    public async Task OrdinaryHttpSseResponse_ShouldCorrelateFinalResultAfterNotification()
    {
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var result = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = body.RootElement.GetProperty("id"), result = new { resultType = "complete", content = Array.Empty<object>() } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{}}\n\ndata: " + result + "\n\n",
                    System.Text.Encoding.UTF8, "text/event-stream")
            };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2026-07-28");
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowPrivateAddresses = true, AllowedHosts = ["127.0.0.1"] });
        var response = await client.CallAsync("https://127.0.0.1/mcp", "tools/call", new { name = "owned-tool", arguments = new { } });
        response.IsSuccess.Should().BeTrue(response.Error);
        using var parsed = JsonDocument.Parse(response.RawJson!);
        parsed.RootElement.GetProperty("id").GetString().Should().Be(response.ProbeContext!.RequestId);
    }

    [Fact]
    public async Task ExplicitArguments_ShouldNotGrantSafeModeToolExecution()
    {
        var contacted = false;
        using var http = new HttpClient(new RoutingHandler(_ => { contacted = true; return CreateJsonResponse("{}"); }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2026-07-28");
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Safe, AllowPrivateAddresses = true, AllowedHosts = ["127.0.0.1"] });
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        using var arguments = JsonDocument.Parse("{}");
        var response = await client.CallAsync("https://127.0.0.1/mcp", "tools/call", new ToolCallParameters("owned-tool", arguments.RootElement, schema.RootElement));
        contacted.Should().BeFalse();
        response.IsSuccess.Should().BeFalse();
        response.ProbeContext!.ResponseClassification.Should().Be(ProbeResponseClassification.PolicyBlocked);
    }

    [Fact]
    public async Task OrdinaryHttpFailure_ShouldNotRetainExceptionPayloadInResultOrLogs()
    {
        const string canary = "private-http-exception-canary";
        var logger = new Mock<ILogger<McpHttpClient>>();
        using var http = new HttpClient(new RoutingHandler(_ => throw new HttpRequestException(canary)));
        using var client = new McpHttpClient(http, logger.Object, Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowPrivateAddresses = true, AllowedHosts = ["127.0.0.1"] });
        var response = await client.CallAsync("https://127.0.0.1/mcp", "tools/call", new { name = "owned-tool", arguments = new { value = canary } });

        response.IsSuccess.Should().BeFalse();
        JsonSerializer.Serialize(response).Should().NotContain(canary);
        var logged = string.Join("\n", logger.Invocations.SelectMany(invocation => invocation.Arguments).Select(argument => argument?.ToString()));
        logged.Should().NotContain(canary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrdinaryHttpResponse_ShouldMatchOutstandingRequestIdentity(bool matches)
    {
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var expected = body.RootElement.GetProperty("id").GetString();
            return CreateJsonResponse(JsonSerializer.Serialize(new
            { jsonrpc = "2.0", id = matches ? expected : "unrelated-response-id", result = new { resultType = "complete", content = Array.Empty<object>() } }));
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2026-07-28");
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowPrivateAddresses = true, AllowedHosts = ["127.0.0.1"] });

        var response = await client.CallAsync("https://127.0.0.1/mcp", "tools/call", new { name = "owned-tool", arguments = new { } });

        response.IsSuccess.Should().Be(matches);
        if (!matches) response.Error.Should().Contain("identity").And.NotContain("unrelated-response-id");
    }

    [Theory]
    [InlineData("{\"kind\":\"phone\",\"phone\":\"test\"}", true)]
    [InlineData("{\"kind\":\"phone\"}", false)]
    [InlineData("{\"kind\":\"email\",\"email\":\"test\"}", true)]
    [InlineData("{\"kind\":\"email\",\"phone\":\"test\"}", false)]
    public void SuppliedArgumentInspection_ShouldAdmitBoundedConditionalSchemaWithoutGeneratingInputs(string argumentsJson, bool valid)
    {
        const string schemaJson = """
            {"type":"object","$defs":{"address":{"type":"object","properties":{"city":{"type":"string"}}}},
             "properties":{"kind":{"type":"string"},"phone":{"type":"string"},"email":{"type":"string"},"address":{"$ref":"#/$defs/address"}},
             "if":{"properties":{"kind":{"const":"phone"}},"required":["kind"]},"then":{"required":["phone"]},"else":{"required":["email"]}}
            """;
        using var schema = JsonDocument.Parse(schemaJson);
        var inspection = new SchemaInspectionContext(schema.RootElement);
        inspection.CanEvaluateSuppliedArguments().Should().BeTrue();
        inspection.TryCreateObjectCandidate(out _).Should().BeFalse();
        using var instance = JsonDocument.Parse(argumentsJson);
        Json.Schema.JsonSchema.FromText(schemaJson, new Json.Schema.BuildOptions { Dialect = Json.Schema.Dialect.Draft202012 })
            .Evaluate(instance.RootElement).IsValid.Should().Be(valid, "the standard dialect must enforce the conditional requirement");
        new JsonSchemaValidator().Validate(System.Text.Json.Nodes.JsonNode.Parse(argumentsJson), System.Text.Json.Nodes.JsonNode.Parse(schemaJson)!)
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"$ref\":\"#\"}")]
    [InlineData("{\"type\":\"object\",\"$ref\":\"https://example.test/external\"}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\",\"pattern\":\"(a+)+$\"}}}")]
    [InlineData("{\"type\":\"object\",\"$defs\":{\"nested\":{\"$id\":\"https://example.test/nested\"}}}")]
    [InlineData("""{"type":"object","$defs":{"branch":{"properties":{"next":{"$ref":"#"}}}},"allOf":[{"$ref":"#/$defs/branch"},{"$ref":"#/$defs/branch"}]}""")]
    public void SuppliedArgumentInspection_ShouldRejectUnboundedOrAmbiguousEvaluation(string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        new SchemaInspectionContext(schema.RootElement).CanEvaluateSuppliedArguments().Should().BeFalse();
    }

    [Theory]
    [InlineData("if", false)]
    [InlineData("if", true)]
    [InlineData("not", false)]
    [InlineData("not", true)]
    [InlineData("patternProperties", false)]
    [InlineData("patternProperties", true)]
    [InlineData("dependentSchemas", false)]
    [InlineData("dependentSchemas", true)]
    public void ParameterHeaderInspection_ShouldSeparateStaticPlacementFromArgumentGeneration(string keyword, bool nestedHeader)
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse("""
            {"type":"object","properties":{"region":{"type":"string","x-mcp-header":"Region"}}}
            """)!;
        var child = System.Text.Json.Nodes.JsonNode.Parse("""{"properties":{"detail":{"type":"string"}}}""")!;
        if (nestedHeader) child["properties"]!["detail"]!["x-mcp-header"] = "Nested";
        schema[keyword] = keyword is "patternProperties" or "dependentSchemas"
            ? new System.Text.Json.Nodes.JsonObject { ["region"] = child } : child;
        using var document = JsonDocument.Parse(schema.ToJsonString());
        using var arguments = JsonDocument.Parse("""{"region":"test-region"}""");
        var inspection = new SchemaInspectionContext(document.RootElement);

        inspection.InspectParameterHeaderSchema().Should().Be(nestedHeader ? ValidationOutcome.Failed : ValidationOutcome.Succeeded);
        inspection.TryCreateObjectCandidate(out _).Should().BeFalse("header placement does not establish conditional argument-generation support");
        inspection.TryCreateParameterHeaders(arguments.RootElement, out var headers).Should().Be(!nestedHeader);
        if (!nestedHeader) headers.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Mcp-Param-Region", "test-region"));
    }

    [Theory]
    [InlineData("2026-07-28")]
    [InlineData("2025-11-25")]
    public async Task SchemaParameterHeaders_ShouldMirrorStaticPrimitiveValuesWithoutLeakingSchema(string version)
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"region":{"type":"string","x-mcp-header":"Region"},"settings":{"type":"object","properties":{"enabled":{"type":"boolean","x-mcp-header":"Enabled"}}},"count":{"type":"integer","x-mcp-header":"Count"},"absent":{"type":"string","x-mcp-header":"Absent"},"nullable":{"type":"string","x-mcp-header":"Nullable"}}}""");
        using var arguments = JsonDocument.Parse("""{"region":" padded ","settings":{"enabled":true},"count":42.0,"nullable":null}""");
        var parameters = new ToolCallParameters("owned-tool", arguments.RootElement, schema.RootElement);
        var observed = false;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            if (version == "2026-07-28")
            {
                request.Headers.GetValues("Mcp-Param-Region").Should().Equal("=?base64?IHBhZGRlZCA=?=");
                request.Headers.GetValues("Mcp-Param-Enabled").Should().Equal("true");
                request.Headers.GetValues("Mcp-Param-Count").Should().Equal("42");
            }
            else request.Headers.Should().NotContain(header => header.Key.StartsWith("Mcp-Param-", StringComparison.OrdinalIgnoreCase));
            request.Headers.Contains("Mcp-Param-Absent").Should().BeFalse();
            request.Headers.Contains("Mcp-Param-Nullable").Should().BeFalse();
            request.Headers.Contains("Mcp-Param-Unselected").Should().BeFalse();
            var json = await request.Content!.ReadAsStringAsync(token);
            json.Should().NotContain("InputSchema").And.NotContain("inputSchema").And.NotContain("x-mcp-header");
            using var body = JsonDocument.Parse(json);
            body.RootElement.GetProperty("params").EnumerateObject().Select(property => property.Name).Should()
                .BeEquivalentTo(version == "2026-07-28" ? new[] { "name", "arguments", "_meta" } : new[] { "name", "arguments" });
            observed = true;
            return CreateJsonResponse(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = body.RootElement.GetProperty("id"), result = new { resultType = "complete" } }));
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowPrivateAddresses = true, AllowedHosts = ["127.0.0.1"] });

        var result = await client.CallAsync("https://127.0.0.1/mcp", "tools/call", parameters,
            new AuthenticationConfig { CustomHeaders = new Dictionary<string, string> { ["Mcp-Param-Region"] = "auth-override-canary", ["Mcp-Param-Unselected"] = "auth-unselected-canary" } });

        observed.Should().BeTrue(result.Error);
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":"Region"},"b":{"type":"string","x-mcp-header":"region"}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"number","x-mcp-header":"Value"}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":"Bad Name"}}}""", "{}", false)]
    [InlineData("""{"type":"object","allOf":[{"properties":{"a":{"type":"string","x-mcp-header":"Value"}}}]}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":9007199254740991}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":9007199254740992}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":"Value"}}}""", """{"a":null}""", true)]
    [InlineData("""{"type":"object","examples":[{"x-mcp-header":"not-an-annotation"}]}""", "{}", true)]
    [InlineData("""{"type":"object","const":{"x-mcp-header":"not-an-annotation"}}""", "{}", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":""}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":42}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":"Bad\r\nName"}}}""", "{}", false)]
    [InlineData("""{"type":"string","x-mcp-header":"Root"}""", "{}", false)]
    [InlineData("""{"type":"object","$defs":{"value":{"type":"string","x-mcp-header":"Value"}},"properties":{"a":{"$ref":"#/$defs/value"}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"array","items":{"type":"string","x-mcp-header":"Value"}}}}""", "{}", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":-9007199254740991}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":-9007199254740992}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":42.5}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":42.00000000000000000000000000001}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":9007199254740991.0000000000001}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":1e-1000}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"integer","x-mcp-header":"Value"}}}""", """{"a":42e1}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","x-mcp-header":"Value"}}}""", """{"a":42}""", false)]
    public void ParameterHeaderSchema_ShouldRespectStaticPathsAndValueBounds(string schemaJson, string argumentsJson, bool expected)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        using var arguments = JsonDocument.Parse(argumentsJson);
        new SchemaInspectionContext(schema.RootElement).TryCreateParameterHeaders(arguments.RootElement, out _).Should().Be(expected);
    }

    [Fact]
    public void ParameterHeaderSchema_ShouldRejectExhaustedInspectionBudget()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", examples = Enumerable.Range(0, 4097).ToArray() });
        var arguments = JsonSerializer.SerializeToElement(new { });
        new SchemaInspectionContext(schema).TryCreateParameterHeaders(arguments, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Region", ValidationOutcome.Inconclusive)]
    [InlineData("Bad Name", ValidationOutcome.Failed)]
    public void ParameterHeaderSchema_ShouldRetainKnownFailureWithUninspectableNeighbor(string header, ValidationOutcome expected)
    {
        using var schema = JsonDocument.Parse("""
            {"type":"object","$defs":{"unused":{"$id":"https://example.invalid/schema","type":"string"}},"properties":{"region":{"type":"string","x-mcp-header":
            """ + JsonSerializer.Serialize(header) + "}}}");

        new SchemaInspectionContext(schema.RootElement).InspectParameterHeaderSchema().Should().Be(expected);
    }

    [Fact]
    public async Task ParameterHeaderEvidence_ShouldOmitMirroredValues()
    {
        const string canary = "synthetic-parameter-canary";
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.GetValues("Mcp-Param-Tenant").Should().Equal(canary);
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            Headers = new() { ["Mcp-Param-Tenant"] = canary }
        });

        response.IsSuccess.Should().BeTrue();
        response.RequestHeaders.Keys.Should().NotContain(name => name.StartsWith("Mcp-Param-", StringComparison.OrdinalIgnoreCase));
        JsonSerializer.Serialize(response.RequestHeaders).Should().NotContain(canary);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ParseErrorProbe_ShouldPreserveValidTransportControlHeaders(string version)
    {
        var observed = false;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            (await request.Content!.ReadAsStringAsync(token)).Should().Be("{ invalid json");
            request.Headers.Accept.Select(value => value.MediaType).Should().BeEquivalentTo("application/json", "text/event-stream");
            request.Headers.GetValues("MCP-Protocol-Version").Should().Equal(version);
            if (version == "2026-07-28") request.Headers.GetValues("Mcp-Method").Should().Equal("server/discover");
            else request.Headers.Contains("Mcp-Method").Should().BeFalse();
            observed = true;
            var response = CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}");
            response.StatusCode = HttpStatusCode.BadRequest;
            return response;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowedHosts = ["127.0.0.1"], AllowPrivateAddresses = true });

        var result = await client.ValidateErrorCodesAsync("https://127.0.0.1/mcp", JsonRpcErrorProbeSelection.ParseError);

        observed.Should().BeTrue();
        result.Tests.Should().ContainSingle().Which.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-07-28", "tools/call", "plain-tool", false)]
    [InlineData("2026-07-28", "prompts/get", " padded ", true)]
    [InlineData("2026-07-28", "resources/read", "test://resource/\u00e9", true)]
    [InlineData("2026-07-28", "tools/call", "=?base64?literal?=", true)]
    [InlineData("2026-07-28", "tools/call", "line\r\nbreak", true)]
    [InlineData("2025-11-25", "tools/call", "plain-tool", false)]
    public async Task NamedRpcHeaders_ShouldMatchCapturedBodyAndIgnoreAuthOverrides(string version, string method, string name, bool encoded)
    {
        var observed = false;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = document.RootElement;
            var property = method == "resources/read" ? "uri" : "name";
            root.GetProperty("params").GetProperty(property).GetString().Should().Be(name);
            if (version == "2026-07-28")
            {
                var expected = encoded ? "=?base64?" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name)) + "?=" : name;
                request.Headers.GetValues("Mcp-Name").Should().Equal(expected);
            }
            else request.Headers.Contains("Mcp-Name").Should().BeFalse();
            observed = true;
            return CreateJsonResponse(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = root.GetProperty("id"), result = new { resultType = "complete" } }));
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, AllowedHosts = ["127.0.0.1"], AllowPrivateAddresses = true });
        var parameters = new Dictionary<string, object> { [method == "resources/read" ? "uri" : "name"] = name };
        var response = await client.CallAsync("https://127.0.0.1/mcp", method, parameters,
            new AuthenticationConfig { CustomHeaders = new Dictionary<string, string> { ["Mcp-Name"] = "auth-override-canary" } });

        observed.Should().BeTrue(response.Error);
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GovernedSdkHttp_SafeMode_ShouldPreserveDiscoveryAndSessionCleanup()
    {
        var methods = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy
        {
            Mode = ExecutionMode.Safe,
            AllowedHosts = ["example.test"]
        }));
        using var policy = new GovernedRequestPolicyHandler(context)
        {
            InnerHandler = new AsyncRoutingHandler(async (request, token) =>
            {
                if (request.Method == HttpMethod.Delete)
                {
                    methods.Enqueue("DELETE");
                    request.Headers.GetValues("MCP-Session-Id").Should().Equal("synthetic-owned-session");
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = document.RootElement;
                var method = root.GetProperty("method").GetString()!;
                methods.Enqueue(method);
                if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
                object result = method switch
                {
                    "initialize" => new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "owned-policy-fixture", version = "1.0" }
                    },
                    "tools/list" => new { tools = Array.Empty<object>() },
                    _ => throw new InvalidOperationException("Unexpected synthetic fixture method.")
                };
                var response = CreateJsonResponse(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = root.GetProperty("id"), result }));
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                if (method == "initialize") response.Headers.Add("MCP-Session-Id", "synthetic-owned-session");
                return response;
            })
        };
        using var http = new HttpClient(policy);
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://example.test/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, http, ownsHttpClient: false);
        await using (var sdk = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-03-26" }))
        {
            var tools = await sdk.ListToolsAsync();
            tools.Should().BeEmpty();
        }

        methods.Should().Equal("initialize", "notifications/initialized", "tools/list", "DELETE");
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("raw")]
    [InlineData("timeout")]
    [InlineData("interruption")]
    public async Task ConfiguredSafeStdio_ShouldDenyActiveMethodBeforeSessionAccess(string path)
    {
        using var client = new StdioMcpClientAdapter(Mock.Of<ILogger<StdioMcpClientAdapter>>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Safe });
        Func<Task> send = () => path switch
        {
            "raw" => client.SendRawJsonAsync("owned-command", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"}", CancellationToken.None),
            "timeout" => client.ProbeTimeoutRecoveryAsync("owned-command"),
            "interruption" => client.ProbeConnectionInterruptionRecoveryAsync("owned-command"),
            _ => client.CallAsync("owned-command", "tools/call")
        };

        await send.Should().ThrowAsync<OperationAdmissionException>().WithMessage("*did not admit*");
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("TRACE", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("DELETE", false)]
    public async Task GovernedSdkHttp_SafeMode_ShouldRejectUnclassifiedHttpOperations(string method, bool expectedContact)
    {
        var requests = 0;
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { Mode = ExecutionMode.Safe, AllowedHosts = ["example.test"] }));
        using var policy = new GovernedRequestPolicyHandler(context)
            { InnerHandler = new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }) };
        using var client = new HttpClient(policy);
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://example.test/mcp");

        if (expectedContact) using (await client.SendAsync(request)) { }
        else await FluentActions.Awaiting(() => client.SendAsync(request)).Should().ThrowAsync<OperationAdmissionException>();
        requests.Should().Be(expectedContact ? 1 : 0);
    }

    [Theory]
    [InlineData("tools/list", true)]
    [InlineData("tools/call", false)]
    public async Task GovernedSdkHttp_SafeMode_ShouldAdmitOnlyKnownDiscovery(string method, bool expectedContact)
    {
        var requests = 0;
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { Mode = ExecutionMode.Safe, AllowedHosts = ["example.test"] }));
        using var policy = new GovernedRequestPolicyHandler(context)
        {
            InnerHandler = new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); })
        };
        using var client = new HttpClient(policy);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = new { } }))
        };

        if (expectedContact) using (await client.SendAsync(request)) { }
        else await FluentActions.Awaiting(() => client.SendAsync(request)).Should().ThrowAsync<OperationAdmissionException>().WithMessage("*did not admit*");
        requests.Should().Be(expectedContact ? 1 : 0);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":-32700}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":\"-32700\",\"message\":\"Parse error\"}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"method\":\"tools/list\"}")]
    public async Task GovernedSdkHttp_SafeMode_ShouldRejectAmbiguousOrMalformedEnvelope(string body)
    {
        var requests = 0;
        var context = new RunNetworkPolicyContext();
        context.Configure(OperationPolicySnapshot.From(new ExecutionPolicy { Mode = ExecutionMode.Safe, AllowedHosts = ["example.test"] }));
        using var policy = new GovernedRequestPolicyHandler(context)
            { InnerHandler = new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }) };
        using var client = new HttpClient(policy);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/mcp") { Content = new StringContent(body) };

        await FluentActions.Awaiting(() => client.SendAsync(request)).Should().ThrowAsync<OperationAdmissionException>();
        requests.Should().Be(0);
    }

    [Theory]
    [InlineData("tools/call", "rpc")]
    [InlineData("tools/call", "raw")]
    [InlineData("tools/call", "probe")]
    [InlineData("resources/read", "rpc")]
    [InlineData("prompts/get", "rpc")]
    [InlineData("custom/write", "rpc")]
    [InlineData("tools/list", "raw")]
    [InlineData("tools/list", "probe")]
    public async Task ConfiguredSafeMode_ShouldRejectActiveOperationsBeforeReceiver(string method, string path)
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Safe, AllowedHosts = ["example.test"], AllowPrivateAddresses = true });
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = new { } });

        ProbeContext? context;
        if (path == "rpc") context = (await client.CallAsync("https://example.test/mcp", method)).ProbeContext;
        else if (path == "raw") context = (await client.SendRawJsonAsync("https://example.test/mcp", body, CancellationToken.None)).ProbeContext;
        else context = (await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest { Endpoint = "https://example.test/mcp", Body = body })).ProbeContext;

        requests.Should().Be(0);
        context.Should().NotBeNull();
        ValidationOutcomeTaxonomy.From(context!.ResponseClassification).Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("raw")]
    [InlineData("probe")]
    public async Task ConnectionPolicyDenial_ShouldSurviveHttpWrappingWithoutRetry(string path)
    {
        var connections = 0;
        using var sockets = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = (_, _) =>
            {
                Interlocked.Increment(ref connections);
                throw new OperationAdmissionException("Synthetic connection-policy denial.");
            }
        };
        using var http = new HttpClient(sockets);
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            Mode = ExecutionMode.Standard,
            AllowedHosts = ["example.test"],
            AllowPrivateAddresses = true
        });
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        ProbeContext? context;
        if (path == "rpc") context = (await client.CallAsync("https://example.test/mcp", "tools/list")).ProbeContext;
        else if (path == "raw") context = (await client.SendRawJsonAsync("https://example.test/mcp", body, CancellationToken.None)).ProbeContext;
        else context = (await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest { Endpoint = "https://example.test/mcp", Body = body })).ProbeContext;

        connections.Should().Be(1);
        context.Should().NotBeNull();
        context!.ResponseClassification.Should().Be(ProbeResponseClassification.PolicyBlocked);
        Mcp.Benchmark.Core.Services.ValidationReliability.ShouldRetryException(
            new HttpRequestException("Synthetic wrapper.", new OperationAdmissionException("Denied.")), CancellationToken.None).Should().BeFalse();
    }

    [Theory]
    [InlineData("host", "rpc")]
    [InlineData("host", "raw")]
    [InlineData("host", "probe")]
    [InlineData("origin", "rpc")]
    [InlineData("origin", "raw")]
    [InlineData("origin", "probe")]
    [InlineData("budget", "rpc")]
    [InlineData("budget", "raw")]
    [InlineData("budget", "probe")]
    public async Task ConfiguredTargetPolicy_ShouldRetainBlockedAttribution(string denial, string path)
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            requests++;
            return CreateCorrelatedJsonResponse(request, "{\"jsonrpc\":\"2.0\",\"result\":{}}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            Mode = ExecutionMode.Standard,
            AllowedHosts = denial == "host" ? ["allowed.example.test"] : ["example.test"],
            AllowedOrigins = denial == "origin" ? ["https://example.test:8443"] : [],
            AllowPrivateAddresses = true,
            MaxRequests = 1
        });
        if (denial == "budget")
            (await client.CallAsync("https://example.test/mcp", "tools/list")).IsSuccess.Should().BeTrue();

        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        ProbeContext? context;
        if (path == "rpc") context = (await client.CallAsync("https://example.test/mcp", "tools/list")).ProbeContext;
        else if (path == "raw") context = (await client.SendRawJsonAsync("https://example.test/mcp", body, CancellationToken.None)).ProbeContext;
        else context = (await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest { Endpoint = "https://example.test/mcp", Body = body })).ProbeContext;

        requests.Should().Be(denial == "budget" ? 1 : 0);
        context.Should().NotBeNull();
        ValidationOutcomeTaxonomy.From(context!.ResponseClassification).Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResilienceProbe_DisallowedTarget_ShouldNotReachReceiver(bool interruption)
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            requests++;
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            Mode = ExecutionMode.Standard,
            AllowedHosts = ["allowed.example.test"],
            AllowPrivateAddresses = true
        });

        if (interruption) await client.ProbeConnectionInterruptionRecoveryAsync("https://blocked.example.test/mcp");
        else await client.ProbeTimeoutRecoveryAsync("https://blocked.example.test/mcp");

        requests.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawRpc_QueuedRequest_ShouldKeepDefaultRoutingHeaders(bool addedAfterSelection)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        string? route = null;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            else route = request.Headers.GetValues("X-Tenant-Route").Single();
            return CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}");
        }));
        if (!addedAfterSelection) http.DefaultRequestHeaders.Add("X-Tenant-Route", "first-route");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetConcurrencyLimit(1);
        var first = client.CallAsync("https://example.test/mcp", "tools/list");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = client.CallAsync("https://example.test/mcp", "tools/list");
        http.DefaultRequestHeaders.Remove("X-Tenant-Route");
        http.DefaultRequestHeaders.Add("X-Tenant-Route", "second-route");
        release.SetResult();
        var responses = await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(10));

        if (addedAfterSelection)
        {
            count.Should().Be(1);
            responses[1].IsSuccess.Should().BeFalse();
            responses[1].StatusCode.Should().BeLessThan(0);
        }
        else route.Should().Be("first-route");
    }

    [Fact]
    public async Task RawRpc_QueuedRequest_ShouldKeepSessionSnapshotAcrossProbeRotation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        string? queuedSession = null;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            var response = CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{}}}");
            if (body.Contains("session-one", StringComparison.Ordinal)) response.Headers.Add("MCP-Session-Id", "synthetic-session-one");
            else if (body.Contains("session-two", StringComparison.Ordinal)) response.Headers.Add("MCP-Session-Id", "synthetic-session-two");
            else if (Interlocked.Increment(ref requests) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            else queuedSession = request.Headers.GetValues("MCP-Session-Id").Single();
            return response;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2025-11-25");
        client.SetConcurrencyLimit(1);
        await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
            { Endpoint = "https://example.test/mcp", Body = "{\"method\":\"initialize\",\"id\":\"session-one\"}", CaptureSessionId = true });
        var first = client.CallAsync("https://example.test/mcp", "tools/list");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = client.CallAsync("https://example.test/mcp", "tools/list");
        try
        {
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
                { Endpoint = "https://example.test/mcp", Body = "{\"method\":\"initialize\",\"id\":\"session-two\"}", CaptureSessionId = true });
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(10));

        queuedSession.Should().Be("synthetic-session-one");
    }

    [Fact]
    public async Task RawRpc_QueuedRequest_ShouldKeepSelectedAuthenticationAndRoutingSnapshot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        string? authorization = null;
        string? route = null;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            else
            {
                authorization = request.Headers.Authorization?.Parameter;
                route = request.Headers.GetValues("X-Tenant-Route").Single();
            }
            return CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetConcurrencyLimit(1);
        var selected = new AuthenticationConfig
        {
            Type = "bearer", Token = "synthetic-first-principal",
            CustomHeaders = new() { ["X-Tenant-Route"] = "first-route" }
        };
        client.SetAuthentication(selected);
        var first = client.CallAsync("https://example.test/mcp", "tools/list");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = client.CallAsync("https://example.test/mcp", "tools/list");
        selected.Token = "synthetic-mutated-principal";
        selected.CustomHeaders["X-Tenant-Route"] = "mutated-route";
        client.SetAuthentication(new AuthenticationConfig
        {
            Type = "bearer", Token = "synthetic-second-principal",
            CustomHeaders = new() { ["X-Tenant-Route"] = "second-route" }
        });
        release.SetResult();
        var responses = await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(10));

        authorization.Should().Be("synthetic-first-principal");
        route.Should().Be("first-route");
        JsonSerializer.Serialize(responses.Select(response => response.ProbeContext)).Should().NotContain("synthetic-");
    }

    [Fact]
    public async Task RawRpc_QueuedRequest_ShouldKeepProtocolHeaderAndBodyOnSameSnapshot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        string? bodyVersion = null;
        string? headerVersion = null;
        string? methodHeader = null;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            else
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                bodyVersion = body.RootElement.GetProperty("params").GetProperty("_meta").GetProperty("io.modelcontextprotocol/protocolVersion").GetString();
                headerVersion = request.Headers.GetValues("MCP-Protocol-Version").Single();
                methodHeader = request.Headers.TryGetValues("MCP-Method", out var values) ? values.Single() : null;
            }
            return CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"complete\"}}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetConcurrencyLimit(1);
        client.SetProtocolVersion("2026-07-28");
        var first = client.CallAsync("https://example.test/mcp", "tools/list");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = client.CallAsync("https://example.test/mcp", "tools/list");
        client.SetProtocolVersion("2025-11-25");
        release.SetResult();
        await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(10));

        bodyVersion.Should().Be("2026-07-28");
        headerVersion.Should().Be(bodyVersion);
        methodHeader.Should().Be("tools/list");
    }

    [Theory]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    public async Task RawAndTransportRequests_ShouldCarryRevisionAppropriateMethodHeader(string version, bool transportProbe)
    {
        string? observedMethod = null;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            observedMethod = request.Headers.TryGetValues("MCP-Method", out var values) ? values.Single() : null;
            return CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"resultType\":\"complete\"}}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}";

        if (transportProbe)
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest { Endpoint = "https://example.test/mcp", Body = body });
        else
            await client.SendRawJsonAsync("https://example.test/mcp", body, CancellationToken.None);

        observedMethod.Should().Be(version == "2026-07-28" ? "tools/list" : null);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "intentional-mismatch")]
    public async Task TransportProbe_MethodHeaderMutation_ShouldRemainExplicit(bool includeMethod, string? overriddenMethod)
    {
        string? observed = null;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            observed = request.Headers.TryGetValues("MCP-Method", out var values) ? values.Single() : null;
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2026-07-28");
        var headers = new Dictionary<string, string>();
        if (overriddenMethod is not null) headers["MCP-Method"] = overriddenMethod;

        await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Body = "{\"method\":\"tools/list\"}",
            IncludeMethodHeader = includeMethod, Headers = headers
        });

        observed.Should().Be(overriddenMethod);
    }

    [Fact]
    public async Task ErrorHandling_LocalDispatchRejection_ShouldNotBlameTarget()
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }));
        http.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new ErrorHandlingConfig { TestInvalidMethods = true, TestMalformedJson = false, TestGracefulDegradation = false,
                TestTimeoutHandling = false, TestConnectionInterruption = false });

        requests.Should().Be(0);
        result.Status.Should().Be(TestStatus.Inconclusive);
        result.Findings.Should().NotContain(finding => finding.EffectiveSource == ValidationRuleSource.Spec);
        result.ErrorScenariosHandledCorrectly.Should().Be(0);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task RawJson_ShouldDescribeActualAuthorization(int statusCode)
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            return new HttpResponseMessage((HttpStatusCode)statusCode) { Content = new StringContent("{}") };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["Authorization"] = "Bearer synthetic-canary" } });

        var response = await client.SendRawJsonAsync("https://example.test/mcp", "{}", CancellationToken.None);

        response.ProbeContext!.AuthApplied.Should().BeTrue();
        response.ProbeContext.AuthStatus.Should().Be(statusCode == 401 ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.Applied);
        JsonSerializer.Serialize(response.ProbeContext).Should().NotContain("synthetic-canary");
    }

    [Fact]
    public async Task PostConstructionFailure_ShouldDisposeOwnedContentWithoutContact()
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ => { requests++; return CreateJsonResponse("{}"); }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["Invalid Header"] = "synthetic-canary" } });
        using var content = new DisposalTrackingContent();

        var send = () => client.SendAsync("https://example.test/mcp", content);

        await send.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().Be(0);
        content.WasDisposed.Should().BeTrue();
    }

    private sealed class DisposalTrackingContent() : StringContent("{}")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData(JsonRpcErrorProbeSelection.None, 0)]
    [InlineData(JsonRpcErrorProbeSelection.ParseError, 1)]
    [InlineData(JsonRpcErrorProbeSelection.InvalidRequest, 1)]
    [InlineData(JsonRpcErrorProbeSelection.MethodNotFound, 1)]
    [InlineData(JsonRpcErrorProbeSelection.InvalidParams, 1)]
    [InlineData(JsonRpcErrorProbeSelection.All, 4)]
    public async Task SelectiveHttpErrorProbes_ShouldNotDispatchUnselectedOperations(JsonRpcErrorProbeSelection selection, int expectedAttempts)
    {
        var requests = 0;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            requests++;
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.ValidateErrorCodesAsync("https://example.test/mcp", selection);

        requests.Should().Be(expectedAttempts);
        result.Tests.Should().HaveCount(expectedAttempts);
        if (selection == JsonRpcErrorProbeSelection.None) result.IsCompliant.Should().BeFalse();
        var invalid = () => client.ValidateErrorCodesAsync("https://example.test/mcp", (JsonRpcErrorProbeSelection)16);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().Be(expectedAttempts);
    }

    [Fact]
    public async Task ErrorHandling_InvalidMethodOnly_ShouldDispatchOnlyTheAdmittedProbe()
    {
        var payloads = new List<string>();
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            payloads.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var validator = new ErrorHandlingValidator(Mock.Of<ILogger<ErrorHandlingValidator>>(), client);

        var result = await validator.ValidateErrorHandlingAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            new ErrorHandlingConfig
            {
                TestInvalidMethods = true, TestMalformedJson = false, TestGracefulDegradation = false,
                TestTimeoutHandling = false, TestConnectionInterruption = false
            });

        payloads.Should().ContainSingle();
        using var requestBody = JsonDocument.Parse(payloads[0]);
        requestBody.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        requestBody.RootElement.GetProperty("method").GetString().Should().NotBe("tools/call");
        result.Status.Should().Be(TestStatus.Passed);
        result.ErrorScenariosTestCount.Should().Be(1);
    }

    [Fact]
    public async Task TransportProbe_SessionComparison_ShouldUseRequestSnapshotAcrossConcurrentSessionChange()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializeCount = 0;
        using var http = new HttpClient(new AsyncRoutingHandler(async (request, token) =>
        {
            var payload = await request.Content!.ReadAsStringAsync(token);
            if (payload.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
            {
                var response = CreateCorrelatedJsonResponse(request, """{"jsonrpc":"2.0","result":{"protocolVersion":"2025-11-25","capabilities":{}}}""");
                response.Headers.Add("MCP-Session-Id", $"session-{Interlocked.Increment(ref initializeCount)}");
                return response;
            }
            request.Headers.GetValues("MCP-Session-Id").Should().Equal("session-1");
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(string.Empty) };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        await client.CallAsync("https://example.test/mcp", "initialize", new { });
        var probe = client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST",
            Body = """{"jsonrpc":"2.0","method":"notifications/initialized"}"""
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await client.CallAsync("https://example.test/mcp", "initialize", new { }); }
        finally { release.TrySetResult(); }

        var result = await probe.WaitAsync(TimeSpan.FromSeconds(10));

        result.RequestSessionIdMatchesSession.Should().BeTrue();
    }

    private sealed class AsyncRoutingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => factory(request, cancellationToken);
    }

    private static HttpResponseMessage CreateCorrelatedJsonResponse(HttpRequestMessage request, string json)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var response = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        response["id"] = System.Text.Json.Nodes.JsonNode.Parse(body.RootElement.GetProperty("id").GetRawText());
        return CreateJsonResponse(response.ToJsonString());
    }

    [Theory]
    [InlineData("tools/call", true)]
    [InlineData("tools/call", false)]
    [InlineData("custom/write", true)]
    [InlineData("custom/write", false)]
    [InlineData("initialize", true)]
    [InlineData("initialize", false)]
    public async Task RawRpc_ShouldNotReplayUncertainOperations(string method, bool timeout)
    {
        var acceptedOperations = 0;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            acceptedOperations++;
            if (acceptedOperations > 1) return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
            if (timeout) throw new TaskCanceledException("synthetic timeout after acceptance");
            var failure = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("unavailable after acceptance") };
            failure.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return failure;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { Type = "bearer", Token = "synthetic-token" });

        var result = await client.CallAsync("https://example.test/mcp", method, new { });

        acceptedOperations.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RawRpc_DiscoveryRetry_ShouldPreserveRequestCorrelation()
    {
        var ids = new List<string>();
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            ids.Add(body.RootElement.GetProperty("id").GetString()!);
            if (ids.Count > 1) return CreateCorrelatedJsonResponse(request, """{"jsonrpc":"2.0","result":{"tools":[]}}""");
            var failure = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("temporary discovery failure") };
            failure.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return failure;
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.CallAsync("https://example.test/mcp", "tools/list");

        result.IsSuccess.Should().BeTrue();
        ids.Should().HaveCount(2);
        ids.Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Theory]
    [InlineData("matched", true)]
    [InlineData("missing", false)]
    [InlineData("wrong", false)]
    public async Task ProtocolValidation_ShouldVerifySessionPropagationWithoutPersistingSessionCanary(string sessionBehavior, bool expectedMatch)
    {
        const string canary = "synthetic-session-response-canary";
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            if (sessionBehavior != "matched" && payload.Contains("\"notifications/initialized\"", StringComparison.Ordinal))
            {
                request.Headers.Remove("MCP-Session-Id");
                if (sessionBehavior == "wrong") request.Headers.Add("MCP-Session-Id", "wrong-session");
            }
            var response = CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{}}}""");
            response.Headers.Add("MCP-Session-Id", canary);
            return response;
        }));
        var sdk = new Mock<IMcpClient>();
        sdk.Setup(client => client.InitializeAsync(It.IsAny<McpServerConfig>(), It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25", ServerInfo = new Implementation { Name = "fixture", Version = "1" }, Capabilities = new McpServerCapabilities()
            });
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), sdk.Object);
        client.SetProtocolVersion("2025-11-25");
        var schemas = new EmbeddedSchemaRegistry();
        var validator = new ProtocolComplianceValidator(Mock.Of<ILogger<ProtocolComplianceValidator>>(), client,
            new ProtocolRuleRegistry(), new ValidationApplicabilityResolver(schemas),
            new ProtocolFeatureResolver(new ValidationPackRegistry<IProtocolFeaturePack>([new BuiltInProtocolFeaturePack(schemas)])));

        var result = await validator.ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http", ProtocolVersion = "2025-11-25" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.StreamableHttpTransport.Should().NotBeNull();
        result.StreamableHttpTransport!.Probes.Should().Contain(probe => probe.ProbeId == "session-id-propagated" && probe.Passed == expectedMatch);
        var canonical = new ValidationResult { ProtocolCompliance = result }.CloneWithoutSecrets();
        JsonSerializer.Serialize(canonical).Should().NotContain(canary);
    }

    [Theory]
    [InlineData("MCP-Protocol-Version", "2024-11-05")]
    [InlineData("MCP-Session-Id", "ambient-session")]
    [InlineData("Mcp-Method", "tools/call")]
    [InlineData("Mcp-Name", "ambient-name")]
    [InlineData("mcp-param-Tenant", "ambient-tenant")]
    [InlineData("Authorization", "Bearer ambient-canary")]
    public async Task TransportProbe_ShouldRejectAmbientRuntimeHeadersBeforeContact(string name, string value)
    {
        var contacted = false;
        using var http = new HttpClient(new RoutingHandler(_ => { contacted = true; return CreateJsonResponse("{}"); }));
        http.DefaultRequestHeaders.Add(name, value);
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var result = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            IncludeDefaultAuthentication = false, IncludeProtocolVersionHeader = false, IncludeSessionIdHeader = false
        });

        contacted.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotContain(value);
        http.DefaultRequestHeaders.GetValues(name).Should().Equal(value);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task RawRpc_ShouldPreserveRuntimeIdentityAgainstCustomHeaders(string version)
    {
        var received = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            foreach (var header in request.Headers) received[header.Key] = string.Join(",", header.Value);
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion(version);
        var authentication = new AuthenticationConfig
        {
            CustomHeaders = new()
            {
                ["MCP-Protocol-Version"] = "2024-11-05",
                ["MCP-Session-Id"] = "unapproved-session",
                ["Mcp-Method"] = "tools/call"
            }
        };

        var response = await client.CallAsync("https://example.test/mcp", "tools/list", null, authentication);

        received["MCP-Protocol-Version"].Should().Be(version);
        received.Should().NotContainKey("MCP-Session-Id");
        if (version == "2026-07-28") received["Mcp-Method"].Should().Be("tools/list");
        else received.Should().NotContainKey("Mcp-Method");
        response.ProbeContext!.ProtocolVersion.Should().Be(version);
    }

    [Fact]
    public async Task TransportProbe_ShouldOmitRuntimeHeadersAndRouteContentHeaders()
    {
        var received = false;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            received = true;
            request.Headers.Contains("MCP-Protocol-Version").Should().BeFalse();
            request.Headers.Contains("MCP-Session-Id").Should().BeFalse();
            request.Content!.Headers.ContentLanguage.Should().Equal("en");
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetProtocolVersion("2025-11-25");
        client.SetAuthentication(new AuthenticationConfig
        {
            CustomHeaders = new() { ["MCP-Protocol-Version"] = "2024-11-05", ["MCP-Session-Id"] = "unapproved-session" }
        });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            Headers = new() { ["Content-Language"] = "en" },
            IncludeProtocolVersionHeader = false, IncludeSessionIdHeader = false
        });

        received.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task RawRpc_ShouldDeriveAuthEvidenceFromFinalAuthorizationHeader(int statusCode)
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{}}""")
            };
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());

        var response = await client.CallAsync("https://example.test/mcp", "tools/list", null,
            new AuthenticationConfig { CustomHeaders = new() { ["Authorization"] = "Bearer synthetic-canary" } });

        response.ProbeContext!.AuthApplied.Should().BeTrue();
        response.ProbeContext.AuthStatus.Should().Be(statusCode == 401 ? ProbeAuthStatus.InvalidOrExpired : ProbeAuthStatus.Applied);
    }

    [Fact]
    public async Task TransportProbe_ShouldRedactSessionCanaryFromEvidenceAndConfiguration()
    {
        const string canary = "synthetic-session-canary";
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.GetValues("MCP-Session-Id").Should().Equal(canary);
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
            Headers = new() { ["MCP-Session-Id"] = canary }, IncludeSessionIdHeader = false
        });
        var configuration = new McpServerConfig
        {
            Headers = new() { ["MCP-Session-Id"] = canary },
            Authentication = new AuthenticationConfig { CustomHeaders = new() { ["MCP-Session-Id"] = canary } }
        };

        response.IsSuccess.Should().BeTrue();
        JsonSerializer.Serialize(response.RequestHeaders).Should().NotContain(canary);
        JsonSerializer.Serialize(configuration.CloneWithoutSecrets()).Should().NotContain(canary);
    }

    [Theory]
    [InlineData("rpc", false)]
    [InlineData("rpc", true)]
    [InlineData("post", false)]
    [InlineData("probe", false)]
    [InlineData("probe", true)]
    public async Task RawRequests_ShouldApplySelectedCustomHeadersWithoutMutatingDefaults(string path, bool explicitAuthentication)
    {
        string? receivedRoute = null;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            receivedRoute = string.Join(",", request.Headers.GetValues("X-Route"));
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        http.DefaultRequestHeaders.Add("X-Route", "client-default");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { ["X-Route"] = "run-default" } });
        var authentication = explicitAuthentication
            ? new AuthenticationConfig { CustomHeaders = new() { ["x-route"] = "request-specific" } }
            : null;

        if (path == "rpc")
            await client.CallAsync("https://example.test/mcp", "tools/list", null, authentication);
        else if (path == "post")
        {
            using var response = await client.SendAsync("https://example.test/mcp", new StringContent("{}"));
        }
        else
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
            {
                Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}",
                Authentication = authentication, IncludeDefaultAuthentication = true
            });

        receivedRoute.Should().Be(explicitAuthentication ? "request-specific" : "run-default");
        http.DefaultRequestHeaders.GetValues("X-Route").Should().Equal("client-default");
    }

    [Fact]
    public async Task TransportProbe_ShouldTransmitButRedactCredentialHeaders()
    {
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            request.Headers.GetValues("Cookie").Should().Equal("synthetic-session=value");
            request.Headers.GetValues("X-Api-Key").Should().Equal("synthetic-api-value");
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig
        {
            CustomHeaders = new() { ["Cookie"] = "synthetic-session=value", ["X-Api-Key"] = "synthetic-api-value", ["X-Route"] = "route" }
        });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}"
        });

        response.IsSuccess.Should().BeTrue();
        response.RequestHeaders["cookie"].Should().NotContain("synthetic-session");
        response.RequestHeaders["X-Api-Key"].Should().NotContain("synthetic-api-value");
        response.RequestHeaders["X-Route"].Should().Be("route");
    }

    [Theory]
    [InlineData("Invalid Header", "synthetic-secret")]
    [InlineData("X-Api-Key", "synthetic-secret\r\nInjected: value")]
    public async Task TransportProbe_WithInvalidCustomHeader_ShouldFailWithoutContactOrSecretEcho(string name, string value)
    {
        var contacted = false;
        using var http = new HttpClient(new RoutingHandler(_ =>
        {
            contacted = true;
            return CreateJsonResponse("{}");
        }));
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig { CustomHeaders = new() { [name] = value } });

        var response = await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
        {
            Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}"
        });

        contacted.Should().BeFalse();
        response.IsSuccess.Should().BeFalse();
        response.Error.Should().NotContain("synthetic-secret");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitNoAuthentication_ShouldNotRestoreDefaultCredentials(bool useRpc)
    {
        var received = false;
        using var http = new HttpClient(new RoutingHandler(request =>
        {
            received = true;
            request.Headers.Authorization.Should().BeNull();
            request.Headers.Contains("X-Api-Key").Should().BeFalse();
            request.Headers.GetValues("X-Route").Should().Equal("stable-route");
            return CreateJsonResponse("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        }));
        http.DefaultRequestHeaders.Add("X-Route", "stable-route");
        using var client = new McpHttpClient(http, Mock.Of<ILogger<McpHttpClient>>(), Mock.Of<IMcpClient>());
        client.SetAuthentication(new AuthenticationConfig
        {
            Type = "bearer", Token = "synthetic-token", CustomHeaders = new() { ["X-Api-Key"] = "synthetic-key" }
        });

        if (useRpc)
            await client.CallAsync("https://example.test/mcp", "tools/list", null, new AuthenticationConfig { Token = string.Empty });
        else
            await client.SendHttpTransportProbeAsync(new HttpTransportProbeRequest
            {
                Endpoint = "https://example.test/mcp", Method = "POST", Body = "{}", IncludeDefaultAuthentication = false
            });

        received.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateInitializeAsync_CallerCancellation_ShouldPropagateWithoutRawFallback()
    {
        var mcpClient = new Mock<IMcpClient>();
        mcpClient
            .Setup(client => client.InitializeAsync(
                It.IsAny<McpServerConfig>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var httpClient = new HttpClient(new RoutingHandler(_ => throw new InvalidOperationException("Raw fallback must not execute after caller cancellation.")));
        var client = new McpHttpClient(httpClient, Mock.Of<ILogger<McpHttpClient>>(), mcpClient.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => client.ValidateInitializeAsync("https://example.test/mcp", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }
    [Fact]
    public async Task ValidateCapabilitiesAsync_UsesMcpClientForToolsAndBuildsSummary()
    {
        // Arrange
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities
                {
                    Tools = new ToolsCapability()
                }
            });

        mcpClientMock
            .Setup(x => x.ListToolsAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<McpClientTool>());

        mcpClientMock
            .Setup(x => x.CallToolAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                "demo-tool",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        // Act
        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();

        var summary = result.Payload!;
        summary.Tools.Should().NotBeNull();
        summary.Tools.Should().BeEmpty();
        summary.ToolListingSucceeded.Should().BeTrue();
        summary.ToolInvocationSucceeded.Should().BeFalse();
        summary.DiscoveredToolsCount.Should().Be(0);

        mcpClientMock.Verify(x => x.ListToolsAsync(
            It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        mcpClientMock.Verify(x => x.CallToolAsync(
            It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_WhenSdkDiscoveryFallsBackToRawJson_UsesRawToolCount()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var method = document.RootElement.GetProperty("method").GetString();

            return method switch
            {
                "tools/list" => CreateCorrelatedJsonResponse(request, "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"demo-tool\"},{\"name\":\"secondary-tool\"}]}}"),
                "resources/list" => CreateCorrelatedJsonResponse(request, "{\"jsonrpc\":\"2.0\",\"result\":{\"resources\":[]}}"),
                "prompts/list" => CreateCorrelatedJsonResponse(request, "{\"jsonrpc\":\"2.0\",\"result\":{\"prompts\":[]}}"),
                "tools/call" => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"},\"id\":\"call\"}")
                },
                _ => throw new InvalidOperationException($"Unexpected method '{method}'.")
            };
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities
                {
                    Tools = new ToolsCapability(),
                    Resources = new ResourcesCapability(),
                    Prompts = new PromptsCapability()
                }
            });

        mcpClientMock
            .Setup(x => x.ListToolsAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SDK tool discovery failed"));

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        result.Payload!.DiscoveredToolsCount.Should().Be(2);
        result.Payload.FirstToolName.Should().Be("demo-tool");
        result.Payload.ToolListingSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_WhenToolsAreNotAdvertised_DoesNotProbeToolSurface()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(_ =>
            throw new InvalidOperationException("No JSON-RPC probes should run when no capabilities are advertised.")))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();

        mcpClientMock
            .Setup(x => x.InitializeAsync(
                It.Is<McpServerConfig>(cfg => cfg.Endpoint == endpoint),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                Capabilities = new McpServerCapabilities()
            });

        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateCapabilitiesAsync(endpoint, CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        result.Payload!.CapabilityDeclarationsAvailable.Should().BeTrue();
        result.Payload.AdvertisedCapabilities.Should().BeEmpty();
        result.Payload.ToolListResponse.Should().BeNull();
        result.Payload.ToolListingSucceeded.Should().BeFalse();

        mcpClientMock.Verify(x => x.ListToolsAsync(
            It.IsAny<McpServerConfig>(),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateErrorCodesAsync_WithPlainText400MalformedRequests_ShouldNotTreatThemAsJsonRpcCompliant()
    {
        const string endpoint = "http://localhost:8080/mcp";

        using var httpClient = new HttpClient(new RoutingHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            if (payload == "{ invalid json")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("malformed payload")
                };
            }

            if (payload == "{\"method\":\"test\",\"id\":1,\"params\":{}}")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("invalid message version tag")
                };
            }

            using var document = JsonDocument.Parse(payload);
            var method = document.RootElement.GetProperty("method").GetString();

            return method switch
            {
                "nonexistent_method_12345" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":\"1\"}"),
                "tools/call" => CreateJsonResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"},\"id\":\"1\"}"),
                _ => throw new InvalidOperationException($"Unexpected payload '{payload}'.")
            };
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var result = await client.ValidateErrorCodesAsync(endpoint, CancellationToken.None);

        result.IsCompliant.Should().BeFalse();
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32700 && !test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32600 && !test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32601 && test.IsValid);
        result.Tests.Should().ContainSingle(test => test.ExpectedErrorCode == -32602 && test.IsValid);
    }

    [Fact]
    public async Task CallAsync_WithMultilineSseEvent_ShouldPreserveFullJsonPayload()
    {
        const string endpoint = "http://localhost:8080/mcp";
        const string ssePayload = "event: message\n" +
                                  "data: {\"jsonrpc\":\"2.0\",\n" +
                                  "data: \"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":\"1\"}\n\n";

        using var httpClient = new HttpClient(new RoutingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return response;
        }))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var loggerMock = new Mock<ILogger<McpHttpClient>>();
        var mcpClientMock = new Mock<IMcpClient>();
        var client = new McpHttpClient(httpClient, loggerMock.Object, mcpClientMock.Object);

        var response = await client.CallAsync(endpoint, "nonexistent_method_12345", null, CancellationToken.None);

        response.RawJson.Should().NotBeNullOrWhiteSpace();

        using var document = JsonDocument.Parse(response.RawJson!);
        document.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
