# Rule: Verify Before Acting

**Status:** MANDATORY
**Applies to:** All planning and implementation

---

## The Rule

**Every plan must be based on verified facts, not assumptions. Verify before you act.**

---

## Verification Checklist

Before proposing any change, answer these questions with evidence:

### 1. Does this endpoint/component/file already exist?
- [ ] Checked the codebase (grep, file search, directory listing)
- [ ] Cited: `File: <path>, Line: <N>`

### 2. What is the current database schema?
- [ ] Read the relevant entity from `dotnet-api/Models/Models.cs`
- [ ] Checked existing migrations in `dotnet-api/Migrations/`
- [ ] Cited: `Entity: <Name>, Fields: <list>`

### 3. What is the current API surface?
- [ ] Read the relevant controller in `dotnet-api/Controllers/`
- [ ] Checked existing DTOs in `dotnet-api/DTOs/`
- [ ] Cited: `Controller: <Name>, Endpoints: <list>`

### 4. What is the current frontend state?
- [ ] Read the relevant component in `src/components/`
- [ ] Checked existing types in `src/types.ts`
- [ ] Checked existing API client functions in `src/apiClient.ts`
- [ ] Cited: `Component: <Name>, Props: <list>`

### 5. Does this change affect the pipeline?
- [ ] Checked pipeline stage transitions in `ContentService.PipelineStages`
- [ ] Verified that new states/transitions are valid

### 6. Does this change require a migration?
- [ ] If modifying models, confirmed migration needed
- [ ] If adding entities, confirmed relationships and foreign keys

---

## Assumption Detection

If you find yourself thinking or writing any of these phrases, **STOP and verify**:

| Assumption Phrase | Action |
|---|---|
| "I assume..." | Read the code instead |
| "There's probably..." | Search the codebase |
| "It should be..." | Verify, don't assume |
| "I think the database has..." | Read the models file |
| "I believe the endpoint..." | Read the controller |
| "It likely works like..." | Trace the call chain |

---

## Verification Tools

1. **`grep_search`** — Search for symbols, patterns, endpoints across the codebase
2. **`file_search`** — Find files by glob pattern
3. **`list_dir`** — List directory contents
4. **`read_file`** — Read specific files with line ranges
5. **`vscode_listCodeUsages`** — Find all references to a symbol

**Default behavior:** When uncertain, use these tools before speaking or coding.
