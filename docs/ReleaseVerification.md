# Release Verification

Every published artifact must identify the same exact version from `Directory.Build.props` and originate from the same tested commit.

## Standalone binaries

1. Download the binary, `SHA256SUMS.txt`, its `.sig`, `.pem`, and `.sigstore.json` files from the GitHub release.
2. Verify bytes:

```bash
sha256sum --check SHA256SUMS.txt
```

1. Verify the Sigstore bundle and certificate identity:

```bash
cosign verify-blob mcpval-linux-x64 \
  --bundle mcpval-linux-x64.sigstore.json \
  --certificate-identity-regexp 'github.com/navalerakesh/mcp-validation-security' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

1. Verify GitHub build provenance:

```bash
gh attestation verify mcpval-linux-x64 --repo navalerakesh/mcp-validation-security
./mcpval-linux-x64 --version
```

## NuGet and npm

NuGet and npm packages, channel-specific checksum files, SPDX SBOMs, Sigstore bundles, signatures, and certificates are durable GitHub Release assets. GitHub attestations bind each package archive to the workflow commit. npm publication additionally uses registry provenance.

After downloading the channel artifact bundle:

```bash
sha256sum --check mcpval.nuget.SHA256SUMS.txt
# or: sha256sum --check mcpval.npm.SHA256SUMS.txt
cosign verify-blob PACKAGE \
  --bundle PACKAGE.sigstore.json \
  --certificate-identity-regexp 'github.com/navalerakesh/mcp-validation-security' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify PACKAGE --repo navalerakesh/mcp-validation-security
```

For npm, also inspect registry provenance:

```bash
npm audit signatures
npm view mcpval-localmcp@VERSION dist.shasum dist.integrity
```

Install packages into an empty location before use:

```bash
dotnet tool install McpVal --tool-path ./verified-tool --version VERSION
./verified-tool/mcpval --version

npm install mcpval-localmcp@VERSION
npx mcpval-localmcp
```

## Container

GHCR images include OCI SBOM and provenance attestations. Pin deployments by digest:

```bash
docker pull ghcr.io/navalerakesh/mcp-validation-security:VERSION
docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/navalerakesh/mcp-validation-security:VERSION
docker run --rm ghcr.io/navalerakesh/mcp-validation-security:VERSION --version

digest=$(docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/navalerakesh/mcp-validation-security:VERSION)
cosign verify "$digest" \
  --certificate-identity-regexp 'github.com/navalerakesh/mcp-validation-security' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
cosign verify-attestation "$digest" --type spdxjson \
  --certificate-identity-regexp 'github.com/navalerakesh/mcp-validation-security' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify "oci://$digest" --repo navalerakesh/mcp-validation-security
```

Stable releases update `latest`, major, and minor Action tags. Prerelease versions publish to the `preview` npm/container channel and do not move stable Action tags.

`DISTRIBUTION-MANIFEST.json` binds the exact version and source commit to the NuGet/npm package hashes, pushed OCI digest, and digest of the complete release-asset checksum file. Verify and recompute it before comparing channel values:

```bash
cosign verify-blob DISTRIBUTION-MANIFEST.json \
  --bundle DISTRIBUTION-MANIFEST.json.sigstore.json \
  --certificate-identity-regexp 'github.com/navalerakesh/mcp-validation-security' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com

test "$(sha256sum SHA256SUMS.txt | awk '{print $1}')" = \
  "$(jq -r '.channels.releaseAssets.checksumsSha256' DISTRIBUTION-MANIFEST.json)"
test "$(sha256sum McpVal.VERSION.nupkg | awk '{print $1}')" = \
  "$(jq -r '.channels.nuget.sha256' DISTRIBUTION-MANIFEST.json)"
test "$(sha256sum mcpval-localmcp-VERSION.tgz | awk '{print $1}')" = \
  "$(jq -r '.channels.npm.sha256' DISTRIBUTION-MANIFEST.json)"
test "$(jq -r '.version' DISTRIBUTION-MANIFEST.json)" = "VERSION"
test "$(jq -r '.sourceRevision' DISTRIBUTION-MANIFEST.json)" = "$(git rev-parse RELEASE_TAG^{commit})"
test "${digest#*@}" = "$(jq -r '.channels.container.digest' DISTRIBUTION-MANIFEST.json)"
```

Publication retries are byte-aware: existing npm archives must match the staged SHA-1, and existing NuGet payloads must match after excluding NuGet.org's repository signature. A mismatch aborts rather than overwriting. Container tags are re-pushed only from the staged, checksummed image archive. To roll back, pin a previously verified immutable package version, standalone checksum, or OCI digest; stable mutable aliases are not rollback evidence.
