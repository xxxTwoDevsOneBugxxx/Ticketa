# Ticketa Engineering Context

> Last reviewed: 2026-09-19
> Scope: Server + Customer Web Client
> Purpose: persistent context for future development, debugging, architecture, testing, performance, security, and AI-assisted engineering discussions.

## 1. Project Identity

Ticketa is a cinema booking and management system.

Monorepo structure:

```text
Ticketa/
├── apps/
│   ├── server/
│   │   ├── Ticketa.Core
│   │   ├── Ticketa.Infrastructure
│   │   ├── Ticketa.API
│   │   ├── Ticketa.Web
│   │   └── Ticketa.Tests
│   └── client/
└── docs/
```

Important terminology:

* Ticketa uses **layered architecture**. Do not call it Clean Architecture unless the project is intentionally redesigned.
* Server patterns: Repository, Unit of Work, Specification.
* ASP.NET Core MVC is used for the admin/backoffice application.
* The React client consumes the customer-facing REST API.

## 2. Technology Stack

### Server

* .NET 8
* ASP.NET Core Web API
* ASP.NET Core MVC
* Entity Framework Core
* SQL Server
* ASP.NET Identity
* JWT authentication
* Stripe
* TMDB
* Repository Pattern
* Unit of Work
* Specification Pattern
* AutoMapper
* xUnit
* Moq
* Testcontainers / SQL Server integration testing
* Playwright for selected E2E scenarios
* GitHub Actions

### Client

* React
* TypeScript
* Vite
* React Router
* Axios
* TanStack React Query
* Tailwind CSS
* shadcn/ui
* Framer Motion
* lucide-react
* Stripe React integration

## 3. Server Request/Data Flow

```text
HTTP Request
    ↓
Ticketa.API Controller
    ↓
Service / Business Logic
    ↓
Specification / Repository
    ↓
Unit of Work
    ↓
EF Core
    ↓
SQL Server
```

Controllers normally should not directly access `DbContext`.

When investigating a query, trace:

```text
Controller → Service → Specification → Repository/UoW → EF Core → SQL Server
```

Do not assume a direct `_context.Showtime` style access exists.

## 4. Server Projects

### Ticketa.Core

Domain/application-facing contracts:

* entities
* DTOs
* repository interfaces
* service interfaces
* specifications
* shared abstractions

Important contracts include:

* `IBookingRepository`
* `IShowtimeRepository`
* `IUnitOfWork`
* service interfaces for booking, payment, profile, token, etc.

### Ticketa.Infrastructure

Implementation details:

* EF Core
* SQL Server
* repository implementations
* Unit of Work
* Identity infrastructure
* Stripe
* TMDB
* persistence configuration
* migrations

### Ticketa.API

Customer-facing REST API:

* authentication
* authorization
* movies
* showtimes
* seat availability
* booking
* payments
* profile
* API errors
* CORS

### Ticketa.Web

Admin/backoffice MVC:

* movies
* halls
* showtimes
* users
* roles
* payments
* management tables/dashboards

## 5. Specification Pattern

Specifications encapsulate query behavior such as:

* filtering
* sorting
* eager loading
* pagination

Conceptual flow:

```text
Service
 ↓
Specification
 ↓
Repository
 ↓
IQueryable
 ↓
EF Core translation
 ↓
SQL Server
```

Performance investigations must follow this complete chain.

## 6. Authentication

The customer client uses an access-token + refresh-token flow.

The Axios layer has a 401 refresh queue. Conceptually:

```text
Request A ─┐
Request B ─┼→ 401
Request C ─┘
       ↓
one refresh request
       ↓
new access token
       ↓
replay failed requests
```

Important future questions:

* What happens with many simultaneous 401s?
* Is only one refresh request sent?
* What happens when refresh fails?
* What happens during logout?
* How are refresh credentials protected?
* Is CSRF relevant to the chosen cookie/token flow?

Do not change this flow without understanding its invariants first.

## 7. CORS / Security

CORS, authentication, cookies, credentials, and browser behavior must be considered together.

Future security audit:

* explicit trusted origins in production
* credential configuration
* authentication/refresh behavior
* authorization boundaries
* sensitive response data
* anonymous lookup endpoints
* payment retry/idempotency behavior

Treat any permissive development configuration as something to verify before production.

## 8. Booking Domain

Important booking invariants:

* selected seats must be valid for the showtime/hall
* maximum selected-seat rules must be enforced
* the same seat must not be booked twice
* persistence must remain consistent
* concurrent requests must be handled safely

Basic flow:

```text
Customer
 ↓
Showtime
 ↓
Seat selection
 ↓
Booking request
 ↓
Validation
 ↓
Conflict/concurrency protection
 ↓
Persistence
 ↓
Payment/confirmation
```

## 9. Booking Concurrency — High Priority

A check-then-insert sequence can race:

```text
User A ─┐
        ├→ check seat → available
User B ─┘
        └→ check seat → available
             ↓
       both try to insert
```

Application-level checks alone are not sufficient for a database invariant under concurrency.

Desired invariant:

```text
N concurrent attempts for the same showtime + seat
        ↓
exactly one successful claim
        ↓
other conflicting attempts fail safely
        ↓
no duplicate booked-seat records
```

### Required engineering exercise

Create a real SQL Server integration test using Testcontainers:

* 50 concurrent booking attempts
* same showtime
* same seat
* assert exactly one succeeds
* assert conflicts are handled correctly
* assert no duplicate database state

This is a key learning task.

## 10. Payments / Idempotency

Ticketa integrates Stripe.

Keep these concepts separate:

### Deduplication

Prevent multiple logical operations for the same business action.

### Idempotency

Allow retries of the same operation without creating another logical external operation.

Future review:

* who owns the idempotency key?
* is it stable across retries?
* what exact operation does it identify?
* how are client retries handled?
* how are server retries handled?
* how are payment and booking states reconciled?

Do not assume a new random key per retry is equivalent to idempotent retry behavior.

## 11. Booking Reference / Anonymous Lookup

Human-readable booking references should be reviewed as a business/security invariant.

Questions:

* Is uniqueness guaranteed by the database?
* Can references be enumerated?
* Does knowing a reference reveal personal information?
* Is an additional secret needed?
* Is ticket validation separated from booking-data lookup?

Treat this as an investigation item until verified against current code.

## 12. EF Core + SQL Server Performance

Database path:

```text
SQL Server
 ↑
EF Core
 ↑
Repository + Specification + UnitOfWork
 ↑
Services
```

For important endpoints:

1. Find the service.
2. Find the specification.
3. Find repository behavior.
4. Obtain generated SQL.
5. Run/analyze the SQL when appropriate.
6. Inspect the actual execution plan.
7. Check scans/seeks/sorts/lookups.
8. Check indexes.
9. Measure before/after.

Execution-plan questions:

* Index Seek or Scan?
* Expensive sort?
* Key Lookup?
* Estimated vs actual rows?
* Excessive joins from Includes?
* Excessive selected columns?
* Stable/effective pagination?
* Index aligned with filtering and sorting?

Never optimize only from intuition.

## 13. Testing Strategy

### Unit Tests

Use for:

* business logic
* validation
* isolated service behavior
* edge cases

Tools:

* xUnit
* Moq
* test builders

### Integration Tests

Use for:

* EF Core behavior
* SQL Server behavior
* query translation
* unique constraints
* concurrency
* database interactions

Infrastructure:

* Testcontainers
* SQL Server

Important principle:

> Do not mock away the database behavior when the database behavior is what the test needs to prove.

### E2E

Use for:

* critical customer journeys
* frontend/backend integration
* real user flows

Playwright is used for selected scenarios.

## 14. Load Testing

Load testing is separate from correctness testing.

Useful metrics:

* requests/sec
* average latency
* p95
* p99
* error rate
* CPU
* memory
* DB CPU
* DB connections
* response size

Suggested progression:

```text
10 VUs → 50 → 100 → 250 → 500
```

Possible tools:

* k6
* an appropriate .NET load-testing tool

Do not equate registered users with concurrent users.

Define:

* concurrent users
* request rate
* traffic pattern
* endpoint mix
* expected response time
* acceptable error rate

## 15. Client Architecture

Conceptual flow:

```text
React Page
 ↓
Feature Component
 ↓
React Query Hook
 ↓
API Function
 ↓
Axios Client
 ↓
ASP.NET Core API
```

The client uses React Query for server state.

Separate:

* local UI state
* server state
* authentication state

Avoid turning all server data into global client state.

## 16. Client Pagination

Movie browsing uses TanStack React Query infinite querying and IntersectionObserver.

Concept:

```text
Initial page
 ↓
scroll
 ↓
IntersectionObserver
 ↓
fetchNextPage()
 ↓
append page
```

Future questions:

* server-side max page size
* stable ordering
* pagination under concurrent inserts
* query-key correctness
* invalidation after mutations
* memory retained by infinite queries

## 17. Dashboard Direction

Prefer building a dashboard using the existing React stack instead of introducing Next.js solely for this purpose.

Example:

```http
GET /api/dashboard/overview
```

Possible server-side aggregates:

* revenue
* booking count
* occupancy
* popular movies
* showtime utilization
* cancellations
* users

Prefer server/database aggregation over downloading raw records to React and calculating everything client-side.

This feature can teach:

* SQL aggregation
* indexes
* DTO design
* API design
* caching
* React Query
* charts
* authorization

## 18. Engineering Roadmap

### Phase 1 — Correctness / Concurrency

* [ ] booking race-condition integration test
* [ ] verify unique booked-seat constraint
* [ ] verify transaction behavior
* [ ] verify payment idempotency/retry semantics

### Phase 2 — SQL / EF Performance

* [ ] generated SQL
* [ ] execution plans
* [ ] slow query investigation
* [ ] indexes
* [ ] before/after measurement

### Phase 3 — Load Testing

* [ ] baseline important endpoints
* [ ] test increasing concurrency
* [ ] identify bottlenecks
* [ ] optimize based on measurements

### Phase 4 — Security

* [ ] CORS
* [ ] authentication/refresh
* [ ] authorization
* [ ] anonymous lookup
* [ ] sensitive responses
* [ ] payment retries/idempotency

### Phase 5 — Dashboard

* [ ] server-side aggregation
* [ ] dashboard API
* [ ] React Query
* [ ] charts
* [ ] authorization

### Phase 6 — DevOps

* [ ] Docker
* [ ] local containerized environment
* [ ] CI/CD
* [ ] deployment
* [ ] environment configuration
* [ ] secrets
* [ ] logging
* [ ] health checks
* [ ] monitoring

### Phase 7 — System Design

Use Ticketa itself to learn:

* caching
* background jobs
* queues
* rate limiting
* scaling
* stateless APIs
* horizontal scaling
* failure/retry strategies

## 19. What Not to Chase Right Now

Do not interrupt the Ticketa roadmap just to learn:

* Next.js from scratch
* deep Kubernetes
* advanced cloud architecture
* dozens of design patterns
* purely theoretical SOLID courses
* generic system-design courses disconnected from Ticketa

Learn SOLID through real refactoring.

Learn system design from Ticketa's actual problems.

Learn DevOps by deploying Ticketa.

## 20. AI-Assisted Engineering Workflow

The goal is AI-assisted engineering, not manually writing every line.

Preferred loop:

```text
1. Define requirement
2. Define constraints/invariants
3. Predict expected behavior
4. Ask AI to implement/propose
5. Review architecture
6. Run
7. Test edge cases
8. Measure
9. Compare prediction vs reality
10. Decide
```

AI is useful for:

* boilerplate
* implementation
* refactoring
* test scaffolding
* documentation
* repetitive code
* code review
* debugging hypotheses

Developer ownership remains:

* requirements
* architecture
* trade-offs
* invariants
* security decisions
* performance interpretation
* production readiness
* final technical decisions

## 21. Investigation Template

For every important engineering problem:

### Problem

What can go wrong?

### Invariant

What must always remain true?

### Current Flow

How does the current implementation work?

### Threat / Bottleneck

What can break under concurrency, bad input, high traffic, database growth, retries, or failures?

### Hypothesis

What do we think is happening?

### Experiment

How can we prove/disprove it?

### Measurement

What metric tells us the result?

### Change

What did we modify?

### Verification

What evidence proves it worked?

### Decision

Why was the final approach selected?

## 22. High-Priority Backlog

### P0 — Correctness

* [ ] Booking concurrency integration test
* [ ] Verify unique booked-seat constraint
* [ ] Verify transaction behavior
* [ ] Verify payment retry/idempotency semantics

### P1 — Performance

* [ ] API performance baseline
* [ ] Load test movies endpoint
* [ ] Load test showtime/seat endpoint
* [ ] Trace EF Core → SQL
* [ ] Analyze execution plans
* [ ] Review important indexes

### P1 — Security

* [ ] CORS review
* [ ] authentication/refresh review
* [ ] anonymous booking lookup review
* [ ] sensitive response review
* [ ] authorization boundary review

### P2 — Product/Client

* [ ] Admin/customer dashboard
* [ ] server-side aggregation
* [ ] client loading/error/empty-state improvements

### P2 — DevOps

* [ ] Docker
* [ ] containerized local environment
* [ ] CI/CD
* [ ] deployment
* [ ] logging
* [ ] health checks
* [ ] monitoring

### P3 — Architecture

* [ ] caching
* [ ] background jobs
* [ ] queues
* [ ] rate limiting
* [ ] scaling
* [ ] retry/failure strategies

## 23. Source of Truth

For implementation-specific questions:

**Current GitHub code on the current branch is the source of truth.**

For intended architecture/testing decisions:

**docs/ is the design record, but it can become stale.**

If code and docs disagree:

```text
Code ≠ Documentation
      ↓
Flag discrepancy
      ↓
Do not silently assume which one is correct
```

When a future conversation references current code, inspect the current `development` branch before making code-specific claims.

## 24. End Goal

Ticketa should demonstrate understanding of:

* request flow
* authentication
* authorization
* EF Core → SQL Server
* concurrency
* transactions
* testing
* performance measurement
* security boundaries
* deployment
* load behavior
* architectural trade-offs

The project is not just meant to "work". It is intended to be the user's engineering laboratory.
