# Build And Release Readiness Receipt

```markdown
## Change Classification
- Changed surfaces:
- Conditional matrix rows applied:
- Local environment/tool versions:

## Focused Evidence
| Command | Exit | Passed/Failed/Skipped | Notes |
| --- | ---: | --- | --- |

## Mandatory Local Gates
| Gate | Exit | Result | Notes |
| --- | ---: | --- | --- |
| git diff --check | | | |
| sensitive artifacts | | | |
| distribution contracts | | | |
| release retry simulation | | | |
| repository validation | | | |

## Conditional Gates
| Gate | Exit | Result | Notes |
| --- | ---: | --- | --- |

## Remote-Only Residual Gates
- Required GitHub checks not locally reproducible:
- Environment approvals/registry/signing checks:

## Commit Payload
- Staged paths/stat:
- Locks/generated/schema/snapshot/changelog review:
- Untracked/ignored output review:

## Decision
READY TO COMMIT | NOT READY
```

`READY TO COMMIT` requires every applicable local gate to be observed and passing. Merge/release readiness additionally requires protected GitHub checks and environment approvals.
