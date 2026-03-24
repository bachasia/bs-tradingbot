---
phase: 6
title: "Visual Elements (Box, Lines, Labels, Dashboard)"
status: complete
effort: 1h
---

# Phase 6: Visual Elements

## Overview

Draw London range box, extending horizontal lines, trade level lines with price labels, and an info dashboard table. All drawings managed via `var` references with daily cleanup.

## Requirements

### Functional
- **London range box**: Shaded box from session start to end, top=high, bottom=low
- **Extending lines**: Horizontal lines from London high/low extending right
- **Trade lines**: Entry (blue), SL (red), TP (green) with `line.style_dashed`
- **Price labels**: Show exact price on each trade line
- **Dashboard table**: Top-right, shows H4 trend, H1 momentum, range size, sweep status, trade status
- Old drawings deleted on new day start

### Non-Functional
- Use `var` for drawing object references (persist across bars)
- Dashboard uses `table.new()` with fixed cell layout
- Colors from user inputs

## Implementation Steps

1. Add drawing var declarations:
```pine
// ─── VISUALS ───
var box   londonBox    = na
var line  londonHiLine = na
var line  londonLoLine = na
var line  entryLine    = na
var line  slLine       = na
var line  tpLine       = na
var label entryLabel   = na
var label slLabel      = na
var label tpLabel      = na
```

2. Clean up old drawings on new day (add to isNewDay block):
```pine
    // Delete previous day's drawings
    if not na(londonBox),    box.delete(londonBox)
    if not na(londonHiLine), line.delete(londonHiLine)
    if not na(londonLoLine), line.delete(londonLoLine)
    if not na(entryLine),    line.delete(entryLine)
    if not na(slLine),       line.delete(slLine)
    if not na(tpLine),       line.delete(tpLine)
    if not na(entryLabel),   label.delete(entryLabel)
    if not na(slLabel),      label.delete(slLabel)
    if not na(tpLabel),      label.delete(tpLabel)
```

3. Draw London range box:
```pine
// Create box at London start
if londonStart
    londonBox := box.new(bar_index, high, bar_index, low,
        border_color=color.blue, bgcolor=i_boxColor, border_width=1)

// Update box during London session
if state == STATE_LONDON and inLondon
    box.set_top(londonBox, londonHigh)
    box.set_bottom(londonBox, londonLow)
    box.set_right(londonBox, bar_index)

// Add extending lines at London end
if londonEnd and state == STATE_RANGE_DONE
    londonHiLine := line.new(bar_index, londonHigh, bar_index + 1, londonHigh,
        color=color.blue, style=line.style_dotted, extend=extend.right, width=1)
    londonLoLine := line.new(bar_index, londonLow, bar_index + 1, londonLow,
        color=color.blue, style=line.style_dotted, extend=extend.right, width=1)
```

4. Draw trade levels on sweep signal:
```pine
if (longSweep or shortSweep) and i_showTradeLines
    entryLine := line.new(bar_index, entryPrice, bar_index + 1, entryPrice,
        color=i_entryColor, style=line.style_dashed, extend=extend.right, width=2)
    slLine := line.new(bar_index, slPrice, bar_index + 1, slPrice,
        color=i_slColor, style=line.style_dashed, extend=extend.right, width=2)
    tpLine := line.new(bar_index, tpPrice, bar_index + 1, tpPrice,
        color=i_tpColor, style=line.style_dashed, extend=extend.right, width=2)

    entryLabel := label.new(bar_index + 5, entryPrice, "Entry: " + str.tostring(entryPrice, format.mintick),
        color=color.new(color.blue, 100), textcolor=i_entryColor, style=label.style_label_left)
    slLabel := label.new(bar_index + 5, slPrice, "SL: " + str.tostring(slPrice, format.mintick),
        color=color.new(color.red, 100), textcolor=i_slColor, style=label.style_label_left)
    tpLabel := label.new(bar_index + 5, tpPrice, "TP: " + str.tostring(tpPrice, format.mintick),
        color=color.new(color.green, 100), textcolor=i_tpColor, style=label.style_label_left)
```

5. Stop extending lines when trade resolves:
```pine
if state == STATE_DONE and (slHit or tpHit or eodClose)
    if not na(entryLine), line.set_extend(entryLine, extend.none), line.set_x2(entryLine, bar_index)
    if not na(slLine),    line.set_extend(slLine, extend.none),    line.set_x2(slLine, bar_index)
    if not na(tpLine),    line.set_extend(tpLine, extend.none),    line.set_x2(tpLine, bar_index)
    // Also stop London level extensions
    if not na(londonHiLine), line.set_extend(londonHiLine, extend.none), line.set_x2(londonHiLine, bar_index)
    if not na(londonLoLine), line.set_extend(londonLoLine, extend.none), line.set_x2(londonLoLine, bar_index)
```

6. Dashboard table:
```pine
// ─── DASHBOARD ───
var table dash = na
if i_showDashboard
    if barstate.islast
        dash := table.new(position.top_right, 2, 6, bgcolor=color.new(color.black, 80), border_width=1)

        table.cell(dash, 0, 0, "Filter",    text_color=color.white, text_size=size.small)
        table.cell(dash, 1, 0, "Status",    text_color=color.white, text_size=size.small)

        // H4 Trend
        string h4Status = h4Bullish ? "Bullish" : h4Bearish ? "Bearish" : "Neutral"
        color h4Clr = h4Bullish ? color.green : h4Bearish ? color.red : color.gray
        table.cell(dash, 0, 1, "H4 Trend",  text_color=color.white, text_size=size.tiny)
        table.cell(dash, 1, 1, h4Status,     text_color=h4Clr,      text_size=size.tiny)

        // H1 Momentum
        string momStatus = momentumPositive ? "Positive" : momentumNegative ? "Negative" : "Flat"
        color momClr = momentumPositive ? color.green : momentumNegative ? color.red : color.gray
        table.cell(dash, 0, 2, "H1 Mom",    text_color=color.white, text_size=size.tiny)
        table.cell(dash, 1, 2, momStatus,    text_color=momClr,      text_size=size.tiny)

        // Range
        string rngStr = na(rangeSize) ? "N/A" : str.tostring(rangeSize, "#.#") + "pts"
        color rngClr = rangeValid ? color.green : color.red
        table.cell(dash, 0, 3, "Range",     text_color=color.white, text_size=size.tiny)
        table.cell(dash, 1, 3, rngStr,      text_color=rngClr,      text_size=size.tiny)

        // Sweep
        string sweepStr = longSweep ? "LONG" : shortSweep ? "SHORT" : tradedToday ? "Done" : "Watching"
        table.cell(dash, 0, 4, "Sweep",     text_color=color.white, text_size=size.tiny)
        table.cell(dash, 1, 4, sweepStr,    text_color=color.yellow, text_size=size.tiny)

        // Trade
        string tradeStr = state == STATE_IN_TRADE ? "Active" : tradeResult != "" ? tradeResult : "None"
        table.cell(dash, 0, 5, "Trade",     text_color=color.white, text_size=size.tiny)
        table.cell(dash, 1, 5, tradeStr,    text_color=color.yellow, text_size=size.tiny)
```

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] London box correctly spans session with accurate high/low
- [ ] Extending lines visible from London high/low through sweep window
- [ ] Trade lines appear on sweep with correct colors and prices
- [ ] Labels show exact prices formatted to tick
- [ ] Lines stop extending when trade resolves
- [ ] Dashboard shows all 5 filter/status rows
- [ ] Old drawings cleaned up on new day
- [ ] Toggle inputs hide/show elements correctly

## Todo

- [ ] Add drawing var declarations
- [ ] Implement new-day drawing cleanup
- [ ] Draw London range box (create + update)
- [ ] Add extending London level lines
- [ ] Draw trade level lines + labels on sweep
- [ ] Stop line extensions on trade resolution
- [ ] Build dashboard table with all status rows
- [ ] Test visual alignment on multiple days
