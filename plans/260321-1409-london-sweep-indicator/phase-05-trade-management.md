---
phase: 5
title: "Trade Management (Entry/SL/TP/EOD)"
status: complete
effort: 45m
---

# Phase 5: Trade Management (Entry/SL/TP/EOD)

## Overview

Calculate entry, stop loss, and take profit levels on sweep signal. Monitor price against SL/TP. Implement EOD cutoff at 15:00 EST. Track trade outcome for dashboard display.

## Requirements

### Functional
- **LONG**: Entry = sweep bar close, SL = sweep bar low - 8pts, TP = entry + (0.65 x range)
- **SHORT**: Entry = sweep bar close, SL = sweep bar high + 8pts, TP = entry - (0.65 x range)
- Monitor each bar: if low <= SL (long) or high >= SL (short) -> SL hit
- Monitor each bar: if high >= TP (long) or low <= TP (short) -> TP hit
- EOD cutoff: if time >= 15:00 EST and still in trade -> close at market
- Track result: "SL", "TP", "EOD", or still "Active"

### Non-Functional
- State transitions: IN_TRADE -> DONE (on SL/TP/EOD)
- All levels stored in `var` for persistence across bars

## Implementation Steps

1. Add trade level variables:
```pine
// ─── TRADE MANAGEMENT ───
var float entryPrice = na
var float slPrice    = na
var float tpPrice    = na
var string tradeResult = ""
```

2. Calculate levels on sweep signal:
```pine
if longSweep
    entryPrice := close
    slPrice    := low - i_slBuffer
    tpPrice    := close + (i_tpMult * rangeSize)

if shortSweep
    entryPrice := close
    slPrice    := high + i_slBuffer
    tpPrice    := close - (i_tpMult * rangeSize)
```

3. Monitor trade outcome:
```pine
// EOD time check
eodTime = not na(time(timeframe.period, sessionStr(i_eodCutoff, 2359), i_tz))
bool isEOD = eodTime and not eodTime[1]  // trigger once at cutoff boundary

bool slHit  = false
bool tpHit  = false
bool eodClose = false

if state == STATE_IN_TRADE
    if tradeDir == 1  // Long
        if low <= slPrice
            slHit := true
        else if high >= tpPrice
            tpHit := true
    else if tradeDir == -1  // Short
        if high >= slPrice
            slHit := true
        else if low <= tpPrice
            tpHit := true

    if isEOD and not slHit and not tpHit
        eodClose := true

    if slHit
        tradeResult := "SL Hit"
        state := STATE_DONE
    else if tpHit
        tradeResult := "TP Hit"
        state := STATE_DONE
    else if eodClose
        tradeResult := "EOD Close"
        state := STATE_DONE
```

4. Reset trade vars on new day (add to existing reset block):
```pine
// Add to isNewDay block:
    entryPrice  := na
    slPrice     := na
    tpPrice     := na
    tradeDir    := 0
    tradeResult := ""
```

## Key Design Decisions

- **SL checked before TP**: If both hit on same bar (gap scenario), SL takes priority (conservative).
- **EOD fires once**: Using edge detection (`eodTime and not eodTime[1]`) to prevent repeated triggers.
- **tradeResult string**: Used by dashboard (Phase 6) to display outcome.

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] Entry/SL/TP calculated correctly for both long and short
- [ ] SL buffer of 8pts applied correctly (below low for long, above high for short)
- [ ] TP = entry +/- (0.65 x London range) matches manual calculation
- [ ] SL hit correctly detected when price touches SL level
- [ ] TP hit correctly detected when price touches TP level
- [ ] EOD cutoff fires at 15:00 EST exactly once
- [ ] State transitions to DONE after any exit
- [ ] All vars reset on new day

## Todo

- [ ] Add trade level var declarations
- [ ] Implement level calculation on sweep signals
- [ ] Implement SL/TP monitoring logic
- [ ] Implement EOD cutoff logic
- [ ] Add trade result tracking
- [ ] Extend new-day reset with trade vars
- [ ] Verify SL priority over TP on same-bar scenarios
