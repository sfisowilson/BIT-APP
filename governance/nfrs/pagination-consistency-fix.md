# NFRs: Pagination Consistency Fix

**Feature:** Pagination Consistency Across List Pages
**Date:** 2026-07-31
**Status:** In Progress

---

## Performance

- Default page size stays at 20 (matches existing `usePaginatedData` default and backend `PaginationParams` default) except Content tab's established 12/page, which is unchanged.
- Search inputs debounce is not required — `usePaginatedData.setFilters` already resets to page 1 and only refetches on actual filter change (dedup via `stripUndefined` comparison), matching the existing Content-tab/Telemetry-tab pattern.
- RendersTab's 4 status-breakdown counts (Queued, Processing, Finished, Failed) and AdminConsoleTab's 3 role-breakdown counts (Admin, Editor, Advertiser) are fetched via `pageSize:1` COUNT-only requests in parallel (`Promise.all`) — cheap, sub-millisecond COUNT queries per the existing AttentionController precedent, not full row fetches.
- `AbortController` cancellation (already built into `usePaginatedData`) prevents race conditions when a user pages/searches quickly.

## Security

- `/api/users` remains `[Authorize(Roles = "Admin")]` — pagination adds query filtering, not a permission change.
- No new endpoints exposed beyond the existing pattern (GET with query-string filters, `[Authorize]`).

## Scalability

- Every fix in this pass reuses the existing `PaginatedResult<T>` / `PaginationParams` / `ToPaginatedResultAsync()` / `ApplySort()` infrastructure already proven in Content, Logs, and Alarms — no new pagination mechanism introduced.
- `AssetFilterParams` gains one new optional field (`Unassigned: bool?`) to let the Unassigned Assets list filter server-side (`CampaignId == null`) instead of fetching everything and filtering client-side.
- `IUserService`/`UsersController` gain real pagination for the first time — `GetUsersAsync()` previously returned an unbounded `IEnumerable<User>` with no filter support at all despite `UserFilterParams` already existing (unused) in `PaginatedDtos.cs`.

## Error Handling

- Existing admin-safety business rules (`UserService.UpdateUserAsync`/`DeleteUserAsync` — "cannot demote/suspend/delete the last admin") continue to use `_userRepository.GetAllAsync()` directly (the full, unfiltered set) — untouched by this change, since they were never routed through the paginated `GetUsersAsync(filter)` path in the first place.
- Empty states (`0` results, no campaigns/assets/renders/users) render the same "no results" messaging already used elsewhere, not a raw empty table.

## Explicitly Out of Scope

- `CampaignSelector.tsx` (the top-nav campaign switcher) and `App.tsx`'s global `campaignList`/`assetList`/`renderList` state (used for cross-page lookups, sidebar badges, and the "Assign to Campaign" quick-pick dropdown inside `CampaignsTab.tsx`) are **not** touched. They keep using the existing unpaginated first-page fetch. Converting these to true pagination would require a larger architectural change (remote-search-capable dropdowns) touching many unrelated consumers across `App.tsx`, and was judged out of scope for a pagination-consistency bug fix. Documented as a known follow-up.
- `JobsTab.tsx` (`/api/jobs`) is left alone — it is naturally self-limiting (only content with an active/recent detection job qualifies) rather than an unbounded historical list, so the risk profile is different from the three real fixes here.
