function createOwnedIdentityPolicy(supplied, options = {}) {
  const names = ['actorA', 'actorB', 'readOnlyA', 'observer'];
  if (names.some(name => !/^[a-f0-9]{64}$/.test(supplied[name] ?? '')) || new Set(Object.values(supplied)).size !== 4)
    throw new Error('Invalid fixture admission');
  const expiresAt = Math.floor(Date.now() / 1000) + 120;
  const grants = new Map([
    [supplied.actorA, { actor: 'actor-a', scopes: ['write'], observer: false }],
    [supplied.actorB, { actor: 'actor-b', scopes: ['write'], observer: false }],
    [supplied.readOnlyA, { actor: 'actor-a', scopes: ['read'], observer: false }],
    [supplied.observer, { actor: 'observer', scopes: ['observe'], observer: true }],
  ]);
  let value = 0;
  const ledger = [];
  return {
    ownsCredential: token => grants.has(token),
    verifier(endpoint, observerEndpoint, invalidToken) {
      return { verifyAccessToken: async token => {
        const grant = grants.get(token);
        if (!grant) throw invalidToken();
        return { token, clientId: 'owned-test-client', scopes: grant.scopes, expiresAt,
          resource: grant.observer ? observerEndpoint : endpoint, extra: { actor: grant.actor } };
      } };
    },
    observe() {
      if (options.observerUnavailable) return null;
      return { value: value + (options.counterDrift && ledger.length === 4 ? 1 : 0), entries: ledger.map(entry => ({ ...entry })) };
    },
    call(args, authInfo, requestId) {
      if (ledger.length >= 8) throw new Error('Fixture operation budget exceeded');
      const actor = authInfo?.extra?.actor;
      const authenticated = actor === 'actor-a' || actor === 'actor-b';
      const allowed = authenticated && actor === 'actor-a' && authInfo.scopes.includes('write') && (options.allowSpoof || args.claimedActor === actor);
      const before = value;
      if (allowed) value++;
      ledger.push({ requestId: String(requestId), actor, object: args.object, authenticated, allowed, before, after: value });
      return { isError: !allowed, content: [{ type: 'text', text: allowed ? 'Accepted' : 'Denied' }] };
    },
    close() { grants.clear(); },
  };
}

module.exports = { createOwnedIdentityPolicy };