# TinkerFlow — Backend API

Backend REST API for **TinkerFlow** — an internal operational management and 3D print logistics platform used by field instructors, regional coordinators, and print lab operators managing mobile 3D modeling courses in elementary schools.

Currently managing live progress records for **1,000+ students across 5 regional branches**, coordinating **1,000–2,000 physical print jobs and matrix state transitions weekly**.

> **Related repository:** Frontend client available at [TinkerFlow-front](https://github.com/Maksiator/TinkerFlow-front).

---

## The Problem (Case Study)

Running mobile technology workshops across distributed school branches created distinct operational bottlenecks:

* **Instructor substitutions & student privacy:** Instructors regularly take short-notice substitutions across different schools. Without time-scoped permissions, substitute trainers either had zero insight into what students were building, or had to be granted broad access to the entire branch — posing serious data governance and student privacy issues.
* **Material packing overhead:** Instructors travel between multiple schools daily with physical tutorial binders. Without aggregated session planning, instructors routinely overpacked dozens of duplicate copies instead of sharing materials across consecutive groups.
* **Returning students with zero history:** When kids enrolled for a second school year, visiting instructors treated them as blank slates because historical progress lived in archived personal files of previous staff.
* **Chaotic 3D print farm intake:** The central 3D printer operator had no structured intake. Managing thousands of monthly print requests, custom models (e.g., dual mini-models), filament color requests, and print failures through fragmented chat channels and spreadsheets became unsustainable.

---

## Core Backend Architecture & Domain Logic

The system coordinates four operational roles (**Admin**, **Coordinator**, **Trainer**, **Printer**) through clear domain boundaries:

### 1. Scoped Role-Based Access Control (RBAC) & Dynamic Substitutions
* **Branch Isolation:** Coordinators and instructors are strictly scoped to their assigned educational branches.
* **Time-Scoped Substitution Engine:** When a substitute instructor is scheduled, the backend enforces a dynamic authorization window (**configurable, defaults to ±2 days around the session date**). Outside this window, queries for student matrices and personal details are rejected at the ORM query level to protect student privacy.

### 2. Cross-Season State Machine
Tracks student model progress across school years through a deterministic state machine:  
`NotStarted` ➔ `Planned` / `InProgress` ➔ `ReadyToPrint` ➔ `SentToPrint` ➔ `Completed`
* Supplies structured aggregation data for pre-class material packing.
* Automatically maintains multi-year student portfolios, preventing students from repeating previously completed models in subsequent school years.

### 3. Print Batching & Lab Logistics Engine
* **Batch Aggregation:** Groups physical print jobs by school and session date into clean production batches.
* **Bi-directional Feedback:** Persists instructions from the instructor (e.g., custom models, filament choices) and feedback notes from the 3D printer operator (e.g., slicing errors, mechanical failures).
* **Delivery Verification Transactions:** Handles multi-step delivery confirmation — when a batch is marked received, verified models transition to `Completed`, while failed prints automatically revert to `InProgress` on the classroom matrix so instructors know they need attention.

---

## Tech Stack

* **Language/Framework:** .NET 9 Web API (C#)
* **Architecture:** Clean Architecture (`TinkerFlow.API`, `TinkerFlow.Domain`, `TinkerFlow.Infrastructure`)
* **Database & ORM:** PostgreSQL + Entity Framework Core (Code-First migrations)
* **Authentication & Security:** JWT Bearer tokens + active account status memory caching + rate limiting
* **API Documentation:** OpenAPI with Scalar interactive UI
* **DevOps & CI/CD:** GitHub Actions (automated build, validation, and container publication to GHCR) + Docker Compose

---

## Getting Started

### Prerequisites
* Docker & Docker Compose
* .NET 9 SDK (optional, for local development outside Docker)

### Local Setup (Docker)

1. Clone the repository:
   ```bash
   git clone https://github.com/Maksiator/TinkerFlow-back.git
   cd TinkerFlow-back
   ```

2. Start the local database:
   ```bash
   docker compose -f docker-compose.db.yaml up -d
   ```

3. Run the API:
   ```bash
   dotnet run --project TinkerFlow.API
   ```

The API will be available at `http://localhost:8080` (interactive API documentation at `/scalar/v1`).

---

## Project Structure

```text
├── TinkerFlow.API/             # Controllers, Middlewares, Rate Limiting, Program.cs
├── TinkerFlow.Domain/          # Domain Entities, Enums (ProjectStates, UserRoles)
├── TinkerFlow.Infrastructure/  # EF Core DbContext, Migrations, GroupAccessService
├── docker-compose.prod.yml     # Production orchestration spec
└── docker-compose.db.yaml      # Local developer PostgreSQL container
```

---

## License

Copyright © 2026 Maksymilian Fijoł. All rights reserved.  
This repository and its codebase are proprietary. Published strictly for portfolio, architectural review, and hiring evaluation purposes. Unauthorized copying, distribution, modification, or commercial use without prior written permission is strictly prohibited.

