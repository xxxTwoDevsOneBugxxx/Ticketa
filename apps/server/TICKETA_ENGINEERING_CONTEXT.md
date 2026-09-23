# Ticketa Engineering Context & Verification Log

## Overview
This document tracks technical decisions, architecture, testing phases, and verification milestones for **Ticketa Server** ([ASP.NET](http://ASP.NET) Core 10.0, EF Core, SQL Server, and Testcontainers).

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
  - 🏆 **Exactly 1 winner** succeeds (`Succeeded == true`, booking reference assigned, amount charged).
  - ❌ **Exactly 49 losers** receive conflict response (`Succeeded == false`, `ConflictingSeats` contains `[R1S1]`).
  - 🔒 **Database Integrity**: Exactly 1 `Booking` and 1 `BookedSeat` saved in SQL Server. Zero duplicated records.

---

### Phase 0.3: Database-Level Transaction Atomicity & Rollback (Testcontainers)
- **Scope**: Verify that a multi-seat booking operation is **truly atomic at the database level** during `SaveChangesAsync()`.
- **Test Class**: [`BookingTransactionAtomicityTests.cs`](file:///C:/Users/moame/RiderProjects/Ticketa/apps/server/Ticketa.Tests/Concurrency/BookingTransactionAtomicityTests.cs)
- **Environment**: Shared `[Collection("MsSqlCollection")]` running `Testcontainers.MsSql`.
- **Methodology (Direct DbContext Execution)**:
  - Intentionally bypassed `BookingService.CreateAsync()` to avoid its early in-memory pre-checks (`GetConflictAsync`), forcing the operation directly into the database engine.
- **Scenario & Execution Flow**:
  1. **Initial State & Step 1**: User A successfully books `Seat A1 (Row 1, Seat 1)`. Persisted to DB.
  2. **Step 2 (Database-Level Collision)**: User B attempts a single `SaveChangesAsync()` containing:
     - `Booking B`
       - `BookedSeat A1` (duplicate → violates database unique constraint `(ShowtimeId, Row, SeatNumber)`)
       - `BookedSeat A2` (valid / available)
     - `Showtime.Status = SoldOut` (related capacity state change attached in the same change set).
  3. **Trigger**: `SaveChangesAsync()` triggers SQL Server unique index violation → throws `DbUpdateException`.
  4. **Critical Verification (Fresh DbContext)**:
     -  **Original A1 Booking Intact**: User A's booking for `Seat A1` remains unchanged.
     -  **No Partial Persistence of Available Seats**: Valid `Seat A2` was **NOT inserted**.
     -  **No Orphaned Booking**: `Booking B` was **NOT inserted**.
     -  **Related State Rolled Back**: `Showtime.Status` remained `Scheduled` and was **NOT** persisted as `SoldOut`.
     -  **Exact Counts**: Total `Bookings` count == 1, Total `BookedSeats` count == 1.

---

## Technical Architecture & Invariants Summary

| Component | Responsibility | Concurrency / Transaction Guarantee |
| :--- | :--- | :--- |
| **`BookedSeat` Unique Index** | `(ShowtimeId, Row, SeatNumber)` | Hard database constraint against concurrent double-booking |
| **Database Transaction** | EF Core `SaveChangesAsync` / SQL Server Transaction | Atomic persistence — any constraint violation aborts and rolls back the entire batch |
| **`Showtime.Status`** | `Scheduled` $\leftrightarrow$ `SoldOut` / `Completed` | Changes rolled back atomically if booking fails |
| **Test Fixture** | `MsSqlDatabaseFixture` | Real SQL Server container initialized with EF Core migrations |