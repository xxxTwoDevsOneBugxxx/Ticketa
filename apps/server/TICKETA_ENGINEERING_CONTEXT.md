# Ticketa Engineering Context & Verification Log

## Overview
This document tracks the technical decisions, architecture, testing phases, and verification milestones for **Ticketa Server** ([ASP.NET](http://ASP.NET) Core 10.0, EF Core, SQL Server, and Testcontainers).

---

## Phase 0: Testing & Verification Roadmap (P0)

### Phase 0.1: Pure Unit Testing (Isolated Domain & Core Helpers)
- **Scope**: Deterministic testing of mathematical/business rules without external I/O or dependencies.
- **Implemented Tests**:
  - `HallTypeHelper.GetPriceMultiplier`: Verified VIP (1.5x), Premium (1.5x/1.2x), Regular (1.0x) price multipliers via `[Theory]` + `[InlineData]`.
  - `HallTemplate.VisibleSeatCount` / `InvisibleSeatCount`: Verified stadium-bowl skip calculations across `Standard`, `IMAX`, and `Gold` hall types.

---

### Phase 0.2: Concurrency & Race Condition Verification (Testcontainers)
- **Scope**: Verification of database-level unique constraints and conflict-detection mechanisms under high simultaneous contention.
- **Test Class**: [`BookingConcurrencyTests.cs`](file:///C:/Users/moame/RiderProjects/Ticketa/apps/server/Ticketa.Tests/Concurrency/BookingConcurrencyTests.cs)
- **Environment**: Microsoft SQL Server 2022 running inside Docker via `Testcontainers.MsSql` (`MsSqlDatabaseFixture`).
- **Scenario**:
  - 50 concurrent users dispatch booking requests for the exact same seat (`Row 1, Seat 1`) simultaneously using non-blocking asynchronous barrier (`TaskCompletionSource`).
- **Verified Invariants**:
  -  **Exactly 1 winner** succeeds (`Succeeded == true`, booking reference assigned, amount charged).
  -  **Exactly 49 losers** receive conflict response (`Succeeded == false`, `ConflictingSeats` contains `[R1S1]`).
  -  **Database Integrity**: Exactly 1 `Booking` and 1 `BookedSeat` saved in SQL Server. Zero duplicated records.

---

### Phase 0.3: Atomicity & Transaction Integrity (Testcontainers)
- **Scope**: All-or-nothing transactional guarantees for multi-seat bookings and capacity state invariants.
- **Test Class**: [`BookingTransactionAtomicityTests.cs`](file:///C:/Users/moame/RiderProjects/Ticketa/apps/server/Ticketa.Tests/Concurrency/BookingTransactionAtomicityTests.cs)
- **Environment**: Shared `[Collection("MsSqlCollection")]` running `Testcontainers.MsSql`.
- **Scenario 1: Multi-Seat Atomic Rollback**:
  - **Setup**: User A books `Seat (2, 1)`.
  - **Action**: User B attempts a single request booking `[Seat (2, 1), Seat (2, 2)]` (one taken, one available).
  - **Verified Invariants**:
    - Request fails with `Succeeded == false` and lists `Seat (2, 1)` as conflicting.
    - **No Partial Insertion**: The available seat `Seat (2, 2)` is **NOT** inserted into `BookedSeats`.
    - **No Orphaned Records**: Zero booking records created for User B. Total `Bookings` count remains 1. Total `BookedSeats` count remains 1.
- **Scenario 2: Showtime Capacity & SoldOut State Invariant**:
  - **Setup**: Gold Hall (38 visible seats) with 37 seats pre-booked (1 seat remaining, status `Scheduled`).
  - **Action**: Candidate attempts to book `[AlreadyBookedSeat, LastAvailableSeat]`.
  - **Verified Invariants**:
    - Request fails atomically.
    - Showtime `Status` **remains `ShowtimeStatus.Scheduled`** and does not prematurely transition to `SoldOut`.
    - Total booked seats count remains 37, keeping the last seat available for other customers.

---

## Technical Architecture & Invariants Summary

| Component | Responsibility | Concurrency / Transaction Guarantee |
| :--- | :--- | :--- |
| **`BookedSeat` Unique Index** | `(ShowtimeId, Row, SeatNumber)` | Hard database constraint against concurrent double-booking |
| **`BookingService.CreateAsync`** | Read conflict check + EF Core `SaveAsync` | Atomic persistence — fails entire seat batch if any seat is contested |
| **`Showtime.Status`** | `Scheduled` $\leftrightarrow$ `SoldOut` / `Completed` | Transition only when `BookedSeats.Count >= template.VisibleSeatCount` successfully commits |
| **Test Fixture** | `MsSqlDatabaseFixture` | Real SQL Server container initialized with EF Core migrations |