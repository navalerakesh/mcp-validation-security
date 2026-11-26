const http = require("node:http");
const mode = process.argv[2] ?? "positive";
const credentials = JSON.parse(process.env.MCPVAL_RELAY_CREDENTIALS);
const records = { front: [], backend: [], value: 0 };
const reply = (res, code, body) => {
  res.writeHead(code, { "content-type": "application/json" });
  res.end(JSON.stringify(body));
};
async function body(req) {
  let text = "";
  for await (const chunk of req) {
    text += chunk;
    if (Buffer.byteLength(text) > 8192) throw new Error("Bound exceeded");
  }
  return JSON.parse(text);
}
const backend = http.createServer(async (req, res) => {
  try {
    const request = await body(req);
    const entry = {
      request, startedAt: new Date().toISOString(),
      endpoint: `http://127.0.0.1:${backend.address().port}${req.url}`,
      identityMatched: req.headers.authorization === `Bearer ${credentials.backend}`
    };
    records.backend.push(entry);
    if (mode === "late-response") await new Promise(resolve => setTimeout(resolve, 50));
    const code = entry.identityMatched ? 200 : 401;
    if (entry.identityMatched) records.value += Number(request.params.arguments.value);
    entry.response = { resultType: "complete", content: [{ type: "text", text: "backend-saved" }] };
    entry.responseRequestId = request.id;
    entry.httpStatus = code;
    entry.completedAt = new Date().toISOString();
    reply(res, code, { jsonrpc: "2.0", id: request.id, result: entry.response });
  } catch {
    reply(res, 400, { error: "Fixture request invalid" });
  }
});
const front = http.createServer(async (req, res) => {
  try {
    if (req.url === "/observer") {
      if (req.headers.authorization !== `Bearer ${credentials.observer}`) return reply(res, 403, {});
      return reply(res, mode === "observer-unavailable" ? 503 : 200, records);
    }
    const request = await body(req);
    const entry = {
      request, startedAt: new Date().toISOString(),
      endpoint: `http://127.0.0.1:${front.address().port}${req.url}`,
      identityMatched: req.headers.authorization === `Bearer ${credentials.front}`
    };
    records.front.push(entry);
    entry.cancellationObserved = false;
    const closed = new Promise(resolve => res.once("close", () => {
      if (!res.writableEnded) entry.cancellationObserved = true;
      resolve();
    }));
    if (!entry.identityMatched) return reply(res, 401, {});
    for (let attempt = 0; attempt < (mode === "replay" ? 2 : 1); attempt++) {
      const downstream = {
        jsonrpc: "2.0", id: `backend-${attempt}`,
        method: "tools/call",
        params: {
          name: mode === "wrong-route" ? "unapproved-write" : "internal-write",
          arguments: { value: Number(request.params.arguments.value) * 2 },
          _meta: { "io.modelcontextprotocol/protocolVersion": "2026-07-28", "io.modelcontextprotocol/clientCapabilities": {},
            "fixture.parentRequestId": mode === "wrong-correlation" ? "unrelated" : request.id }
        }
      };
      const response = await fetch(`http://127.0.0.1:${backend.address().port}/${mode === "wrong-target" ? "unapproved" : "mcp"}`, {
        method: "POST", headers: { authorization: `Bearer ${mode === "wrong-identity" ? credentials.front : credentials.backend}`,
          "content-type": "application/json" },
        body: JSON.stringify(downstream), signal: AbortSignal.timeout(2000)
      });
      await response.text();
    }
    if (mode === "client-cancel") {
      entry.waitingForCancellation = true;
      await closed;
      entry.response = null;
      entry.responseRequestId = null;
      entry.httpStatus = null;
      entry.completedAt = new Date().toISOString();
      return;
    }
    entry.response = { resultType: "complete", content: [{ type: "text", text: "saved" }] };
    entry.responseRequestId = mode === "wrong-response-id" ? "unrelated" : request.id;
    entry.httpStatus = 200;
    entry.completedAt = new Date().toISOString();
    if (mode === "response-lost") {
      entry.response = null;
      entry.httpStatus = null;
      res.destroy();
    } else reply(res, 200, { jsonrpc: "2.0", id: entry.responseRequestId, result: entry.response });
  } catch {
    reply(res, 503, { error: "Owned downstream unavailable" });
  }
});
backend.listen(0, "127.0.0.1", () => front.listen(0, "127.0.0.1", () =>
  process.stdout.write(JSON.stringify({ front: front.address().port, backend: backend.address().port }) + "\n")));
const lifetime = setTimeout(() => process.exit(2), 30_000);
process.stdin.resume();
process.stdin.on("end", () => {
  clearTimeout(lifetime);
  front.closeAllConnections();
  backend.closeAllConnections();
  front.close(() => backend.close(() => process.exit(0)));
});
