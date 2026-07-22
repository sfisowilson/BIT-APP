# Brand Inserts Technology (BIT) — Executive Presentation Master Guide (20–30 Minutes)

**Document Purpose:** Presentation script, slide deck structure, live demonstration playbook, and Q&A guide for presenting the **Brand Inserts Technology (BIT)** platform to executive stakeholders, media partners, broadcast engineers, and advertisers.  
**Target Duration:** 20 to 30 Minutes (18–22 min presentation & demo + 8–10 min executive Q&A)  
**Presenter Roles:** Executive Lead / Solution Architect  

---

## ⏱️ Master Agenda & Time Allocation (20–30 Minutes)

| Segment | Topic | Target Time | Primary Goal |
|---|---|---|---|
| **Section 1** | **Executive Hook & Strategic Value** | 2 mins | Establish the revenue problem in African video real estate and our solution. |
| **Section 2** | **System Architecture & Key Capabilities** | 3 mins | Explain the end-to-end pipeline (Ingestion $\rightarrow$ AI Detection $\rightarrow$ Compositing). |
| **Section 3** | **Live Interactive Platform Demonstration** | 10 mins | Walk through live campaign setup, ingestion, AI scene detection, surface placement, and compositing. |
| **Section 4** | **Brand Safety & Compliance Engine** | 2 mins | Highlight face exclusion, competitor sign suppression, and human-in-the-loop review. |
| **Section 5** | **Operations, Telemetry & Swappable AI Engine** | 3 mins | Show RBAC, live alarm handling, and switching AI engines (SAM 2, Google Vision, Gemini). |
| **Section 6** | **Executive Q&A & Next Steps** | 5–10 mins | Address technical, financial, and operational questions. |

---

## 📽️ Slide-by-Slide Presentation Deck

---

### Slide 1: Title & Vision
**Slide Title:** Brand Inserts Technology (BIT)  
**Subtitle:** Monetising Unused In-Content Video Real Estate with Computer Vision & Generative AI  
**Visuals:** Modern split screen displaying raw broadcast footage (SuperSport / M1 Gauteng) side-by-side with a photorealistic, dynamic Coca-Cola/Nike virtual product insertion.  

**Speaking Script (1.5 mins):**
> "Good day everyone. Thank you for joining. Across Southern Africa and global streaming, millions of hours of premium sports, drama, and news content generate revenue *only* during traditional, skippable commercial breaks. The visual real estate *inside* the content—perimeter boards, empty walls, tables, and gantries—generates **zero** ongoing revenue.
> 
> Brand Inserts Technology (BIT) changes that equation completely. We turn static, dead video surfaces into **dynamic, monetisable advertising inventory**—inserted post-production with sub-pixel photorealism, fully locked to camera motion, and strictly protected by automated brand safety."

---

### Slide 2: The Core Problem & Market Opportunity
**Slide Title:** Unlocking Non-Interruptive Monetisation  
**Key Points:**
- **Ad Avoidance:** 74% of streaming viewers skip or look away during traditional video ad breaks.
- **Production Deadlock:** Physical product placement must be agreed months before filming and is locked forever to a single regional sponsor.
- **Unused Real Estate:** Over 35% of broadcast frames contain prime candidate surfaces suitable for dynamic brand placement.
- **The BIT Solution:** Post-production insertion that is region-aware, campaign-targeted, and non-interruptive.

**Speaking Script (2 mins):**
> "Traditional commercial breaks face declining engagement and ad-blocker resistance. Meanwhile, physical product placement is inflexible—a sponsor logo shot in 2024 cannot be updated for a 2026 campaign in a different SADC region.
> 
> BIT enables **post-production dynamic insertion**. An episode of a hit drama or a derby match can display Nike in South Africa, Samsung in Kenya, and Coca-Cola in Nigeria—without modifying the original broadcast source or changing video timing by a single millisecond."

---

### Slide 3: End-to-End System Architecture
**Slide Title:** Modular, AI-First & Human-Guarded Architecture  
**Visual Diagram:**
```
[ Video Ingestion ] ➔ [ Scene-Cut & Transcode ] ➔ [ Computer Vision Detection ]
                                                                │
[ Broadcast Compositing ]  [ Human Approval Gate ]  [ Brand Safety Filter ]
```

**Speaking Script (2.5 mins):**
> "Our architecture rests on five decoupled core pillars:
> 1. **Ingestion Engine:** Supports MXF, MP4, and ProRes in 1080p and 4K resolution.
> 2. **AI Surface Detection:** Combines depth mapping, orientation vectors, and surface classification.
> 3. **Brand Safety Enforcement:** Automated policy checking that permanently flags faces, underage contexts, or competing signs.
> 4. **Homography Compositing:** High-speed perspective warping, lighting compensation, and motion locking.
> 5. **Human-in-the-Loop Governance:** Mandatory approval gates ensuring no unvetted ad ever reaches broadcast distribution."

---

### Slide 4: Live Demonstration — Ingestion & AI Scene Breakdown
**Slide Title:** Step 1: Ingestion & Intelligent Scene Segmentation  
**Live Demo Cue:** Switch to live web application $\rightarrow$ Navigate to **Ingestion Tab** (`v-01` Orlando Pirates vs Kaizer Chiefs).  

**Speaking Script & Demo Cues (3 mins):**
> *"Let's look at the live platform in action.*
> *(Click Ingestion Tab)* Here we have raw 1080p broadcast footage from a SuperSport derby match.
> 
> The system automatically runs scene-cut detection using FFmpeg and computer vision algorithms. Notice how the match is broken down into structured scenes with frame counts and timestamps.
> 
> When we examine Scene 1 and Scene 2, BIT automatically generates candidate surface bounding boxes—identifying stadium perimeter LED boards, 3D grass mats, and overhead signage."*

---

### Slide 5: Live Demonstration — Computer Vision Surface Detection & Scoring
**Slide Title:** Step 2: Surface Quality Scoring & Depth Mapping  
**Live Demo Cue:** In Scene Editor, click **Surface Candidates** for Scene 2 (`sf-03` Mid-pitch Stadium 3D Grass Mat).  

**Speaking Script & Demo Cues (2.5 mins):**
> *"Every detected surface receives two critical AI metrics:*
> 1. **Confidence Score:** How accurately the AI bounded the physical structure.
> 2. **Viability Score:** How stable, readable, and non-obscured the surface remains across the scene duration.
> 
> Here, `sf-03` is a mid-pitch grass mat estimated at 22.1 meters depth with a 92% viability score. The system automatically computes the 3D orientation vector—yaw, pitch, and roll—to ensure perfect perspective alignment during compositing."*

---

### Slide 6: Brand Safety & Exclusion Engine
**Slide Title:** Step 3: Non-Negotiable Brand Safety Filter (MReq 4)  
**Live Demo Cue:** Highlight `sf-02` (Spectator Face) and `sf-04` (Pre-existing Billboard).  

**Speaking Script (2 mins):**
> *"Brand safety is non-negotiable. Notice `sf-02` in our candidate list. The surface type is a close-up spectator face.
> 
> Our automated Brand Safety Classifier immediately triggers a **Permanent Exclusion** under rule MReq 4: 'Face detection filter triggered'. It cannot be overridden by an operator.
> 
> Similarly, `sf-04` was flagged because a pre-existing Coca-Cola billboard was detected in the scene, preventing competitive collision if another beverage brand is active in the same campaign."*

---

### Slide 7: Live Demonstration — Asset Placement & Homography Compositing
**Slide Title:** Step 4: Asset Placement & Broadcast Rendering  
**Live Demo Cue:** Open **Editor Tab / Composer Tab**, select Coca-Cola Classic Banner, preview homography warp.  

**Speaking Script & Demo Cues (3 mins):**
> *"Now we match an approved creative asset to the candidate surface.*
> *(Click Composer Tab)* We select the **Coca-Cola SADC Winter Oasis** campaign asset.
> 
> The system performs **homography transformation**, mapping the 2D creative asset onto the 3D coordinates of the target surface. It applies blur matching to match the broadcast camera's depth of field, along with ambient lighting adjustment.
> 
> With a single click, we submit the placement to our GPU Render Cluster, outputting a broadcast-ready ProRes or H.264 file."*

---

### Slide 8: Campaign Management & SADC Regional Targeting
**Slide Title:** Campaign Operations & Regional Scheduling  
**Live Demo Cue:** Navigate to **Campaigns Tab** / Campaign Selector (`c-01` Coke, `c-02` Nike, `c-03` Samsung).  

**Speaking Script (1.5 mins):**
> *"Campaign managers can manage regional targeting across the SADC region. Campaigns enforce standardized naming codes (e.g., `UZ01EP12_COKE`), budget tracking, and schedule start/end windows.
> 
> Assets uploaded to a campaign are automatically indexed by brand category (e.g., Non-Alcoholic Beverages, Footwear, Electronics) to ensure smart matching against available video surfaces."*

---

### Slide 9: Telemetry, Observability & Swappable AI Engine
**Slide Title:** Operational Health & Swappable AI Engine  
**Live Demo Cue:** Navigate to **Telemetry Tab** and **Admin Console $\rightarrow$ AI Engine**.  

**Speaking Script (2 mins):**
> *"For enterprise operations, BIT includes complete observability:*
> - **Event Logging:** Audit trail for login attempts, AI triggers, and render completions.
> - **Live Alarms:** Real-time telemetry monitoring GPU node temperatures, memory capacity, and gateway relays.
> - **Swappable AI Engine:** Administrators can dynamically select backend AI providers—switching surface detection from Basic to **Replicate SAM 2** or **Google Cloud Vision**, and brand analysis to **Gemini Multimodal Vision** with masked API key management."*

---

### Slide 10: Summary & Strategic Value
**Slide Title:** Why Afrobotics BIT Wins  
**Summary Bullets:**
- ✅ **New Uncapped Revenue Stream:** Monetises static video real estate without additional filming cost.
- ✅ **Sub-Pixel Realism:** Computer vision homography + lighting & depth matching.
- ✅ **Ironclad Brand Safety:** Face exclusion + competitor separation + human approval gate.
- ✅ **Enterprise Ready:** Full RBAC, audit logging, telemetry, and swappable AI providers.

---

## ❓ Executive Q&A Handling Guide

### Q1: "How do you ensure the virtual ad doesn't look fake or pasted on?"
**Answer:**  
> "We combine three mathematical layers: **Homography Matrix Transformation** for exact perspective warp, **Spatial Depth Estimation** to calculate motion blur and camera lens defocus, and **Color Histogram Matching** to sample the surrounding ambient lighting. The result matches the grain, resolution, and lighting of the original source frame."

### Q2: "What happens if a actor or player walks in front of the inserted advertisement?"
**Answer:**  
> "When using our advanced AI detection engines (e.g., SAM 2 or Google Vision), the system creates per-frame occlusion masks. If an object or player passes in front of the surface, the occlusion mask clips the overlay in real-time so the player appears in front of the inserted banner, preserving physical depth."

### Q3: "Is human approval required for every placement?"
**Answer:**  
> "Yes. Under requirement MReq 11, human approval is a mandatory quality gate. While the AI automatically identifies, scores, and warps the asset onto candidate surfaces, an authorized Editor or Admin must review and click 'Approve Placement' before the video can be rendered to broadcast distribution."

### Q4: "Can we run this on-premise or in our cloud infrastructure?"
**Answer:**  
> "Absolutey. The BIT architecture is containerised with Docker/Cloud Run and backed by a lightweight, high-performance API. It can deploy on-premise with GPU hardware or in AWS/GCP/Azure environments."

---

## 📋 Checklist for Presenters
- [ ] Log in as **Admin (`admin@afrobotics.co.za`)** before starting the presentation.
- [ ] Verify that sample campaign content (`v-01`, `v-02`) and creative assets are loaded.
- [ ] Open the **Presentation Deck** tab in the top header bar for smooth 16:9 slide navigation.
- [ ] Use shortcut key **Space** or **Right Arrow** to advance slides during presentation.
- [ ] Keep the **Speaker Notes** drawer open if presenting on a second monitor.
