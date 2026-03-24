---
phase: 4
title: "Sweep Detection Logic"
status: complete
effort: 45m
---

# Phase 4: Sweep Detection Logic

## Overview

Detect liquidity sweeps during the NY open window (09:30-11:00 EST) on M15 bars. A sweep occurs when price breaches the London range boundary by a threshold and then closes back inside. Must respect directional filters and one-trade-per-day limit.

## Requirements

### Functional
- **LONG sweep**: M15 low goes >= `i_sweepThresh` pts below London low AND same bar closes above London low
- **SHORT sweep**: M15 high goes >= `i_sweepThresh` pts above London high AND same bar closes below London high
- Only detect during sweep window (09:30-11:00 EST)
- Only detect if `rangeValid` (range >= min threshold)
- Only detect if directional filter allows (`longAllowed`/`shortAllowed`)
- One trade per day — once sweep detected, no more scans
- Use `barstate.isconfirmed` to ensure bar is complete

### Non-Functional
- State machine transitions: RANGE_DONE -> SWEEP_WINDOW -> IN_TRADE or DONE

## Implementation Steps

1. Add sweep window state transition:
```pine
// Enter sweep window
if state == STATE_RANGE_DONE and inSweepWindow and rangeValid
    state := STATE_SWEEP_WINDOW

// Exit sweep window without signal
if state == STATE_SWEEP_WINDOW and not inSweepWindow
    state := STATE_DONE
```

2. Implement sweep detection on confirmed bars:
```pine
// ─── SWEEP DETECTION ───
var int tradeDir = 0  // 1=long, -1=short, 0=none

bool longSweep  = false
bool shortSweep = false

if state == STATE_SWEEP_WINDOW and barstate.isconfirmed and not tradedToday
    // Long sweep: wick below London low + close back above
    if longAllowed
        float sweepDepth = londonLow - low
        if sweepDepth >= i_sweepThresh and close > londonLow
            longSweep  := true
            tradeDir   := 1
            state      := STATE_IN_TRADE
            tradedToday := true

    // Short sweep: wick above London high + close back below
    if shortAllowed and not longSweep  // don't double-trigger
        float sweepHeight = high - londonHigh
        if sweepHeight >= i_sweepThresh and close < londonHigh
            shortSweep := true
            tradeDir   := -1
            state      := STATE_IN_TRADE
            tradedToday := true
```

3. Note: `longSweep` and `shortSweep` booleans are consumed by Phase 5 (trade entry) and Phase 7 (alerts).

## Key Design Decisions

- **Long priority over short**: If both conditions met on same bar (unlikely), long takes precedence. Can be changed.
- **barstate.isconfirmed**: Only evaluate on closed bars to prevent repainting.
- **tradedToday flag**: Prevents multiple signals per day even if multiple sweeps occur.

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] Long sweep detected when low dips >= 5pts below London low AND closes above it
- [ ] Short sweep detected when high spikes >= 5pts above London high AND closes below it
- [ ] No sweep detected outside 09:30-11:00 window
- [ ] No sweep if range < 50pts
- [ ] No sweep if directional filter disagrees
- [ ] Only one sweep per day (tradedToday enforced)
- [ ] No signal on unconfirmed bars

## Todo

- [ ] Add sweep window state transitions
- [ ] Implement long sweep detection with threshold check
- [ ] Implement short sweep detection with threshold check
- [ ] Add one-trade-per-day guard
- [ ] Verify barstate.isconfirmed usage
- [ ] Test with historical data showing clear sweep patterns
