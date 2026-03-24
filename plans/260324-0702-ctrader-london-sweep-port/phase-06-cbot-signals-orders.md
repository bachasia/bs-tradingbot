# Phase 6: cBot — Reference Indicator, Read Signals, Pending Order Placement

## Context Links
- [Phase 4: Sweep Detection & Outputs](./phase-04-sweep-detection.md)
- [Phase 1: cBot Boilerplate](./phase-01-project-setup.md)
- [Brainstorm — Pending Order Flow](../../plans/reports/brainstorm-260323-2330-ctrader-london-sweep-port.md) (lines 96-104)
- [cTrader: Use Custom Indicators in cBots](https://help.ctrader.com/ctrader-algo/how-tos/indicators/use-custom-indicators-in-cbots/)
- [cTrader Robot API — PlaceLimitOrder](https://help.ctrader.com/ctrader-algo/references/General/Robot/)
- [cTrader cBot Trading Operations](https://help.ctrader.com/ctrader-algo/how-tos/cbots/cbot-trading-operations/)
- [cTrader Bar Events Guide](https://help.ctrader.com/ctrader-algo/guides/bar-events/)

## Overview
- **Priority**: P1 — this is the execution layer
- **Status**: complete
- **Effort**: 1.5h
- **Description**: Implement cBot's `OnStart()` to reference the indicator via `GetIndicator<>()`, `OnBarClosed()` to read signal outputs, and pending limit order placement with SL/TP. Includes volume validation and signal-to-order mapping.

## Key Insights

### GetIndicator<T>() — Parameter Passing
- `Indicators.GetIndicator<LondonSweepIndicator>(param1, param2, ...)` passes params **in declaration order** from the indicator class
- Every `[Parameter]` in the indicator must have a matching value passed from the cBot
- Missing or mis-ordered params → compile error or wrong values
- The cBot duplicates indicator params so the user sets them once in the cBot UI, and the cBot forwards them

### OnBarClosed() — Confirmed Bar Logic
- `OnBarClosed()` fires when the last bar completes (NOT when a new bar opens — subtle but important)
- Inside `OnBarClosed()`, `Bars.ClosePrices.Last(1)` = the bar that just closed
- The indicator's `[Output]` values at `Last(1)` correspond to the same closed bar
- This is the cBot equivalent of Pine's `barstate.isconfirmed` — no repainting

### PlaceLimitOrder Overloads
The cBot places a **pending limit order** (not market order) so the user can review before fill.

Recommended overload (pips-based SL/TP):
```csharp
PlaceLimitOrder(TradeType tradeType, string symbolName, double volume,
    double targetPrice, string label, double? stopLossPips, double? takeProfitPips)
```

**Problem**: SL/TP from indicator are in absolute price, but this overload wants pips.
**Solution**: Convert price distance to pips: `slPips = Math.Abs(entryPrice - slPrice) / Symbol.PipSize`

Alternative (newer API with ProtectionType):
```csharp
PlaceLimitOrder(TradeType.Buy, SymbolName, volume, entryPrice, label,
    slDistance, tpDistance, ProtectionType.Price)
```
Need to verify which overloads are available on user's cTrader version.

### Volume Units
- cTrader uses **volume in units**, not lots
- `Symbol.VolumeInUnitsMin` = minimum allowed volume
- `Symbol.NormalizeVolumeInUnits(volume)` = rounds to valid step
- User's `TradeVolume` parameter should be in units (e.g., 1.0 for US30 = 1 contract)

## Requirements

### Functional
- On start: initialize indicator reference with all params forwarded
- On each bar close: check if indicator fired a long or short signal
- If signal detected AND `AutoTrade` enabled:
  - Validate volume against symbol minimums
  - Place pending limit order at signal's entry price
  - Set SL and TP on the order
  - Log order details to cTrader log
  - Play notification sound
- If signal detected AND `AutoTrade` disabled:
  - Log signal details only
  - Play notification sound (alert-only mode)
- Respect `MaxTradesPerDay` limit
- Track trades per day, reset on new day

### Non-Functional
- All order placement wrapped in try/catch for error handling
- Log every decision (signal found, order placed, order skipped) for transparency
- Use `_botLabel` as order label for easy identification in cTrader trade log

## Architecture

```
OnStart()
├── Initialize indicator: _indicator = GetIndicator<LondonSweepIndicator>(all params)
├── Validate symbol: Print(Symbol.Name, Symbol.PipSize, Symbol.VolumeInUnitsMin)
└── Reset _tradesToday = 0

OnBarClosed()
├── Day change check → reset _tradesToday
├── Read indicator outputs:
│   ├── longSignal  = _indicator.LongSignal.Last(1) > 0.5
│   ├── shortSignal = _indicator.ShortSignal.Last(1) > 0.5
│   ├── entryPrice  = _indicator.EntryPriceOutput.Last(1)
│   ├── slPrice     = _indicator.SlPriceOutput.Last(1)
│   └── tpPrice     = _indicator.TpPriceOutput.Last(1)
├── Guard: no signal → return
├── Guard: _tradesToday >= MaxTradesPerDay → log skip, return
├── Guard: !AutoTrade → log signal, play sound, return
├── Validate volume
├── Compute SL/TP in pips: slPips = |entry - sl| / Symbol.PipSize
├── PlaceLimitOrder(tradeType, SymbolName, volume, entry, label, slPips, tpPips)
├── Log result
├── Play sound
└── _tradesToday++
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepBot.cs` — `OnStart()`, `OnBarClosed()`

### Files to Reference
- `ctrader/LondonSweepIndicator.cs` — `[Parameter]` declaration order, `[Output]` names

## Implementation Steps

### Step 1: Add `using` directives and indicator reference

Ensure top of file has:
```csharp
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;  // Namespace of LondonSweepIndicator
```

Add reference management note as comment:
```csharp
// IMPORTANT: In cTrader Automate IDE, click "Manage References" (top toolbar)
// and add a reference to LondonSweepIndicator. Without this, the cBot
// can't find the indicator class and will fail to compile.
```

### Step 2: Implement OnStart() — indicator initialization

```csharp
protected override void OnStart()
{
    // ─── INITIALIZE INDICATOR REFERENCE ───────────────────────────────
    // GetIndicator<T>() creates an instance of the indicator and links it
    // to this cBot. Parameters are passed in the EXACT ORDER they are
    // declared as [Parameter] in LondonSweepIndicator.cs.
    //
    // If you add/remove/reorder indicator parameters, you MUST update
    // this call to match.
    _indicator = Indicators.GetIndicator<LondonSweepIndicator>(
        // Session Times (Group 1)
        LondonStartHHMM,       // London Start (HHMM)
        LondonEndHHMM,         // London End (HHMM)
        SweepStartHHMM,        // Sweep Window Start (HHMM)
        SweepEndHHMM,          // Sweep Window End (HHMM)
        EodCutoffHHMM,         // EOD Cutoff (HHMM)
        // Trade Parameters (Group 2)
        MinRange,              // Min London Range (pts)
        SweepThreshold,        // Sweep Threshold (pts)
        SlBuffer,              // SL Buffer (pts)
        TpMultiplier,          // TP Multiplier (x range)
        // Filters (Group 3)
        H4EmaFastPeriod,       // H4 EMA Fast Period
        H4EmaSlowPeriod,       // H4 EMA Slow Period
        UseH4Filter,           // Enable H4 Trend Filter
        UseH1Filter,           // Enable H1 Momentum Filter
        // Visual (Group 4)
        ShowDashboard,         // Show Dashboard
        ShowTradeLines         // Show Trade Lines
    );

    // ─── LOG SYMBOL INFO ──────────────────────────────────────────────
    // Print symbol details so the user can verify they're on the right instrument.
    Print("London Sweep Bot started on {0}", Symbol.Name);
    Print("  Pip Size: {0}", Symbol.PipSize);
    Print("  Tick Size: {0}", Symbol.TickSize);
    Print("  Min Volume: {0} units", Symbol.VolumeInUnitsMin);
    Print("  Auto-Trade: {0}", AutoTrade ? "ENABLED" : "DISABLED (alerts only)");

    // Reset daily counter
    _tradesToday = 0;
    _lastDate = DateTime.MinValue;
}
```

### Step 3: Implement OnBarClosed() — signal reading and order placement

```csharp
protected override void OnBarClosed()
{
    // ─── DAY CHANGE RESET ─────────────────────────────────────────────
    DateTime barDate = Bars.OpenTimes.Last(1).Date;
    if (barDate != _lastDate)
    {
        _lastDate = barDate;
        _tradesToday = 0;
    }

    // ─── READ INDICATOR SIGNALS ───────────────────────────────────────
    // The indicator writes to [Output] data series on the bar it detected
    // the sweep. We read Last(1) = the bar that just closed = the bar
    // the indicator just processed.
    //
    // Convention: 1.0 = signal, NaN = no signal.
    // We check > 0.5 to handle floating-point comparison safely.
    double longVal  = _indicator.LongSignal.Last(1);
    double shortVal = _indicator.ShortSignal.Last(1);

    bool isLongSignal  = !double.IsNaN(longVal) && longVal > 0.5;
    bool isShortSignal = !double.IsNaN(shortVal) && shortVal > 0.5;

    // No signal this bar — nothing to do
    if (!isLongSignal && !isShortSignal)
        return;

    // ─── READ TRADE LEVELS ────────────────────────────────────────────
    double entryPrice = _indicator.EntryPriceOutput.Last(1);
    double slPrice    = _indicator.SlPriceOutput.Last(1);
    double tpPrice    = _indicator.TpPriceOutput.Last(1);

    // Validate prices are not NaN
    if (double.IsNaN(entryPrice) || double.IsNaN(slPrice) || double.IsNaN(tpPrice))
    {
        Print("WARNING: Signal detected but prices are NaN — skipping.");
        return;
    }

    TradeType tradeType = isLongSignal ? TradeType.Buy : TradeType.Sell;
    string directionStr = isLongSignal ? "LONG" : "SHORT";

    // ─── LOG SIGNAL ───────────────────────────────────────────────────
    Print("=== SIGNAL: {0} ===", directionStr);
    Print("  Entry: {0:F1}  SL: {1:F1}  TP: {2:F1}", entryPrice, slPrice, tpPrice);

    // ─── MAX TRADES CHECK ─────────────────────────────────────────────
    if (_tradesToday >= MaxTradesPerDay)
    {
        Print("  SKIPPED: Max trades per day reached ({0})", MaxTradesPerDay);
        if (EnableSound)
            Notifications.PlaySound(SoundType.Buzz);
        return;
    }

    // ─── SOUND ALERT ──────────────────────────────────────────────────
    // Always play sound on signal (even if AutoTrade is off).
    if (EnableSound)
        Notifications.PlaySound(SoundType.Good);

    // ─── ALERT-ONLY MODE ──────────────────────────────────────────────
    if (!AutoTrade)
    {
        Print("  Auto-Trade OFF — signal logged, no order placed.");
        Print("  To place order manually: {0} at {1:F1}, SL={2:F1}, TP={3:F1}",
            directionStr, entryPrice, slPrice, tpPrice);
        return;
    }

    // ─── PLACE PENDING LIMIT ORDER ────────────────────────────────────
    // We place a LIMIT order (not market order) so the user can review
    // and cancel before it fills.
    //
    // SL/TP must be converted from absolute price to pips distance.
    // Pips = |priceDistance| / Symbol.PipSize
    double slPips = Math.Abs(entryPrice - slPrice) / Symbol.PipSize;
    double tpPips = Math.Abs(tpPrice - entryPrice) / Symbol.PipSize;

    // Normalize volume to valid step for this symbol
    double volume = Symbol.NormalizeVolumeInUnits(TradeVolume,
        RoundingMode.ToNearest);

    // Validate volume meets minimum
    if (volume < Symbol.VolumeInUnitsMin)
    {
        Print("  ERROR: Volume {0} below minimum {1}", volume, Symbol.VolumeInUnitsMin);
        return;
    }

    try
    {
        string label = _botLabel + "_" + directionStr;
        TradeResult result = PlaceLimitOrder(
            tradeType,
            SymbolName,
            volume,
            entryPrice,
            label,
            slPips,
            tpPips
        );

        if (result.IsSuccessful)
        {
            Print("  ORDER PLACED: {0} Limit at {1:F1}, Vol={2}, SL={3:F1}pips, TP={4:F1}pips",
                directionStr, entryPrice, volume, slPips, tpPips);
            _tradesToday++;
        }
        else
        {
            Print("  ORDER FAILED: {0}", result.Error);
        }
    }
    catch (Exception ex)
    {
        Print("  EXCEPTION placing order: {0}", ex.Message);
    }
}
```

### Step 4: Handle indicator output timing edge case

The indicator writes to `prevIndex` (the closed bar), and the cBot reads `Last(1)` (also the closed bar). These should align because:
- Indicator `Calculate(index)` fires, writes signal to `index - 1`
- cBot `OnBarClosed()` fires for the same bar close
- `_indicator.LongSignal.Last(1)` reads the same index the indicator just wrote to

**Verification needed**: In testing, add a debug print comparing bar times:
```csharp
Print("  cBot bar time: {0}", Bars.OpenTimes.Last(1));
```
And in indicator: `Print("  Indicator signal at: {0}", Bars.OpenTimes[prevIndex]);`
Both should match.

### Step 5: Add Manage References instructions as code comment

```csharp
// ══════════════════════════════════════════════════════════════════════════
// SETUP INSTRUCTIONS:
// 1. Open cTrader Desktop → Automate tab
// 2. Create new cBot → paste this code
// 3. Click "Manage References" in the top toolbar
// 4. Check the box next to "LondonSweepIndicator"
// 5. Build the cBot (Ctrl+B)
// 6. Attach to US30 M15 chart
// 7. Set parameters in the dialog
// 8. Start the cBot
// ══════════════════════════════════════════════════════════════════════════
```

## Todo List

- [ ] Add `using cAlgo.Indicators;` to cBot file
- [ ] Implement `OnStart()` with `GetIndicator<>()` call (correct param order)
- [ ] Log symbol info (PipSize, TickSize, VolumeInUnitsMin) on start
- [ ] Implement `OnBarClosed()` with signal reading via `Last(1)`
- [ ] Add NaN validation on all indicator output reads
- [ ] Implement max trades per day guard
- [ ] Implement alert-only mode (sound + log, no order)
- [ ] Compute SL/TP in pips from absolute prices
- [ ] Validate and normalize volume
- [ ] Place pending limit order with try/catch
- [ ] Log all decisions (signal, skip reason, order result)
- [ ] Play notification sound on signal
- [ ] Add setup instructions comment block
- [ ] Verify indicator output timing alignment in test

## Success Criteria
- cBot compiles with indicator reference
- Signal detection: cBot logs signal on the same bar the indicator detects a sweep
- Order placement: Pending limit order appears in cTrader Pending Orders tab
- SL/TP values on order match indicator's computed levels
- Alert-only mode: No order placed, sound plays, log shows signal details
- Max trades: Second signal in same day is skipped with log message
- Volume: Validated against symbol minimum, rounded to valid step

## Risk Assessment
- **GetIndicator param order**: If indicator params are reordered, cBot breaks silently (wrong values, not compile error). Mitigation: document param order in both files with matching comments. Consider a future refactor to use a shared config class
- **PlaceLimitOrder overload**: The pips-based overload may be marked obsolete in newer cTrader. If compile warns, switch to `ProtectionType.Pips` overload. Test on actual cTrader IDE
- **SL/TP pips rounding**: `slPips` could be fractional — `PlaceLimitOrder` should handle it, but verify. Some symbols require integer pips — `US30` likely accepts fractional
- **Limit order rejection**: FTMO may reject limit orders if entry price is too far from current market. This is by design — the limit order IS the confirmation mechanism. If market moves away, order won't fill = user protected
- **Timing gap**: Between indicator signal and cBot order placement, market may have moved. Since this is a pending limit order (not market), the entry price is fixed — order fills only if price returns to that level

## Security Considerations
- `AccessRights.None` sufficient for order placement (trading API is always available to cBots)
- No network access needed for basic operation
- Order label contains strategy name for audit trail

## Next Steps
- Phase 7: Trade management — EOD cutoff, pending order cancellation, notifications, OnStop cleanup
