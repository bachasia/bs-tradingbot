# Phase 3: MTF Filters (H4 EMA Trend + H1 Momentum), Non-Repainting

## Context Links
- [Phase 2: Session & Range](./phase-02-session-and-range.md)
- [PineScript Source — MTF Data](../../london-sweep-indicator.pine) (lines 47-63)
- [PineScript Source — Filter Evaluation](../../london-sweep-indicator.pine) (lines 159-171)
- [Researcher Report — MTF Patterns](../../plans/reports/researcher-260323-2330-ctrader-automation-research.md) (section 3)
- [cTrader MTF Strategies Guide](https://help.ctrader.com/ctrader-algo/how-tos/cbots/code-multitimeframe-strategies/)
- [cTrader MarketData API](https://help.ctrader.com/ctrader-algo/references/MarketData/MarketData/)
- [ExponentialMovingAverage API](https://help.ctrader.com/ctrader-algo/references/Indicators/ExponentialMovingAverage/)

## Overview
- **Priority**: P1 — filters gate all sweep signals
- **Status**: pending
- **Effort**: 1.5h
- **Description**: Implement H4 EMA dual crossover trend filter and H1 close-based momentum filter using cTrader's MTF API. Ensure non-repainting via `Last(1)` / index mapping to confirmed bars only.

## Key Insights

### Pine MTF vs cTrader MTF — Critical Difference

**Pine approach** (non-repainting):
```pine
[h4EmaFast, h4EmaSlow] = request.security(syminfo.tickerid, "240",
    [ta.ema(close, 20)[1], ta.ema(close, i_h4EmaSlow)[1]],
    lookahead = barmerge.lookahead_on)
```
- `[1]` offset + `lookahead_on` = returns last **confirmed** H4 bar's EMA value
- No repainting because it never uses the current (forming) H4 bar

**cTrader approach** (equivalent non-repainting):
```csharp
var h4Bars = MarketData.GetBars(TimeFrame.Hour4);
var h4EmaFast = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 20);
// Use Last(1) to skip the current forming bar
double h4FastValue = h4EmaFast.Result.Last(1);
```
- `Result.Last(1)` = previous completed H4 bar's EMA value
- `Result.Last(0)` = current forming bar's value (would repaint — avoid)

### H1 Momentum Logic
- Pine captures `h1Close` at London start (03:00) and London end (09:00) using `request.security("60", close[1])`
- If 09:00 close > 03:00 close → bullish momentum
- In cTrader: use `_h1Bars.ClosePrices.Last(1)` at the relevant times
- The `[1]` offset means we get the **previous confirmed** H1 bar's close

### Index Mapping Between Timeframes
- When inside indicator's `Calculate(int index)`, the `index` is on the chart timeframe (M15)
- To find corresponding H4/H1 bar: `_h4Bars.OpenTimes.GetIndexByTime(Bars.OpenTimes[index])`
- This returns the H4 bar index whose open time is nearest to the M15 bar's open time

## Requirements

### Functional
- H4 EMA fast/slow crossover: bullish when fast > slow, bearish when fast < slow
- H1 momentum: positive when 09:00 close > 03:00 close, negative when opposite
- Both filters toggleable via `UseH4Filter` / `UseH1Filter` parameters
- When filter disabled, it always passes (returns `true`)
- Filter values captured at specific times: H1 close at London start and London end
- Non-repainting: all MTF values from confirmed (closed) bars only

### Non-Functional
- MTF bars initialized once in `Initialize()`, not recreated each Calculate call
- EMA indicators created once, auto-update as new bars arrive
- Handle edge case: if H4/H1 bars not yet loaded (early history), filter defaults to neutral

## Architecture

```
Initialize()
├── _h4Bars = MarketData.GetBars(TimeFrame.Hour4)
├── _h1Bars = MarketData.GetBars(TimeFrame.Hour)
├── _h4EmaFast = Indicators.EMA(_h4Bars.ClosePrices, FastPeriod)
└── _h4EmaSlow = Indicators.EMA(_h4Bars.ClosePrices, SlowPeriod)

Calculate(int index) — filter evaluation section
├── Get H4 EMA values via Last(1) on confirmed bar
├── h4Bullish = h4FastValue > h4SlowValue
├── Capture H1 close at London start → _h1Close0300
├── Capture H1 close at London end → _h1Close0900
├── momentumPositive = _h1Close0900 > _h1Close0300
├── Apply toggle: trendLong = UseH4Filter ? h4Bullish : true
└── longAllowed = trendLong && momLong
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepIndicator.cs` — `Initialize()` and `Calculate()` methods

### Files to Reference
- `london-sweep-indicator.pine` (lines 47-63, 159-171)

## Implementation Steps

### Step 1: Verify MTF bars initialization (already stubbed in Phase 2)

In `Initialize()`, confirm these are present:
```csharp
protected override void Initialize()
{
    // ─── MULTI-TIMEFRAME DATA ─────────────────────────────────────────
    // Get H4 and H1 bar series for higher-timeframe analysis.
    // MarketData.GetBars() returns a Bars object with OHLC + time data.
    // cTrader auto-loads historical bars and keeps them updated live.
    _h4Bars = MarketData.GetBars(TimeFrame.Hour4);
    _h1Bars = MarketData.GetBars(TimeFrame.Hour);

    // ─── H4 EMA INDICATORS ───────────────────────────────────────────
    // Create EMA indicators on H4 close prices.
    // These auto-calculate on every new H4 bar — we just read Result.
    _h4EmaFast = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaFastPeriod);
    _h4EmaSlow = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaSlowPeriod);
}
```

### Step 2: Add filter state fields

Ensure these fields exist (from Phase 1):
```csharp
// Filter evaluation results (recalculated each bar)
private bool _h4Bullish = false;
private bool _h4Bearish = false;
private bool _longAllowed = false;
private bool _shortAllowed = false;
```

### Step 3: Implement H4 EMA filter evaluation in Calculate()

Add this section after the daily reset and session detection code:

```csharp
    // ─── H4 TREND FILTER (non-repainting) ────────────────────────────
    // Read the PREVIOUS CONFIRMED H4 bar's EMA values.
    // Last(1) = one bar back from current = last closed H4 bar.
    // Last(0) = current forming H4 bar (would repaint — never use).
    //
    // Pine equivalent:
    //   request.security("240", ta.ema(close, 20)[1], lookahead=on)
    double h4FastValue = _h4EmaFast.Result.Last(1);
    double h4SlowValue = _h4EmaSlow.Result.Last(1);

    _h4Bullish = !double.IsNaN(h4FastValue) && !double.IsNaN(h4SlowValue)
                 && h4FastValue > h4SlowValue;
    _h4Bearish = !double.IsNaN(h4FastValue) && !double.IsNaN(h4SlowValue)
                 && h4FastValue < h4SlowValue;
```

### Step 4: Implement H1 momentum capture at London start/end

Inside the London session start block (from Phase 2), add:
```csharp
    // Capture H1 close at London session start
    // Pine equivalent: h1Close0300 := h1Close (where h1Close = request.security("60", close[1]))
    if (_state == SessionState.Idle && inLondon)
    {
        // ... existing London start code ...
        _h1Close0300 = _h1Bars.ClosePrices.Last(1);  // Previous confirmed H1 bar close
    }
```

Inside the London session end block, add:
```csharp
    // Capture H1 close at London session end
    if (_state == SessionState.London && !inLondon)
    {
        // ... existing London end code ...
        _h1Close0900 = _h1Bars.ClosePrices.Last(1);  // Previous confirmed H1 bar close
    }
```

### Step 5: Implement combined filter evaluation

Add this after the H4 evaluation:

```csharp
    // ─── H1 MOMENTUM FILTER ──────────────────────────────────────────
    // Compares H1 close at London end vs London start.
    // Positive momentum (price went up during London) → favors longs.
    // Negative momentum (price went down) → favors shorts.
    //
    // Pine equivalent:
    //   momentumPositive = h1Close0900 > h1Close0300
    bool momentumPositive = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300)
                            && _h1Close0900 > _h1Close0300;
    bool momentumNegative = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300)
                            && _h1Close0900 < _h1Close0300;

    // ─── COMBINED FILTER (with toggle bypass) ─────────────────────────
    // If a filter is disabled, it always passes (returns true).
    // Pine equivalent:
    //   trendLong  = i_useH4Filter ? h4Bullish : true
    //   longAllowed = trendLong and momLong
    bool trendLong  = UseH4Filter ? _h4Bullish : true;
    bool trendShort = UseH4Filter ? _h4Bearish : true;
    bool momLong    = UseH1Filter ? momentumPositive : true;
    bool momShort   = UseH1Filter ? momentumNegative : true;

    _longAllowed  = trendLong && momLong;
    _shortAllowed = trendShort && momShort;
```

### Step 6: Handle NaN / insufficient data gracefully

```csharp
    // If H4 bars haven't loaded enough history for EMA to compute,
    // the Result will be NaN. In that case, filters evaluate to false
    // (conservative — no trades until data is available).
    // This is handled by the double.IsNaN checks above.
```

### Step 7: Verify non-repainting behavior

Checklist:
- `_h4EmaFast.Result.Last(1)` — confirmed H4 bar, will NOT change
- `_h1Bars.ClosePrices.Last(1)` — confirmed H1 bar close, will NOT change
- All reads happen inside `if (isNewBar)` block — only on M15 bar close
- No `Last(0)` or `LastValue` calls for decision-making

## Todo List

- [ ] Confirm `_h4Bars`, `_h1Bars`, `_h4EmaFast`, `_h4EmaSlow` initialized in `Initialize()`
- [ ] Add `_h4Bullish`, `_h4Bearish`, `_longAllowed`, `_shortAllowed` fields
- [ ] Implement H4 EMA evaluation with `Last(1)` pattern
- [ ] Capture `_h1Close0300` at London session start
- [ ] Capture `_h1Close0900` at London session end
- [ ] Implement H1 momentum comparison
- [ ] Implement combined filter with toggle bypass
- [ ] Add NaN guards on all MTF reads
- [ ] Verify no `Last(0)` / `LastValue` calls in decision logic
- [ ] Compare H4 EMA values against TradingView to confirm parity

## Success Criteria
- H4 EMA fast/slow values match TradingView's H4 EMA values (within rounding)
- Filter correctly gates sweep signals (bullish trend → only long sweeps)
- Toggling `UseH4Filter = false` disables trend filter (all directions allowed)
- No repainting: filter values for a given bar don't change after bar closes
- H1 momentum correctly captures London start/end closes

## Risk Assessment
- **H4 bar alignment**: H4 bars may not align perfectly with M15 chart times depending on broker server timezone — `Last(1)` covers this by always using the last fully closed H4 bar regardless of alignment
- **Insufficient history**: If chart has <50 H4 bars loaded, slow EMA will be NaN — add `_h4Bars.LoadMoreHistory()` in Initialize if needed; alternatively log a warning
- **H1 close timing**: `_h1Bars.ClosePrices.Last(1)` at 03:00 gives the 02:00 H1 bar's close (the bar that just closed). At 09:00 gives the 08:00 bar's close. This matches Pine's `close[1]` on the H1 timeframe — confirmed correct
- **EMA value drift**: cTrader's EMA implementation should match Pine's `ta.ema()` (standard EMA formula), but minor floating-point differences possible — accept if within 1 point

## Security Considerations
- No external data access — MTF bars come from the same broker feed
- No network calls

## Next Steps
- Phase 4: Implement sweep detection logic using filter results, populate `[Output]` properties
