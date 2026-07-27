# Contract Maintenance Rule

**Version:** 1.0 | **Date:** 2026-07-23

---

## The Rule

**Every governance contract must be kept in sync with the codebase. A stale contract is worse than no contract — it causes agents to act on false information.**

---

## When to Update Contracts

### Mandatory Update Triggers

| You changed... | You MUST update... |
|---|---|
| Any controller (new/edited endpoint) | `governance/contracts/api-contract.md` |
| `Models/Models.cs` (new/edited entity or field) | `governance/contracts/db-schema.md` |
| Any component props interface | `governance/contracts/component-contracts.md` |
| `ContentService.PipelineStages` | `governance/contracts/api-contract.md` (pipeline section) |
| New project dependency or technology | `governance/architecture/bit-platform-architecture.md` |
| New subsystem or changed subsystem design | `governance/design/bit-platform-design.md` |
| New file that owns a domain concept | `governance/architecture/source-of-truth.md` |
| Changed cross-stack dependencies | `governance/rules/file-ownership.md` |

---

## Validation

### Run the staleness check

```powershell
# From project root
& governance/scripts/validate-contracts.ps1
```

This script compares last-modified timestamps of source files vs. contract files and reports any contract that is older than the source it documents.

### What it checks

| Contract | Checked Against |
|---|---|
| `api-contract.md` | All `Controllers/*.cs` files |
| `db-schema.md` | `Models/Models.cs` + latest migration |
| `component-contracts.md` | All `src/components/*.tsx` files |
| `bit-platform-architecture.md` | `Program.cs`, `Models.cs`, `App.tsx` |
| `bit-platform-design.md` | `Program.cs`, `Models.cs` |
| `source-of-truth.md` | Directory listings |

### Exit codes

- `0` — All contracts up to date
- `1` — One or more contracts are stale (older than source files)
- `2` — Script error (missing files, etc.)

---

## Enforcement

### Pre-commit / Pre-push (recommended)

Add to your Git workflow:

```powershell
# .git/hooks/pre-commit or pre-push
& governance/scripts/validate-contracts.ps1
if ($LASTEXITCODE -ne 0) {
    Write-Host "⚠️  Governance contracts are stale. Run 'governance/scripts/validate-contracts.ps1' for details."
    Write-Host "    Update the listed contracts before committing."
    exit 1
}
```

### CI/CD (recommended)

Add a GitHub Actions step that runs the validation on every PR. See suggested workflow below.

### Manual Gate

Before marking any PR as "ready for review," the author must:
1. Run `validate-contracts.ps1`
2. Fix any stale contracts
3. Include "Contracts updated: [list]" in the PR description

---

## How to Update a Contract

### api-contract.md
1. Read the controller file you changed
2. Find the section for that controller in `api-contract.md`
3. Update method, path, auth, request body, response shape
4. If you added a new controller, add a new section following the existing format
5. Update the frontend `apiClient.ts` section if you changed client-side functions

### db-schema.md
1. Read the entity in `Models.cs`
2. Find the table section in `db-schema.md`
3. Add/update columns with exact types, required/optional, default values
4. If you added a new entity, add a new table section following the existing format

### component-contracts.md
1. Read the component's props interface
2. Find the component section in `component-contracts.md`
3. Update the interface signature exactly
4. If it's a new component, add a new section

---

## Exceptions

- **Emergency hotfixes** may defer contract updates for up to 24 hours, but the PR must include a `TODO: update contracts` comment
- **Refactors that don't change the public API** (endpoints, DB schema, component props) don't require contract updates
- **Internal implementation changes** (service logic, repository queries) don't require contract updates unless they change observable behavior

---

## Suggested GitHub Actions Workflow

```yaml
# .github/workflows/validate-contracts.yml
name: Validate Governance Contracts

on:
  pull_request:
    paths:
      - 'dotnet-api/Controllers/**'
      - 'dotnet-api/Models/**'
      - 'dotnet-api/Services/ContentService.cs'
      - 'src/components/**'
      - 'src/types.ts'
      - 'src/apiClient.ts'
      - 'dotnet-api/Program.cs'

jobs:
  validate:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Check contract freshness
        run: |
          & governance/scripts/validate-contracts.ps1
        shell: pwsh
      - name: Fail if stale
        if: failure()
        run: |
          Write-Host "::error::Governance contracts are stale. Update them before merging."
          exit 1
```
