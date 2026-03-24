---
phase: 1
title: "Project Setup + Inputs"
status: complete
effort: 30m
---

# Phase 1: Project Setup + Inputs

## Overview

Create the PineScript v6 indicator scaffold with all configurable inputs organized by group. Establish the file structure with commented sections.

## Requirements

### Functional
- PineScript v6 `indicator()` declaration with overlay=true
- All strategy parameters exposed as `input()` with sensible defaults
- Input groups: Session Times, Trade Parameters, Filters, Visual, Alerts
- `max_lines_count`, `max_boxes_count`, `max_labels_count` set appropriately

### Non-Functional
- Clean section headers via comments
- US30-appropriate default values

## Implementation Steps

1. Create `london-sweep-indicator.pine` in project root
2. Add v6 declaration and indicator header:
```pine
//@version=6
indicator("London Sweep | US30", overlay=true,
    max_lines_count=50, max_boxes_count=20, max_labels_count=50)
```

3. Define input groups and all inputs:

```pine
// ─── INPUTS: Session Times ───
string GRP_SESSION = "Session Times"
string i_tz        = input.string("America/New_York", "Timezone", group=GRP_SESSION)
int i_londonStart  = input.int(300,  "London Start (HHMM)", group=GRP_SESSION)
int i_londonEnd    = input.int(900,  "London End (HHMM)",   group=GRP_SESSION)
int i_sweepStart   = input.int(930,  "Sweep Window Start",  group=GRP_SESSION)
int i_sweepEnd     = input.int(1100, "Sweep Window End",    group=GRP_SESSION)
int i_eodCutoff    = input.int(1500, "EOD Cutoff (HHMM)",   group=GRP_SESSION)

// ─── INPUTS: Trade Parameters ───
string GRP_TRADE    = "Trade Parameters"
float i_minRange    = input.float(50.0, "Min London Range (pts)", group=GRP_TRADE)
float i_sweepThresh = input.float(5.0,  "Sweep Threshold (pts)",  group=GRP_TRADE)
float i_slBuffer    = input.float(8.0,  "SL Buffer (pts)",        group=GRP_TRADE)
float i_tpMult      = input.float(0.65, "TP Multiplier (x range)",group=GRP_TRADE, step=0.05)

// ─── INPUTS: Filters ───
string GRP_FILTER   = "Filters"
int i_h4EmaFast     = input.int(20, "H4 EMA Fast",   group=GRP_FILTER)
int i_h4EmaSlow     = input.int(50, "H4 EMA Slow",   group=GRP_FILTER)
bool i_useH4Filter  = input.bool(true, "Enable H4 Trend Filter",    group=GRP_FILTER)
bool i_useH1Filter  = input.bool(true, "Enable H1 Momentum Filter", group=GRP_FILTER)

// ─── INPUTS: Visual ───
string GRP_VISUAL      = "Visual"
color i_boxColor       = input.color(color.new(color.blue, 85), "London Range Box", group=GRP_VISUAL)
color i_entryColor     = input.color(color.blue,  "Entry Line",  group=GRP_VISUAL)
color i_slColor        = input.color(color.red,   "SL Line",     group=GRP_VISUAL)
color i_tpColor        = input.color(color.green, "TP Line",     group=GRP_VISUAL)
bool i_showDashboard   = input.bool(true, "Show Dashboard", group=GRP_VISUAL)
bool i_showTradeLines  = input.bool(true, "Show Trade Lines", group=GRP_VISUAL)
```

4. Add commented section placeholders for remaining phases:
```pine
// ─── MULTI-TF DATA ───
// ─── STATE VARIABLES ───
// ─── SESSION DETECTION ───
// ─── SWEEP DETECTION ───
// ─── TRADE MANAGEMENT ───
// ─── VISUALS ───
// ─── DASHBOARD ───
// ─── ALERTS ───
```

## Related Files

- **Create**: `london-sweep-indicator.pine`

## Success Criteria

- [ ] File compiles without errors in TradingView editor
- [ ] All inputs appear in Settings panel with correct groups
- [ ] Default values appropriate for US30 (50pt range, 8pt SL buffer, etc.)
- [ ] Section comments provide clear code organization

## Todo

- [ ] Create pine file with v6 header
- [ ] Add all input definitions grouped correctly
- [ ] Add section comment placeholders
- [ ] Verify compiles in TradingView
