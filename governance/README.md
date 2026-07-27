# BIT Platform Governance

This folder contains the living governance documents for the BIT platform. All agents, developers, and automated tools must consult these documents before taking any action on the project.

---

## 🚀 Agent Quick Start

**If you're an AI agent, start here:** `architecture/agent-quickstart.md`

That single page gives you the minimum context needed to not hallucinate. Then consult specific documents below as needed.

---

## Folder Structure

```
governance/
├── README.md                          # This file
├── rules/                             # Mandatory governance rules
│   ├── agent-workflow.md              # Top-level agent workflow rules (7 rules)
│   ├── no-mock-code.md                # NON-NEGOTIABLE: never use mock code
│   ├── verification.md                # Verify before acting — no assumptions
│   ├── testing.md                     # Always add unit tests
│   ├── prerequisites.md              # Feature.gherkin, NFRs, plan required
│   ├── file-ownership.md             # "If you change X, you MUST touch Y"
│   ├── hallucination-prevention.md   # 10 rules to eliminate AI hallucinations
│   └── no-assumptions-no-temp-fixes.md  # Never assume — if unsure, ask. No temp fixes.
├── architecture/                      # System architecture documentation
│   ├── agent-quickstart.md           # 🚀 Minimum context for any agent
│   ├── bit-platform-architecture.md  # Comprehensive architecture reference
│   └── source-of-truth.md            # Which file owns each domain concept
├── design/                            # Subsystem design documentation
│   └── bit-platform-design.md        # Detailed subsystem designs
├── contracts/                         # Exact signatures — no guessing
│   ├── api-contract.md               # Every endpoint with exact method/path/body/response
│   ├── db-schema.md                  # Every table with exact columns and types
│   └── component-contracts.md        # Every React component with exact props
├── plans/                             # Implementation plans (one per feature)
├── features/                          # Gherkin feature files (one per feature)
├── nfrs/                              # Non-functional requirements (one per feature)
└── templates/                         # Templates for plans, features, NFRs
```

---

## How to Use

### For Agents (Copilot, Claude, etc.)

1. **START HERE:** `architecture/agent-quickstart.md` — minimum context, 2-minute read
2. **Follow all rules** in `rules/` — especially `hallucination-prevention.md`
3. **Before ANY code change**, verify prerequisites: `features/<name>.gherkin`, `nfrs/<name>.md`, `plans/<name>.md`
4. **When you need exact information:**
   - Endpoints → `contracts/api-contract.md`
   - Database → `contracts/db-schema.md`
   - Component props → `contracts/component-contracts.md`
   - Which file owns what → `architecture/source-of-truth.md`
   - What files to touch → `rules/file-ownership.md`
5. **If it's not in these documents, it doesn't exist. Verify, don't assume.**

### For Developers

1. Create a feature file in `features/` before starting work
2. Document NFRs in `nfrs/`
3. Write an implementation plan in `plans/`
4. Reference architecture and design docs in PRs
5. Update contracts when you add/change endpoints, DB schema, or component props

---

## Rule Summary

| # | Rule | File | Severity |
|---|---|---|---|
| H1-H10 | Hallucination prevention | `rules/hallucination-prevention.md` | MANDATORY |
| 1 | No mock code — ever | `rules/no-mock-code.md` | NON-NEGOTIABLE |
| 2 | Verify before acting | `rules/verification.md` | MANDATORY |
| 3 | Always add unit tests | `rules/testing.md` | MANDATORY |
| 4 | Prerequisites required | `rules/prerequisites.md` | MANDATORY |
| 5 | Follow architecture patterns | `rules/agent-workflow.md` | MANDATORY |
| 6 | Cross-stack completeness | `rules/agent-workflow.md` | MANDATORY |
| 7 | File ownership traceability | `rules/file-ownership.md` | MANDATORY |
| 8 | Contract freshness validation | `rules/agent-workflow.md` (Rule 8) | MANDATORY |
| 9 | Commit & push after every feature | `rules/commit-discipline.md` | MANDATORY |
| 10 | No assumptions, no temp fixes | `rules/no-assumptions-no-temp-fixes.md` | MANDATORY |
| M1 | Contract maintenance protocol | `rules/contract-maintenance.md` | MANDATORY |

---

## 🔧 Keeping Contracts Current

**Stale contracts are worse than no contracts.** The validation script catches staleness automatically:

```powershell
# Run from project root — exit 0 = all fresh, exit 1 = stale contracts found
governance/scripts/validate-contracts.ps1

# Quiet mode (CI-friendly — only prints problems)
governance/scripts/validate-contracts.ps1 -Quiet

# Show exactly which source files caused each stale contract
governance/scripts/validate-contracts.ps1 -FixHint
```

### When to run it
- **Before committing** any change to controllers, models, or component props
- **Before marking a PR** as "ready for review"
- **In CI/CD** on every PR that touches tracked source files

### What it checks
| Contract | Checked Against |
|---|---|
| `api-contract.md` | All `Controllers/*.cs` files |
| `db-schema.md` | `Models.cs` + latest migration |
| `component-contracts.md` | All `components/*.tsx` files |
| `bit-platform-architecture.md` | `Program.cs`, `Models.cs`, `App.tsx`, `apiClient.ts`, `main.py` |
| `bit-platform-design.md` | `Program.cs`, `Models.cs`, `ContentService.cs` |
| `source-of-truth.md` | Controller directory listing |

See `rules/contract-maintenance.md` for the full protocol including when to update each contract and how.

These documents are designed so that an agent never needs to guess:

| Question | Answer | Document |
|---|---|---|
| "Does endpoint X exist?" | If not in api-contract.md → NO | `contracts/api-contract.md` |
| "Does table Y have column Z?" | If not in db-schema.md → NO | `contracts/db-schema.md` |
| "Does component A accept prop B?" | If not in component-contracts.md → Verify in source | `contracts/component-contracts.md` |
| "Which file defines concept C?" | Look up in source-of-truth.md | `architecture/source-of-truth.md` |
| "What files do I need to touch?" | Look up in file-ownership.md | `rules/file-ownership.md` |

---

**Last Updated:** 2026-07-23
**Maintained by:** BIT Platform Engineering
