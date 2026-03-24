---
phase: 2
title: "Session Detection + London Range Tracking"
status: complete
effort: 45m
---

# Phase 2: Session Detection + London Range Tracking

## Overview

Implement time-based session detection using `time()` with timezone support. Track London session high/low using `var` variables. Detect session boundaries (start/end) and new trading day transitions.

## Requirements

### Functional
- Detect London session window (03:00-09:00 EST) per bar
- Track highest high and lowest low during London session
- Detect session start (reset tracking) and session end (finalize range)
- Store London high/low as persistent vars for use in later phases
- Handle new day reset of all daily state

### Non-Functional
- Use `"America/New_York"` for automatic EST/EDT handling
- No hardcoded GMT offsets

## Key Insights

- `time(timeframe.period, "0300-0900", tz)` returns non-na when bar falls in session
- Session start = `inLondon and not inLondon[1]`
- Session end = `not inLondon and inLondon[1]`
- Need helper function to convert HHMM int inputs to time session strings

## Implementation Steps

1. Add time helper function:
```pine
// Convert HHMM int pair to session string "HHMM-HHMM"
sessionStr(int startHHMM, int endHHMM) =>
    str.format("{0,number,0000}-{1,number,0000}", startHHMM, endHHMM)
```

2. Add session detection:
```pine
// ─── SESSION DETECTION ───
string londonSess = sessionStr(i_londonStart, i_londonEnd)
string sweepSess  = sessionStr(i_sweepStart, i_sweepEnd)

bool inLondon     = not na(time(timeframe.period, londonSess, i_tz))
bool londonStart  = inLondon and not inLondon[1]
bool londonEnd    = not inLondon and inLondon[1]

bool inSweepWindow = not na(time(timeframe.period, sweepSess, i_tz))
```

3. Add state machine enum and daily state variables:
```pine
// ─── STATE VARIABLES ───
// State enum
var int STATE_IDLE          = 0
var int STATE_LONDON        = 1
var int STATE_RANGE_DONE    = 2
var int STATE_SWEEP_WINDOW  = 3
var int STATE_IN_TRADE      = 4
var int STATE_DONE          = 5

var int   state       = STATE_IDLE
var float londonHigh  = na
var float londonLow   = na
var float rangeSize   = na
var bool  tradedToday = false
```

4. Implement new-day reset + London range tracking:
```pine
// New day detection (reset at midnight EST)
isNewDay = ta.change(time("D")) != 0

if isNewDay
    state       := STATE_IDLE
    londonHigh  := na
    londonLow   := na
    rangeSize   := na
    tradedToday := false

// London session tracking
if londonStart
    state      := STATE_LONDON
    londonHigh := high
    londonLow  := low

if state == STATE_LONDON and inLondon
    londonHigh := math.max(londonHigh, high)
    londonLow  := math.min(londonLow, low)

if londonEnd and state == STATE_LONDON
    state     := STATE_RANGE_DONE
    rangeSize := londonHigh - londonLow
```

5. Add range validity check:
```pine
bool rangeValid = not na(rangeSize) and rangeSize >= i_minRange
```

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] London range box boundaries match visual 03:00-09:00 candles
- [ ] `londonHigh`/`londonLow` capture exact session extremes
- [ ] State transitions correctly: IDLE -> LONDON -> RANGE_DONE
- [ ] New day resets all daily state
- [ ] Range < 50pts correctly flags `rangeValid = false`

## Todo

- [ ] Add sessionStr() helper function
- [ ] Implement session detection booleans
- [ ] Add state machine variables and enum
- [ ] Implement new-day reset logic
- [ ] Implement London range tracking (high/low)
- [ ] Add range validity check
- [ ] Test on multiple days of US30 M15 data
