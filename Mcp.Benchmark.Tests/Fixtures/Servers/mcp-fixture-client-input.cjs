const assert = require("node:assert/strict");
const readline = require("node:readline");
const input = readline.createInterface({ input: process.stdin });
const state = " \topaque-client-input-canary ";
let calls = 0;
let previousId;

const lifetime = setTimeout(() => process.exit(2), 30_000);
input.on("close", () => { clearTimeout(lifetime); process.exit(0); });
input.on("line", line => {
  const request = JSON.parse(line);
  let result;
  try {
    const capabilities = request.params._meta["io.modelcontextprotocol/clientCapabilities"];
    if (request.method === "tools/call" || request.method === "prompts/get") {
      calls++;
      assert.deepEqual(capabilities, { roots: {} });
      assert.equal(request.params.name, "owned-client-input");
      assert.deepEqual(request.params.arguments, { value: "private-client-input-canary" });
      if (calls === 1) {
        assert.equal(request.params.inputResponses, undefined);
        assert.equal(request.params.requestState, undefined);
        previousId = request.id;
        result = { resultType: "input_required", inputRequests: { roots: { method: "roots/list" } }, requestState: state };
      } else {
        assert.equal(calls, 2);
        assert.notEqual(request.id, previousId);
        assert.equal(request.params.requestState, state);
        assert.deepEqual(request.params.inputResponses, { roots: { roots: [] } });
        result = request.method === "tools/call"
          ? { resultType: "complete", content: [{ type: "text", text: "private-client-input-canary" }] }
          : { resultType: "complete", messages: [{ role: "user", content: { type: "text", text: "private-client-input-canary" } }] };
      }
    } else {
      assert.deepEqual(capabilities, {});
      if (request.method === "tools/list")
        result = { resultType: "complete", tools: [{ name: "owned-client-input", inputSchema: { type: "object", properties: { value: { type: "string" } } } }] };
      else if (request.method === "prompts/list")
        result = { resultType: "complete", cacheScope: "public", ttlMs: 0, prompts: [{ name: "owned-client-input", arguments: [{ name: "value", required: true }] }] };
      else if (request.method === "fixture/stats")
        result = { resultType: "complete", calls, pid: process.pid };
      else throw new Error("Unsupported fixture operation");
    }
    process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id: request.id, result }) + "\n");
  } catch {
    process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id: request.id, error: { code: -32602, message: "Fixture contract mismatch" } }) + "\n");
  }
});
