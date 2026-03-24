# Phase 2: Session Detection, London Range Tracking & Range Box Visual

## Context Links
- [Phase 1: Project Setup](./phase-01-project-setup.md)
- [PineScript Source — Session Detection](../../london-sweep-indicator.pine) (lines 64-72)
- [PineScript Source — Range Tracking](../../london-sweep-indicator.pine) (lines 129-157)
- [cTrader Bar Events Guide](https://help.ctrader.com/ctrader-algo/guides/bar-events/)
- [cTrader Chart Objects Guide](https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/)
- [ChartRectangle API](https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/Shapes/ChartRectangle/)

## Overview
- **Priority**: P1 — core logic that all other phases depend on
- **Status**: complete
- **Effort**: 1.5h
- **Description**: Implement session time checks (London 03:00-09:00, Sweep 09:30-11:00), daily reset, London range high/low tracking during session, and the visual range box on chart. Also implement the `lastIndex` bar-close detection pattern.

## Key Insights

### Time Handling in cTrader vs Pine
- Pine uses `time(tf, "0300-0900", tz)` which returns `na` when outside session
- cTrader: `Bars.OpenTimes[index]` gives a `DateTime` already in the indicator's `TimeZone` (EST)
- Extract hour/minute: `int hhmm = t.Hour * 100 + t.Minute;` then compare with HHMM params
- DST is handled automatically by `TimeZones.EasternStandardTime` (EST in winter, EDT in summer)

### Bar-Close Detection in Indicators
- Indicators only have `Calculate(int index)` — called every tick for live bar, once per historical bar
- To run logic only on bar close: track `_lastIndex` and check `if (index > _lastIndex)`
- When `index > _lastIndex`, it means a new bar opened, so the previous bar (`index - 1`) just closed
- This is equivalent to Pine's `barstate.isconfirmed`

### Drawing Rectangles
- `Chart.DrawRectangle(name, barIdx1, priceTop, barIdx2, priceBot, color)` returns `ChartRectangle`
- Set `IsFilled = true` for transparent fill (like Pine `bgcolor`)
- Set `Color` for border, use `Color.FromArgb(alpha, r, g, b)` for transparency
- Rectangle name must be unique — use `"LondonBox_" + _dayCount` pattern

## Requirements

### Functional
- Detect London session start (first bar where HHMM >= LondonStart and previous bar was outside)
- Track London high/low by updating on each bar during session
- Detect London session end (first bar where HHMM >= LondonEnd after being in session)
- Draw range box from session start to end, top = high, bottom = low
- Draw extending dotted lines at London high and London low after session ends
- Reset all state on new calendar day
- Skip weekends (Saturday/Sunday)

### Non-Functional
- No repainting — all decisions based on completed bars only
- Drawing names unique per day to avoid overwriting previous days' visuals
- Efficient: avoid redrawing on every tick — only update when bar closes

## Architecture

```
Calculate(int index)
├── 1. Check if new bar (index > _lastIndex)
│   ├── Yes → run OnBarClosed logic for (index - 1)
│   └── No → skip (tick update, ignore for logic)
├── 2. Daily reset check
│   ├── New date → reset all state to Idle, clear prices
│   └── Weekend → skip
├── 3. Session detection (using HHMM from bar time)
│   ├── London session start → state = London, init high/low
│   ├── Inside London → update high/low, update box
│   ├── London session end → state = RangeDone, calc range, draw level lines
│   └── Sweep window → handled in Phase 4
└── 4. Update range box right edge (extend to current bar)
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepIndicator.cs` — `Initialize()` and `Calculate()` methods

### Files to Reference
- `london-sweep-indicator.pine` (lines 64-157)

## Implementation Steps

### Step 1: Add HHMM helper method to indicator class

```csharp
// ─── HELPER: Extract HHMM integer from a DateTime ────────────────────────
// Converts DateTime to HHMM format (e.g., 03:00 → 300, 09:30 → 930)
// This matches the Pine input format and makes comparisons simple.
private int GetHHMM(DateTime dt)
{
    return dt.Hour * 100 + dt.Minute;
}
```

### Step 2: Add session detection helper methods

```csharp
// ─── HELPER: Check if a time is inside a session range ────────────────────
// Example: IsInSession(930, 300, 900) returns false (9:30 is after 9:00)
// Example: IsInSession(430, 300, 900) returns true  (4:30 is in 3:00-9:00)
private bool IsInSession(int hhmm, int startHHMM, int endHHMM)
{
    return hhmm >= startHHMM && hhmm < endHHMM;
}
```

### Step 3: Implement Initialize() — set up MTF bars (stubs for Phase 3)

```csharp
protected override void Initialize()
{
    // Get H4 and H1 bar series for multi-timeframe analysis (used in Phase 3)
    _h4Bars = MarketData.GetBars(TimeFrame.Hour4);
    _h1Bars = MarketData.GetBars(TimeFrame.Hour);

    // Initialize H4 EMAs on H4 close prices (used in Phase 3)
    _h4EmaFast = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaFastPeriod);
    _h4EmaSlow = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaSlowPeriod);
}
```

### Step 4: Implement bar-close detection pattern in Calculate()

```csharp
public override void Calculate(int index)
{
    // ─── BAR-CLOSE DETECTION ──────────────────────────────────────────
    // Calculate() is called on EVERY tick for the live bar.
    // We only want to run our strategy logic when a bar CLOSES.
    // When index > _lastIndex, a new bar just opened, meaning bar (index-1) closed.
    bool isNewBar = index > _lastIndex;
    _lastIndex = index;

    // For historical bars, Calculate() is called once per bar, so isNewBar is always true.
    // For live bars, only the first tick of a new bar triggers our logic.
    if (!isNewBar)
        return;  // Skip — this is just a tick update on the current bar

    // From here on, we work with the PREVIOUS bar (index - 1) which is now confirmed/closed.
    int prevIndex = index - 1;
    if (prevIndex < 0)
        return;

    // Get the time of the closed bar
    DateTime barTime = Bars.OpenTimes[prevIndex];
    int hhmm = GetHHMM(barTime);

    // ... rest of logic follows
}
```

### Step 5: Implement daily reset

```csharp
    // ─── DAILY RESET ──────────────────────────────────────────────────
    // Reset all state when a new trading day starts.
    // Compare calendar dates to detect day change.
    DateTime barDate = barTime.Date;
    if (barDate != _lastDate)
    {
        _lastDate = barDate;
        _dayCount++;  // Increment for unique drawing names

        // Skip weekends entirely (Saturday = 6, Sunday = 0)
        if (barTime.DayOfWeek == DayOfWeek.Saturday || barTime.DayOfWeek == DayOfWeek.Sunday)
            return;

        // Reset state for the new day
        _state = SessionState.Idle;
        _londonHigh = double.NaN;
        _londonLow = double.NaN;
        _rangeSize = double.NaN;
        _tradedToday = false;
        _h1Close0300 = double.NaN;
        _h1Close0900 = double.NaN;
        _tradeDir = 0;
        _entryPrice = double.NaN;
        _slPrice = double.NaN;
        _tpPrice = double.NaN;
        _tradeResult = "";
    }
```

### Step 6: Implement London session range tracking

```csharp
    // ─── LONDON SESSION START ─────────────────────────────────────────
    // Transition from Idle → London when the first bar of the session closes.
    bool inLondon = IsInSession(hhmm, LondonStartHHMM, LondonEndHHMM);

    if (_state == SessionState.Idle && inLondon)
    {
        _state = SessionState.London;
        _londonHigh = Bars.HighPrices[prevIndex];
        _londonLow = Bars.LowPrices[prevIndex];

        // Draw London range box — starts at this bar, will be extended
        string boxName = "LondonBox_" + _dayCount;
        var box = Chart.DrawRectangle(boxName, prevIndex, _londonHigh, prevIndex, _londonLow,
            Color.FromArgb(40, 0, 100, 255));  // Semi-transparent blue
        box.IsFilled = true;
        box.LineStyle = LineStyle.Solid;
        box.Thickness = 1;
    }

    // ─── LONDON SESSION: UPDATE HIGH/LOW ──────────────────────────────
    // While inside London session, track the highest high and lowest low.
    if (_state == SessionState.London && inLondon)
    {
        _londonHigh = Math.Max(_londonHigh, Bars.HighPrices[prevIndex]);
        _londonLow = Math.Min(_londonLow, Bars.LowPrices[prevIndex]);

        // Update box boundaries to reflect new high/low
        string boxName = "LondonBox_" + _dayCount;
        var box = Chart.DrawRectangle(boxName, /* keep original left */, _londonHigh,
            prevIndex, _londonLow, Color.FromArgb(40, 0, 100, 255));
        box.IsFilled = true;
    }

    // ─── LONDON SESSION END ───────────────────────────────────────────
    // When bar closes outside London session and we were tracking, finalize range.
    if (_state == SessionState.London && !inLondon)
    {
        _state = SessionState.RangeDone;
        _rangeSize = _londonHigh - _londonLow;

        // Draw extending horizontal lines at London high and low
        string hiLineName = "LondonHi_" + _dayCount;
        string loLineName = "LondonLo_" + _dayCount;

        var hiLine = Chart.DrawHorizontalLine(hiLineName, _londonHigh, Color.Blue);
        hiLine.LineStyle = LineStyle.Dots;
        hiLine.Thickness = 1;

        var loLine = Chart.DrawHorizontalLine(loLineName, _londonLow, Color.Blue);
        loLine.LineStyle = LineStyle.Dots;
        loLine.Thickness = 1;
    }
```

### Step 7: Track box start index for proper rectangle updates

Add a field to store the box left edge:
```csharp
private int _londonBoxStartIndex = 0;
```

When London starts: `_londonBoxStartIndex = prevIndex;`

When updating box during session, always use `_londonBoxStartIndex` as the left edge.

### Step 8: Verify drawing behavior

- Range box should appear during London session, expanding as high/low updates
- After London ends, dotted horizontal lines should extend across chart
- Each new day creates new drawings with unique names (FIFO doesn't apply in cTrader — old drawings persist until algo stops)

## Todo List

- [ ] Add `GetHHMM()` helper method
- [ ] Add `IsInSession()` helper method
- [ ] Implement `Initialize()` with MTF bar setup
- [ ] Implement bar-close detection pattern (`_lastIndex` check)
- [ ] Implement daily reset logic with weekend skip
- [ ] Implement London session start detection and range initialization
- [ ] Implement London range high/low tracking during session
- [ ] Implement London session end detection with range calculation
- [ ] Draw London range box (DrawRectangle) with transparent fill
- [ ] Draw London high/low dotted lines (DrawHorizontalLine)
- [ ] Add `_londonBoxStartIndex` field for proper box updates
- [ ] Test on historical M15 chart — verify box matches TradingView visual

## Success Criteria
- London range box visible on chart, matching TradingView's blue box
- High/low dotted lines appear after 09:00 EST
- Range resets each trading day
- No drawings on weekends
- Box top/bottom match Pine indicator's London high/low values (within 1 point)
- State transitions: Idle → London → RangeDone work correctly

## Risk Assessment
- **Box redraw flicker**: Updating rectangle on every bar could cause visual flicker — mitigate by only calling DrawRectangle when high/low actually changes
- **HHMM edge case**: If chart timeframe is not M15 (e.g., M5), HHMM alignment may differ — the logic still works because we check `>=` not `==`
- **Historical bar loading**: If chart doesn't have enough history, `_h4Bars` may have fewer bars — cTrader auto-loads; add `_h4Bars.LoadMoreHistory()` in Initialize if needed
- **DrawHorizontalLine extends infinitely**: Unlike Pine lines with `extend.right`, cTrader horizontal lines extend across the entire chart — may need to switch to `DrawTrendLine` with fixed right endpoint for cleaner look

## Security Considerations
- No network access needed
- No external data — all from chart bars and MTF bars

## Next Steps
- Phase 3: Add H4 EMA trend filter and H1 momentum filter evaluation
