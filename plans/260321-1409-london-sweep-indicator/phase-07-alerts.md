---
phase: 7
title: "Alert Conditions"
status: complete
effort: 30m
---

# Phase 7: Alert Conditions

## Overview

Add `alertcondition()` calls at global scope for all trade events. Alerts fire only on confirmed bars.

## Requirements

### Functional
- Alert on: long sweep, short sweep, SL hit, TP hit, EOD close
- Alert messages include `{{close}}` placeholder for price
- All conditions use `barstate.isconfirmed` gate

### Non-Functional
- `alertcondition()` must be at **global scope** (not inside if blocks)
- Condition variables (`longSweep`, `shortSweep`, `slHit`, `tpHit`, `eodClose`) already computed in prior phases

## Implementation Steps

1. Add alert conditions at end of script:
```pine
// ─── ALERTS ───
alertcondition(longSweep  and barstate.isconfirmed, "Long Sweep Signal",
    "London Sweep LONG on US30 | Entry: {{close}}")
alertcondition(shortSweep and barstate.isconfirmed, "Short Sweep Signal",
    "London Sweep SHORT on US30 | Entry: {{close}}")
alertcondition(slHit      and barstate.isconfirmed, "Stop Loss Hit",
    "SL Hit on US30 at {{close}}")
alertcondition(tpHit      and barstate.isconfirmed, "Take Profit Hit",
    "TP Hit on US30 at {{close}}")
alertcondition(eodClose   and barstate.isconfirmed, "EOD Close",
    "EOD position closed on US30 at {{close}}")
```

2. Note: `alertcondition()` already includes `barstate.isconfirmed` because the sweep/trade logic in Phases 4-5 gates on it. The extra `and barstate.isconfirmed` in the alert is belt-and-suspenders safety.

## Related Files

- **Modify**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] All 5 alert conditions visible in TradingView "Create Alert" dialog
- [ ] Alert names are descriptive and unique
- [ ] Messages include `{{close}}` price placeholder
- [ ] Alerts fire only on confirmed bars (no premature triggers)

## Todo

- [ ] Add 5 alertcondition() calls at global scope
- [ ] Verify all appear in alert creation dialog
- [ ] Test alert firing on historical sweep bars
