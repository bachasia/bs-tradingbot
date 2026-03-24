# Code Review: London Sweep v2 (cTrader Indicator + Bot)

**Reviewer:** code-reviewer | **Date:** 2026-03-24
**Files:** `ctrader/LondonSweepIndicator.cs` (521 LOC), `ctrader/LondonSweepBot.cs` (335 LOC)
**Focus:** Strategy correctness, cTrader API, non-repainting, risk calc, state machine, edge cases, FTMO compliance

---

## Overall Assessment

Solid v2 implementation. The pre-sweep check (09:00-09:29) is now correctly implemented as a dedicated state. State machine is well-structured with 7 states. The shift from limit orders (v1) to market orders (v2) is correct for the strategy. However, there are **several critical and high-priority issues** that need attention before live trading, particularly around risk calculation, H1 momentum timing, and a state transition gap.

---

## CRITICAL Issues

### C1. Risk Calculation Formula is Wrong for US30

**File:** `LondonSweepBot.cs` line 195
**Code:** `double volume = riskAmount / (slDistance / Symbol.PipSize) / Symbol.PipValue;`

**Problem:** For US30 on cTrader, `PipSize` is typically 1.0 and `PipValue` depends on lot size. The formula `riskAmount / slPips / PipValue` is the standard forex lot-sizing formula, but for indices like US30:
- `Symbol.PipSize` for US30 varies by broker (could be 0.01, 0.1, or 1.0)
- `Symbol.PipValue` is the value of 1 pip for 1 unit (not 1 lot)
- If `PipSize = 0.01` and SL distance is 50 points, `slDistance / PipSize = 5000 pips`, resulting in near-zero volume

**Impact:** Could place drastically wrong position sizes -- either too large (blow FTMO account) or too small (near-zero lots rejected).

**Fix:** Use point-based calculation instead:
```csharp
// Safer: calculate directly in monetary terms
double riskAmount = Account.Balance * (RiskPercent / 100.0);
double slPoints = Math.Abs(entryPrice - slPrice);
// For US30: 1 unit = $1 per point movement (verify with Symbol.TickValue / Symbol.TickSize)
double valuePerPoint = Symbol.TickValue / Symbol.TickSize;
double volume = riskAmount / (slPoints * valuePerPoint);
volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
```

**Recommendation:** Add a startup log that prints `PipSize`, `PipValue`, `TickSize`, `TickValue` so the user can verify the calculation is correct for their specific broker's US30 symbol.

### C2. H1 Momentum Capture Timing Is Incorrect

**File:** `LondonSweepIndicator.cs` lines 314-326

**Problem:** `CaptureH1Close0300()` is called when the first London bar is detected (state Idle -> London transition). `CaptureH1Close0800()` is called when London ends (state London -> RangeDone). Both use `_h1Bars.ClosePrices.Last(1)` which returns the most recently **closed** H1 bar at that moment -- not necessarily the bar that closed at 03:00 or 08:00 EST.

**Strategy requires:**
- Close of H1 candle at 03:00 EST (i.e., the 02:00-03:00 candle)
- Close of H1 candle at 08:00 EST (i.e., the 07:00-08:00 candle)

**What actually happens:**
- `CaptureH1Close0300()` runs when the M15 bar at 03:00 or 03:15 closes. The H1 `Last(1)` at that point is indeed the 02:00-03:00 candle -- this is **approximately correct** but only if the indicator processes the 03:15 bar (first London bar on M15).
- `CaptureH1Close0800()` runs when London ends (first M15 bar after 09:00). The H1 `Last(1)` at that time is the 08:00-09:00 candle, **not** the 07:00-08:00 candle the strategy requires.

**Impact:** Momentum filter uses wrong candle. Could allow trades that should be blocked, or block valid ones.

**Fix:** Look up H1 bars by specific time instead of relying on `Last(1)`:
```csharp
private void CaptureH1Close0800()
{
    // Need the candle that closed at 08:00 = the 07:00-08:00 candle
    // At 09:00, Last(1) is the 08:00-09:00 candle, Last(2) is 07:00-08:00
    if (_h1Bars.Count > 2)
        _h1Close0800 = _h1Bars.ClosePrices.Last(2);
}
```
Or better: use `_h1Bars.OpenTimes` to find the exact bar by timestamp.

### C3. Pre-Sweep Check Threshold Missing

**File:** `LondonSweepIndicator.cs` lines 252-253
**Code:** `if (prevHigh > _londonHigh || prevLow < _londonLow)`

**Problem:** The pre-sweep check invalidates if price merely touches London High/Low by even a fraction of a point. But the actual sweep detection (line 342) requires a 5-point threshold (`sweepDepth >= SweepThreshold`).

**Strategy says (Step 5.1):** "If price **already swept** past High or Low London in 09:00-09:29 -> no trade."

Sweeping implies actually trading through the level, not just touching it. The current code is stricter than necessary -- any tick above London High invalidates, even if it's 0.1 points. This is arguably a conservative choice but inconsistent with the sweep definition used later.

**Impact:** May produce false invalidations. On volatile days, price briefly poking 0.5 points above London High during 09:00-09:29 would kill the setup.

**Recommendation:** Add a small tolerance or use the same SweepThreshold:
```csharp
if (prevHigh > _londonHigh + SweepThreshold || prevLow < _londonLow - SweepThreshold)
```

---

## HIGH Priority Issues

### H1. State Transition Gap: RangeDone Can Skip PreSweepCheck

**File:** `LondonSweepIndicator.cs` lines 238-267

**Scenario:** If the London session ends at 09:00 and the M15 bar timestamps are [08:45, 09:00, 09:15, 09:30...]:
1. Bar at 09:00 (prev bar time): `hhmm = 900`. London state check `InRange(900, 300, 900)` = false -> transitions to RangeDone.
2. Same bar: `InRange(900, 900, 930)` = true -> transitions to PreSweepCheck. Good.
3. Bar at 09:15: `InRange(915, 900, 930)` = true -> PreSweepCheck logic runs. Good.
4. Bar at 09:30: `InRange(930, 930, 1100)` = true -> transitions to SweepWindow. Good.

**But:** If London range check (`_rangeSize < MinRange`) triggers `Done` on the 09:00 bar (line 227), the flow correctly exits. However, the transition from RangeDone -> PreSweepCheck (line 239) only fires when `InRange(hhmm, 900, 930)`. If for some reason the first post-London bar is at 09:30 (e.g., data gap), PreSweepCheck is entirely skipped. The fallback on line 262 (`_state == SessionState.RangeDone`) catches this, which is good.

**But there's a subtler bug:** On the same bar where London -> RangeDone fires (hhmm=900), the code continues executing and hits the PreSweepCheck transition (line 239) AND the PreSweepCheck check (line 246-258) in the same iteration. This means the very first pre-sweep check uses `Bars.HighPrices[prev]` / `Bars.LowPrices[prev]` which is the bar that just closed London. This bar's high/low were already part of the London range, so `prevHigh > _londonHigh` will never be true for it. **Not a bug per se, but a wasted check.**

### H2. Non-Repainting Risk in H4 Filter

**File:** `LondonSweepIndicator.cs` line 294
**Code:** `double fast = _h4EmaFast.Result.Last(1);`

**Problem:** `Last(1)` on a multi-timeframe indicator returns the value from the most recently closed H4 bar. This is called on **every M15 bar** (line 197, `UpdateH4Filter()`). During an H4 candle (e.g., 08:00-12:00 EST), the H4 EMA values from `Last(1)` are stable (previous closed H4 bar). This is correct and non-repainting.

**However:** The `UpdateH4Filter()` is called unconditionally every bar, including during London session hours (03:00-09:00) when the H4 filter result doesn't matter yet. Not a bug but unnecessary computation.

**Status:** Correctly non-repainting. The indicator uses `Last(1)` throughout.

### H3. ExecuteMarketOrder Uses Pips for SL/TP, Not Absolute Prices

**File:** `LondonSweepBot.cs` lines 205-212
**Code:** `ExecuteMarketOrder(tradeType, SymbolName, volume, label, slPips, tpPips);`

**Problem:** `ExecuteMarketOrder` with SL/TP in pips sets them relative to the **fill price**, not the indicator's calculated entry price. Since this is a market order, the fill price could differ from `entryPrice` (which is the previous candle's close) due to:
- Spread (always present on US30)
- Slippage during volatile NY open

If the spread is 3 points and `PipSize = 1.0`, the actual SL level shifts by 3 points from the intended level. For a typical SL of ~55 points on US30, this is a ~5% error.

**Impact:** SL/TP placement slightly off from strategy spec. Cumulative impact on win rate and R:R.

**Fix:** Use `ModifyPosition()` after fill to set exact price levels, or accept the slippage as acceptable for market orders.

### H4. Daily PnL Check Uses Balance, Not Equity

**File:** `LondonSweepBot.cs` line 247
**Code:** `double dailyPnL = Account.Balance - _dayStartBalance;`

**Problem:** `Account.Balance` does not include unrealized P&L from open positions. If this bot is the only one trading, the balance only changes when a position closes. So before the first trade of the day, `dailyPnL` will always be 0 (or reflect only closed positions from earlier that day).

FTMO measures daily loss including open positions (equity-based). The drawdown check (line 257) correctly uses `Account.Equity`, but the daily loss check does not.

**Fix:**
```csharp
double dailyPnL = Account.Equity - _dayStartBalance;
```

### H5. Indicator Output Written at `prev` Index -- Bot Reads `Last(1)` Correctly?

**File:** `LondonSweepIndicator.cs` line 372: `LongSignal[prev] = longSweep ? 1.0 : double.NaN;`
**File:** `LondonSweepBot.cs` line 153: `double longVal = _indicator.LongSignal.Last(1);`

**Analysis:** In the indicator, `prev = index - 1` where `index` is the current (forming) bar. So signals are written to the just-closed bar. In the bot, `OnBarClosed()` fires after a bar closes, and `Last(1)` reads the most recently closed bar. These should align.

**Potential issue:** Timing of indicator `Calculate()` vs bot `OnBarClosed()`. cTrader processes the indicator's Calculate first, then the bot's OnBarClosed. Since the indicator writes to `prev` (the bar that just closed) and the bot reads `Last(1)` (same bar), this should work. **Confirmed correct.**

---

## MEDIUM Priority Issues

### M1. DST Handling Not Addressed

**Problem:** The strategy uses Eastern Standard Time (EST) throughout. cTrader's `TimeZone = TimeZones.EasternStandardTime` attribute adjusts chart times, but EST != EDT. During US Daylight Saving Time (March-November), real EST is UTC-5 while EDT is UTC-4.

The `TimeZones.EasternStandardTime` in cTrader is actually equivalent to "America/New_York" (automatically adjusts for DST). This is a naming quirk in the cTrader API.

**Verify:** Confirm this is the case in cTrader documentation. If `EasternStandardTime` truly means fixed UTC-5, London hours will be off by 1 hour during summer.

### M2. London Range Initialized from Single Bar

**File:** `LondonSweepIndicator.cs` lines 203-204
```csharp
_londonHigh = Bars.HighPrices[prev];
_londonLow = Bars.LowPrices[prev];
```

**Problem:** On the first London bar, high and low are initialized from that single bar. If Idle -> London triggers on a bar where `hhmm = 300` but the actual first London candle is the one closing at 03:15, the 03:00-03:15 bar (prev) is correctly captured. However, on M15, the bar opening at 03:00 closes at 03:15, and `barTime = Bars.OpenTimes[prev]` is 03:00. `InRange(300, 300, 900)` = true. Correct.

**Edge case:** If market data is missing the 03:00 bar (data gap), the first London bar might be 03:15 or 03:30. The range would still be tracked from that point, missing early price action. Acceptable for live trading (data gaps are rare).

### M3. `_dayCount` Counter Never Reset

**File:** `LondonSweepIndicator.cs` line 118, 186

`_dayCount` increments on every date change (including weekends) and is used as a suffix for chart object names. It never resets, so over a long backtest, object names grow. Not a functional issue but chart objects accumulate unbounded.

### M4. Weekend Reset Edge Case

**File:** `LondonSweepIndicator.cs` lines 183-194

The code resets on date change (line 183-189) then returns early on Saturday/Sunday (line 193-194). If Friday's last bar is at 16:45 and the next bar processed is Sunday at 18:00, the reset fires for Sunday, then returns. Monday's first bar triggers another reset. Two resets is harmless but indicates the weekend guard could be cleaner.

### M5. London Box Drawing Called Every Bar

**File:** `LondonSweepIndicator.cs` line 215: `DrawLondonBox(prev);`

Every M15 bar during London session redraws the rectangle. On M15, that's 24 redraws (6 hours x 4 bars/hour). Not a performance problem but technically redundant draws.

### M6. Indicator Trade Management Is Visual-Only But Could Confuse

**File:** `LondonSweepIndicator.cs` lines 384-412 (`ManageTrade`)

The indicator tracks SL/TP hits for visual backtest display. This is independent of the bot's actual trade management. In live trading, the bot handles real SL/TP via the broker, while the indicator simulates it visually. This dual tracking could show different results if slippage occurs.

**Recommendation:** Add a comment clarifying this is visual-only for backtesting.

---

## LOW Priority Issues

### L1. `using System.Linq` May Be Unnecessary in Indicator

The indicator doesn't use any LINQ methods. Could be removed for cleanliness.

### L2. Magic Numbers in DrawSweepArrow

**File:** `LondonSweepIndicator.cs` lines 459-461
`Bars.LowPrices[idx] - 10` and `+ 10` -- the offset of 10 points is hardcoded. On US30 this is fine but would look wrong on other instruments.

### L3. Dashboard String Concatenation

**File:** `LondonSweepIndicator.cs` lines 499-517
Could use `StringBuilder` for efficiency but this runs infrequently (tick-level on a single overlay). Negligible impact.

---

## Strategy Correctness Checklist

| Rule | Status | Notes |
|------|--------|-------|
| London Range 03:00-09:00 EST | PASS | Correctly tracked via state machine |
| Range >= 50 pts filter | PASS | Line 225, transitions to Done if too small |
| H4 EMA20 > EMA50 trend filter | PASS | Non-repainting via Last(1) |
| H1 momentum (03:00 vs 08:00 close) | **FAIL** | C2: Captures 08:00-09:00 close instead of 07:00-08:00 |
| Pre-sweep check 09:00-09:29 | PASS (with note) | C3: Threshold too strict (0 pts vs 5 pts) |
| Sweep detection 09:30-11:00 | PASS | Correct wick + close logic |
| Same-candle sweep + reversal | PASS | Single bar check on prev |
| Entry = sweep candle close | PASS | `_entryPrice = prevClose` |
| SL = sweep low/high +/- 8 pts | PASS | Lines 347, 361 |
| TP = entry +/- 0.65 x range | PASS | Lines 348, 362 |
| Max 1 trade/day | PASS | `_tradedToday` flag + bot `MaxTradesPerDay` |
| EOD cutoff 15:00 EST | PASS | Both indicator and bot implement this |
| 1% risk sizing | **FAIL** | C1: Formula may be wrong for US30 |
| Market order (not limit) | PASS | v2 correctly uses ExecuteMarketOrder |

## State Machine Verification

```
Idle -> London           (03:00 bar detected)       PASS
London -> RangeDone      (first bar >= 09:00)       PASS
London -> Done           (range < 50 pts)           PASS (via RangeDone)
RangeDone -> PreSweepCheck (09:00-09:29 bar)        PASS
PreSweepCheck -> Done    (early sweep detected)     PASS
PreSweepCheck -> SweepWindow (09:30 bar, no sweep)  PASS
RangeDone -> SweepWindow (fallback if no pre-sweep bars) PASS
SweepWindow -> InTrade   (sweep detected)           PASS
SweepWindow -> Done      (past 11:00, no sweep)     PASS
InTrade -> Done          (SL/TP/EOD hit)            PASS
```

All 7 states accounted for. Transitions are correct.

## Non-Repainting Verification

| Component | Method | Status |
|-----------|--------|--------|
| M15 strategy logic | Uses `Bars[prev]` where prev = index-1 | PASS |
| H4 EMA filter | `_h4EmaFast.Result.Last(1)` | PASS |
| H1 momentum | `_h1Bars.ClosePrices.Last(1)` | PASS |
| Signal output | Written to `prev` index | PASS |
| Bot signal read | `Last(1)` in OnBarClosed | PASS |

All signals use closed-bar data. No current-bar references in strategy logic.

## FTMO Compliance

| Guard | Status | Notes |
|-------|--------|-------|
| Daily loss limit | **PARTIAL** | H4: Uses Balance not Equity |
| Max drawdown | PASS | Uses Equity correctly |
| Max trades/day | PASS | Configurable, default 1 |
| EOD position close | PASS | 15:00 EST cutoff |
| 80% threshold trigger | PASS | Conservative approach |

---

## v1 -> v2 Improvements Verified

| Improvement | Implemented | Correct |
|-------------|-------------|---------|
| Pre-sweep check (09:00-09:29) | Yes | Yes (with C3 threshold note) |
| 7-state machine | Yes | Yes |
| Market order (not limit) | Yes | Yes |
| 1% risk auto-calc | Yes | Formula needs fix (C1) |
| Cleaner code structure | Yes | Yes |
| Removed PendingOrders.Filled handler | Yes | Correct (no longer needed) |

---

## Recommended Actions (Priority Order)

1. **[CRITICAL] Fix risk calculation** (C1) -- Use TickValue/TickSize for US30-safe sizing. Add startup diagnostic log.
2. **[CRITICAL] Fix H1 momentum capture** (C2) -- Use `Last(2)` or timestamp lookup for 07:00-08:00 candle close.
3. **[CRITICAL] Align pre-sweep threshold** (C3) -- Use SweepThreshold for consistency.
4. **[HIGH] Fix daily PnL to use Equity** (H4) -- FTMO measures unrealized losses.
5. **[HIGH] Document market order slippage impact** (H3) -- Consider ModifyPosition for exact SL/TP.
6. **[MEDIUM] Verify DST behavior** (M1) -- Test with cTrader's TimeZones.EasternStandardTime.
7. **[LOW] Minor cleanups** (L1-L3).

---

## Positive Observations

- Clean state machine design with explicit transitions
- Good separation of concerns (indicator = signals, bot = execution)
- Non-repainting discipline consistently maintained
- FTMO guards with 80% threshold is a smart safety margin
- AutoTrade toggle allows signal-only mode for manual trading
- Dashboard provides real-time strategy state visibility
- Proper error handling with try-catch around order execution
- Label-based position filtering prevents interference with other bots

---

## Unresolved Questions

1. What is `Symbol.PipSize` for US30 on the specific FTMO cTrader broker? This is critical for C1.
2. Does `TimeZones.EasternStandardTime` auto-adjust for DST in cTrader? (M1)
3. Should the pre-sweep check use any threshold at all, or is "any touch" the intended behavior? (C3 -- needs strategy owner input)
4. Is there a need for trailing stop or breakeven logic? The strategy doc doesn't mention it.
5. The indicator param order in `GetIndicator<>()` must exactly match the `[Parameter]` declaration order -- has this been verified to compile?
