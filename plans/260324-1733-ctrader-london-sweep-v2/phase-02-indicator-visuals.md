# Phase 2 — Indicator Visuals & Dashboard

## Priority: MEDIUM | Status: Pending

## Overview

Add chart drawings and dashboard to `LondonSweepIndicator.cs`. Visual feedback for manual trading and signal verification.

## Context Links

- [Phase 1 — Core Logic](phase-01-indicator-core.md)
- [Plan Overview](plan.md)

## Requirements

### Chart Drawings
1. **London Range Box** — semi-transparent blue rectangle from session start to end
2. **High/Low Lines** — dotted horizontal lines at London High and London Low
3. **Trade Lines** — Entry (blue), SL (red), TP (green) trend lines from signal bar
4. **Labels** — Price labels for Entry, SL, TP values
5. **Sweep Arrow** — up/down arrow at sweep candle for quick visual identification

### Dashboard (Top-Right Corner)
Display live status:
```
══ London Sweep ══
H4 Trend:  Bullish [+]
H1 Mom:    Positive [+]
Range:     87.3 pts OK
Pre-Sweep: Clear
Sweep:     Watching
Trade:     None
State:     SweepWindow
───────────────
Entry: 42150.5
SL:    42098.2
TP:    42207.2
```

### Visual Toggle
- `ShowDashboard` parameter — enable/disable dashboard
- `ShowTradeLines` parameter — enable/disable trade level drawings

## Implementation Steps

1. London Range box: `Chart.DrawRectangle()` updated each bar during London session
2. High/Low lines: `Chart.DrawHorizontalLine()` at London close
3. Trade lines: `Chart.DrawTrendLine()` from signal bar, stopped at trade resolution
4. Dashboard: `Chart.DrawStaticText()` updated on each Calculate call
5. Sweep arrow: `Chart.DrawIcon()` at sweep candle

## Todo

- [ ] London Range box drawing (update during session)
- [ ] High/Low horizontal lines at range close
- [ ] Entry/SL/TP trade lines on signal
- [ ] Stop extending lines on trade resolution (SL/TP/EOD)
- [ ] Price labels for trade levels
- [ ] Sweep arrow indicator
- [ ] Dashboard with live status
- [ ] Parameter toggles for visuals

## Success Criteria

- London box visible on chart during backtesting
- Dashboard updates in real-time on live chart
- Trade lines appear on signal and stop at resolution
- All drawings use unique names per day (no overlap)
