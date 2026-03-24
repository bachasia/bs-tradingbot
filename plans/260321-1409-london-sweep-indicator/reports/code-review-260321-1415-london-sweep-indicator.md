# Code Review: London Session Sweep Indicator (PineScript v6)

**File**: `/Users/bachasia/Data/VibeCoding/tradingview-project/london-sweep-indicator.pine`
**Date**: 2026-03-21
**LOC**: 333 lines (with comments/whitespace)
**Focus**: Full static analysis — correctness, non-repainting, edge cases, v6 compatibility

## Overall Assessment

Solid implementation. Clean section organization, proper state machine, correct non-repainting patterns. A few issues found, mostly medium priority. The code matches the brainstorm spec well.

---

## Critical Issues

**None found.** No security issues (N/A for PineScript indicators), no data-loss risks.

---

## High Priority

### H1. alertcondition + barstate.isconfirmed is redundant/problematic

**Lines 323-332**: `alertcondition(longSweep and barstate.isconfirmed, ...)` — but `longSweep` is already gated behind `barstate.isconfirmed` at line 186. The `barstate.isconfirmed` inside `alertcondition()` is harmless but misleading. More importantly, `alertcondition()` evaluates on every bar in history but `barstate.isconfirmed` is only true on realtime bars. This means **alerts will never trigger on historical bars** during backtesting/alert creation preview, which can confuse users during setup.

**Recommendation**: Remove `barstate.isconfirmed` from `alertcondition()` calls since the boolean flags are already properly gated:
```pine
alertcondition(longSweep, "Long Sweep Signal", ...)
alertcondition(shortSweep, "Short Sweep Signal", ...)
alertcondition(slHit, "Stop Loss Hit", ...)
alertcondition(tpHit, "Take Profit Hit", ...)
alertcondition(eodClose, "EOD Close", ...)
```

### H2. Both long AND short sweep can trigger on same bar (race condition)

**Lines 186-209**: The `if longAllowed` block runs first. If it does NOT set `longSweep`, the `if shortAllowed and not longSweep` block runs. However, if BOTH `longAllowed` and `shortAllowed` are true and the bar wicks both above London high AND below London low (volatile bar), only the long sweep triggers. This is acceptable behavior (priority to long), but **undocumented**. More concerning: if `longAllowed` is false but `shortAllowed` is true, a candle that sweeps below London low will be ignored for shorts because the `not longSweep` check passes but the inner conditions check `high - londonHigh`, not `londonLow - low`.

**Impact**: Low in practice (rare for M15 US30 to wick both extremes), but worth a comment.

### H3. SL/TP evaluation order creates bias

**Lines 239-248**: SL is checked before TP. On a bar where both SL and TP prices are breached (gap/spike), SL always wins. This is conservative (prevents over-reporting wins) but should be documented.

**Recommendation**: Add comment: `// SL checked first — conservative bias on gap bars`

### H4. H1 momentum captures may be stale/misaligned

**Lines 137, 154**: `h1Close0300` is captured at `londonStart` (first M15 bar of London session), and `h1Close0900` at `londonEnd`. The `h1Close` value comes from `request.security("60", close[1])` which returns the **previous completed H1 bar's close**. At 03:00 EST on M15, the previous H1 close is the 02:00 bar. At 09:00 EST, it's the 08:00 bar. This is technically the H1 close from one hour prior to each boundary.

**Impact**: The momentum calculation compares 02:00 H1 close vs 08:00 H1 close — a 6-hour momentum reading. This works conceptually but doesn't exactly match "London session momentum." The brainstorm says "H1 close at 03:00 and 09:00" which this approximates but doesn't precisely deliver.

**Recommendation**: Document this offset explicitly in a comment so users understand what's being compared.

---

## Medium Priority

### M1. Drawing cleanup deletes previous day — no multi-day history

**Lines 122-130**: On `isNewDay`, all drawings are deleted. Users scrolling back in history will see no London boxes or trade levels from previous days. The `max_boxes_count = 20` and `max_lines_count = 50` limits were set presumably to allow multi-day display.

**Recommendation**: Remove the deletion block. Let PineScript's max drawing limits handle cleanup naturally (FIFO). This gives ~20 days of box history and ~10 days of full line history.

### M2. `ta.change(time("D"))` for new day detection

**Line 106**: `ta.change(time("D")) != 0` — this works but uses the **exchange timezone** for day boundaries, not `i_tz` (America/New_York). If the exchange is in a different timezone (e.g., CME for US30 which uses CT), the day reset may happen at the wrong time relative to the session logic.

**Recommendation**: Use a time-based reset aligned to `i_tz`:
```pine
currentDay = dayofweek(time, i_tz)
isNewDay = ta.change(currentDay) != 0
```

### M3. `sessionStr` format string may produce unexpected results

**Line 44**: `str.format("{0,number,0000}-{1,number,0000}", 300, 900)` — the `{0,number,0000}` format should produce `"0300-0900"`. This is correct for PineScript v6's `str.format()` which follows Java's MessageFormat. Verified OK.

### M4. No weekend/holiday guard

The brainstorm identified "Weekend/holiday gaps" as a risk with mitigation via `dayofweek` check. The implementation has no such guard. On weekends or holidays with partial sessions, the state machine could enter London state on Friday evening (for Sunday's first data) or behave unexpectedly.

**Recommendation**: Add guard:
```pine
bool isTradingDay = dayofweek(time, i_tz) != dayofweek.saturday and dayofweek(time, i_tz) != dayofweek.sunday
```
And gate `londonStart` with `isTradingDay`.

### M5. EOD session string is fragile

**Line 230**: `eodSess = sessionStr(i_eodCutoff, 2359)` creates `"1500-2359"`. The `isEOD` check on line 232 (`eodTime and not eodTime[1]`) fires on the first bar entering this window. This works but if the user sets `i_eodCutoff` to, say, 1545 and there's no bar exactly at 15:45 on M15 (M15 bars are at :00, :15, :30, :45 so 15:45 is valid), it's fine. But if they enter a non-M15-aligned time like 1510, the session start detection may trigger on the next available bar (15:15), which is earlier than expected.

**Recommendation**: Add tooltip to input noting times should be M15-aligned, or round to nearest 15-min boundary.

### M6. Dashboard table recreation on every `barstate.islast`

**Lines 283-320**: The table is deleted and recreated from scratch on every last-bar update. This causes flicker on live charts.

**Recommendation**: Use `var table` and only update cell contents instead of recreating:
```pine
var table dash = table.new(position.top_right, 2, 6, ...)
if i_showDashboard and barstate.islast
    table.cell(dash, 0, 0, "Filter", ...)
    // ... update cells only
```

---

## Low Priority

### L1. No `syminfo.ticker` validation

The indicator title says "US30" but nothing prevents it from being applied to other symbols. Consider adding a runtime note (not a hard block, since users may want it on similar indices).

### L2. Magic numbers in dashboard

Lines 286-320: Column/row indices are hardcoded. Fine for a small table but could use named constants if the table grows.

### L3. Trade level labels at fixed offset

**Lines 219-227**: Labels are placed at `bar_index + 5`. On live charts this is fine, but on historical bars these labels overlap with subsequent price action. Minor visual issue.

### L4. No input validation on HHMM values

Users could enter invalid times like `9999` or `100` (1:00 AM with no leading zero handling). The `str.format` with `0000` pattern handles padding, but `sessionStr(100, 9999)` would produce `"0100-9999"` which is invalid. PineScript would ignore it silently (session never matches).

---

## PineScript v6 Compatibility

| Check | Status | Notes |
|-------|--------|-------|
| `//@version=6` declaration | OK | Line 6 |
| `indicator()` instead of `study()` | OK | Line 7 |
| `input.*()` typed functions | OK | All inputs use v6 syntax |
| `request.security()` | OK | Correct v6 name (not `security()`) |
| `barmerge.lookahead_on` | OK | Correct enum |
| `ta.*` namespace | OK | `ta.ema`, `ta.change` properly namespaced |
| `math.*` namespace | OK | `math.max`, `math.min` |
| `str.*` namespace | OK | `str.format`, `str.tostring` |
| `color.new()` | OK | Correct v6 syntax |
| `box.*`, `line.*`, `label.*`, `table.*` | OK | Correct v6 namespaces |
| `format.mintick` | OK | Valid v6 constant |
| Type annotations on vars | OK | Explicit types used |
| `barstate.isconfirmed` | OK | Valid v6 |
| `alertcondition()` at global scope | OK | Lines 323-332, all at global scope |

**No v5-isms detected.** Code is clean v6 throughout.

---

## Non-Repainting Analysis

| Data Source | Pattern | Repaints? | Notes |
|-------------|---------|-----------|-------|
| H4 EMA (line 48-51) | `[1]` + `lookahead_on` | No | Correct — uses previous confirmed H4 bar |
| H1 Close (line 57-58) | `close[1]` + `lookahead_on` | No | Correct — uses previous confirmed H1 bar |
| Sweep detection (line 186) | `barstate.isconfirmed` | No | Only fires on confirmed bars |
| Session detection (lines 64-67) | `time()` | No | Time-based, deterministic |
| Trade management (lines 238-261) | No HTF, uses current bar high/low | Caution | SL/TP checked on current bar — intra-bar SL/TP hits will repaint on same bar. This is acceptable for indicators (not strategies) since entry already confirmed. |

**Verdict**: Non-repainting for signals. Trade exit detection has standard intra-bar evaluation (acceptable).

---

## Drawing Management

| Drawing Type | Max Allowed | Created Per Day | Cleanup | Risk |
|--------------|-------------|----------------|---------|------|
| Box | 20 | 1 | Delete on new day | OK — but see M1 |
| Lines | 50 | 5 (2 London + 3 trade) | Delete on new day | OK |
| Labels | 50 | 3 | Delete on new day | OK |
| Table | 1 | 1 (recreated) | Delete + recreate | See M6 |

**No exhaustion risk** given the max limits and 1-trade-per-day design. Even without deletion, 20 boxes = 20 days of history, 50/5 = 10 days of lines.

---

## Positive Observations

- Clean section organization with clear comment headers
- Proper state machine with explicit state constants
- Good use of `var` for persistent state
- `barstate.isconfirmed` correctly gates sweep detection
- Input grouping is user-friendly
- Filter enable/disable toggles add flexibility
- `tradedToday` flag correctly enforces one-trade-per-day
- Trade lines stop extending when trade resolves (lines 264-279) — nice UX detail
- Line count (~333 with comments) is reasonable for a single PineScript file

---

## Recommended Actions (Priority Order)

1. **[HIGH]** Remove `barstate.isconfirmed` from `alertcondition()` calls (H1)
2. **[HIGH]** Add comment documenting H1 momentum offset (H4)
3. **[HIGH]** Add comment on SL-before-TP bias (H3)
4. **[MED]** Fix new-day detection to use `i_tz` timezone (M2)
5. **[MED]** Add weekend guard (M4)
6. **[MED]** Remove drawing deletion on new day — let FIFO handle it (M1)
7. **[MED]** Optimize dashboard to update cells instead of recreate (M6)
8. **[LOW]** Add input tooltips for HHMM alignment (M5, L4)

---

## Metrics

- **Type Coverage**: N/A (PineScript is dynamically typed; explicit type annotations used where possible)
- **Test Coverage**: 0% (cannot unit test PineScript; manual validation required per phase-08)
- **Linting Issues**: 0 static issues detected (no v5 syntax, no obvious compile errors)
- **Line Count**: 333 (within acceptable range for single-file PineScript)

---

## Unresolved Questions

1. Is the 1-hour offset in H1 momentum capture (comparing 02:00 vs 08:00 instead of 03:00 vs 09:00) acceptable to the strategy author, or should it be corrected with a different approach?
2. Should multi-day drawing history be preserved (remove deletion) or is clean-slate-per-day the desired behavior?
3. Should there be a hard block or warning when applied to non-US30 symbols?
4. The brainstorm mentions `dayofweek` guard for weekends — is this required for the initial version?
