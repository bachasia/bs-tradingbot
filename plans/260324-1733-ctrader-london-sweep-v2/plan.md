# London Sweep Strategy — cTrader C# Implementation Plan

## Overview

Implement London Sweep liquidity strategy as cTrader Indicator + cBot in C#.
Strategy: detect London session range sweep during NY open, filtered by H4 trend + H1 momentum.

**Target:** FTMO cTrader, US30, M15 chart.

## Architecture

```
LondonSweepIndicator.cs     →  Visual + signal generation (overlay on M15 chart)
  ├── London Range tracking (03:00-09:00 EST)
  ├── H4 EMA trend filter
  ├── H1 momentum filter
  ├── Pre-sweep invalidation (09:00-09:29)
  ├── Sweep detection (09:30-11:00)
  ├── Entry/SL/TP calculation
  ├── Dashboard + chart drawings
  └── Output signals for cBot

LondonSweepBot.cs           →  Order execution (reads indicator signals)
  ├── Indicator reference
  ├── Signal reading + order placement
  ├── FTMO risk guards
  ├── EOD cutoff management
  └── Position/order lifecycle
```

**Note:** cTrader requires single-file per indicator/bot. Both files will exceed 200 LOC but this is standard cTrader practice — users paste single files into cTrader IDE. Internal organization via regions + helper methods.

## Key Improvements Over v1

| Issue | v1 | v2 |
|-------|----|----|
| Pre-sweep check (09:00-09:29) | Missing | Implemented |
| State machine | 6 states, no pre-sweep | 7 states with PreSweepCheck |
| Risk sizing | Fixed volume | 1% account risk auto-calc |
| Market order | Limit order (may not fill) | Market order at sweep candle close |
| Code clarity | Implicit flow | Explicit state transitions |

## Phases

| # | Phase | Status | Effort |
|---|-------|--------|--------|
| 1 | [Indicator Core](phase-01-indicator-core.md) | Complete | Medium |
| 2 | [Indicator Visuals](phase-02-indicator-visuals.md) | Complete | Low |
| 3 | [Bot Implementation](phase-03-bot-implementation.md) | Complete | Medium |
| 4 | [Testing & Validation](phase-04-testing-validation.md) | Manual (cTrader) | Low |

## Dependencies

- Phase 2 depends on Phase 1
- Phase 3 depends on Phase 1 (reads indicator outputs)
- Phase 4 depends on all phases

## Strategy Flow Reference

```
London Close (09:00 EST)
    │
    ▼
[Range ≥ 50 pts?] ── No ──→ STOP
    │ Yes
    ▼
[H4 EMA20 > EMA50?] ──→ Direction (Long/Short)
    │
    ▼
[H1 momentum matches?] ── No ──→ STOP
    │ Yes
    ▼
[09:00-09:29: Price already swept?] ── Yes ──→ STOP  ← NEW in v2
    │ No
    ▼
[09:30-11:00: Find M15 Sweep candle]
    │
    ▼
[Wick beyond + close back inside?] ── No ──→ Wait
    │ Yes
    ▼
ENTER TRADE → SL & TP → Close before 15:00 EST
```
