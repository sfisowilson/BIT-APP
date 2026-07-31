# Implementation Plan: Pagination Consistency Fix

**Version:** 1.0
**Date:** 2026-07-31

## Overview

Backend pagination (`PaginatedResult<T>`, `PaginationParams`, 20/page default) is fully built and already used correctly by the Content tab and Telemetry tab (via `usePaginatedData` + `<Pagination>`). Three other list-bearing pages fetch their data via plain unpaginated calls and silently show only the first ~20 rows with no page controls: the Assets view (`CampaignsTab.tsx` — campaigns grid, campaign assets, unassigned assets), the Renders view (`RendersTab.tsx`), and Admin → Users (`AdminConsoleTab.tsx`). The Users endpoint additionally has **no pagination support server-side at all** — `IUserService.GetUsersAsync()` returns an unbounded `IEnumerable<User>` despite `UserFilterParams` already existing (unused) in `PaginatedDtos.cs`.

## Status
- [x] Feature specification (`governance/features/pagination-consistency-fix.gherkin`)
- [x] NFRs documented (`governance/nfrs/pagination-consistency-fix.md`)
- [x] Backend implementation
- [x] Frontend integration
- [x] Unit & contract verification

## Files to change

### Backend
1. `dotnet-api/DTOs/PaginatedDtos.cs` — add `bool? Unassigned` to `AssetFilterParams`.
2. `dotnet-api/Services/AssetService.cs` — `GetAssetsAsync`: when `filter.CampaignId` is empty and `filter.Unassigned == true`, filter `CampaignId == null`.
3. `dotnet-api/Services/UserService.cs` (+ `IUserService`) — replace `GetUsersAsync()` (no-arg, unbounded) with `GetUsersAsync(UserFilterParams filter)` returning `PaginatedResult<User>`, filtering by `Role`, `AccountStatus`, and `Search` (name/email/role/status, matching the existing frontend client-side search fields), sorted by `CreatedAt`-equivalent (User has no CreatedAt — sort by `LastLoginAt` descending, matching `PaginationParams`' "newest first" default intent) unless `SortBy` given.
   - No other caller depends on the old no-arg signature (`UpdateUserAsync`/`DeleteUserAsync`'s admin-count checks already call `_userRepository.GetAllAsync()` directly) — safe direct replacement, not an overload.
4. `dotnet-api/Controllers/UsersController.cs` — `GetUsers()` takes `[FromQuery] UserFilterParams filter`, returns `ActionResult<PaginatedResult<User>>`.
5. Campaigns (`CampaignService.GetCampaignsAsync`) and Renders (`RenderService.GetRendersAsync`) already fully support the filters needed — **no backend change** for those two.

### Frontend
6. `src/components/CampaignsTab.tsx`:
   - Campaign Database grid → self-fetch via `usePaginatedData<CampaignItem>('/api/campaigns', { search })`, add search input + `<Pagination>`. Stop reading the full list from the `campaignList` prop for this grid (prop stays for the "Assign to Campaign" dropdown — out of scope, see NFRs).
   - Campaign Assets list → self-fetch via `usePaginatedData<CreativeAsset>('/api/assets', { campaignId: selectedCampaignId })`, add `<Pagination>`.
   - Unassigned Assets list → self-fetch via `usePaginatedData<CreativeAsset>('/api/assets', { unassigned: true })`, add `<Pagination>`.
   - Mutation handlers (`handleAssociateAsset`, `handleUnassociateAsset`, `handleDeleteAsset`, `handleCreateAsset` success, `handleUpdateAsset` success) call the relevant hook's `refresh()` afterward so the lists stay in sync — these handlers live in `App.tsx` and return void/Promise today; simplest correct wiring is to call all three hooks' `refresh()` after every mutation from inside `CampaignsTab` itself (it already owns the mutation call sites via its prop callbacks).
7. `src/components/RendersTab.tsx`:
   - Add `campaignId?: string` prop (from `App.tsx`'s `selectedCampaignId`).
   - Self-fetch via `usePaginatedData<RenderItem>('/api/renders', { campaignId })`, add a status filter dropdown + `<Pagination>`.
   - Stat cards (Processing/Completed/Failed) fetch true totals via 4 parallel `pageSize:1` count-only requests (Queued, Processing, Finished, Failed), refetched alongside the main list.
   - Drop the `renderList` prop (no longer needed by this component — `App.tsx`'s global `renderList` state remains for other consumers, e.g. `EditorTab`'s active-prompt-render lookup and the Dashboard's Recent Renders widget).
8. `src/components/AdminConsoleTab.tsx`:
   - Self-fetch via `usePaginatedData<User>('/api/users', { search, role, accountStatus })`, add `<Pagination>`.
   - Stat cards (Admin/Editor/Advertiser counts) fetch true totals via 3 parallel `pageSize:1` count-only requests.
   - Existing CRUD handlers (`handleAddUser`, `handleUpdateUser`, `handleDeleteUser`) call the hook's `refresh()` (and re-run the count fetches) after a successful mutation instead of the old `fetchUsers()`.
9. `src/App.tsx` — pass `selectedCampaignId` to `<RendersTab campaignId={selectedCampaignId ?? undefined} .../>`, drop the `renderList` prop from that call site only (other `renderList` usages in `App.tsx` are untouched).

### Tests
10. `dotnet-api.Tests/AssetServiceTests.cs` (new or extend existing) — test `Unassigned=true` filter returns only `CampaignId == null` assets, and combined with `CampaignId` set, `CampaignId` takes precedence.
11. `dotnet-api.Tests/UserServiceTests.cs` (new) — test pagination (page/pageSize), `Role`/`AccountStatus`/`Search` filters, and that `UpdateUserAsync`/`DeleteUserAsync`'s last-admin protection still works (regression guard — these must keep using the full unfiltered set).

### Governance
12. Update `governance/contracts/api-contract.md` — `/api/users` becomes paginated (`PaginatedResult<User>`, `UserFilterParams` query), `/api/assets` gains `unassigned` query param.
13. Update `governance/contracts/component-contracts.md` — `CampaignsTab`, `RendersTab`, `AdminConsoleTab` prop/behavior changes.
14. Run `governance/scripts/validate-contracts.ps1`.

## Testing strategy
- Backend: xUnit tests per above, `dotnet test dotnet-api.Tests` full suite green.
- Frontend: `npm run lint` (tsc --noEmit) clean.
- Manual browser verification: for each of the three pages, confirm pagination controls appear when data exceeds one page, confirm search/filter narrows results, confirm stat cards match reality, confirm existing mutation flows (approve/reject asset association, retry render, add/edit/delete user) still work and refresh their respective lists.

## Rollback
All changes are additive (new optional filter fields, new pagination wiring) except the `UserService.GetUsersAsync()` signature change — reverting is a straight `git revert` of the relevant commit(s), no data migration involved (no schema/DB changes in this fix).
