---
phase: 3
title: "Multi-TF Filters (H4 EMA + H1 Momentum)"
status: complete
effort: 45m
---

# Phase 3: Multi-TF Filters (H4 EMA + H1 Momentum)

## Overview

Implement H4 trend direction filter (EMA20 vs EMA50) and H1 momentum filter (close at 09:00 vs 03:00 EST). Both filters must agree for a trade direction to be valid. Non-repainting via `[1]` offset + `lookahead_on`.

## Requirements

### Functional
- H4 EMA20 > EMA50 = bullish (longs only); EMA20 < EMA50 = bearish (shorts only)
- H1 close at 09:00 > close at 03:00 = positive momentum (longs); opposite = negative (shorts)
- Both must agree for `longAllowed` or `shortAllowed` to be true
- Filters individually toggleable via input booleans

### Non-Functional
- **CRITICAL**: EMA calculated INSIDE `request.security()` for true HTF values
- **CRITICAL**: `[1]` offset + `lookahead_on` pattern to prevent repainting

## Implementation Steps

1. Add H4 EMA data fetch (non-repainting):
```pine
// ─── MULTI-TF DATA ───
[h4EmaFast, h4EmaSlow] = request.security(
    syminfo.tickerid, "240",
    [ta.ema(close, i_h4EmaFast)[1], ta.ema(close, i_h4EmaSlow)[1]],
    lookahead = barmerge.lookahead_on)

bool h4Bullish = h4EmaFast > h4EmaSlow
bool h4Bearish = h4EmaFast < h4EmaSlow
```

2. Add H1 close data for momentum:
```pine
h1Close = request.security(syminfo.tickerid, "60", close[1],
    lookahead = barmerge.lookahead_on)
```

3. Capture H1 closes at session boundaries:
```pine
var float h1Close0300 = na
var float h1Close0900 = na

// Capture at London start (03:00) and end (09:00)
if londonStart
    h1Close0300 := h1Close
if londonEnd
    h1Close0900 := h1Close
```

4. Compute momentum and combined filter:
```pine
bool momentumPositive = not na(h1Close0900) and not na(h1Close0300) and h1Close0900 > h1Close0300
bool momentumNegative = not na(h1Close0900) and not na(h1Close0300) and h1Close0900 < h1Close0300

// Combined directional filter (respect enable toggles)
bool trendLong  = (i_useH4Filter ? h4Bullish : true)
bool trendShort = (i_useH4Filter ? h4Bearish : true)
bool momLong    = (i_useH1Filter ? momentumPositive : true)
bool momShort   = (i_useH1Filter ? momentumNegative : true)

bool longAllowed  = trendLong  and momLong
bool shortAllowed = trendShort and momShort
```

## Key Risks

| Risk | Mitigation |
|------|-----------|
| HTF EMA repaints | `[1]` + `lookahead_on` — always uses confirmed bar |
| H1 close timing off | Capture at session boundary events, not arbitrary bars |
| Filters disabled but logic breaks | Ternary defaults to `true` when filter disabled |

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] H4 EMAs match H4 chart EMA values (offset by 1 bar)
- [ ] h1Close0300 and h1Close0900 capture correct hourly closes
- [ ] `longAllowed`/`shortAllowed` correctly reflect combined filters
- [ ] Disabling a filter via input makes it pass-through (always true)
- [ ] No repainting — values don't change on same bar

## Todo

- [ ] Add request.security() for H4 EMAs
- [ ] Add request.security() for H1 close
- [ ] Capture H1 closes at 03:00 and 09:00 boundaries
- [ ] Compute momentum direction
- [ ] Combine filters with enable/disable toggles
- [ ] Verify non-repainting behavior
