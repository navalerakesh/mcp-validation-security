const { createRequire } = require('node:module');
const { join } = require('node:path');

async function main() {
  const packageRoot = process.env.MCPVAL_FIXTURE_PACKAGE_ROOT;
  const supplied = JSON.parse(process.env.MCPVAL_FIXTURE_CREDENTIALS ?? '{}');
  delete process.env.MCPVAL_FIXTURE_CREDENTIALS;
  const names = ['actorA', 'actorB', 'readOnlyA', 'observer'];
  if (!packageRoot || names.some(name => !/^[a-f0-9]{64}$/.test(supplied[name] ?? '')) || new Set(Object.values(supplied)).size !== 4)
    throw new Error('Invalid fixture admission');
  const load = createRequire(join(packageRoot, 'package.json'));
  const { McpServer } = load('@modelcontextprotocol/sdk/server/mcp.js');
  const { StreamableHTTPServerTransport } = load('@modelcontextprotocol/sdk/server/streamableHttp.js');
  const { createMcpExpressApp } = load('@modelcontextprotocol/sdk/server/express.js');
  const { requireBearerAuth } = load('@modelcontextprotocol/sdk/server/auth/middleware/bearerAuth.js');
  const { InvalidTokenError } = load('@modelcontextprotocol/sdk/server/auth/errors.js');
  const z = load('zod/v4');
  const allowSpoof = process.env.MCPVAL_FIXTURE_ALLOW_SPOOF === 'true';
  const lifetimeMs = Number(process.env.MCPVAL_FIXTURE_LIFETIME_MS ?? 60000);
  if (!Number.isInteger(lifetimeMs) || lifetimeMs < 100 || lifetimeMs > 60000) throw new Error('Invalid fixture lifetime');
  const expiresAt = Math.floor(Date.now() / 1000) + 120;
  const grants = new Map([
    [supplied.actorA, { actor: 'actor-a', scopes: ['write'], observer: false }],
    [supplied.actorB, { actor: 'actor-b', scopes: ['write'], observer: false }],
    [supplied.readOnlyA, { actor: 'actor-a', scopes: ['read'], observer: false }],
    [supplied.observer, { actor: 'observer', scopes: ['observe'], observer: true }],
  ]);
  let endpoint;
  let observerEndpoint;
  let value = 0;
  let attempts = 0;
  const ledger = [];
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
    const verifier = {
      verifyAccessToken: async token => {
        const grant = grants.get(token);
        if (!grant) throw new InvalidTokenError('Unrecognized fixture credential');
        return { token, clientId: 'owned-test-client', scopes: grant.scopes, expiresAt,
          resource: grant.observer ? observerEndpoint : endpoint, extra: { actor: grant.actor } };
      },
    };
    app.get('/observer', requireBearerAuth({ verifier, requiredScopes: ['observe'], expectedResource: observerEndpoint }), (request, response) => {
      if (process.env.MCPVAL_FIXTURE_OBSERVER_UNAVAILABLE === 'true') return response.status(503).end();
      response.json({ value: value + (process.env.MCPVAL_FIXTURE_COUNTER_DRIFT === 'true' && ledger.length === 4 ? 1 : 0), entries: ledger });
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
        if (ledger.length >= 8) throw new Error('Fixture operation budget exceeded');
        const actor = extra.authInfo?.extra?.actor;
        const authenticated = actor === 'actor-a' || actor === 'actor-b';
        const allowed = authenticated && actor === 'actor-a' && extra.authInfo.scopes.includes('write') && (allowSpoof || args.claimedActor === actor);
        const before = value;
        if (allowed) value++;
        ledger.push({ requestId: String(extra.requestId), actor, object: args.object, authenticated, allowed, before, after: value });
        return { isError: !allowed, content: [{ type: 'text', text: allowed ? 'Accepted' : 'Denied' }] };
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
    grants.clear();
  })();
  process.stdin.resume();
  process.stdin.on('end', () => { stop().catch(() => {}); });
  setTimeout(() => { stop().catch(() => {}); }, lifetimeMs).unref();
}

main().catch(() => { process.stderr.write('Owned identity fixture failed.\n'); process.exitCode = 1; });