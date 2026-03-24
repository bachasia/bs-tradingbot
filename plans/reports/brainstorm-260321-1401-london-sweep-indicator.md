# Brainstorm: London Session Sweep Indicator (PineScript v6)

## Problem Statement

Build a TradingView indicator (PineScript v6) for US30 that:
- Marks London session (03:00–09:00 EST) high/low range
- Filters trades via H4 EMA trend + H1 momentum
- Detects liquidity sweeps during NY open (09:30–11:00 EST)
- Calculates entry/SL/TP levels with configurable parameters
- Displays dashboard, visual levels, and alert conditions
- One trade per day, EOD cutoff at 15:00 EST

## Agreed Requirements

| Aspect | Decision |
|--------|----------|
| Script type | Indicator (overlay) |
| Chart TF | M15 |
| Parameters | All configurable via input() |
| Alerts | Yes — alertcondition() for sweep signals |
| Range visual | Shaded box + extending horizontal lines |
| Dashboard | Info table with filter statuses |
| Trade levels | Lines + labels (Entry blue, SL red, TP green) |

## Technical Architecture

### State Machine Design

```
IDLE → LONDON_SESSION → RANGE_COMPLETE → SWEEP_WINDOW → SIGNAL/NO_TRADE → IN_TRADE → DONE
  ↑                                                                                    |
  └──────────────────────── new trading day ──────────────────────────────────────────┘
```

States:
1. **IDLE**: Before 03:00 EST, waiting for London session
2. **LONDON_SESSION**: 03:00–09:00 EST, tracking high/low
3. **RANGE_COMPLETE**: 09:00 EST, range captured, checking filters
4. **SWEEP_WINDOW**: 09:30–11:00 EST, watching for sweep pattern
5. **IN_TRADE**: Signal fired, tracking SL/TP/EOD
6. **DONE**: Trade resolved or no valid setup, wait for next day

### Multi-Timeframe Data (Non-Repainting)

```pine
// H4 EMAs — computed in H4 context, offset [1] to avoid repaint
[h4Ema20, h4Ema50] = request.security(syminfo.tickerid, "240",
    [ta.ema(close, 20)[1], ta.ema(close, 50)[1]],
    lookahead = barmerge.lookahead_on)

// H1 close — for momentum comparison
h1Close = request.security(syminfo.tickerid, "60", close[1],
    lookahead = barmerge.lookahead_on)
```

**Critical**: EMA must be calculated INSIDE `request.security()` to get true HTF EMA.

### Timezone Handling

Use `"America/New_York"` timezone string (auto-handles EST/EDT):

```pine
londonSession = time(timeframe.period, "0300-0900", "America/New_York")
inLondon = not na(londonSession)
londonStart = inLondon and not inLondon[1]
londonEnd = not inLondon and inLondon[1]
```

### H1 Momentum at Specific Times

Store H1 closes at 03:00 and 09:00 EST using time-gated vars:

```pine
var float h1Close0300 = na
var float h1Close0900 = na

if isTime0300EST
    h1Close0300 := h1Close
if isTime0900EST
    h1Close0900 := h1Close

momentumPositive = h1Close0900 > h1Close0300
```

### Input Organization

Groups:
- **Session Times**: London start/end, sweep window start/end, EOD cutoff
- **Trade Parameters**: Min range, sweep threshold, SL buffer, TP multiplier
- **Filters**: H4 EMA lengths, enable/disable toggles
- **Visual**: Colors, line styles, show/hide dashboard
- **Alerts**: Enable/disable per alert type

### Drawing Management

- **Box**: 1 per day for London range (var, reuse/update)
- **Lines**: 2 extending lines (London high/low), 3 trade levels (entry/SL/TP)
- **Labels**: Price labels on trade level lines
- **Table**: 1 info table (top-right corner)
- **Cleanup**: Delete old drawings when new day starts, set `max_lines_count=50`

### Alert Conditions

```pine
alertcondition(longSweep, "Long Sweep", "London sweep LONG: entry={{close}}")
alertcondition(shortSweep, "Short Sweep", "London sweep SHORT: entry={{close}}")
alertcondition(slHit, "Stop Loss Hit", "SL hit at {{close}}")
alertcondition(tpHit, "Take Profit Hit", "TP hit at {{close}}")
alertcondition(eodClose, "EOD Close", "EOD position closed at {{close}}")
```

Must be at global scope. Use `barstate.isconfirmed` in conditions.

## Key Technical Risks

| Risk | Mitigation |
|------|-----------|
| H1 close timing mismatch | Use time() with "America/New_York" + bar time check |
| HTF EMA repainting | Always use `[1]` offset with `lookahead_on` |
| Drawing ID exhaustion | Reuse var objects, delete on new day |
| Weekend/holiday gaps | Check `dayofweek` before enabling logic |
| DST transitions | `"America/New_York"` auto-handles, unlike GMT offsets |
| Exchange timezone varies | Explicit timezone in all time() calls |

## File Structure (Proposed)

Single file: `london-sweep-indicator.pine` (~180-200 lines)

PineScript doesn't support multi-file. Keep modular via commented sections:
1. Inputs (~40 lines)
2. Multi-TF data (~15 lines)
3. Session/state logic (~40 lines)
4. Sweep detection (~30 lines)
5. Trade management (~25 lines)
6. Visuals (~30 lines)
7. Dashboard table (~20 lines)
8. Alerts (~10 lines)

## Success Criteria

- [ ] London range box drawn correctly on historical + live data
- [ ] H4 trend + H1 momentum filters work without repainting
- [ ] Sweep detection matches manual identification on 20+ historical examples
- [ ] Entry/SL/TP levels calculated correctly per specification
- [ ] One trade per day limit enforced
- [ ] EOD cutoff at 15:00 EST works
- [ ] Dashboard shows all filter statuses in real-time
- [ ] Alerts fire on confirmed bars only (no repaint)
- [ ] All parameters adjustable via settings

## Next Steps

1. Create implementation plan with phased approach
2. Implement core logic (session detection + range tracking)
3. Add multi-TF filters
4. Add sweep detection + trade management
5. Add visuals + dashboard + alerts
6. Test on US30 M15 historical data
