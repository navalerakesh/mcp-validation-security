const { createRequire } = require('node:module');
const { join } = require('node:path');
const { createOwnedIdentityPolicy } = require('./owned-identity-policy.cjs');

async function main() {
  const packageRoot = process.env.MCPVAL_FIXTURE_PACKAGE_ROOT;
  const supplied = JSON.parse(process.env.MCPVAL_FIXTURE_CREDENTIALS ?? '{}');
  delete process.env.MCPVAL_FIXTURE_CREDENTIALS;
  if (!packageRoot) throw new Error('Invalid fixture admission');
  const identity = createOwnedIdentityPolicy(supplied, {
    allowSpoof: process.env.MCPVAL_FIXTURE_ALLOW_SPOOF === 'true',
    observerUnavailable: process.env.MCPVAL_FIXTURE_OBSERVER_UNAVAILABLE === 'true',
    counterDrift: process.env.MCPVAL_FIXTURE_COUNTER_DRIFT === 'true',
  });
  const load = createRequire(join(packageRoot, 'package.json'));
  const { McpServer } = load('@modelcontextprotocol/sdk/server/mcp.js');
  const { StreamableHTTPServerTransport } = load('@modelcontextprotocol/sdk/server/streamableHttp.js');
  const { createMcpExpressApp } = load('@modelcontextprotocol/sdk/server/express.js');
  const { requireBearerAuth } = load('@modelcontextprotocol/sdk/server/auth/middleware/bearerAuth.js');
  const { InvalidTokenError } = load('@modelcontextprotocol/sdk/server/auth/errors.js');
  const z = load('zod/v4');
  const lifetimeMs = Number(process.env.MCPVAL_FIXTURE_LIFETIME_MS ?? 60000);
  if (!Number.isInteger(lifetimeMs) || lifetimeMs < 100 || lifetimeMs > 60000) throw new Error('Invalid fixture lifetime');
  let endpoint;
  let observerEndpoint;
  let attempts = 0;
  const active = new Set();
  const app = createMcpExpressApp({ host: '127.0.0.1' });
  app.use((request, response, next) => {
    if (++attempts > 64) return response.status(429).end();
    next();
  });
  const listener = app.listen(0, '127.0.0.1', () => {
    const port = listener.address().port;
    endpoint = new URL(`http://127.0.0.1:${port}/mcp`);
    observerEndpoint = new URL(`http://127.0.0.1:${port}/observer`);
    const verifier = identity.verifier(endpoint, observerEndpoint, () => new InvalidTokenError('Unrecognized fixture credential'));
    app.get('/observer', requireBearerAuth({ verifier, requiredScopes: ['observe'], expectedResource: observerEndpoint }), (request, response) => {
      const observation = identity.observe();
      if (observation === null) return response.status(503).end();
      response.json(observation);
    });
    app.post('/mcp', requireBearerAuth({ verifier, expectedResource: endpoint }), async (request, response) => {
      const server = new McpServer({ name: 'owned-identity-fixture', version: '1.0.0' });
      const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true });
      active.add(server);
      server.registerTool('owned_update', {
        description: 'Update the single owned fixture object under an explicit actor and scope policy.',
        inputSchema: { object: z.literal('object-a'), claimedActor: z.enum(['actor-a', 'actor-b']) },
        annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
      }, async (args, extra) => {
        return identity.call(args, extra.authInfo, extra.requestId);
      });
      response.on('close', () => { server.close().catch(() => {}); active.delete(server); });
      try {
        await server.connect(transport);
        await transport.handleRequest(request, response, request.body);
      } catch {
        if (!response.headersSent) response.status(500).end();
      }
    });
    app.get('/mcp', (request, response) => response.status(405).end());
    app.delete('/mcp', (request, response) => response.status(405).end());
    app.use((error, request, response, next) => { if (!response.headersSent) response.status(400).end(); });
    process.stdout.write(JSON.stringify({ port, pid: process.pid }) + '\n');
  });
  let stopping;
  const stop = () => stopping ??= (async () => {
    process.stdin.destroy();
    listener.close();
    listener.closeAllConnections();
    await Promise.allSettled([...active].map(server => server.close()));
    identity.close();
  })();
  process.stdin.resume();
  process.stdin.on('end', () => { stop().catch(() => {}); });
  setTimeout(() => { stop().catch(() => {}); }, lifetimeMs).unref();
}

main().catch(() => { process.stderr.write('Owned identity fixture failed.\n'); process.exitCode = 1; });