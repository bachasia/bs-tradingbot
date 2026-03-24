---
phase: 8
title: "Testing + Validation"
status: pending
effort: 1h
---

# Phase 8: Testing + Validation

## Overview

Manual validation of the indicator on US30 M15 historical data. Verify each component works correctly and edge cases are handled.

## Test Plan

### 1. Compilation Test
- [ ] Script compiles without errors in TradingView editor
- [ ] No warnings about deprecated functions
- [ ] All inputs appear in Settings panel with correct groups/defaults

### 2. London Range Accuracy (5 random days)
- [ ] Box spans exactly 03:00-09:00 EST
- [ ] Box top = highest high of session bars
- [ ] Box bottom = lowest low of session bars
- [ ] Extending lines match box boundaries
- [ ] Range size in dashboard matches manual calculation

### 3. Range Filter
- [ ] Day with range < 50pts: no sweep detection occurs, state goes to DONE
- [ ] Day with range >= 50pts: sweep window activates normally

### 4. H4 Trend Filter
- [ ] Compare dashboard H4 status with manual H4 chart EMA overlay
- [ ] Bullish trend: only long sweeps detected
- [ ] Bearish trend: only short sweeps detected
- [ ] Toggle off: both directions allowed

### 5. H1 Momentum Filter
- [ ] Verify h1Close0300 and h1Close0900 values match H1 chart
- [ ] Positive momentum + bullish H4: longs confirmed
- [ ] Conflicting momentum/trend: no trade

### 6. Sweep Detection (10+ historical examples)
- [ ] Long sweep: M15 wick dips >= 5pts below London low, closes above
- [ ] Short sweep: M15 wick spikes >= 5pts above London high, closes below
- [ ] No sweep on bars outside 09:30-11:00 window
- [ ] Only first sweep per day triggers signal

### 7. Trade Levels
- [ ] Entry = sweep bar close price (exact match)
- [ ] Long SL = sweep bar low - 8pts
- [ ] Short SL = sweep bar high + 8pts
- [ ] TP = entry +/- (0.65 x range) matches manual calc
- [ ] Lines appear at correct prices

### 8. Trade Resolution
- [ ] SL hit: state -> DONE, dashboard shows "SL Hit"
- [ ] TP hit: state -> DONE, dashboard shows "TP Hit"
- [ ] Neither by 15:00 EST: EOD close fires, dashboard shows "EOD Close"
- [ ] Lines stop extending after resolution

### 9. Daily Reset
- [ ] All state variables reset at midnight
- [ ] Previous day drawings deleted
- [ ] New London box drawn fresh

### 10. Edge Cases
- [ ] Weekend bars: no false triggers on Friday close / Monday open
- [ ] Holiday/half days: graceful handling
- [ ] DST transition days: session times still correct
- [ ] Very large range (>200pts): TP calculation still reasonable
- [ ] Exact threshold values (range=50, sweep=5): boundary behavior correct

### 11. Non-Repainting Verification
- [ ] Add indicator to live chart during sweep window
- [ ] Verify signals don't appear/disappear on unconfirmed bars
- [ ] H4 EMA values don't change within same bar

### 12. Alert Testing
- [ ] Create alert for each condition
- [ ] Verify alerts fire on expected historical bars
- [ ] Alert messages contain correct price values

## Validation Method

For each test:
1. Load indicator on US30 M15 chart
2. Navigate to relevant date/time
3. Compare indicator output with manual calculation
4. Document any discrepancies

## Success Criteria

All checkboxes above must pass. Any failure requires fix and re-test.

## Related Files

- **Read**: `london-sweep-indicator.pine`
- **Reference**: [Brainstorm](../reports/brainstorm-260321-1401-london-sweep-indicator.md)

## Todo

- [ ] Run compilation test
- [ ] Validate London range on 5 days
- [ ] Verify range filter behavior
- [ ] Cross-check H4 EMAs with H4 chart
- [ ] Cross-check H1 momentum values
- [ ] Validate sweep detection on 10+ examples
- [ ] Verify trade level calculations
- [ ] Test all 3 exit scenarios (SL/TP/EOD)
- [ ] Confirm daily reset works
- [ ] Test edge cases
- [ ] Verify non-repainting on live chart
- [ ] Test all alert conditions
