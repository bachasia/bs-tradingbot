# Phase 4: Sweep Detection Logic & Output Properties for cBot

## Context Links
- [Phase 3: MTF Filters](./phase-03-mtf-filters.md)
- [PineScript Source — Sweep Detection](../../london-sweep-indicator.pine) (lines 172-208)
- [PineScript Source — Trade Visuals](../../london-sweep-indicator.pine) (lines 209-226)
- [PineScript Source — Trade Management](../../london-sweep-indicator.pine) (lines 227-278)
- [cTrader OutputAttribute API](https://help.ctrader.com/ctrader-algo/references/Attributes/OutputAttribute/)
- [cTrader Chart Objects Guide](https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/)

## Overview
- **Priority**: P1 — core strategy logic + cBot interface
- **Status**: pending
- **Effort**: 1.5h
- **Description**: Implement sweep detection during the NY sweep window (09:30-11:00 EST), trade level computation (entry/SL/TP), trade management (SL hit, TP hit, EOD close), trade level visuals, and populate `[Output]` data series so the cBot can read signals.

## Key Insights

### Sweep Logic (from Pine)
A **long sweep** occurs when:
1. State is `SweepWindow` and `barstate.isconfirmed` (bar just closed)
2. Not already traded today
3. Long filters pass (`_longAllowed`)
4. Wick dips below London low by at least `SweepThreshold` points: `londonLow - low >= SweepThreshold`
5. But candle closes back above London low: `close > londonLow`
6. Entry = close, SL = low - SLBuffer, TP = close + (TPMultiplier * rangeSize)

A **short sweep** is the mirror: wick above London high, close below, only if long sweep didn't fire on same bar.

### Trade Management (Indicator-Side)
The Pine indicator tracks SL/TP/EOD resolution itself (for visuals and dashboard). The cBot will independently manage the actual order/position. The indicator's trade tracking is purely for:
- Drawing entry/SL/TP lines on chart
- Updating dashboard status
- Setting state to `Done` when trade resolves

### Output Properties for cBot Communication
- `[Output]` in cTrader must be `IndicatorDataSeries` (indexed by bar)
- Signal convention: `1.0` = signal active, `0.0` = no signal
- Price outputs: actual price values (`double.NaN` when no signal)
- cBot reads these via `_indicator.LongSignal.Last(1)` to check the last closed bar

### Important: Output Must Be Written Every Bar
- `IndicatorDataSeries` is indexed — you must set `[index]` or `[prevIndex]` each bar
- Default value is `double.NaN` — so only write `1.0` on signal bars, leave rest as NaN

## Requirements

### Functional
- Enter sweep window when: state is `RangeDone`, bar is in sweep session, range >= `MinRange`
- Exit sweep window: state is `SweepWindow` but bar is outside sweep session → state = `Done`
- Long sweep detection: wick below London low + close above + filters pass
- Short sweep detection: wick above London high + close below + filters pass (only if no long sweep)
- Compute entry/SL/TP prices on sweep detection
- Track SL hit, TP hit, EOD close on subsequent bars
- Draw entry/SL/TP lines and labels on sweep detection
- Stop extending lines when trade resolves
- Populate `[Output]` data series on sweep bar

### Non-Functional
- One trade per day max (`_tradedToday` flag)
- SL checked before TP (conservative bias — matches Pine)
- EOD check: first bar at or after `EodCutoffHHMM`

## Architecture

```
Calculate(int index) — sweep detection section
├── Sweep Window Entry
│   └── RangeDone + inSweepWindow + rangeValid → state = SweepWindow
├── Sweep Window Exit
│   └── SweepWindow + !inSweepWindow → state = Done
├── Long Sweep Check
│   ├── _longAllowed
│   ├── sweepDepth = _londonLow - low >= SweepThreshold
│   ├── close > _londonLow
│   └── → Set entry/SL/TP, state = InTrade, tradedToday = true
├── Short Sweep Check (only if no long)
│   ├── _shortAllowed
│   ├── sweepHeight = high - _londonHigh >= SweepThreshold
│   ├── close < _londonHigh
│   └── → Set entry/SL/TP, state = InTrade, tradedToday = true
├── Trade Management
│   ├── SL hit check (conservative: check SL first)
│   ├── TP hit check
│   ├── EOD cutoff check
│   └── → state = Done, set _tradeResult
├── Trade Visuals (entry/SL/TP lines)
│   ├── Draw on sweep detection
│   └── Stop extending on resolution
└── Output Population
    ├── LongSignal[prevIndex] = 1.0 on long sweep bar
    ├── ShortSignal[prevIndex] = 1.0 on short sweep bar
    ├── EntryPriceOutput[prevIndex] = _entryPrice
    ├── SlPriceOutput[prevIndex] = _slPrice
    └── TpPriceOutput[prevIndex] = _tpPrice
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepIndicator.cs` — `Calculate()` method (sweep detection, trade mgmt, outputs)

### Files to Reference
- `london-sweep-indicator.pine` (lines 172-278)

## Implementation Steps

### Step 1: Implement sweep window state transitions

Add after the London session end block in `Calculate()`:

```csharp
    // ─── SWEEP WINDOW ENTRY ──────────────────────────────────────────
    // After London range is established, wait for NY sweep window.
    // Range must be >= MinRange to be tradeable.
    //
    // Pine equivalent:
    //   if state == STATE_RANGE_DONE and inSweepWindow and rangeValid
    //       state := STATE_SWEEP_WINDOW
    bool inSweepWindow = IsInSession(hhmm, SweepStartHHMM, SweepEndHHMM);
    bool rangeValid = !double.IsNaN(_rangeSize) && _rangeSize >= MinRange;

    if (_state == SessionState.RangeDone && inSweepWindow && rangeValid)
    {
        _state = SessionState.SweepWindow;
    }

    // ─── SWEEP WINDOW EXIT (no signal found) ─────────────────────────
    if (_state == SessionState.SweepWindow && !inSweepWindow)
    {
        _state = SessionState.Done;
    }
```

### Step 2: Implement long sweep detection

```csharp
    // ─── SWEEP DETECTION ──────────────────────────────────────────────
    // Look for liquidity sweeps: price wicks beyond London range
    // then closes back inside. This traps breakout traders.
    //
    // All checks run on the PREVIOUS CONFIRMED bar (prevIndex).
    bool longSweep = false;
    bool shortSweep = false;

    if (_state == SessionState.SweepWindow && !_tradedToday)
    {
        double prevHigh  = Bars.HighPrices[prevIndex];
        double prevLow   = Bars.LowPrices[prevIndex];
        double prevClose = Bars.ClosePrices[prevIndex];

        // ─── LONG SWEEP ──────────────────────────────────────────
        // Wick dips BELOW London low (sweeps sell-side liquidity)
        // but candle CLOSES BACK ABOVE London low (rejection).
        //
        // Pine equivalent:
        //   sweepDepth = londonLow - low
        //   if sweepDepth >= i_sweepThresh and close > londonLow
        if (_longAllowed)
        {
            double sweepDepth = _londonLow - prevLow;
            if (sweepDepth >= SweepThreshold && prevClose > _londonLow)
            {
                longSweep = true;
                _tradeDir = 1;
                _state = SessionState.InTrade;
                _tradedToday = true;
                _entryPrice = prevClose;
                _slPrice = prevLow - SlBuffer;
                _tpPrice = prevClose + (TpMultiplier * _rangeSize);
            }
        }

        // ─── SHORT SWEEP ─────────────────────────────────────────
        // Wick pokes ABOVE London high (sweeps buy-side liquidity)
        // but candle CLOSES BACK BELOW London high.
        // Only checked if no long sweep on this bar.
        //
        // Pine equivalent:
        //   sweepHeight = high - londonHigh
        //   if sweepHeight >= i_sweepThresh and close < londonHigh
        if (_shortAllowed && !longSweep)
        {
            double sweepHeight = prevHigh - _londonHigh;
            if (sweepHeight >= SweepThreshold && prevClose < _londonHigh)
            {
                shortSweep = true;
                _tradeDir = -1;
                _state = SessionState.InTrade;
                _tradedToday = true;
                _entryPrice = prevClose;
                _slPrice = prevHigh + SlBuffer;
                _tpPrice = prevClose - (TpMultiplier * _rangeSize);
            }
        }
    }
```

### Step 3: Implement trade level visuals

```csharp
    // ─── TRADE LEVEL VISUALS ──────────────────────────────────────────
    // Draw entry, SL, and TP lines when a sweep signal fires.
    if ((longSweep || shortSweep) && ShowTradeLines)
    {
        string suffix = "_" + _dayCount;
        Color entryColor = Color.DodgerBlue;
        Color slColor    = Color.Red;
        Color tpColor    = Color.Green;

        // Entry line (dashed blue)
        var eLine = Chart.DrawTrendLine("Entry" + suffix,
            prevIndex, _entryPrice, prevIndex + 50, _entryPrice, entryColor);
        eLine.LineStyle = LineStyle.Lines;    // dashed
        eLine.Thickness = 2;

        // SL line (dashed red)
        var sLine = Chart.DrawTrendLine("SL" + suffix,
            prevIndex, _slPrice, prevIndex + 50, _slPrice, slColor);
        sLine.LineStyle = LineStyle.Lines;
        sLine.Thickness = 2;

        // TP line (dashed green)
        var tLine = Chart.DrawTrendLine("TP" + suffix,
            prevIndex, _tpPrice, prevIndex + 50, _tpPrice, tpColor);
        tLine.LineStyle = LineStyle.Lines;
        tLine.Thickness = 2;

        // Labels
        Chart.DrawText("EntryLbl" + suffix,
            "Entry: " + _entryPrice.ToString("F1"),
            prevIndex + 5, _entryPrice, entryColor);
        Chart.DrawText("SLLbl" + suffix,
            "SL: " + _slPrice.ToString("F1"),
            prevIndex + 5, _slPrice, slColor);
        Chart.DrawText("TPLbl" + suffix,
            "TP: " + _tpPrice.ToString("F1"),
            prevIndex + 5, _tpPrice, tpColor);
    }
```

### Step 4: Implement trade management (SL/TP/EOD)

```csharp
    // ─── TRADE MANAGEMENT ─────────────────────────────────────────────
    // Track SL/TP/EOD resolution for visuals and dashboard.
    // The actual position is managed by the cBot — this is display only.
    //
    // SL checked FIRST (conservative bias): if a gap bar breaches both
    // SL and TP, assume SL was hit first.
    bool slHit = false;
    bool tpHit = false;
    bool eodClose = false;

    if (_state == SessionState.InTrade)
    {
        double prevHigh  = Bars.HighPrices[prevIndex];
        double prevLow   = Bars.LowPrices[prevIndex];

        if (_tradeDir == 1)  // Long trade
        {
            if (prevLow <= _slPrice)
                slHit = true;
            else if (prevHigh >= _tpPrice)
                tpHit = true;
        }
        else if (_tradeDir == -1)  // Short trade
        {
            if (prevHigh >= _slPrice)
                slHit = true;
            else if (prevLow <= _tpPrice)
                tpHit = true;
        }

        // ─── EOD CUTOFF ──────────────────────────────────────────
        // Close trade at end of day if SL/TP not yet hit.
        // Pine equivalent: isEOD = eodTime and not eodTime[1]
        bool isEOD = hhmm >= EodCutoffHHMM;
        if (isEOD && !slHit && !tpHit)
            eodClose = true;

        // ─── RESOLVE TRADE ───────────────────────────────────────
        if (slHit)
        {
            _tradeResult = "SL Hit";
            _state = SessionState.Done;
        }
        else if (tpHit)
        {
            _tradeResult = "TP Hit";
            _state = SessionState.Done;
        }
        else if (eodClose)
        {
            _tradeResult = "EOD Close";
            _state = SessionState.Done;
        }
    }

    // ─── STOP EXTENDING LINES ON TRADE RESOLUTION ─────────────────
    // When trade resolves, cap the lines at the current bar
    // instead of letting them extend to the right forever.
    if (slHit || tpHit || eodClose)
    {
        string suffix = "_" + _dayCount;
        // Update trend line right endpoints to current bar
        var eLine = Chart.Objects.FirstOrDefault(o => o.Name == "Entry" + suffix) as ChartTrendLine;
        if (eLine != null) { eLine.Time2 = Bars.OpenTimes[prevIndex]; }

        var sLine = Chart.Objects.FirstOrDefault(o => o.Name == "SL" + suffix) as ChartTrendLine;
        if (sLine != null) { sLine.Time2 = Bars.OpenTimes[prevIndex]; }

        var tLine = Chart.Objects.FirstOrDefault(o => o.Name == "TP" + suffix) as ChartTrendLine;
        if (tLine != null) { tLine.Time2 = Bars.OpenTimes[prevIndex]; }

        // Also cap London level lines
        var hiLine = Chart.Objects.FirstOrDefault(o => o.Name == "LondonHi_" + _dayCount) as ChartHorizontalLine;
        if (hiLine != null) Chart.RemoveObject("LondonHi_" + _dayCount);

        var loLine = Chart.Objects.FirstOrDefault(o => o.Name == "LondonLo_" + _dayCount) as ChartHorizontalLine;
        if (loLine != null) Chart.RemoveObject("LondonLo_" + _dayCount);
    }
```

**Note on line capping**: `DrawHorizontalLine` extends infinitely and can't be capped. Alternative: switch London level lines to `DrawTrendLine` with explicit right endpoint, or simply remove them on trade resolution. The implementation above removes them — implementer can adjust.

### Step 5: Populate output data series

```csharp
    // ─── OUTPUT PROPERTIES (for cBot) ─────────────────────────────────
    // Write signal data to IndicatorDataSeries so the cBot can read them.
    // Convention: 1.0 = signal active, NaN = no signal.
    // The cBot will check: _indicator.LongSignal.Last(1) > 0.5
    //
    // We write to prevIndex because that's the confirmed bar we just analyzed.
    LongSignal[prevIndex]      = longSweep ? 1.0 : double.NaN;
    ShortSignal[prevIndex]     = shortSweep ? 1.0 : double.NaN;
    EntryPriceOutput[prevIndex] = (longSweep || shortSweep) ? _entryPrice : double.NaN;
    SlPriceOutput[prevIndex]   = (longSweep || shortSweep) ? _slPrice : double.NaN;
    TpPriceOutput[prevIndex]   = (longSweep || shortSweep) ? _tpPrice : double.NaN;
```

### Step 6: Handle edge case — writing default output for non-signal bars

The `IndicatorDataSeries` default is `double.NaN`, so non-signal bars automatically have NaN. No explicit write needed for non-signal bars. But confirm this works in practice during testing.

## Todo List

- [ ] Implement sweep window entry transition (RangeDone → SweepWindow)
- [ ] Implement sweep window exit (SweepWindow → Done)
- [ ] Implement long sweep detection with filter gate
- [ ] Implement short sweep detection (only if no long)
- [ ] Compute entry/SL/TP prices on sweep
- [ ] Draw entry/SL/TP trend lines with labels
- [ ] Implement SL hit detection (check first for conservative bias)
- [ ] Implement TP hit detection
- [ ] Implement EOD cutoff detection
- [ ] Resolve trade: set `_tradeResult`, transition to Done
- [ ] Cap/remove lines on trade resolution
- [ ] Populate all 5 `[Output]` data series on signal bar
- [ ] Verify output values readable from test cBot via `GetIndicator<>()`

## Success Criteria
- Sweep signals fire on the same bars as the TradingView Pine indicator
- Entry/SL/TP prices match Pine values exactly (same close, same low/high ± buffer)
- Trade resolves correctly (SL, TP, or EOD) matching Pine behavior
- `[Output]` data series contain correct values on signal bars and NaN on others
- Only one trade per day
- Long sweep prioritized over short on same bar (matching Pine)

## Risk Assessment
- **Output indexing**: Writing to `prevIndex` instead of `index` is correct since we're processing the closed bar — but verify cBot reads `Last(1)` not `Last(0)` to match
- **Chart.Objects.FirstOrDefault**: Requires `System.Linq` — ensure `using System.Linq;` is in file
- **Line capping vs removal**: Horizontal lines can't be capped — plan uses removal. If user prefers visual continuity, switch London levels to TrendLines from the start (Phase 2 adjustment)
- **Float precision**: `close > londonLow` comparison could fail if close == londonLow exactly — matches Pine behavior (Pine also uses strict `>`)

## Security Considerations
- No execution happens in indicator — signals only
- Output properties are read-only from cBot's perspective

## Next Steps
- Phase 5: Dashboard visuals (status panel)
- Phase 6: cBot reads output properties, places pending orders
