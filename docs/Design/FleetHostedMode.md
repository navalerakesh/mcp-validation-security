# Fleet Hosted Mode

Fleet is a separate product mode in `Mcp.Benchmark.Fleet`. The CLI and validation core do not depend on it. This boundary prevents hosted tenancy, persistence, and worker concerns from weakening local run isolation.

## Implemented foundation

- Immutable tenant-bound jobs with closed states, optimistic versions, bounded attempts, leases, cancellation, retry, dead-letter, and expired-lease recovery.
- Encrypted AES-256-GCM single-node reference persistence with tenant-bound associated data, hashed filenames, bounded snapshots, atomic replacement, idempotency conflict detection, and compare-exchange transition validation.
- Explicit tenant RBAC, fail-closed quotas and cost reservation, legal-hold-aware retention, and keyed tamper-evident audit chaining with key identifiers.
- Fair tenant selection with active-tenant and per-target controls plus exact HTTP/STDIO worker-pool dispatch separation.
- SLO evaluation, signed owner approval rules, and a fail-closed hosted-readiness checklist.
- ECDSA verification of deployment capability evidence, external review evidence, and owner approvals bound to exact build and environment digests.

## Non-claims

The encrypted file store is a single-node reference implementation. It is not a shared horizontal job or artifact store. In-process worker pool contracts do not prove process/container isolation or network-enforced egress. Capability booleans and review metadata are not sufficient deployment attestations.

Fleet must not be exposed as hosted-ready until all of the following are independently evidenced for the exact build and environment:

1. Shared transactional job and encrypted artifact/index stores pass concurrency, tenant isolation, backup, restore, and disaster-recovery exercises.
2. HTTP and STDIO workers are disposable, separately scheduled, resource constrained, and subject to network-enforced default-deny egress.
3. Identity-provider claims, tenant RBAC, quota accounting, retention, deletion, legal hold, and audit export pass integration and abuse tests.
4. Queue backpressure, tenant fairness, per-target rate controls, cost ceilings, SLO alerts, dashboards, and runbooks pass load and failure injection.
5. Sensitive auth, transport, scoring, schema, and release changes require a current cryptographically attributable approval from the declared repository owner.
6. Current external threat-model, penetration-test, and calibration-review reports are signed and bound to the deployed build/environment digests.

Signed evidence verification is implemented, but hosted readiness remains false until a hosted composition atomically enforces the repository contracts and trusted operators supply valid evidence for the exact deployed build and environment.
