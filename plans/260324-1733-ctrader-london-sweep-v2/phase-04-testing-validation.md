# Phase 4 — Testing & Validation

## Priority: MEDIUM | Status: Pending

## Overview

Validate implementation against strategy rules. cTrader has no unit test framework — validation is manual via backtesting + visual inspection.

## Context Links

- [Strategy Requirements](../../london-sweep-strategy.md)
- [Plan Overview](plan.md)

## Validation Checklist

### Compilation
- [ ] Indicator compiles without errors in cTrader Automate
- [ ] Bot compiles with indicator reference added

### Backtesting (US30 M15, 3+ months data)
- [ ] London Range box drawn correctly each day
- [ ] Range < 50 pts days → no signals generated
- [ ] H4 trend filter blocks counter-trend signals
- [ ] H1 momentum filter blocks mismatched signals
- [ ] Pre-sweep (09:00-09:29) invalidation works
- [ ] Sweep detection only in 09:30-11:00 window
- [ ] Max 1 trade per day enforced
- [ ] SL/TP levels match formula
- [ ] EOD cutoff closes open trades at 15:00 EST
- [ ] Dashboard displays correct live state

### FTMO Compliance
- [ ] Daily loss guard triggers at 80% threshold
- [ ] Max drawdown guard triggers at 80% threshold
- [ ] 1% risk sizing calculates correct volume

### Edge Cases
- [ ] Weekend bars ignored
- [ ] DST transition days handled (March/November)
- [ ] No signal when both Long and Short conditions met simultaneously
- [ ] Empty range (no London bars) → stays Idle

## Success Criteria

- Zero compile errors
- Backtest matches manual strategy application on sample days
- No repainting — signals don't change on historical bars
