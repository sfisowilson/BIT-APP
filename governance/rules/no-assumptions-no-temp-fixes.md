# Rule: No Assumptions, No Temp Fixes

**Status:** MANDATORY
**Version:** 1.0 | **Date:** 2026-07-26
**Applies to:** All planning, implementation, and code review

---

## Part 1: Never Assume — If Unsure, Ask

### The Rule

**When code verification is inconclusive, ask the developer or stakeholder. Never guess, fabricate, or assume business logic, intent, or requirements.**

Existing rules (`verification.md`, `hallucination-prevention.md`) require you to verify facts by reading the codebase. This rule adds the next step: **when verification doesn't yield a clear answer, escalate to a human.**

### When to Ask vs. When to Verify

| Situation | Action |
|---|---|
| The endpoint/field/component exists in the codebase | **Verify** — read the file, check contracts |
| The code is ambiguous or undocumented | **Ask** — the developer knows intent |
| Business logic isn't captured in tests or docs | **Ask** — don't guess at rules |
| A requirement is missing from feature files/NFRs | **Ask** — don't fabricate requirements |
| You're unsure which of two valid approaches to take | **Ask** — let the human decide |
| The code appears to have a bug but you're not sure | **Ask** — "I noticed X, was this intentional?" |

### Escalation Protocol

When you need to ask:
1. **Be specific** — cite the exact file, line, and what's unclear
2. **State what you've already verified** — show you did your homework
3. **Propose options if possible** — "It could be A or B; which is correct?"
4. **Block, don't guess** — do not proceed with assumptions

### Anti-Patterns (Violations)

| Violation | Why it's wrong |
|---|---|
| "I'll assume the business rule is X" | You don't own the business logic |
| "It probably means Y, I'll just code it that way" | Guessing wastes time and creates bugs |
| "There's no spec, so I'll make reasonable choices" | Ask for a spec; don't invent one |
| "I'm not sure but I'll submit the PR anyway" | Ambiguity should be resolved before code |
| Silently proceeding past unclear instructions | Stop and ask for clarification |

### Default Response When Unsure

**"I'm not certain about [specific thing]. I've verified [what you checked]. Could you clarify [specific question]?"**

Never say "I think" or "probably" when you could ask instead.

---

## Part 2: Never Hide Errors with Temp Fixes

### The Rule

**Never suppress, swallow, or paper over errors with temporary fixes, empty catch blocks, hardcoded fallbacks, or `// FIXME` placeholders. Implement proper error handling, propagation, and logging.**

A "temp fix" that hides an error is a future production incident. Fix the root cause or surface the error properly.

### What is Forbidden

1. **Empty catch blocks** — `catch { }` or `catch (Exception) { }` with no handling
2. **Swallowed exceptions** — `catch { return null; }` or `catch { return false; }`
3. **Silent fallback values** — `try { return realValue; } catch { return defaultValue; }` without logging
4. **Comment placeholders for errors** — `// FIXME: this crashes sometimes` left in production code
5. **`try { ... } catch { // ignore }`** — any pattern that discards the exception
6. **Hardcoded values to bypass errors** — e.g., forcing `success = true` to get past a failing check
7. **Commented-out code as "safety net"** — keeping broken code paths "just in case"
8. **`Debug.Assert` or `Console.WriteLine` as sole error handling** — not a substitute for proper logging and user feedback

### What is Required

- **Propagate errors** — let exceptions bubble up to a global handler; don't swallow them mid-stack
- **Log all errors** — use the established logging framework (Serilog in .NET, console.error in frontend, logging in Python) with sufficient context
- **User-visible error states** — show meaningful error UI in the frontend, return proper HTTP error responses from the API
- **Fail fast and loud** — a visible error is better than a silent wrong result
- **Fix the root cause** — if something fails intermittently, investigate and fix the underlying issue; don't wrap it in a retry loop or try/catch and move on

### Acceptable Patterns

```csharp
// ✅ GOOD: Log and rethrow
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process content {ContentId}", contentId);
    throw;  // Preserves stack trace
}

// ✅ GOOD: Handle specific exception, log, return error result
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Database update failed for surface {SurfaceId}", surfaceId);
    return Result.Failure("Database error occurred. Please try again.");
}

// ✅ GOOD: Global exception middleware (catches unhandled, logs, returns 500)
```

```csharp
// ❌ FORBIDDEN: Empty catch
catch { }

// ❌ FORBIDDEN: Swallowed with silent null
catch (Exception) { return null; }

// ❌ FORBIDDEN: Swallowed with no logging
catch (Exception ex)
{
    return Result.Failure("Something went wrong");  // No logging, no context
}

// ❌ FORBIDDEN: Temp fix with comment
result = CallService();
if (result == null)
    result = new Result { Success = true };  // FIXME: service sometimes returns null
```

### Detection Patterns (Code Review)

Look for these during code review:

| Pattern | grep/regex |
|---|---|
| Empty catch block | `catch\s*\{\s*\}` |
| Catch with return null | `catch.*\{\s*return\s+null` |
| Catch with return false | `catch.*\{\s*return\s+false` |
| Silent catch with no log | `catch\s*\{[^}]*\}` (no `Log` or `logger` inside) |
| FIXME/TODO in production code | `FIXME\|TODO.*catch\|TODO.*error\|TODO.*crash` |
| Console.WriteLine as error handler | `Console\.WriteLine.*exception\|Console\.WriteLine.*error` |

---

## Rationale

### Why "If Unsure, Ask"
- Guessing at business logic produces features that don't match requirements
- Assumptions compound — one bad guess leads to more guesses downstream
- Asking is faster than rework — a 30-second clarification saves hours of wrong implementation
- Code can verify facts (what exists) but cannot verify intent (what should exist)

### Why "No Temp Fixes"
- Temp fixes become permanent — they're never revisited once the pressure is off
- Silent error suppression hides cascading failures
- Empty catch blocks make debugging nearly impossible
- Every swallowed exception is a mystery bug waiting to surface in production
- Proper error handling is not optional — it's part of the feature, not an afterthought

---

## Enforcement

1. **Pre-commit review** — Reviewer must check for temp-fix anti-patterns
2. **Automated grep** — CI can flag empty catch blocks and silent returns (see detection patterns above)
3. **PR checklist** — Add "No swallowed exceptions or temp fixes" to the pre-commit checklist in `commit-discipline.md`
4. **Pairing** — When unsure, ask before coding; don't wait for code review to catch assumptions

---

## Related Rules

- `verification.md` — Verify before acting (code-level)
- `hallucination-prevention.md` — Rules H1-H10 (codebase-specific anti-hallucination)
- `no-mock-code.md` — No stub/fake/placeholder code
- `agent-workflow.md` Rule 1 — Fact-based planning
