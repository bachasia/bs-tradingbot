# Code Review: cTrader London Sweep Port

- **Date**: 2026-03-24
- **Reviewer**: code-reviewer
- **Scope**: LondonSweepIndicator.cs (387 LOC), LondonSweepBot.cs (323 LOC)
- **Reference**: london-sweep-indicator.pine (PineScript v6)

## Overall Assessment

Solid port with correct architecture (indicator + cBot separation), proper bar-close detection pattern, and accurate logic parity with PineScript. **Two critical compilation errors** (wrong `SoundType` enum values) must be fixed before build. Several high-priority API and logic issues also need attention.

---

## Critical Issues (will not compile)

### C1. `SoundType.Good` and `SoundType.Buzz` do not exist

**File**: `LondonSweepBot.cs` lines 175, 188, 194, 199, 286, 310

The `SoundType` enum has: `Positive`, `Negative`, `Announcement`, `DoorBell`, `Confirmation`. Neither `Good` nor `Buzz` exist.

**Fix**: Replace all occurrences:
```csharp
// Before
PlaySound(SoundType.Good);
PlaySound(SoundType.Buzz);

// After
PlaySound(SoundType.Positive);
PlaySound(SoundType.Negative);
```

Affected lines:
- L175: `PlaySound(SoundType.Buzz)` -> `PlaySound(SoundType.Negative)`
- L188: `PlaySound(SoundType.Buzz)` -> `PlaySound(SoundType.Negative)`
- L194: `PlaySound(SoundType.Buzz)` -> `PlaySound(SoundType.Negative)`
- L199: `PlaySound(SoundType.Good)` -> `PlaySound(SoundType.Positive)`
- L286: `PlaySound(SoundType.Buzz)` -> `PlaySound(SoundType.Negative)`
- L310: `SoundType.Good` -> `SoundType.Positive`, `SoundType.Buzz` -> `SoundType.Negative`

**Ref**: [SoundType - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Notifications/SoundType/)

---

## High Priority

### H1. `PlaceLimitOrder` overload is obsolete (deprecation warning, potential future breakage)

**File**: `LondonSweepBot.cs` lines 222-223

The pips-based `PlaceLimitOrder(tradeType, symbol, volume, price, label, slPips, tpPips)` overload is **obsolete** in newer cTrader versions. The new API requires a `ProtectionType` parameter.

**Current code**:
```csharp
var result = PlaceLimitOrder(tradeType, SymbolName, volume,
    entryPrice, label, slPips, tpPips);
```

**Recommended fix** (absolute prices, avoids pip conversion entirely):
```csharp
var result = PlaceLimitOrder(tradeType, SymbolName, volume,
    entryPrice, label, Math.Abs(entryPrice - slPrice) / Symbol.PipSize,
    Math.Abs(tpPrice - entryPrice) / Symbol.PipSize,
    ProtectionType.Relative);
```

**Or even simpler** (absolute prices, no pip math needed):
```csharp
var result = PlaceLimitOrder(tradeType, SymbolName, volume,
    entryPrice, label, slPrice, tpPrice, ProtectionType.Absolute);
```

If using absolute prices, remove the `slPips`/`tpPips` computation on lines 209-210 entirely.

**Note**: `ProtectionType` has been reported to crash in cTrader Cloud environments. If targeting cloud, keep the obsolete overload but add a `#pragma warning disable CS0618` suppression with a comment explaining why.

**Ref**: [cTrader Forum - ProtectionType](https://community.ctrader.com/forum/ctrader-algo/46005/)

### H2. FTMO drawdown calculation is inverted

**File**: `LondonSweepBot.cs` line 190

```csharp
double drawdown = Account.Balance - Account.Equity;
```

When a position is underwater, `Equity < Balance`, making this a **positive** number. The comparison `drawdown >= FtmoMaxDrawdown * 0.8` then works. However, this measures **unrealized loss only** — it misses the case where `Account.Balance` has already decreased from realized losses.

FTMO max drawdown is measured from the **initial account balance** (or highest watermark), not daily start balance. The current code uses `Account.Balance` which decreases after realized losses, effectively hiding already-taken losses from the drawdown guard.

**Recommended fix**: Track the initial/highest account equity as the high-water mark:
```csharp
// In field declarations
private double _highWaterMark;

// In OnStart()
_highWaterMark = Account.Balance;

// In OnBarClosed() daily reset
_highWaterMark = Math.Max(_highWaterMark, Account.Balance);

// In FTMO guard
double totalDrawdown = _highWaterMark - Account.Equity;
if (totalDrawdown >= FtmoMaxDrawdown * 0.8)
```

### H3. EOD cutoff in indicator uses `>=` but PineScript uses session-start edge detection

**File**: `LondonSweepIndicator.cs` line 321 vs PineScript lines 228-230

PineScript:
```pine
string eodSess = sessionStr(i_eodCutoff, 2359)
bool eodTime   = not na(time(timeframe.period, eodSess, i_tz))
bool isEOD     = eodTime and not eodTime[1]  // fires ONCE on session start edge
```

C# indicator:
```csharp
if (hhmm >= EodCutoffHHMM && !slHit && !tpHit)
    eodClose = true;
```

The PineScript fires EOD **once** (rising edge). The C# fires on every bar after `EodCutoffHHMM`. Impact is low because state transitions to `Done` on first trigger, but semantically different. The real problem: if SL/TP is hit on the same bar as EOD, PineScript checks the `isEOD` edge only once per day; C# checks `hhmm >=` continuously. Net effect is same due to state guard, so **functionally equivalent** but worth noting.

### H4. `Chart.Objects` LINQ lookup should use `Chart.FindObject()`

**File**: `LondonSweepIndicator.cs` lines 333-338

```csharp
var eLine = Chart.Objects.FirstOrDefault(o => o.Name == "Entry" + sfx) as ChartTrendLine;
```

The idiomatic cAlgo API provides `Chart.FindObject(name)`:
```csharp
var eLine = Chart.FindObject("Entry" + sfx) as ChartTrendLine;
```

This avoids LINQ iteration over all chart objects. Applied to all three lookups (Entry, SL, TP lines).

### H5. Non-repainting: H1 momentum capture timing may differ from PineScript

**File**: `LondonSweepIndicator.cs` lines 197, 218

PineScript uses:
```pine
h1Close = request.security(syminfo.tickerid, "60", close[1], lookahead = barmerge.lookahead_on)
```
This retrieves the **previous confirmed H1 bar's close** at every M15 bar.

The C# code captures `_h1Close0300` and `_h1Close0900` only at London start/end transitions:
```csharp
_h1Close0300 = _h1Bars.ClosePrices.Last(1);  // at London start
_h1Close0900 = _h1Bars.ClosePrices.Last(1);  // at London end
```

`_h1Bars.ClosePrices.Last(1)` returns the **last completed H1 bar's close** at the moment of evaluation. This is functionally equivalent to Pine's `close[1]` with lookahead on. However, the exact bar alignment depends on when Calculate() fires relative to H1 bar boundaries. On an M15 chart, London start (03:00) aligns perfectly with an H1 boundary, but London end (09:00) also aligns. So timing is correct.

**Verdict**: Likely equivalent, but deserves validation during backtesting by comparing H1 momentum values side-by-side with PineScript output.

---

## Medium Priority

### M1. Weekend guard runs AFTER daily reset on same bar

**File**: `LondonSweepIndicator.cs` lines 157-180

When `barDate != _lastDate` on a Saturday bar, the code resets state (line 164: `_state = SessionState.Idle`) then returns. On the next call (still Saturday), the weekend guard (line 179) catches it. But the daily reset already happened, wastefully clearing state. Minor inefficiency, no functional bug.

**Fix**: Move weekend check before daily reset:
```csharp
if (barTime.DayOfWeek == DayOfWeek.Saturday || barTime.DayOfWeek == DayOfWeek.Sunday)
    return;

DateTime barDate = barTime.Date;
if (barDate != _lastDate)
{
    // ... reset
}
```

### M2. PineScript has per-color visual customization; C# uses hardcoded colors

**File**: `LondonSweepIndicator.cs`

PineScript exposes `i_boxColor`, `i_entryColor`, `i_slColor`, `i_tpColor` as user inputs. C# hardcodes `Color.DodgerBlue`, `Color.Red`, `Color.Green`, `Color.FromArgb(40, 0, 100, 255)`.

Not a bug, but reduces user customization. Low effort to add `[Parameter]` color inputs.

### M3. Indicator file exceeds 200-line threshold

`LondonSweepIndicator.cs` is 387 lines. Per project rules, files over 200 lines should be modularized. Consider extracting:
- Dashboard logic (~35 lines) into a partial class or helper
- Drawing helpers into a separate static utility class

`LondonSweepBot.cs` at 323 lines also exceeds the threshold. Consider extracting:
- FTMO risk guard logic
- Order management helpers

### M4. Missing `using System.Collections.Generic` in indicator

**File**: `LondonSweepIndicator.cs`

`System.Linq` is imported but `FirstOrDefault` returns a generic type. Depending on compiler, `System.Collections.Generic` may be needed. Not strictly required if the LINQ import covers it, but safer to include.

### M5. Trade line drawing uses bar index offset `+50` (hardcoded future projection)

**File**: `LondonSweepIndicator.cs` line 290

```csharp
Chart.DrawTrendLine("Entry" + sfx, prevIndex, _entryPrice, prevIndex + 50, _entryPrice, Color.DodgerBlue);
```

Bar index `prevIndex + 50` projects 50 bars into the future. On M15 = 12.5 hours. This is fine for visual purposes but is a magic number. PineScript uses `extend.right` instead. The C# code terminates lines at trade resolution (good), but the initial +50 is arbitrary.

**Suggestion**: Use a named constant:
```csharp
private const int LineFutureProjectionBars = 50;
```

### M6. Bot does not handle the case where indicator signals arrive but limit order is already pending

**File**: `LondonSweepBot.cs`

If a signal fires, a limit order is placed. If the order is not filled and next day a new signal fires, `_tradesToday` is reset to 0 and a new order could be placed while the old one is still pending. The `CancelBotPendingOrders` at EOD mitigates this daily, but between days (e.g., Monday's unfilled order + Tuesday's signal) there's a gap.

**Fix**: Cancel stale pending orders at daily reset:
```csharp
if (barDate != _lastDate)
{
    _lastDate = barDate;
    _tradesToday = 0;
    _eodDone = false;
    _dayStartBalance = Account.Balance;
    CancelBotPendingOrders("New day cleanup");  // Add this
}
```

---

## Low Priority

### L1. Dashboard updates on every tick (performance)

**File**: `LondonSweepIndicator.cs` lines 139-143

`UpdateDashboard()` runs on every call to `Calculate()` before the bar-close gate. On a volatile instrument like US30, this could fire hundreds of times per second. `DrawStaticText` replaces by name so no leak, but unnecessary CPU.

**Suggestion**: Move dashboard update after bar-close logic, or throttle with a timer.

### L2. `_dayCount` used for object naming but not reset

**File**: `LondonSweepIndicator.cs` line 106, 161

`_dayCount` monotonically increases. Used only for unique chart object names. No functional issue, but on very long backtests, names become like `LondonBox_5000`. No impact on correctness.

### L3. Bot label naming could collide across instances

**File**: `LondonSweepBot.cs` line 101

```csharp
private const string _botLabel = "LondonSweep";
```

If two instances run on different symbols, labels collide. Orders from one instance could be cancelled by the other's cleanup. Add symbol name:
```csharp
private string _botLabel;
// In OnStart()
_botLabel = "LondonSweep_" + Symbol.Name;
```

### L4. `Positions.Closed` event handler: `args.Reason` comparison

**File**: `LondonSweepBot.cs` line 310

```csharp
PlaySound(args.Reason == PositionCloseReason.TakeProfit ? SoundType.Good : SoundType.Buzz);
```

`PositionCloseReason.TakeProfit` is correct (API has `TakeProfit`, `StopLoss`, `Closed`, `StopOut`). Sound types need fixing per C1.

---

## Logic Parity with PineScript

| Feature | PineScript | C# Indicator | Match? |
|---------|-----------|--------------|--------|
| Daily reset | `ta.change(dayofweek)` | `barDate != _lastDate` | Yes |
| Weekend skip | `dayofweek.saturday/sunday` | `DayOfWeek.Saturday/Sunday` | Yes |
| London range tracking | `math.max/min` on high/low | `Math.Max/Min` on HighPrices/LowPrices | Yes |
| H4 EMA non-repainting | `ta.ema(close, n)[1]` + lookahead | `_h4EmaFast.Result.Last(1)` | Yes |
| H1 momentum capture | `close[1]` + lookahead at session boundaries | `_h1Bars.ClosePrices.Last(1)` at transitions | Yes* |
| Sweep detection | `barstate.isconfirmed` guard | `index > _lastIndex` bar-close pattern | Yes |
| Long sweep: wick below, close above | `londonLow - low >= thresh && close > londonLow` | Same logic | Yes |
| Short sweep: wick above, close below | `high - londonHigh >= thresh && close < londonHigh` | Same logic | Yes |
| SL/TP/EOD management | SL first (conservative), then TP, then EOD | Same order | Yes |
| Trade-once-per-day | `tradedToday` flag | `_tradedToday` flag | Yes |
| State machine | 6 states (int constants) | 6 states (enum) | Yes |
| London box visual | `box.new` with bgcolor | `DrawRectangle` with `IsFilled` + `Color.FromArgb` | Yes |
| Level lines | `line.new` with `extend.right` | `DrawHorizontalLine` (infinite extend) | Equivalent |
| Trade lines | `line.new` with extend, stopped at resolution | `DrawTrendLine` + manual Time2 cap | Yes |

*H1 momentum: functionally equivalent but bar alignment should be validated empirically.

---

## Edge Cases Found by Scout

1. **DST transition weeks**: `TimeZones.EasternStandardTime` should handle DST auto, but London session times (03:00-09:00 EST) may shift. Manual validation needed around March/November transitions.

2. **Gap bars on US30**: If price gaps through both SL and TP on a single bar (e.g., opening gap), SL is checked first (conservative bias). Both PineScript and C# handle this consistently.

3. **First bar of data**: `prevIndex < 0` guard on line 151 handles this. H4/H1 `Last(1)` may return NaN on insufficient history -- NaN guards on lines 185-186 handle this.

4. **Multiple London sessions in one calendar day**: The `barDate != _lastDate` check resets once per date change. If the broker's server clock differs from EST, a London session could theoretically span two calendar dates. The `TimeZone` attribute mitigates this.

5. **Indicator output index**: Signals are written to `prevIndex` (confirmed bar), not `index` (forming bar). The bot reads `Last(1)`. This means the bot reads the signal one bar after it was written. If indicator writes to `prevIndex` on bar N, and bot reads `Last(1)` on bar N+1 -- this is correct and non-repainting.

6. **Volume normalization**: `Symbol.NormalizeVolumeInUnits` with `RoundingMode.ToNearest` could round down to 0 for very small volumes. The `volume < Symbol.VolumeInUnitsMin` guard on line 213 catches this.

---

## Positive Observations

- Clean indicator/cBot separation following cTrader best practices
- Proper bar-close detection pattern (`index > _lastIndex`) for non-repainting indicator
- All MTF reads use `.Last(1)` consistently -- no repainting risk
- NaN guards throughout (double.IsNaN checks before comparisons)
- Comprehensive logging with `Print()` for debugging
- Event handler cleanup in `OnStop()` (unsubscribe from Positions.Closed, PendingOrders.Filled)
- Error handling with try/catch around order placement and position close
- FTMO risk guards with 80% threshold buffer
- Clean state machine design with enum (improvement over PineScript int constants)

---

## Recommended Actions (Priority Order)

1. **Fix `SoundType` enum values** (C1) -- compilation blocker
2. **Update `PlaceLimitOrder` to use `ProtectionType`** (H1) -- obsolete API
3. **Fix FTMO drawdown to use high-water mark** (H2) -- risk management correctness
4. **Use `Chart.FindObject()` instead of LINQ** (H4) -- idiomatic API
5. **Add stale order cleanup at daily reset** (M6) -- edge case fix
6. **Add bot label uniqueness per symbol** (L3) -- multi-instance safety
7. **Consider modularization** (M3) -- both files exceed 200-line threshold
8. **Add color parameters** (M2) -- feature parity with PineScript

---

## Metrics

- **Type Coverage**: 100% (C# is fully typed)
- **Test Coverage**: 0% (no unit tests; backtesting is the validation path for cTrader algos)
- **Compilation Issues**: 2 critical (SoundType enum), 1 deprecation warning (PlaceLimitOrder)
- **Logic Parity**: ~98% match with PineScript (minor EOD detection semantics, color customization gaps)
- **Lines of Code**: 710 total (387 + 323)

---

## Unresolved Questions

1. Exact pip size for `#US30` on FxPro -- runtime `Symbol.PipSize` check needed
2. Whether `ProtectionType` works in cTrader Cloud (known bug as of early 2025) -- test on target deployment
3. DST behavior validation needed for March/November transition weeks
4. Whether `Chart.DrawStaticText` causes noticeable performance overhead on every tick for US30
5. EMA seeding differences between Pine `ta.ema()` and cTrader `ExponentialMovingAverage` -- may cause 1-2 point drift on H4 values during backtest comparison

---

## API References

- [SoundType - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Notifications/SoundType/)
- [INotifications - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Notifications/INotifications/)
- [ChartTrendLine - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/ChartTrendLine/)
- [ChartRectangle - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/Shapes/ChartRectangle/)
- [ChartStaticText - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/ChartStaticText/)
- [PositionCloseReason - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Trading/Positions/PositionCloseReason/)
- [Chart objects and drawings guide](https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/)
- [PlaceLimitOrder - cTrader Algo](https://ctrader.com/api/reference/robot/placelimitorder)
