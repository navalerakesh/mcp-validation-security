---
name: report-quality-validation
description: "Use when changing MCPVal report rendering, HTML layout, print/PDF, evidence projections, report fixtures or artifact parity. Verify generated CLI artifacts before accepting report quality."
---

# Validate MCPVal reports

Read the [quality and layout contract](../../../docs/Design/ReportQualityStandard.md), [shipped behavior](../../../docs/Reporting.md) and [repository instructions](../../copilot-instructions.md). Follow the existing factory/composer/theme pipeline; never reinterpret validation in a renderer.

Run the ordinary [contributor report checks](../../../CONTRIBUTING.md#report-changes). The fixture-owned manifest covers all assessment profiles in minimal/full detail, including failed and missing-evidence controls. Check light/dark, in-place investigation, parity, keyboard, offline, no-JavaScript and A4/Letter export. No Copilot tooling is required.

Capture before/after artifacts using identical inputs and the appropriate renderer builds. Review focused tests and snapshot changes before approving a baseline. Check every affected machine/human output within its documented role.

Inspect actual opening screens and PDF pages; follow an action -> domain -> claim -> provenance path. Examine dense, interrupted and missing-data cases, changed page transitions and the final numbered footer. Automated success is not human visual acceptance.

Attach commands/results, source identity, input hashes, browser/version, viewport/export settings, page totals, artifact locations and explicit Pass/Fail/Unverified findings. Keep generated evidence outside tracked paths. Require human review for changed hierarchy or pagination; do not imply tagged PDF, native assistive-technology or release qualification without separate evidence.
