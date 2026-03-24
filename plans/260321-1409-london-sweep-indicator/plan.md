---
title: "London Session Sweep Indicator (PineScript v6)"
description: "TradingView indicator for US30 that detects London session liquidity sweeps with multi-TF filters"
status: complete
priority: P1
effort: 6h
branch: main
tags: [pinescript, tradingview, us30, indicator]
created: 2026-03-21
---

# London Session Sweep Indicator

## Overview

Single-file PineScript v6 overlay indicator (`london-sweep-indicator.pine`) for US30 M15 charts.
Marks London session range, applies H4 trend + H1 momentum filters, detects liquidity sweeps during NY open, and manages trade levels with EOD cutoff.

## Architecture

State machine: `IDLE -> LONDON_SESSION -> RANGE_COMPLETE -> SWEEP_WINDOW -> IN_TRADE -> DONE`
Resets each trading day. Non-repainting via `close[1]` + `lookahead_on` pattern.

## Phases

| # | Phase | Status | Effort | File |
|---|-------|--------|--------|------|
| 1 | Project setup + inputs | complete | 30m | [phase-01](phase-01-setup-and-inputs.md) |
| 2 | Session detection + London range | complete | 45m | [phase-02](phase-02-session-and-range.md) |
| 3 | Multi-TF filters (H4 EMA + H1 momentum) | complete | 45m | [phase-03](phase-03-multi-tf-filters.md) |
| 4 | Sweep detection logic | complete | 45m | [phase-04](phase-04-sweep-detection.md) |
| 5 | Trade management (entry/SL/TP/EOD) | complete | 45m | [phase-05](phase-05-trade-management.md) |
| 6 | Visuals (box, lines, labels, dashboard) | complete | 1h | [phase-06](phase-06-visuals.md) |
| 7 | Alert conditions | complete | 30m | [phase-07](phase-07-alerts.md) |
| 8 | Testing + validation | pending | 1h | [phase-08](phase-08-testing.md) |

## Key Dependencies

- Phases 1-2 are foundational; all later phases depend on them
- Phase 3 depends on Phase 2 (needs session timing)
- Phase 4 depends on Phases 2+3 (needs range + filters)
- Phase 5 depends on Phase 4 (needs sweep signals)
- Phase 6 depends on Phases 2-5 (visualizes all data)
- Phase 7 depends on Phases 4+5 (alert on trade events)
- Phase 8 runs after all code phases complete

## File Output

Single file: `london-sweep-indicator.pine` (~180-200 lines)

## Reference

- [Brainstorm Report](../reports/brainstorm-260321-1401-london-sweep-indicator.md)
