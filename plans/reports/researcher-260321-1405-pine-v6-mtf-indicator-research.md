# Research Report: Pine Script v6 for MTF Indicator (M15 + H1/H4)

Research timestamp: 2026-03-21

## TL;DR
- Pine v6 tightened types, changed some defaults, and enables dynamic requests by default.
- For stable HTF values on M15, use `request.security(..., expr[1], lookahead=barmerge.lookahead_on)`.
- For EST/EDT-safe logic, use IANA timezone (`"America/New_York"`), not fixed `GMT-4/-5`.
- Drawing objects hit hard limits fast; reuse IDs + explicit delete queues.
- `alertcondition()` messages are const; use placeholders for runtime values.

## 1) v6 syntax changes vs v5 (indicator, type system, methods)

### High-impact changes
- `//@version=6` required.
- Dynamic requests behavior changed: v6 supports dynamic `request.*()` by default; can disable via `indicator(..., dynamic_requests=false)`.
- Type strictness:
  - No implicit numeric -> bool cast in conditions.
  - `bool` is strict two-state; no bool `na` behavior pattern.
  - `na()/nz()/fixnan()` no longer for `bool`.
- Strategy changes:
  - `when` removed in order funcs; gate with `if`.
  - default margins changed (`margin_long/short=100`).
- Misc behavior gotchas:
  - `plot(..., offset=...)` cannot be series.
  - `transp` removed; use `color.new(base, alpha)`.
  - `for` loop boundary (`to_num`) re-evaluates each iteration.

### Minimal pattern
```pine
//@version=6
indicator("My v6 indicator", overlay=true)

bool cond = bool(bar_index)  // explicit cast if needed
plot(close, color = cond ? color.green : color.red)
```

## 2) request.security() best practices for H1/H4 from M15

### Non-repainting HTF pattern (recommended)
```pine
h1Close = request.security(syminfo.tickerid, "60", close[1], lookahead=barmerge.lookahead_on)
h4Close = request.security(syminfo.tickerid, "240", close[1], lookahead=barmerge.lookahead_on)
```
Why: stable timing on historical + realtime alignment; avoids future leakage.

### Gaps choice
- `gaps_off` (default): forward-fills HTF value between confirmations.
- `gaps_on`: returns `na` between HTF updates (useful when you want explicit step boundaries).

### Reduce request count with tuple
```pine
[h1O,h1H,h1L,h1C] = request.security(syminfo.tickerid, "60", [open[1],high[1],low[1],close[1]], lookahead=barmerge.lookahead_on)
```

### Guardrail
```pine
tf = input.timeframe("60", "HTF")
if timeframe.in_seconds() > timeframe.in_seconds(tf)
    runtime.error("HTF must be >= chart TF")
```

## 3) Session time handling (specific EST/EDT time on any exchange)

### Core rule
Use IANA timezone strings (`"America/New_York"`) for auto DST switching.
Do not hardcode `GMT-4`/`GMT-5` unless explicitly needed.

### Detect exact NY local time, independent of symbol exchange TZ
```pine
nyH = hour(time, "America/New_York")
nyM = minute(time, "America/New_York")
is0300NY = nyH == 3 and nyM == 0
is0900NY = nyH == 9 and nyM == 0
```

### Session window in NY time
```pine
inNY = not na(time(timeframe.period, "0930-1600", "America/New_York"))
```

## 4) Drawing objects in v6 (limits + cleanup)

### Limits (practical)
- Hard IDs: line/box/label up to 500 each, polyline 100.
- Display default: last 50 drawings shown unless you raise `max_*_count`.
- Tables: max 9 (one per position slot).

### Indicator declaration with limits
```pine
indicator("Draw-heavy", overlay=true, max_lines_count=300, max_boxes_count=200, max_labels_count=200)
```

### Cleanup pattern (queue + delete)
```pine
var line[] lines = array.new_line()

if newSignal
    l = line.new(bar_index, close, bar_index+1, close)
    array.push(lines, l)

maxKeep = 150
if array.size(lines) > maxKeep
    old = array.shift(lines)
    line.delete(old)
```

Notes:
- Reusing IDs (`var` + `set_*`) is cheaper than creating every bar.
- Setting coords/text/color to `na` does not necessarily free ID budget; explicit delete for cleanup.

## 5) alertcondition() in v6

### Usage
```pine
alertcondition(condition, title, message)
```
- `condition`: series bool.
- `title/message`: const strings.
- Put calls in global scope.

### Runtime values in message
Message is const; use placeholders:
- `{{close}}`, `{{ticker}}`, `{{time}}`, `{{plot_0}}`, `{{plot("RSI")}}`.

### Stable alert pattern
```pine
longCond = ta.crossover(close, ta.ema(close, 20)) and barstate.isconfirmed
alertcondition(longCond, "Long", "Long {{ticker}} close={{close}}")
```
Also set alert UI to Once Per Bar Close.

## 6) Store/compare H1 closes at 03:00 vs 09:00 EST/EDT

### Recommended pattern
Compute NY clock on chart bars; capture last confirmed H1 close when NY time matches.

```pine
//@version=6
indicator("H1 03:00 vs 09:00 NY", overlay=false)

h1Close = request.security(syminfo.tickerid, "60", close[1], lookahead=barmerge.lookahead_on)
nyH = hour(time, "America/New_York")
nyM = minute(time, "America/New_York")

var float close0300 = na
var float close0900 = na

if nyH == 3 and nyM == 0
    close0300 := h1Close
if nyH == 9 and nyM == 0
    close0900 := h1Close

diff = close0900 - close0300
plot(diff, "09:00-03:00")
```

Tip: if you only want one value per NY day, reset vars on NY day change using time-derived date in NY timezone.

## 7) input() group organization in v6

### Pattern
- Use `group` for section blocks.
- Use `inline` for same-row compact controls.
- Use constants for group names to keep DRY.

```pine
const string G_MAIN = "Main"
const string G_MTF  = "MTF"

src = input.source(close, "Source", group=G_MAIN, inline="a")
len = input.int(20, "Len", group=G_MAIN, inline="a")

tf1 = input.timeframe("60", "H1", group=G_MTF, inline="tf")
tf2 = input.timeframe("240", "H4", group=G_MTF, inline="tf")
```

## 8) request.security() gotchas for HTF EMA

### Biggest gotcha
These are NOT equivalent:

Correct HTF EMA (computed in HTF context):
```pine
h1Ema20 = request.security(syminfo.tickerid, "60", ta.ema(close, 20)[1], lookahead=barmerge.lookahead_on)
```

Different behavior (EMA on merged LTF series):
```pine
h1Ema20_wrong = ta.ema(request.security(syminfo.tickerid, "60", close), 20)
```

### Additional gotchas
- Mixed lookahead/offset combos cause subtle repaint.
- Using unconfirmed HTF bar values for signals can flip intrabar.
- Excessive duplicated security calls increases overhead; bundle with tuples.

## Practical baseline template (safe defaults)
```pine
//@version=6
indicator("MTF baseline", overlay=true)

h1C = request.security(syminfo.tickerid, "60", close[1], lookahead=barmerge.lookahead_on)
h4C = request.security(syminfo.tickerid, "240", close[1], lookahead=barmerge.lookahead_on)
h1E = request.security(syminfo.tickerid, "60", ta.ema(close, 20)[1], lookahead=barmerge.lookahead_on)

plot(h1C, "H1 Close", color=color.orange)
plot(h4C, "H4 Close", color=color.purple)
plot(h1E, "H1 EMA20", color=color.teal)

sig = ta.crossover(h1C, h1E) and barstate.isconfirmed
alertcondition(sig, "H1 Cross Up", "{{ticker}} H1 close crossed H1 EMA20. close={{close}}")
```

## Unresolved questions
- Should 03:00/09:00 capture use NY session calendar day or exchange trading day for symbols with overnight sessions?
- Do you want realtime-intrabar responsiveness (may repaint) or strict bar-close stability only?