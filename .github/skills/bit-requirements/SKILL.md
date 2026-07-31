---
name: bit-requirements
description: 'Reference guide for BIT (Brand Inserts Technology) project requirements, application insights, tools, and ways of working.'
argument-hint: 'Query about BIT requirements, stack, design, or governance rules'
user-invocable: true
---

# BIT Platform: Requirements, Insights, Tools & Ways of Working

This document serves as the canonical developer and agent skill guide for understanding the **Brand Inserts Technology (BIT)** platform. It consolidates all product requirements, architectural insights, tech stack details, and governance standards.

---

## 1. Project Overview & Business Core

**Brand Inserts Technology (BIT)** is an AI-powered post-production video inventory creation platform. It dynamically inserts photorealistic, motion-locked brand advertisements (e.g., billboards, LED boards, walls, signage, 3D grass mats) into unused video real estate (such as sports match footage or dramas) without changing the duration of the original source video.

### High-Level Value Proposition
*   **New Revenue Streams:** Monetises empty space inside premium video content post-production.
*   **Regional Targeting:** Allows a single source video to display regional-specific advertisements (e.g., Coke in South Africa, Nike in East Africa).
*   **Photorealism:** Uses perspective mapping, ambient lighting compensation, and motion blur to make virtual placements look native.

---

## 2. Core Functional Requirements (MReqs)

The platform implements 25 minimum requirements (MReq 1–25). Key requirements include:

### Ingestion & Pipeline (MReq 1, 11)
*   **MReq 1 (Content Integrity):** Video duration and original video content must never be modified beyond the approved brand insertions.
*   **Pipeline States:** 
    `Staging` ➔ `Transcoding` ➔ `SceneDetecting` ➔ `Completed`
    *(Each stage can transition to `Failed`. Retries transition `Failed` ➔ `Staging`)*
*   **Naming Conventions:** Campaigns must follow the strict format: `XX00XX00_XXXX XX00XX00_XXXX` (e.g., `UZ01EP12_COKE`).

### AI Surface Detection & Analysis
*   **Surface Bounding:** Detects TV/LED screens, perimeter boards, signage, walls, product packaging, etc.
*   **Surface Scoring:** Computes **Confidence Score** (classification accuracy) and **Viability Score** (placement stability & occlusion factor).
*   **3D Geometry:** Solves perspective using orientation vectors (yaw, pitch, roll) and estimates spatial depth.

### Brand Safety (MReq 4)
*   **Permanent Exclusion Categories:** Human faces, children, emergency vehicles, government insignia, religious symbols/spaces.
*   **Competitor Separation:** Detects existing scene logos/text to flag potential conflicts.
*   **Exclusion Enforcement:** Exclusions are enforced automatically at placement time and cannot be silently bypassed.

### Human-in-the-Loop Governance (MReq 11)
*   **Mandatory Approval Gate:** No placement can be processed or rendered without an explicit human review and approval (`ApprovalItem` audit trail).

### Operations, Telemetry & BI (MReq 19, 22)
*   **MReq 19 (Analytics):** Dashboard displays exposure metrics, impressions, peak/average rendering times, processing cost vs. revenue.
*   **MReq 22 (Usage Tracking):** Middleware logs every API usage record for auditing.
*   **System Telemetry:** Real-time logging, alarms (e.g., GPU temperature, memory spikes), and settings control.

---

## 3. Application Insights & Architecture

BIT is a **three-tier, multi-service platform** composed of the following subsystems:

```
┌──────────────────────────────────────────────────────────────────┐
│                     CLIENT LAYER (Browser)                        │
│  React 19 SPA  ·  Vite 6  ·  TypeScript 5.8  ·  Tailwind CSS 4  │
└──────────────────────────┬───────────────────────────────────────┘
                           │ HTTPS / JWT Bearer
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     API GATEWAY LAYER                             │
│  .NET 8 ASP.NET Core Web API  ·  C# 12                           │
└──────────────────────────┬───────────────────────────────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
┌─────────────────┐ ┌──────────┐ ┌──────────────────────┐
│   DATA LAYER    │ │  HANGFIRE│ │   AI/ML SERVICES     │
│  PostgreSQL 16  │ │  Back-   │ │  Python FastAPI      │
│  EF Core 8      │ │  ground  │ │  YOLOv11 + ByteTrack │
└─────────────────┘ └──────────┘ └──────────────────────┘
```

### Architectural Decisions & Conventions
*   **GUID String IDs:** All entity primary keys are string-represented GUIDs (`Guid.NewGuid().ToString()`), NOT integers.
*   **UTC Timestamps:** All timestamps are strictly UTC (`DateTime.UtcNow`).
*   **camelCase JSON:** Serialized as camelCase across the entire stack (both frontend and backend).
*   **DTO Isolation:** Domain database entities are never exposed directly to the API boundary. DTOs are mapped for all request/response payloads.
*   **AI Engine Swappability:** Swappable providers are registered in `Program.cs` under runtime Platform Settings:
    *   `engine_detection` ➔ `"yolo" | "replicate" | "google" | "basic"`
    *   `engine_brand_analysis` ➔ `"gemini" | "google" | "basic"`
    *   `engine_compositing` ➔ `"opencv" | "basic"`

---

## 4. Technology Stack & Tools

### Frontend (React SPA)
*   **React 19.0** & **TypeScript 5.8**
*   **Vite 6.2** (Build tool and proxy configuration)
*   **Tailwind CSS 4.1** (Styling system)
*   **Motion (Framer Motion) 12.23** (Transitions and animations)
*   **Lucide React 0.546** (Icon set)
*   **@google/genai 2.4** (Client-side Gemini API)
*   **Custom hooks:** `useChunkedUpload` (chunked files), `usePaginatedData` (paging), `useIdleTimer` (auto-logout).

### Backend (.NET API)
*   **.NET 8 / C# 12**
*   **Entity Framework Core 8.0** with **Npgsql 8.0**
*   **PostgreSQL 16+** (Primary database)
*   **Hangfire** (Background processing for transcoding, scene analysis, rendering)
*   **BCrypt** (Password hashing)
*   **JWT Bearer Auth** (Stateless authentication)

### AI/ML Detection Service (Python Microservice)
*   **Python 3** running a **FastAPI** application
*   **Ultralytics YOLOv11** & **ByteTrack** (Object surface detection & frame tracking)
*   **OpenCV** (Image processing & math transforms)
*   **Threshold Hot-Swapping:** Configurable `confidence_threshold`, `iou_threshold`, and `model_size` parameters on demand without microservice restart.

---

## 5. Governance & Ways of Working

To maintain platform stability, clean code, and zero hallucinations, all agents and developers must adhere to the following workflow.

### Step 1: Pre-requisites & Rules Reading
1.  Before writing a line of code, you must read the following three documents in order:
    *   `governance/architecture/agent-quickstart.md`
    *   `governance/rules/hallucination-prevention.md`
    *   `governance/rules/agent-workflow.md`
2.  Ensure target feature files exist:
    *   `governance/features/<feature-name>.gherkin`
    *   `governance/nfrs/<feature-name>.md`
    *   `governance/plans/<feature-name>.md`

### Step 2: Coding Rules
*   **Rule H1-H10 (Hallucination Prevention):** Never guess endpoint signatures, database columns, or React component props. Read the canonical files in `governance/contracts/`:
    *   API Endpoints ➔ `governance/contracts/api-contract.md`
    *   Database Schema ➔ `governance/contracts/db-schema.md`
    *   React Props ➔ `governance/contracts/component-contracts.md`
*   **Rule 1 (No Mock Code):** Stubs, fakes, placeholders, or "basic"/no-op fallback engines are strictly prohibited. Code must be fully and natively implemented.
*   **Rule 2 (Unit Testing):** No code changes can be committed without corresponding unit tests:
    *   Backend: xUnit in `dotnet-api.Tests/`
    *   Frontend: Vitest/React Testing Library
    *   Python: pytest in `detection-service/`
*   **Rule 3 (Cross-Stack Completeness):** Database schema change ➔ Migration ➔ DTO ➔ Service ➔ Controller ➔ API Client ➔ Frontend Type ➔ Component ➔ Unit Tests.
*   **Rule 4 (No Assumptions, No Temp Fixes):** Two-part mandatory rule — see `governance/rules/no-assumptions-no-temp-fixes.md`:
    *   **Part 1 — Never Assume:** When code verification is inconclusive, ask the developer. Never guess at business logic, intent, or requirements.
    *   **Part 2 — No Temp Fixes:** Never swallow exceptions with empty catch blocks, silent null returns, or `// FIXME` placeholders. Fix the root cause or surface the error properly.

### Step 3: Validation & Commits
*   **Validation Script:** Prior to committing, run the contract freshness validation:
    ```powershell
    governance/scripts/validate-contracts.ps1 -FixHint
    ```
    This verifies that all contract markdown files are fully synchronized with source code signatures.
*   **Commit Discipline:** Use standard semantic commits:
    ```
    type(scope): summary

    Detailed description of changes

    Governance:
    - updated governance/contracts/api-contract.md
    - updated governance/plans/feature-x.md
    ```
    Push changes immediately after committing.
