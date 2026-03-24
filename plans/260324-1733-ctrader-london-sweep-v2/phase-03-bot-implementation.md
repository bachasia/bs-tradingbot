# Phase 3 — cBot Implementation

## Priority: HIGH | Status: Pending

## Overview

Build `LondonSweepBot.cs` — reads signals from Indicator, places market orders, manages positions with FTMO risk guards.

## Context Links

- [Phase 1 — Indicator Core](phase-01-indicator-core.md)
- [Plan Overview](plan.md)

## Key Design Decisions

### Market Order vs Limit Order
- **v1 used Limit orders** — risk of non-fill if price moves
- **v2 uses Market orders** — execute immediately at sweep candle close (strategy spec says "vào lệnh ngay khi nến Sweep đóng cửa")

### Risk Sizing (1% Account)
Strategy requires: "RISK 1% tài khoản (khi giá chạm SL, chỉ mất 1%)"

```csharp
double riskAmount = Account.Balance * (RiskPercent / 100.0);
double slDistance = Math.Abs(entryPrice - slPrice);
double volume = riskAmount / slDistance;
volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
```

## Architecture

```csharp
[Robot("London Sweep Bot", TimeZone = TimeZones.EasternStandardTime)]
public class LondonSweepBot : Robot
{
    // Inputs: mirror indicator params + bot-only params
    // OnStart: init indicator reference, subscribe events
    // OnBarClosed: read signals, validate, place orders
    // OnStop: cleanup positions/orders
}
```

## Requirements

### Functional
1. Reference `LondonSweepIndicator` via `Indicators.GetIndicator<T>()`
2. Read LongSignal/ShortSignal/Entry/SL/TP from indicator outputs
3. Place market order with calculated volume (1% risk)
4. Max trades per day guard (default: 1)
5. EOD cutoff at 15:00 EST — close all positions + cancel orders
6. FTMO daily loss limit guard (80% threshold warning)
7. FTMO max drawdown guard (80% threshold warning)
8. Sound alerts for signals/fills/closes

### Non-Functional
- Safe order placement with try-catch
- Logging for every action (Print)
- Works in backtesting (no sound) and live

## Related Code Files

- **Create:** `ctrader/LondonSweepBot.cs`
- **Depends on:** `ctrader/LondonSweepIndicator.cs`

## Implementation Steps

1. Create file with namespace, using directives
2. Define `[Robot]` class with `[Parameter]` inputs
   - Mirror all indicator params (session times, trade params, filters, visuals)
   - Bot-only: AutoTrade, RiskPercent, MaxTradesPerDay, EnableSound
   - FTMO: EnableFtmoGuards, DailyLossLimit, MaxDrawdown
3. `OnStart()`: init indicator, set day balance, subscribe events
4. `OnBarClosed()`:
   - Day change reset (trades count, EOD flag, day balance)
   - EOD cutoff check → close all
   - Read indicator signals (Last(1))
   - Validate signals (not NaN, prices valid)
   - Max trades guard
   - FTMO risk guards (daily loss, drawdown)
   - Calculate volume from 1% risk
   - Place market order with SL/TP in pips
5. `OnStop()`: cancel orders, close positions, unsubscribe events
6. Helpers: CancelBotPendingOrders, CloseBotPositions, PlaySound
7. Event handlers: OnPositionClosed, OnPendingOrderFilled

## Todo

- [ ] Robot class with parameter inputs
- [ ] Indicator reference initialization
- [ ] Day change reset logic
- [ ] EOD cutoff (15:00 EST)
- [ ] Signal reading from indicator
- [ ] Volume calculation (1% risk)
- [ ] Market order placement with SL/TP
- [ ] Max trades per day guard
- [ ] FTMO daily loss guard
- [ ] FTMO drawdown guard
- [ ] Position/order cleanup on stop
- [ ] Event handlers + logging
- [ ] Sound alerts

## Success Criteria

- Bot compiles with indicator reference in cTrader
- Orders placed correctly on backtest signals
- Volume matches 1% risk calculation
- FTMO guards prevent over-trading
- EOD cutoff closes all positions

## Risk Assessment

- **PipSize vs Points:** US30 in cTrader — `Symbol.PipSize` may differ from strategy's "points". Verify: SL/TP must be in pips for `ExecuteMarketOrder`. Use `slDistance / Symbol.PipSize` for conversion.
- **Volume normalization:** Must check `Symbol.VolumeInUnitsMin` and `VolumeInUnitsMax`. Round down to avoid over-sizing.
