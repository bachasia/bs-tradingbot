---
title: "Port London Sweep Indicator to cTrader (C# / cAlgo)"
description: "2-file port of PineScript v6 London Sweep strategy to cTrader Indicator + cBot with pending order flow"
status: pending
priority: P1
effort: 10h
branch: feat/ctrader-london-sweep
tags: [ctrader, calgo, csharp, porting, indicator, cbot, trading]
created: 2026-03-24
---

# Port London Sweep Indicator to cTrader

## Overview

Port the existing TradingView PineScript v6 London Sweep Indicator to cTrader Automate (C# / cAlgo API). Two-file architecture: `LondonSweepIndicator.cs` (visual + signal logic) and `LondonSweepBot.cs` (execution + trade management). Target broker: FxPro, symbol `#US30`.

## Architecture

```
ctrader/
  LondonSweepIndicator.cs   (~200 lines)  — Indicator class
  LondonSweepBot.cs          (~150 lines)  — Robot class
```

**Indicator** owns all analysis: session detection, London range, MTF filters (H4 EMA + H1 momentum), sweep detection, visuals (range box, level lines, dashboard). Exposes signals via `[Output]` properties.

**cBot** references indicator via `Indicators.GetIndicator<>()`, reads signals on `OnBarClosed()`, places pending limit orders, manages EOD cutoff, fires notifications.

## Key Porting Decisions

| Decision | Rationale |
|---|---|
| `[Indicator]` + `[Robot]` separation | Reusable indicator, swappable bots |
| `OnBarClosed()` + `Last(1)` in cBot | Non-repainting, confirmed-bar-only logic |
| `lastIndex` pattern in indicator `Calculate()` | Simulate bar-close events inside indicator |
| `Chart.DrawStaticText()` for dashboard | Closest equivalent to Pine `table.new()` |
| `PlaceLimitOrder()` with pips-based SL/TP | Pending order confirmation flow per brainstorm |
| `TimeZones.EasternStandardTime` | Handles EST/EDT transitions automatically |
| State machine as C# `enum` | Clean, type-safe replacement for Pine `var int` |

## Phase Summary

| Phase | Description | Effort | Status | Depends On |
|---|---|---|---|---|
| 1 | Project setup, boilerplate, input parameters | 1h | pending | - |
| 2 | Session detection, London range tracking, range box visual | 1.5h | pending | Phase 1 |
| 3 | MTF filters (H4 EMA + H1 momentum), non-repainting | 1.5h | pending | Phase 2 |
| 4 | Sweep detection logic, output properties for cBot | 1.5h | pending | Phase 3 |
| 5 | Dashboard visuals (status panel) | 1h | pending | Phase 4 |
| 6 | cBot — reference indicator, read signals, pending orders | 1.5h | pending | Phase 4 |
| 7 | cBot — trade management, EOD cutoff, notifications, logging | 1h | pending | Phase 6 |
| 8 | Testing — backtest on FxPro demo, validation checklist | 1h | pending | Phase 5, 7 |

## Dependencies

- cTrader Desktop installed with FxPro demo account
- `#US30` symbol available on FxPro demo
- cTrader Automate IDE (built into cTrader Desktop)
- No external NuGet packages needed — `cAlgo.API` ships with platform

## Porting Reference Map

| PineScript v6 | cTrader cAlgo C# |
|---|---|
| `request.security("240", ta.ema(close, 20)[1])` | `var h4Bars = MarketData.GetBars(TimeFrame.Hour4);` `Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 20).Result.Last(1)` |
| `time(tf, "0300-0900", tz)` | `var t = Bars.OpenTimes.Last(0); t.Hour >= 3 && t.Hour < 9` |
| `box.new(left, top, right, bottom)` | `Chart.DrawRectangle(name, barIdx1, priceTop, barIdx2, priceBottom, Color.Blue)` |
| `line.new(x1, y1, x2, y2)` | `Chart.DrawTrendLine(name, barIdx1, price1, barIdx2, price2, color)` |
| `label.new(x, y, text)` | `Chart.DrawText(name, text, barIdx, price, color)` |
| `table.new(pos, cols, rows)` | `Chart.DrawStaticText(name, text, vAlign, hAlign, color)` |
| `alertcondition(cond, title, msg)` | `Notifications.PlaySound(filePath)` or `.SendEmail(...)` |
| `var float x = na` | `private double _x = double.NaN;` |
| `barstate.isconfirmed` | `lastIndex` pattern in `Calculate()` (see Phase 2) |
| `ta.change(dayofweek)` | `Bars.OpenTimes.Last(0).Date != _lastDate` |

## Files

- [Phase 1: Project Setup](./phase-01-project-setup.md)
- [Phase 2: Session Detection & London Range](./phase-02-session-and-range.md)
- [Phase 3: MTF Filters](./phase-03-mtf-filters.md)
- [Phase 4: Sweep Detection & Outputs](./phase-04-sweep-detection.md)
- [Phase 5: Dashboard Visuals](./phase-05-dashboard-visuals.md)
- [Phase 6: cBot Signal Reading & Orders](./phase-06-cbot-signals-orders.md)
- [Phase 7: cBot Trade Management](./phase-07-cbot-trade-management.md)
- [Phase 8: Testing & Validation](./phase-08-testing-validation.md)

## Unresolved Questions

1. Exact pip size / point value for `#US30` on FxPro — needs runtime check via `Symbol.PipSize`
2. Whether FxPro restricts `AccessRights.FullAccess` for email notifications — test on demo
3. DST edge cases with `TimeZones.EasternStandardTime` around March/November transitions — manual validation needed
4. `PlaceLimitOrder` overload: newer API uses `ProtectionType` enum for SL/TP as price vs pips — verify which overloads available on user's cTrader version
