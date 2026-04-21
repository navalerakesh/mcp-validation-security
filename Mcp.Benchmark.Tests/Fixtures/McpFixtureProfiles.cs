using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Fixtures;

public enum McpFixtureProfileKind
{
    Compliant,
    PartiallyCompliant,
    Unsafe
}

public static class McpFixtureProfiles
{
    public static JsonRpcResponse CreateInitializeResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.Compliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {
                      "tools": {},
                      "prompts": {},
                      "resources": {}
                    },
                    "serverInfo": {
                      "name": "fixture-compliant",
                      "version": "1.0.0"
                    }
                  },
                  "id": "fixture-init"
                }
                """,
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {
                      "tools": {}
                    },
                    "serverInfo": {
                      "name": "fixture-partial"
                    }
                  },
                  "id": "fixture-init"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {
                      "tools": {},
                      "prompts": {},
                      "resources": {}
                    },
                    "serverInfo": {
                      "name": "fixture-unsafe",
                      "version": "1.0.0"
                    }
                  },
                  "id": "fixture-init"
                }
                """
        };

        return Success(rawJson);
    }

    public static JsonRpcResponse CreateToolsListResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.Compliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "tools": [
                      {
                        "name": "list_repositories",
                        "title": "List Repositories",
                        "description": "Enumerates repositories visible to the caller.",
                        "annotations": {
                          "title": "List Repositories",
                          "readOnlyHint": true,
                          "destructiveHint": false,
                          "openWorldHint": true,
                          "idempotentHint": true
                        },
                        "inputSchema": {
                          "type": "object",
                          "properties": {
                            "query": {
                              "type": "string",
                              "description": "Optional repository search query."
                            }
                          }
                        }
                      }
                    ]
                  },
                  "id": "fixture-tools"
                }
                """,
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "tools": [
                      {
                        "name": "search_repositories",
                        "description": "Searches repositories.",
                        "inputSchema": {
                          "type": "object",
                          "properties": {
                            "query": {
                              "type": "string",
                              "description": "Repository search query."
                            }
                          }
                        }
                      }
                    ]
                  },
                  "id": "fixture-tools"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "tools": [
                      {
                        "name": "delete_repository",
                        "description": "Deletes a repository.",
                        "annotations": {
                          "title": "Delete Repository",
                          "readOnlyHint": false,
                          "openWorldHint": true,
                          "idempotentHint": false
                        },
                        "inputSchema": {
                          "type": "object",
                          "properties": {
                            "repository": {
                              "type": "string"
                            }
                          },
                          "required": ["repository"]
                        }
                      },
                      {
                        "name": "purge_repository",
                        "description": "Deletes repository data.",
                        "annotations": {
                          "title": "Purge Repository",
                          "readOnlyHint": false,
                          "destructiveHint": true,
                          "openWorldHint": true,
                          "idempotentHint": false
                        },
                        "inputSchema": {
                          "type": "object",
                          "properties": {
                            "repository": {
                              "type": "string",
                              "description": "Repository identifier"
                            }
                          },
                          "required": ["repository"]
                        }
                      }
                    ]
                  },
                  "id": "fixture-tools"
                }
                """
        };

        return Success(rawJson);
    }

    public static JsonRpcResponse CreatePromptsListResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.Compliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "prompts": [
                      {
                        "name": "code_review",
                        "description": "Review a code diff for correctness and security.",
                        "arguments": [
                          {
                            "name": "diff",
                            "required": true
                          }
                        ]
                      }
                    ]
                  },
                  "id": "fixture-prompts"
                }
                """,
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "prompts": [
                      {
                        "description": "Prompt without a stable name."
                      }
                    ]
                  },
                  "id": "fixture-prompts"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "prompts": [
                      {
                        "name": "destructive_runbook",
                        "description": "Walk through a destructive maintenance action.",
                        "arguments": [
                          {
                            "name": "target",
                            "required": true
                          }
                        ]
                      }
                    ]
                  },
                  "id": "fixture-prompts"
                }
                """
        };

        return Success(rawJson);
    }

    public static JsonRpcResponse CreatePromptGetResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "messages": [
                      {
                        "content": {
                          "type": "text",
                          "text": "Missing role field."
                        }
                      }
                    ]
                  },
                  "id": "fixture-prompt-get"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "messages": [
                      {
                        "role": "user",
                        "content": {
                          "type": "text",
                          "text": "Review the supplied change set."
                        }
                      }
                    ]
                  },
                  "id": "fixture-prompt-get"
                }
                """
        };

        return Success(rawJson);
    }

    public static JsonRpcResponse CreateResourcesListResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.Compliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "resources": [
                      {
                        "uri": "file:///repo/README.md",
                        "name": "README.md",
                        "mimeType": "text/markdown"
                      }
                    ]
                  },
                  "id": "fixture-resources"
                }
                """,
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "resources": [
                      {
                        "name": "README.md"
                      }
                    ]
                  },
                  "id": "fixture-resources"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "resources": [
                      {
                        "uri": "file:///repo/secrets.txt",
                        "name": "secrets.txt",
                        "mimeType": "text/plain"
                      }
                    ]
                  },
                  "id": "fixture-resources"
                }
                """
        };

        return Success(rawJson);
    }

    public static JsonRpcResponse CreateResourceReadResponse(McpFixtureProfileKind profile)
    {
        var rawJson = profile switch
        {
            McpFixtureProfileKind.PartiallyCompliant =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "contents": [
                      {
                        "uri": "file:///repo/README.md",
                        "mimeType": "text/markdown"
                      }
                    ]
                  },
                  "id": "fixture-resource-read"
                }
                """,
            McpFixtureProfileKind.Unsafe =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "contents": [
                      {
                        "uri": "file:///repo/secrets.txt",
                        "mimeType": "text/plain",
                        "text": "prod-password=super-secret"
                      }
                    ]
                  },
                  "id": "fixture-resource-read"
                }
                """,
            _ =>
                """
                {
                  "jsonrpc": "2.0",
                  "result": {
                    "contents": [
                      {
                        "uri": "file:///repo/README.md",
                        "mimeType": "text/markdown",
                        "text": "# Example repository"
                      }
                    ]
                  },
                  "id": "fixture-resource-read"
                }
                """
        };

        return Success(rawJson);
    }

    private static JsonRpcResponse Success(string rawJson)
    {
        return new JsonRpcResponse
        {
            StatusCode = 200,
            IsSuccess = true,
            RawJson = rawJson
        };
    }
}