# Phase 1 — Indicator Core Logic

## Priority: HIGH | Status: Pending

## Overview

Build `LondonSweepIndicator.cs` — the brain of the strategy. Handles all detection logic, outputs signals for the cBot to consume.

## Context Links

- [Strategy Requirements](../../london-sweep-strategy.md)
- [Plan Overview](plan.md)

## State Machine

```
Idle → London → RangeDone → PreSweepCheck → SweepWindow → InTrade → Done
                    │              │
                    │              └── (price swept 09:00-09:29) → Done
                    └── (range < 50 pts) → Done
```

7 states vs 6 in v1. New `PreSweepCheck` state handles 09:00-09:29 invalidation.

## Key Insights

- **Pre-sweep check (CRITICAL):** v1 missed this. If price already sweeps London High/Low during 09:00-09:29, no trade today.
- **Non-repainting:** All signals based on closed bars only (`Last(1)` or `prevIndex`).
- **MTF access:** `MarketData.GetBars(TimeFrame.Hour4)` and `TimeFrame.Hour` for filters.
- **EMA on MTF:** `Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, period)`.

## Architecture

```csharp
[Indicator("London Sweep | US30", IsOverlay = true,
    TimeZone = TimeZones.EasternStandardTime)]
public class LondonSweepIndicator : Indicator
{
    // Inputs: Session times, trade params, filters, visual toggles
    // Outputs: LongSignal, ShortSignal, EntryPrice, SlPrice, TpPrice
    // State: SessionState enum + daily tracking vars
    // MTF: H4 bars + EMA, H1 bars for momentum
}
```

## Requirements

### Functional
1. Track London Range (03:00-09:00 EST) — highest high, lowest low
2. Validate range >= MinRange (default 50 pts)
3. H4 trend filter: EMA20 > EMA50 = bullish, EMA20 < EMA50 = bearish
4. H1 momentum: compare close at 08:00 vs close at 03:00
5. Pre-sweep check: if price sweeps High/Low during 09:00-09:29, invalidate day
6. Sweep detection (09:30-11:00): M15 wick beyond + close back inside
7. Calculate Entry (close), SL (wick ± 8pts), TP (entry ± 0.65 × range)
8. Max 1 trade/day

### Non-Functional
- No repainting — signals on closed bars only
- Works in backtesting and live
- EST timezone handling (DST auto via cTrader TimeZones attribute)

## Related Code Files

- **Create:** `ctrader/LondonSweepIndicator.cs`

## Implementation Steps

1. Create file with namespace, using directives, SessionState enum
2. Define `[Indicator]` class with all `[Parameter]` inputs
3. Define `[Output]` data series (LongSignal, ShortSignal, Entry, SL, TP)
4. `Initialize()`: load H4/H1 bars, create EMA indicators
5. `Calculate(int index)`: implement state machine
   - New bar detection (index > lastIndex)
   - Daily reset on date change
   - Weekend guard
   - H4 trend filter (Last(1) = non-repainting)
   - London session tracking (update high/low)
   - Range validation at London close
   - H1 momentum capture (close at 03:00 and 09:00)
   - **Pre-sweep check (09:00-09:29)** — scan M15 bars for breach
   - Sweep detection (09:30-11:00) — wick + close logic
   - Entry/SL/TP calculation
   - Trade management (SL/TP/EOD simulation for backtesting visuals)
   - Write output signals

## Todo

- [ ] SessionState enum (7 states)
- [ ] Parameter inputs (session times, trade params, filters, visuals)
- [ ] Output data series
- [ ] Initialize (MTF bars, EMAs)
- [ ] Daily reset logic
- [ ] London Range tracking
- [ ] H4 trend filter
- [ ] H1 momentum filter
- [ ] Pre-sweep check (09:00-09:29) — NEW
- [ ] Sweep detection (09:30-11:00)
- [ ] Entry/SL/TP calculation
- [ ] Trade management (SL/TP/EOD)
- [ ] Output signal writing

## Success Criteria

- Indicator compiles in cTrader
- Signals match strategy rules on historical data
- Pre-sweep invalidation works correctly
- No repainting on live chart

## Risk Assessment

- **H1 close capture timing:** Must get correct H1 bar close at 03:00 and 08:00 EST. Use `_h1Bars.ClosePrices` indexed correctly.
- **Pre-sweep detection:** Need to check ALL M15 bars in 09:00-09:29, not just the last one. Iterate backward from current index.
