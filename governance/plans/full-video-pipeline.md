# Implementation Plan: Full Video Processing Pipeline

**Version:** 1.0  
**Date:** 2026-07-24  

## Overview
Full-stack implementation plan for fixing and completing all 9 core stages of the BIT platform pipeline:
1. Engine selection & dynamic resolution factory
2. Scene cut detection & video splitting
3. Surface detection & brand safety enforcement
4. Creative asset association & validation
5. Scene rendering & perspective warping
6. Human approval workflow & audit logging
7. Invoice calculation service & endpoints
8. Full video rejoining (stitching) via FFmpeg
9. Final product video download endpoint & UI integration

## Status
- [x] Feature specification (`governance/features/full-video-pipeline.gherkin`)
- [x] NFRs documented (`governance/nfrs/full-video-pipeline.md`)
- [x] Backend implementation — all 9 stages implemented, including invoice calculation (`InvoiceService`/`InvoicesController`) and video rejoining via FFmpeg (`VideoChunkingService.SpliceChunksAsync`, `RenderJobService.SpliceSceneReplacementAsync`)
- [x] Frontend integration — invoice UI (`InvoicePanel`, wired into the Reports view) and final render watch/download UI (Campaign Dashboard's Recent Renders widget) landed last; all other stages already had frontend coverage
- [x] Unit & contract verification — `InvoiceServiceTests` covers happy path + campaign-not-found + no-renders-yet + multi-render fee summation; `dotnet test dotnet-api.Tests` green; contracts updated in `governance/contracts/api-contract.md` and `governance/contracts/component-contracts.md`
