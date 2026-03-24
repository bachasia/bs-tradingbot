# Phase 5: Dashboard Visuals (Status Panel)

## Context Links
- [Phase 4: Sweep Detection](./phase-04-sweep-detection.md)
- [PineScript Source — Dashboard Table](../../london-sweep-indicator.pine) (lines 280-317)
- [cTrader ChartStaticText API](https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/ChartStaticText/)
- [cTrader Chart Objects Guide](https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/)

## Overview
- **Priority**: P2 — cosmetic, not blocking cBot work
- **Status**: pending
- **Effort**: 1h
- **Description**: Replicate the Pine dashboard table showing H4 trend, H1 momentum, range size, sweep status, and trade result. Use `Chart.DrawStaticText()` since cTrader has no table widget equivalent.

## Key Insights

### Pine Table vs cTrader DrawStaticText
- Pine: `table.new(position.top_right, 2, 6)` creates a 2-column, 6-row grid
- cTrader: No table widget in chart drawing API. Closest is `Chart.DrawStaticText(name, text, vAlign, hAlign, color)`
- **Workaround**: Build a multi-line string with padding/alignment, render as single static text block in top-right corner
- Each call to `DrawStaticText` with the same `name` replaces the previous text (no flicker)

### Color Limitation
- `DrawStaticText` takes a single `Color` parameter for the entire text block
- Pine table has per-cell coloring (green for bullish, red for bearish, etc.)
- **Workaround**: Use ASCII indicators like `[+]` / `[-]` / `[=]` or emoji-free markers, with a single neutral color (white or light gray) for the whole block. Alternatively, use multiple `DrawStaticText` calls at different positions, but they overlap at same alignment point
- **Best approach**: Single text block with labeled status — clarity over color

### When to Update
- Pine: `if i_showDashboard and barstate.islast` — only on the most recent bar
- cTrader: Update dashboard inside `Calculate()` when `IsLastBar` is true (live chart) or on every bar during backtest for historical replay
- For efficiency: always update (text replacement is cheap), gated by `ShowDashboard` param

## Requirements

### Functional
- Display 6 rows of status information:
  1. H4 Trend: Bullish / Bearish / Neutral
  2. H1 Momentum: Positive / Negative / Flat
  3. Range: size in points or "N/A"
  4. Range Valid: Yes / No (above MinRange threshold)
  5. Sweep: LONG / SHORT / Watching
  6. Trade: Active / SL Hit / TP Hit / EOD Close / None
- Positioned in top-right corner of chart
- Only shown when `ShowDashboard` parameter is true
- Updates on each bar

### Non-Functional
- Readable at a glance — clear labels and status values
- Doesn't obstruct price action (top-right is standard position)
- Single `DrawStaticText` call per update (no flicker)

## Architecture

```
Calculate(int index) — dashboard section (at end of method)
├── Guard: if (!ShowDashboard) return early from this section
├── Build status strings for each row
├── Format multi-line text block with alignment
└── Chart.DrawStaticText("Dashboard", text, Top, Right, Color.White)
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepIndicator.cs` — end of `Calculate()` method

### Files to Reference
- `london-sweep-indicator.pine` (lines 280-317)

## Implementation Steps

### Step 1: Add dashboard rendering at end of Calculate()

This section runs on every bar (after all logic above). Rendering static text is cheap. Place at the very end of `Calculate()`, **outside** the `if (!isNewBar) return` guard — we want it to update on ticks too for live charts, or gate it with `IsLastBar` for efficiency.

Actually, for simplicity and to avoid stale display, render only on new bars AND when `IsLastBar`:

```csharp
    // ─── DASHBOARD ────────────────────────────────────────────────────
    // Display a status panel in the top-right corner of the chart.
    // Uses DrawStaticText which replaces previous text with same name.
    //
    // Pine equivalent: table.new(position.top_right, 2, 6) with cell updates
    //
    // cTrader limitation: DrawStaticText is a single-color text block.
    // We build a formatted multi-line string instead of a colored table.
    if (ShowDashboard)
    {
        UpdateDashboard();
    }
```

### Step 2: Create UpdateDashboard() helper method

```csharp
// ─── DASHBOARD HELPER ─────────────────────────────────────────────────
// Builds a multi-line status string and draws it in the top-right corner.
// Called from Calculate() on each new bar.
private void UpdateDashboard()
{
    // ── H4 Trend Status ──
    string h4Status;
    if (_h4Bullish)
        h4Status = "Bullish  [+]";
    else if (_h4Bearish)
        h4Status = "Bearish  [-]";
    else
        h4Status = "Neutral  [=]";

    // ── H1 Momentum Status ──
    bool momPos = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300)
                  && _h1Close0900 > _h1Close0300;
    bool momNeg = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300)
                  && _h1Close0900 < _h1Close0300;
    string momStatus;
    if (momPos)
        momStatus = "Positive [+]";
    else if (momNeg)
        momStatus = "Negative [-]";
    else
        momStatus = "Flat     [=]";

    // ── Range Size ──
    string rangeStr = double.IsNaN(_rangeSize)
        ? "N/A"
        : _rangeSize.ToString("F1") + " pts";
    bool rangeOk = !double.IsNaN(_rangeSize) && _rangeSize >= MinRange;
    string rangeValid = rangeOk ? "Yes" : "No";

    // ── Sweep Status ──
    string sweepStr;
    if (_tradedToday)
        sweepStr = _tradeDir == 1 ? "LONG" : "SHORT";
    else
        sweepStr = "Watching";

    // ── Trade Result ──
    string tradeStr;
    if (_state == SessionState.InTrade)
        tradeStr = "Active";
    else if (!string.IsNullOrEmpty(_tradeResult))
        tradeStr = _tradeResult;
    else
        tradeStr = "None";

    // ── State (debug) ──
    string stateStr = _state.ToString();

    // ── Build formatted text block ──
    // Using fixed-width formatting for alignment.
    // Each line: "Label:    Value"
    string dashText =
        "══ London Sweep ══\n" +
        "─────────────────────\n" +
        "H4 Trend:    " + h4Status + "\n" +
        "H1 Mom:      " + momStatus + "\n" +
        "Range:       " + rangeStr + "\n" +
        "Range OK:    " + rangeValid + "\n" +
        "Sweep:       " + sweepStr + "\n" +
        "Trade:       " + tradeStr + "\n" +
        "State:       " + stateStr + "\n" +
        "─────────────────────";

    // ── Draw static text in top-right corner ──
    // Same name "Dashboard" ensures it replaces previous text each time.
    Chart.DrawStaticText("Dashboard", dashText,
        VerticalAlignment.Top, HorizontalAlignment.Right,
        Color.White);
}
```

### Step 3: Handle ShowDashboard toggle

When `ShowDashboard` is false, remove any existing dashboard text:

```csharp
    if (ShowDashboard)
    {
        UpdateDashboard();
    }
    else
    {
        // Remove dashboard if parameter was toggled off
        Chart.RemoveObject("Dashboard");
    }
```

### Step 4: Consider calling dashboard outside isNewBar guard

The dashboard should update on the live chart even between bar closes (to show current state). Options:

**Option A (simple)**: Update only on new bars — dashboard lags by up to 1 bar interval (M15 = 15 min). Acceptable for this use case.

**Option B (responsive)**: Move dashboard update outside the `if (!isNewBar) return` guard, so it runs on every tick. Cost is negligible (string build + one DrawStaticText call).

Recommend **Option B** for better UX — the state label (`Watching` → `LONG`) updates immediately.

```csharp
public override void Calculate(int index)
{
    // ... bar-close detection ...
    bool isNewBar = index > _lastIndex;

    // Dashboard updates on every tick for responsive display
    if (ShowDashboard)
        UpdateDashboard();
    else
        Chart.RemoveObject("Dashboard");

    _lastIndex = index;
    if (!isNewBar)
        return;

    // ... rest of bar-close logic ...
}
```

### Step 5: Add trade price info to dashboard when in trade

Extend dashboard text when a trade is active or resolved:

```csharp
    // Add trade levels when available
    if (_tradedToday && !double.IsNaN(_entryPrice))
    {
        dashText +=
            "\n─────────────────────\n" +
            "Entry:       " + _entryPrice.ToString("F1") + "\n" +
            "SL:          " + _slPrice.ToString("F1") + "\n" +
            "TP:          " + _tpPrice.ToString("F1");
    }
```

## Todo List

- [ ] Create `UpdateDashboard()` helper method
- [ ] Build H4 trend status string with indicator markers
- [ ] Build H1 momentum status string
- [ ] Build range size and validity strings
- [ ] Build sweep and trade result strings
- [ ] Format multi-line text block with alignment
- [ ] Call `Chart.DrawStaticText("Dashboard", ...)` in top-right
- [ ] Handle `ShowDashboard = false` → remove dashboard
- [ ] Move dashboard update outside `isNewBar` guard for responsiveness
- [ ] Add trade levels (entry/SL/TP) to dashboard when active
- [ ] Visual test: verify dashboard readable on dark and light chart themes

## Success Criteria
- Dashboard visible in top-right corner of chart
- All 6+ status rows display correct values
- Dashboard updates in real-time (on each tick, not just bar close)
- Toggling `ShowDashboard = false` removes the panel
- Text is readable and doesn't overlap with price action
- State machine label matches expected phase of day

## Risk Assessment
- **Font alignment**: `DrawStaticText` uses proportional font in cTrader — fixed-width alignment may not be pixel-perfect. Use tab characters or just accept slight misalignment — readability is what matters
- **Color limitation**: Single color for entire block. ASCII markers `[+][-][=]` compensate. Future improvement: use multiple `DrawStaticText` calls at different offsets (but risk overlapping)
- **Text cutoff**: Very long text blocks may be cut off on small chart windows — keep lines short

## Security Considerations
- Display only — no external data or execution

## Next Steps
- Phase 6: cBot reads indicator output, places pending orders
