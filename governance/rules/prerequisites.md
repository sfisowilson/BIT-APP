# Rule: Work Prerequisites

**Status:** MANDATORY
**Applies to:** All feature work, bug fixes, and enhancements

---

## The Rule

**Never start working on something that does not have a `feature.gherkin`, NFRs, and a plan.**

---

## Required Artifacts

### 1. Feature File (`governance/features/<feature-name>.gherkin`)

Must contain:
- **Feature name** and brief description
- **User stories** as Gherkin scenarios:
  ```gherkin
  Feature: Campaign Creation
    As an advertiser
    I want to create a new campaign
    So that I can manage my brand placements

    Scenario: Create a campaign with valid data
      Given I am logged in as an advertiser
      When I submit a campaign with name, schedule, and budget
      Then the campaign is created with status "Draft"
      And I see the campaign in my dashboard

    Scenario: Create a campaign with missing name
      Given I am logged in as an advertiser
      When I submit a campaign without a name
      Then I receive a validation error
      And the campaign is not created
  ```
- **Acceptance criteria** (checklist)

### 2. NFRs (`governance/nfrs/<feature-name>.md`)

Must cover:
- **Performance:** Expected response times, throughput, concurrency
- **Security:** Auth requirements, data sensitivity, input validation
- **Scalability:** Expected data volumes, growth projections
- **Error Handling:** Expected error scenarios and behavior
- **Observability:** Logging requirements, metrics to emit

### 3. Implementation Plan (`governance/plans/<feature-name>.md`)

Must contain:
- **Files to create/modify** — exhaustive list with absolute paths
- **Step-by-step order** — dependencies between steps
- **Database changes** — new entities, fields, migrations
- **API changes** — new/updated endpoints, DTOs
- **Frontend changes** — new/updated components, routes, types
- **Testing strategy** — what tests will be written
- **Rollback plan** — how to undo if needed

---

## Gate Check Process

Before writing ANY code:

```
1. Does governance/features/<name>.gherkin exist?     → If no, STOP. Create it.
2. Does governance/nfrs/<name>.md exist?               → If no, STOP. Create it.
3. Does governance/plans/<name>.md exist?              → If no, STOP. Create it.
4. Have I verified the current state?                  → If no, STOP. Read the code.
5. Have I traced the full call chain?                  → If no, STOP. Map it out.
6. Do I know what tests I'll write?                    → If no, STOP. Plan them.
```

Only when ALL gates pass → proceed with implementation.

---

## Exceptions

**Emergency hotfixes** (P0 production incidents) may bypass prerequisites BUT:
1. Must be documented with reason in the commit message
2. Feature file, NFRs, and plan must be created retroactively within 24 hours
3. Post-hoc review required to verify the fix matches the retroactive plan

---

## Templates

Templates for each artifact are available in `governance/templates/` (create as needed).
