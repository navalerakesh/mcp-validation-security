#!/usr/bin/env node

const readline = require("node:readline");

function createProfiles() {
  return {
    compliant: {
      initialize: {
        protocolVersion: "2025-11-25",
        capabilities: {
          tools: {},
          prompts: {},
          resources: {}
        },
        serverInfo: {
          name: "fixture-compliant",
          version: "1.0.0"
        }
      },
      toolsPages: [
        [
          {
            name: "list_repositories",
            title: "List Repositories",
            description: "Enumerates repositories visible to the caller.",
            annotations: {
              title: "List Repositories",
              readOnlyHint: true,
              destructiveHint: false,
              openWorldHint: true,
              idempotentHint: true
            },
            inputSchema: {
              type: "object",
              properties: {
                query: {
                  type: "string",
                  description: "Optional repository search query."
                }
              }
            }
          }
        ],
        [
          {
            name: "get_repository",
            title: "Get Repository",
            description: "Fetches repository metadata for a single repository.",
            annotations: {
              title: "Get Repository",
              readOnlyHint: true,
              destructiveHint: false,
              openWorldHint: true,
              idempotentHint: true
            },
            inputSchema: {
              type: "object",
              properties: {
                repository: {
                  type: "string",
                  description: "Repository identifier."
                }
              },
              required: ["repository"]
            }
          }
        ]
      ],
      prompts: [
        {
          name: "code_review",
          description: "Review a code diff for correctness and security.",
          arguments: [
            {
              name: "diff",
              required: true,
              description: "Unified diff content to review."
            }
          ]
        }
      ],
      resources: [
        {
          uri: "file:///repo/README.md",
          name: "README.md",
          mimeType: "text/markdown"
        }
      ],
      resourceTemplates: [
        {
          name: "repository-file",
          uriTemplate: "repo://{owner}/{repo}/{path}",
          description: "Reads a file from a repository by owner, repo, and path."
        }
      ],
      toolCall() {
        return {
          content: [
            {
              type: "text",
              text: "repository-a\nrepository-b"
            }
          ],
          isError: false
        };
      },
      promptGet() {
        return {
          messages: [
            {
              role: "user",
              content: {
                type: "text",
                text: "Review the supplied change set."
              }
            }
          ]
        };
      },
      resourceRead() {
        return {
          contents: [
            {
              uri: "file:///repo/README.md",
              mimeType: "text/markdown",
              text: "# Example repository"
            }
          ]
        };
      }
    },
    partial: {
      initialize: {
        protocolVersion: "2024-11-05",
        capabilities: {
          tools: {}
        },
        serverInfo: {
          name: "fixture-partial",
          version: "1.0.0"
        }
      },
      tools: [
        {
          name: "search_repositories",
          description: "Searches repositories.",
          inputSchema: {
            type: "object",
            properties: {
              query: {
                type: "string",
                description: "Repository search query."
              }
            }
          }
        }
      ],
      prompts: [
        {
          description: "Prompt without a stable name."
        },
        {
          name: "broken_prompt",
          description: "Returns malformed prompt messages.",
          arguments: [
            {
              name: "topic",
              required: true,
              description: "Topic to inspect."
            }
          ]
        }
      ],
      resources: [
        {
          name: "README.md"
        },
        {
          uri: "file:///repo/BROKEN.md",
          name: "BROKEN.md",
          mimeType: "text/markdown"
        },
        {
          uri: "docs/README.md",
          name: "LOCAL.md"
        }
      ],
      resourceTemplates: [
        {
          name: "broken-template"
        },
        {
          name: "repo-file",
          uriTemplate: "repo://{owner}/{repo}/{path}"
        }
      ],
      toolCall() {
        return {
          content: [
            {
              type: "text",
              text: "partial search result"
            }
          ],
          isError: false
        };
      },
      promptGet(request) {
        if (request.params && request.params.name === "broken_prompt") {
          return {
            messages: [
              {
                content: {
                  type: "text",
                  text: "Missing role field."
                }
              }
            ]
          };
        }

        return {
          messages: []
        };
      },
      resourceRead(request) {
        if (request.params && request.params.uri === "file:///repo/BROKEN.md") {
          return {
            contents: [
              {
                uri: "file:///repo/BROKEN.md",
                mimeType: "text/markdown"
              }
            ]
          };
        }

        return {
          contents: []
        };
      }
    },
    unsafe: {
      initialize: {
        protocolVersion: "2025-11-25",
        capabilities: {
          tools: {},
          prompts: {},
          resources: {}
        },
        serverInfo: {
          name: "fixture-unsafe",
          version: "1.0.0"
        }
      },
      tools: [
        {
          name: "delete_repository",
          description: "Deletes a repository.",
          annotations: {
            title: "Delete Repository",
            readOnlyHint: false,
            openWorldHint: true,
            idempotentHint: false
          },
          inputSchema: {
            type: "object",
            properties: {
              repository: {
                type: "string"
              }
            },
            required: ["repository"]
          }
        },
        {
          name: "purge_repository",
          description: "Deletes repository data.",
          annotations: {
            title: "Purge Repository",
            readOnlyHint: false,
            destructiveHint: true,
            openWorldHint: true,
            idempotentHint: false
          },
          inputSchema: {
            type: "object",
            properties: {
              repository: {
                type: "string",
                description: "Repository identifier"
              }
            },
            required: ["repository"]
          }
        }
      ],
      prompts: [
        {
          name: "destructive_runbook",
          description: "Walk through a destructive maintenance action.",
          arguments: [
            {
              name: "target",
              required: true,
              description: "Target system to impact."
            }
          ]
        }
      ],
      resources: [
        {
          uri: "file:///repo/secrets.txt",
          name: "secrets.txt",
          mimeType: "text/plain"
        }
      ],
      resourceTemplates: [],
      toolCall(request) {
        return {
          content: [
            {
              type: "text",
              text: `executed ${request.params && request.params.name ? request.params.name : "tool"}`
            }
          ],
          isError: false
        };
      },
      promptGet() {
        return {
          messages: [
            {
              role: "user",
              content: {
                type: "text",
                text: "Review the destructive maintenance action."
              }
            }
          ]
        };
      },
      resourceRead() {
        return {
          contents: [
            {
              uri: "file:///repo/secrets.txt",
              mimeType: "text/plain",
              text: "prod-password=super-secret"
            }
          ]
        };
      }
    }
  };
}

function writeResponse(response) {
  process.stdout.write(`${JSON.stringify(response)}\n`);
}

function createSuccess(id, result) {
  return {
    jsonrpc: "2.0",
    result,
    id
  };
}

function createError(id, code, message) {
  return {
    jsonrpc: "2.0",
    error: { code, message },
    id
  };
}

function getToolsListResult(profile, request) {
  if (!Array.isArray(profile.toolsPages)) {
    return { tools: profile.tools };
  }

  const cursor = request.params && typeof request.params.cursor === "string"
    ? request.params.cursor
    : null;

  if (cursor === null) {
    return {
      tools: profile.toolsPages[0],
      nextCursor: profile.toolsPages.length > 1 ? "page-2" : undefined
    };
  }

  if (cursor === "page-2") {
    return { tools: profile.toolsPages[1] ?? [] };
  }

  return { tools: [] };
}

function startFixtureServer(profileName) {
  const profiles = createProfiles();
  const profile = profiles[profileName];

  if (!profile) {
    throw new Error(`Unknown fixture profile: ${profileName}`);
  }

  const rl = readline.createInterface({
    input: process.stdin,
    crlfDelay: Infinity
  });

  rl.on("line", (line) => {
    if (!line || !line.trim()) {
      return;
    }

    let request;
    try {
      request = JSON.parse(line);
    } catch {
      writeResponse(createError(null, -32700, "Parse error"));
      return;
    }

    const id = Object.prototype.hasOwnProperty.call(request, "id") ? request.id : null;

    switch (request.method) {
      case "initialize":
        writeResponse(createSuccess(id, profile.initialize));
        return;
      case "ping":
        writeResponse(createSuccess(id, { ok: true, profile: profileName }));
        return;
      case "tools/list":
        writeResponse(createSuccess(id, getToolsListResult(profile, request)));
        return;
      case "tools/call":
        writeResponse(createSuccess(id, profile.toolCall(request)));
        return;
      case "prompts/list":
        writeResponse(createSuccess(id, { prompts: profile.prompts }));
        return;
      case "prompts/get":
        writeResponse(createSuccess(id, profile.promptGet(request)));
        return;
      case "resources/list":
        writeResponse(createSuccess(id, { resources: profile.resources }));
        return;
      case "resources/read":
        writeResponse(createSuccess(id, profile.resourceRead(request)));
        return;
      case "resources/templates/list":
        writeResponse(createSuccess(id, { resourceTemplates: profile.resourceTemplates || [] }));
        return;
      default:
        writeResponse(createError(id, -32601, `Method not found: ${request.method}`));
    }
  });
}

module.exports = { startFixtureServer };

if (require.main === module) {
  const profileName = process.argv[2] || "compliant";
  startFixtureServer(profileName);
}