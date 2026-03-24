# Phase 1: Project Setup, Boilerplate & Input Parameters

## Context Links
- [Brainstorm Report](../../plans/reports/brainstorm-260323-2330-ctrader-london-sweep-port.md)
- [PineScript Source](../../london-sweep-indicator.pine)
- [cTrader Indicator API Reference](https://help.ctrader.com/ctrader-algo/references/General/Indicator/)
- [cTrader Robot API Reference](https://help.ctrader.com/ctrader-algo/references/General/Robot/)
- [cTrader Parameter Types Guide](https://help.ctrader.com/ctrader-algo/guides/parameter-types/)

## Overview
- **Priority**: P1 — foundation for all subsequent phases
- **Status**: complete
- **Effort**: 1h
- **Description**: Create file structure, class boilerplate with correct attributes, all `[Parameter]` inputs mirroring Pine inputs, state machine enum, and class-level field declarations.

## Key Insights
- cTrader Automate uses C# classes inheriting from `Indicator` or `Robot`
- `[Indicator]` attribute requires `IsOverlay = true` (draws on price chart), `TimeZone`, `AccessRights`
- `[Robot]` attribute requires `TimeZone`, `AccessRights`
- Parameters declared as public properties with `[Parameter]` attribute — appear in cTrader settings UI
- `AccessRights.None` sufficient for indicator; cBot may need `AccessRights.FullAccess` if email notifications used
- Pine `var float x = na` maps to `private double _x = double.NaN;` in C#

## Requirements

### Functional
- Two C# files: `LondonSweepIndicator.cs` and `LondonSweepBot.cs`
- Indicator parameters match Pine inputs: session times, trade params, filter toggles, visual colors
- State machine enum with 6 states: `Idle`, `London`, `RangeDone`, `SweepWindow`, `InTrade`, `Done`
- All class fields declared with initial values

### Non-Functional
- Heavily commented code — user has no C# experience
- Each section of code separated with comment banners (matching Pine style)
- All time parameters in HHMM integer format (matching Pine)

## Architecture

```
ctrader/
  LondonSweepIndicator.cs
    ├── [Indicator] attribute (IsOverlay=true, TimeZone=EST, AccessRights=None)
    ├── enum SessionState { Idle, London, RangeDone, SweepWindow, InTrade, Done }
    ├── [Parameter] properties (4 groups: Session, Trade, Filters, Visual)
    ├── Private fields (state, prices, drawing refs, MTF indicator refs)
    ├── [Output] properties (declared here, populated in Phase 4)
    ├── Initialize() — stub
    └── Calculate(int index) — stub

  LondonSweepBot.cs
    ├── [Robot] attribute (TimeZone=EST, AccessRights=None or FullAccess)
    ├── [Parameter] properties (volume, max trades, auto-trade toggle, email toggle)
    ├── Private fields (indicator ref, trade state)
    ├── OnStart() — stub
    ├── OnBarClosed() — stub
    └── OnStop() — stub
```

## Related Code Files

### Files to Create
- `ctrader/LondonSweepIndicator.cs`
- `ctrader/LondonSweepBot.cs`

### Files to Reference
- `london-sweep-indicator.pine` (lines 10-41 for inputs, lines 73-107 for state vars)

## Implementation Steps

### Step 1: Create directory structure
```bash
mkdir -p ctrader
```

### Step 2: Create indicator boilerplate with all parameters

Write `ctrader/LondonSweepIndicator.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Indicator | US30 | cTrader Automate (C#)
// Detects liquidity sweeps of London session range during NY open
// with H4 trend + H1 momentum filters
// ══════════════════════════════════════════════════════════════════════════════
using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    // ─── STATE MACHINE ────────────────────────────────────────────────────────
    // Tracks which phase of the daily strategy cycle we're in.
    // Matches the Pine var int STATE_IDLE / STATE_LONDON / etc. pattern.
    public enum SessionState
    {
        Idle,          // Before London session starts (or after day is done)
        London,        // Inside London session (03:00–09:00 EST), tracking high/low
        RangeDone,     // London session ended, range is established
        SweepWindow,   // Inside NY sweep window (09:30–11:00 EST), watching for sweeps
        InTrade,       // Sweep detected, trade is active
        Done           // Trade resolved (SL/TP/EOD) or sweep window expired
    }

    // IsOverlay = true  → draws on the price chart (not a separate panel)
    // TimeZone          → all Bars.OpenTimes will be in Eastern Time
    // AccessRights      → None = no network/file access needed for indicator
    [Indicator("London Sweep | US30", IsOverlay = true,
        TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepIndicator : Indicator
    {
        // ─── INPUTS: Session Times ────────────────────────────────────────────
        // These match the PineScript input.int() parameters.
        // HHMM format: 300 = 03:00, 900 = 09:00, 930 = 09:30, etc.
        [Parameter("London Start (HHMM)", Group = "Session Times", DefaultValue = 300)]
        public int LondonStartHHMM { get; set; }

        [Parameter("London End (HHMM)", Group = "Session Times", DefaultValue = 900)]
        public int LondonEndHHMM { get; set; }

        [Parameter("Sweep Window Start (HHMM)", Group = "Session Times", DefaultValue = 930)]
        public int SweepStartHHMM { get; set; }

        [Parameter("Sweep Window End (HHMM)", Group = "Session Times", DefaultValue = 1100)]
        public int SweepEndHHMM { get; set; }

        [Parameter("EOD Cutoff (HHMM)", Group = "Session Times", DefaultValue = 1500)]
        public int EodCutoffHHMM { get; set; }

        // ─── INPUTS: Trade Parameters ─────────────────────────────────────────
        [Parameter("Min London Range (pts)", Group = "Trade Parameters", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade Parameters", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade Parameters", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade Parameters", DefaultValue = 0.65, Step = 0.05)]
        public double TpMultiplier { get; set; }

        // ─── INPUTS: Filters ──────────────────────────────────────────────────
        [Parameter("H4 EMA Fast Period", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow Period", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        // ─── INPUTS: Visual ───────────────────────────────────────────────────
        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── OUTPUT PROPERTIES (for cBot to read) ─────────────────────────────
        // These are IndicatorDataSeries so the cBot can call
        // Indicators.GetIndicator<LondonSweepIndicator>() and read them.
        // We use 1.0 = true, 0.0 = false for bool signals in data series.
        [Output("Long Signal", LineColor = "Transparent")]
        public IndicatorDataSeries LongSignal { get; set; }

        [Output("Short Signal", LineColor = "Transparent")]
        public IndicatorDataSeries ShortSignal { get; set; }

        [Output("Entry Price", LineColor = "Transparent")]
        public IndicatorDataSeries EntryPriceOutput { get; set; }

        [Output("SL Price", LineColor = "Transparent")]
        public IndicatorDataSeries SlPriceOutput { get; set; }

        [Output("TP Price", LineColor = "Transparent")]
        public IndicatorDataSeries TpPriceOutput { get; set; }

        // ─── PRIVATE STATE FIELDS ─────────────────────────────────────────────
        // These persist across bars (like Pine "var" variables).
        private SessionState _state = SessionState.Idle;
        private double _londonHigh = double.NaN;
        private double _londonLow = double.NaN;
        private double _rangeSize = double.NaN;
        private bool _tradedToday = false;

        // H1 momentum captures (close at London start vs London end)
        private double _h1Close0300 = double.NaN;
        private double _h1Close0900 = double.NaN;

        // Trade management
        private int _tradeDir = 0;   // 1 = long, -1 = short, 0 = none
        private double _entryPrice = double.NaN;
        private double _slPrice = double.NaN;
        private double _tpPrice = double.NaN;
        private string _tradeResult = "";

        // Day tracking (for daily reset)
        private DateTime _lastDate = DateTime.MinValue;

        // Bar-close detection (to simulate OnBarClosed inside Calculate)
        private int _lastIndex = -1;

        // MTF data references (initialized in Initialize())
        private Bars _h4Bars;
        private Bars _h1Bars;
        private ExponentialMovingAverage _h4EmaFast;
        private ExponentialMovingAverage _h4EmaSlow;

        // Drawing object name counters (for unique names per day)
        private int _dayCount = 0;

        // ─── LIFECYCLE METHODS (stubs — filled in later phases) ───────────────
        protected override void Initialize()
        {
            // Phase 2: Initialize MTF bars and indicators
            // Phase 3: Set up H4/H1 data
        }

        public override void Calculate(int index)
        {
            // Phase 2: Session detection + range tracking
            // Phase 3: MTF filter evaluation
            // Phase 4: Sweep detection + output population
            // Phase 5: Dashboard drawing
        }
    }
}
```

### Step 3: Create cBot boilerplate with parameters

Write `ctrader/LondonSweepBot.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Bot | US30 | cTrader Automate (C#)
// Reads signals from LondonSweepIndicator, places pending limit orders,
// manages trades with EOD cutoff.
// ══════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;  // Required to reference LondonSweepIndicator

namespace cAlgo.Robots
{
    // TimeZone matches indicator so all time comparisons are consistent.
    // AccessRights.None = no network needed. Change to FullAccess if email wanted.
    [Robot("London Sweep Bot", TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepBot : Robot
    {
        // ─── INPUTS: Must match indicator params (passed to GetIndicator) ─────
        // These are passed through to the indicator so both use same settings.
        [Parameter("London Start (HHMM)", Group = "Session Times", DefaultValue = 300)]
        public int LondonStartHHMM { get; set; }

        [Parameter("London End (HHMM)", Group = "Session Times", DefaultValue = 900)]
        public int LondonEndHHMM { get; set; }

        [Parameter("Sweep Window Start (HHMM)", Group = "Session Times", DefaultValue = 930)]
        public int SweepStartHHMM { get; set; }

        [Parameter("Sweep Window End (HHMM)", Group = "Session Times", DefaultValue = 1100)]
        public int SweepEndHHMM { get; set; }

        [Parameter("EOD Cutoff (HHMM)", Group = "Session Times", DefaultValue = 1500)]
        public int EodCutoffHHMM { get; set; }

        // Trade parameters (passed to indicator)
        [Parameter("Min London Range (pts)", Group = "Trade Parameters", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade Parameters", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade Parameters", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade Parameters", DefaultValue = 0.65)]
        public double TpMultiplier { get; set; }

        // Filter parameters (passed to indicator)
        [Parameter("H4 EMA Fast Period", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow Period", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        // Visual (passed to indicator)
        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── INPUTS: Bot-Only Parameters ──────────────────────────────────────
        [Parameter("Auto-Trade (place orders)", Group = "Bot Settings", DefaultValue = false)]
        public bool AutoTrade { get; set; }

        [Parameter("Volume (units)", Group = "Bot Settings", DefaultValue = 1.0)]
        public double TradeVolume { get; set; }

        [Parameter("Max Trades Per Day", Group = "Bot Settings", DefaultValue = 1)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Enable Sound Alerts", Group = "Bot Settings", DefaultValue = true)]
        public bool EnableSound { get; set; }

        // ─── PRIVATE FIELDS ───────────────────────────────────────────────────
        private LondonSweepIndicator _indicator;
        private int _tradesToday = 0;
        private DateTime _lastDate = DateTime.MinValue;
        private string _botLabel = "LondonSweep";

        // ─── LIFECYCLE METHODS (stubs — filled in Phase 6-7) ──────────────────
        protected override void OnStart()
        {
            // Phase 6: Initialize indicator reference
        }

        protected override void OnBarClosed()
        {
            // Phase 6: Read indicator signals
            // Phase 7: Place orders, manage trades
        }

        protected override void OnStop()
        {
            // Phase 7: Cleanup
        }
    }
}
```

### Step 4: Verify files compile conceptually

- Ensure all `using` directives are present
- Ensure namespace hierarchy is correct (`cAlgo.Indicators` for indicator, `cAlgo.Robots` for cBot)
- Ensure enum is declared outside the class but inside the namespace (or inside indicator class — both work)

## Todo List

- [ ] Create `ctrader/` directory
- [ ] Write `LondonSweepIndicator.cs` boilerplate with all `[Parameter]` inputs
- [ ] Write `LondonSweepBot.cs` boilerplate with all `[Parameter]` inputs
- [ ] Declare `SessionState` enum with 6 states
- [ ] Declare all private fields matching Pine `var` variables
- [ ] Declare `[Output]` properties (5 data series)
- [ ] Add comment banners matching Pine section style
- [ ] Load both files in cTrader Automate IDE — verify they compile with no errors

## Success Criteria
- Both files load in cTrader Automate without compile errors
- All parameters appear in cTrader settings dialog grouped correctly
- Indicator appears in chart overlay (blank, since logic is stub)
- cBot appears in bot list (does nothing yet)

## Risk Assessment
- **Wrong namespace**: cTrader expects `cAlgo.Indicators` / `cAlgo.Robots` — verified via official samples
- **Parameter order matters**: When cBot calls `GetIndicator<>()`, params must match declaration order in indicator — ensure consistency
- **Enum visibility**: If enum is inside indicator class, cBot can't reference it unless it's public — declare in namespace scope

## Security Considerations
- `AccessRights.None` for indicator — no network, no file system access
- cBot starts with `AccessRights.None` — upgrade to `FullAccess` only if email notifications needed (Phase 7)
- No API keys or credentials in code

## Next Steps
- Phase 2: Implement session detection and London range tracking inside `Initialize()` and `Calculate()`
