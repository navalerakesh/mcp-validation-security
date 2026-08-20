# Single-Owner Open-Source Governance

Owner: Rakesh Navale (`@navalerakesh`)

Applies to: MCP Validator source, schemas, scoring, transports, authentication, packages, containers, GitHub Action, and releases.

## Operating Model

MCP Validator is authored and maintained by one repository owner. Enterprise readiness is based on transparent controls and reproducible evidence, not maintainer count.

The owner is identified by `.github/CODEOWNERS`. Changes to authentication, transports, scoring, schemas, Fleet governance, and release automation require:

1. A reviewable source diff linked to the exact commit.
2. All required automated CI, security, compatibility, mutation, and distribution gates.
3. A current ECDSA owner approval bound to the change/build digest when Fleet readiness evidence is produced.
4. Signed release artifacts, immutable full-version tags, provenance, SBOMs, and checksums.
5. Public changelog and migration notes for machine-contract changes.

Self-review is not represented as independent review. Threat models, penetration tests, and calibration reviews remain external evidence where required.

## CI/CD Controls

- Exact SDK, Node, dependency locks, package version, and changelog entry are repository-controlled.
- CodeQL and dependency review fail closed when unavailable.
- Immutable release tags are reserved before any registry write.
- NuGet/npm final registry bytes are compared with staged payloads before channel promotion.
- Mutable npm/container/Action aliases use monotonic SemVer guards and cannot be rolled backward by stale reruns.
- Standalone archives are deterministic and executed after artifact download on supported operating systems.
- AMD64 and ARM64 container images are independently built and smoked before the signed manifest is promoted.
- Release retries and signature backfill verify tag, source revision, version, manifest, checksums, SBOM, and signing identity.

GitHub branch, tag, and environment protections are hosting configuration and must enforce the required automated gates for production publication.
The exact required settings and pre-publication evidence are defined in `docs/ReleaseHostConfiguration.md`. Inability to verify any listed setting blocks publication.

## Signing Key Rotation

1. Create a replacement P-256 key in protected hardware or a managed signing service.
2. Add the new public-key identifier to the trusted owner-key set through a normal reviewed change.
3. Produce a transition record signed by the current and replacement keys when the current key remains available.
4. Validate signatures from the replacement key in deterministic tests.
5. Revoke/remove the previous key only after published verification instructions and rollback evidence exist.
6. Never commit private keys, recovery material, tokens, or signing credentials.

## Compromise Recovery

If an owner key or publishing credential may be compromised:

1. Stop publication and disable affected GitHub environments/registry credentials.
2. Revoke the credential or trusted key and rotate it before resuming publication.
3. Audit immutable tags, registry versions, package digests, provenance, and mutable aliases.
4. Restore aliases only to independently verified immutable versions.
5. Publish a security advisory describing affected versions and verification steps.
6. Run the complete repository, package, signature, retry, and distribution gates from a clean checkout.

Immutable registry versions are never overwritten. A corrected release uses a new patch version.

## Knowledge And Continuity

Build, test, release, schema, calibration, incident, and verification procedures must remain in the repository. Enterprise users must be able to reproduce deterministic results and verify releases without private owner knowledge.

If ownership changes, transfer requires:

- updated CODEOWNERS and security contact information;
- rotation of GitHub, NuGet, npm, GHCR, signing, and environment credentials;
- a signed ownership-transition record;
- verification that the new owner can execute all required gates from a clean environment.
