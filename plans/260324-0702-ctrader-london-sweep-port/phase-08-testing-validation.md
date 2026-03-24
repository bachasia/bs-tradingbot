# Phase 8: Testing on FTMO Demo & Validation Checklist

## Context Links
- [Phase 5: Dashboard Visuals](./phase-05-dashboard-visuals.md)
- [Phase 7: cBot Trade Management](./phase-07-cbot-trade-management.md)
- [Brainstorm — Testing Plan](../../plans/reports/brainstorm-260323-2330-ctrader-london-sweep-port.md) (lines 127-131)
- [Brainstorm — Success Metrics](../../plans/reports/brainstorm-260323-2330-ctrader-london-sweep-port.md) (lines 133-141)
- [PineScript Source](../../london-sweep-indicator.pine) (full reference for comparison)

## Overview
- **Priority**: P1 — must validate before live use
- **Status**: pending
- **Effort**: 1h
- **Description**: Backtest indicator and cBot on FTMO demo account with `US30` M15 chart. Compare output against TradingView Pine indicator. Validate all state transitions, MTF filters, sweep detection, order placement, trade management, and EOD cutoff.

## Key Insights

### Backtest vs Live Testing
- **Backtest** (historical): Fast, covers many days, validates logic correctness. Notifications don't fire. Use cTrader's built-in backtester.
- **Forward test** (demo live): Slower but validates real-time behavior — tick-by-tick Calculate calls, notifications, order fill mechanics. Run on FTMO demo for at least a few trading days.
- Both are needed. Backtest first to catch logic bugs, then forward test for integration.

### Cross-Platform Comparison Method
1. Open TradingView with Pine indicator on US30 M15
2. Open cTrader with cAlgo indicator on US30 M15
3. Pick 5-10 specific dates with known sweep signals
4. Compare: London high/low, H4 EMA values, sweep detection bar, entry/SL/TP prices
5. Document any differences and root-cause them

### Known Sources of Minor Differences
- **EMA seeding**: Pine and cTrader may use slightly different initial EMA values (SMA seed vs first-bar seed). Difference diminishes over time — compare bars >100 bars from start.
- **Bar open times**: FTMO server time vs TradingView exchange time — should both be UTC-based, but verify.
- **Tick data vs OHLC**: Backtest on bar data (M15 OHLC) — intra-bar SL/TP hit order is approximated. Same limitation as Pine strategy backtest.
- **Spread**: FTMO US30 has spread (2-5 pts). Pine indicator ignores spread. cTrader backtest can include spread — test both with and without.

## Requirements

### Functional
- Indicator: All visuals render correctly on historical and live chart
- Indicator: Outputs match Pine indicator values for sampled dates
- cBot: Signals read correctly from indicator
- cBot: Pending orders placed with correct parameters
- cBot: EOD cutoff fires, positions/orders cleaned up
- cBot: OnStop cleanup works

### Non-Functional
- Backtest completes without errors (no exceptions in log)
- No repainting on live chart (indicator values for past bars don't change)
- Performance: Calculate() executes fast enough to not cause lag on M15 chart

## Architecture

```
Testing Pipeline
├── Stage 1: Compilation Check
│   └── Both files compile in cTrader Automate IDE
├── Stage 2: Indicator Backtest (visual)
│   ├── Attach indicator to US30 M15 chart
│   ├── Scroll through 30+ days of history
│   └── Verify: boxes, lines, dashboard, state transitions
├── Stage 3: Cross-Platform Comparison
│   ├── Pick 5 dates with sweep signals from TradingView
│   ├── Compare London high/low, filter values, sweep bar
│   └── Document differences
├── Stage 4: cBot Backtest
│   ├── Run cBot on historical data (AutoTrade=true)
│   ├── Check trade log for correct entries/exits
│   └── Verify SL/TP/EOD outcomes match indicator
├── Stage 5: cBot Forward Test (demo)
│   ├── Run cBot on live FTMO demo (AutoTrade=false first)
│   ├── Verify signals, notifications, logging
│   └── Then AutoTrade=true for 2-5 trading days
└── Stage 6: Edge Case Validation
    ├── DST transition week
    ├── Day with no sweep (range too small)
    ├── Day with sweep but filters reject
    └── Multiple signals in one day (only first should trade)
```

## Related Code Files

### Files to Test
- `ctrader/LondonSweepIndicator.cs`
- `ctrader/LondonSweepBot.cs`

### Files to Reference
- `london-sweep-indicator.pine` (ground truth for comparison)

## Implementation Steps

### Step 1: Compilation Check

1. Open cTrader Desktop → Automate tab
2. Create new Indicator → paste `LondonSweepIndicator.cs` → Build (Ctrl+B)
3. Fix any compile errors (likely: missing `using`, wrong types, enum visibility)
4. Create new cBot → paste `LondonSweepBot.cs` → Manage References → check LondonSweepIndicator → Build
5. Fix any compile errors

**Common compile issues to watch for:**
- `SessionState` enum not found → ensure it's in `cAlgo.Indicators` namespace, not inside indicator class
- `Chart.Objects.FirstOrDefault` → requires `using System.Linq;`
- `Color.FromArgb` → cTrader uses `Color.FromArgb(int a, int r, int g, int b)` — verify parameter order
- `PositionCloseReason` enum values → verify against API

### Step 2: Indicator Visual Backtest

1. Attach `LondonSweepIndicator` to `US30` M15 chart
2. Use default parameters
3. Scroll back 30+ trading days
4. Check each day for:

| Check | Expected |
|---|---|
| Blue range box appears during 03:00-09:00 EST | Yes, on every weekday |
| Box top = London session high | Match candle highs |
| Box bottom = London session low | Match candle lows |
| Dotted lines at London high/low after 09:00 | Extending right |
| Dashboard in top-right corner | 6+ rows of status |
| Sweep signal on qualifying days | Entry/SL/TP lines drawn |
| No drawings on Saturday/Sunday | Clean weekends |
| State resets each new day | No carry-over |

### Step 3: Cross-Platform Value Comparison

Pick 5 dates where TradingView shows a sweep signal. For each date, record:

```
Date: YYYY-MM-DD
─────────────────────────────────────────
                    TradingView    cTrader    Diff
London High:        _________      _______    ___
London Low:         _________      _______    ___
Range Size:         _________      _______    ___
H4 EMA Fast:        _________      _______    ___
H4 EMA Slow:        _________      _______    ___
H4 Trend:           _________      _______    ___
H1 Mom:             _________      _______    ___
Sweep Bar Time:     _________      _______    ___
Sweep Direction:    _________      _______    ___
Entry Price:        _________      _______    ___
SL Price:           _________      _______    ___
TP Price:           _________      _______    ___
Trade Result:       _________      _______    ___
```

**Acceptable differences:**
- London high/low: within 1 point (tick data differences)
- H4 EMA: within 2 points (EMA seeding differences over long history)
- Entry/SL/TP: within 1 point (close price may differ by 1 tick)
- Sweep bar: must be the SAME bar (critical — if different, there's a logic bug)

### Step 4: cBot Backtest

1. Open cTrader Backtester
2. Select `LondonSweepBot`
3. Settings:
   - Symbol: `US30`
   - Timeframe: M15
   - Date range: last 60 trading days
   - AutoTrade: `true`
   - Volume: `1.0` (minimum)
   - All other params: default
4. Run backtest
5. Check:

| Check | Expected |
|---|---|
| Trades placed on sweep signal bars | Matching indicator signals |
| SL/TP on orders match indicator levels | Within 1 pip |
| No trades on non-signal days | Clean log |
| Max 1 trade per day | No duplicates |
| EOD cutoff closes open trades at 15:00 | Check trade log |
| No exceptions in log | Clean output |
| P&L makes logical sense | Not wildly different from Pine strategy |

### Step 5: cBot Forward Test (Demo)

1. Attach cBot to live `US30` M15 chart on FTMO demo
2. **Phase A**: `AutoTrade = false` (alerts only)
   - Run for 2-3 trading days
   - Verify signals appear in log
   - Verify sound notifications fire
   - Compare signals against TradingView in real-time
3. **Phase B**: `AutoTrade = true`
   - Run for 2-5 trading days
   - Verify pending orders appear in Pending Orders tab
   - Verify SL/TP values on orders
   - Verify EOD cancellation of unfilled orders
   - Verify position closure on SL/TP hit
   - Monitor P&L on demo account

### Step 6: Edge Case Validation

Run these specific scenarios (find historical dates or wait for live occurrence):

| Scenario | How to Test | Expected Behavior |
|---|---|---|
| Range < MinRange | Day with tight London range | No sweep signal, state goes Idle→London→RangeDone→Done |
| Filters reject sweep | Day with bearish H4 + bullish sweep | Signal not generated, log shows "Watching" |
| Weekend bars | Saturday/Sunday | No state changes, no drawings |
| DST transition | March or November week | Session times shift correctly with EST→EDT |
| Two sweeps same day | Rare — both high and low swept | Only first sweep triggers trade |
| Sweep but no fill | Limit order placed, price doesn't return | Order cancelled at EOD |
| Bot stopped mid-trade | Stop bot during InTrade state | OnStop closes position and cancels orders |
| Chart timeframe change | Attach to M5 instead of M15 | Should still work (HHMM-based, not bar-count-based) |
| FTMO daily loss guard | Set low daily limit, simulate losing trade | No new trades after limit approach |
| FTMO drawdown guard | Set low max drawdown | Bot halts trading when drawdown nears limit |

### Step 7: Non-Repainting Verification

1. Attach indicator to live M15 chart during sweep window (09:30-11:00 EST)
2. Watch a bar form in real-time
3. Record the London high/low and H4 EMA values displayed
4. Wait for bar to close
5. Scroll back to that bar
6. Verify values haven't changed
7. Close and reopen cTrader — values should be identical

**Critical**: If any value changes between live display and after-close, there's a repainting bug. Most likely cause: using `Last(0)` instead of `Last(1)` somewhere.

## Todo List

- [ ] Compile both files in cTrader Automate IDE — zero errors
- [ ] Attach indicator to US30 M15 — visual inspection of 30+ days
- [ ] Verify London range boxes match TradingView
- [ ] Verify dashboard shows correct status for each state
- [ ] Cross-platform comparison for 5 signal dates
- [ ] Document any value differences and root-cause
- [ ] Run cBot backtest for 60 days — check trade log
- [ ] Verify SL/TP/EOD outcomes in backtest
- [ ] Forward test (alerts only) for 2-3 live trading days
- [ ] Forward test (auto-trade) for 2-5 live trading days on demo
- [ ] Test edge cases: small range, filter rejection, weekends
- [ ] Non-repainting verification on live chart
- [ ] Document final test results in a report

## Success Criteria
- Both files compile with zero errors and zero warnings
- London range high/low within 1 point of TradingView values
- H4 EMA values within 2 points of TradingView
- Sweep detection on the same bar as TradingView for all 5 test dates
- Entry/SL/TP prices within 1 point of TradingView
- cBot places correct pending orders in backtest
- EOD cutoff fires at 15:00 EST consistently
- No repainting on live chart — verified manually
- No exceptions in cTrader log during 60-day backtest
- Forward test: signals match TradingView real-time for 2+ consecutive days

## Risk Assessment
- **FTMO data differences**: FTMO's US30 data may differ slightly from TradingView's data feed. Minor OHLC differences are acceptable — sweep detection bar must still match
- **Backtest data quality**: cTrader backtest uses bar data (not tick data by default). Intra-bar SL/TP resolution is approximated. For accurate SL/TP testing, use tick data backtest if available on FTMO
- **Demo vs live execution**: FTMO demo may have different execution (faster fills, no slippage). Real account testing is beyond this plan's scope but recommended before real money
- **Time commitment**: Forward test requires waiting for actual trading days — can't be compressed. Plan 1-2 weeks for thorough forward testing

## Security Considerations
- Testing on demo account only — no real money at risk
- No API keys or credentials needed for demo testing
- Verify no sensitive data in cTrader log output (Print statements)

## FTMO-Specific Validation
- [ ] Verify `US30` symbol loads correctly on FTMO cTrader
- [ ] Check `Symbol.PipSize` and `Symbol.TickSize` values on FTMO
- [ ] Test FTMO daily loss guard: set limit to $100, lose $80+, verify no new trades
- [ ] Test FTMO drawdown guard: set limit to $200, verify guard triggers at 80%
- [ ] Verify cBot runs without `AccessRights` issues on FTMO cTrader
- [ ] Confirm FTMO allows automated trading (no manual-only restriction)

## Next Steps
- After all tests pass: Deploy to FTMO funded account with minimal volume
- Monitor live performance for 2-4 weeks
- Keep FTMO risk guards at conservative 80% thresholds initially
