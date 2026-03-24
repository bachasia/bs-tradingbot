# Brainstorm: Port London Sweep Indicator to cTrader

## Problem Statement

Port existing TradingView PineScript v6 London Sweep Indicator to cTrader platform (FxPro broker, `#US30` symbol). Need visual indicator + optional auto-execution via cBot with pending-order confirmation flow.

## Requirements

- **Functional**: Replicate all 8 steps of the London Sweep strategy
- **Platform**: cTrader Automate (C# / cAlgo API)
- **Broker**: FxPro (`#US30`)
- **Execution**: Pending order mode — cBot places limit order + notification, user reviews
- **Architecture**: Indicator + cBot separation for extensibility
- **User**: No C# experience — code must be heavily commented and self-explanatory

## Evaluated Approaches

### A: Indicator Only (visual + alerts)
- **Pros**: Safe, 1:1 port of TradingView version, no execution risk
- **Cons**: Manual trade execution, misses cTrader's execution advantage
- **Effort**: ~4h
- **Verdict**: Too limited — user wants automation option

### B: Single cBot (all-in-one)
- **Pros**: Simple deployment (1 file), easier to debug
- **Cons**: Poor extensibility, can't reuse indicator separately, harder to share
- **Effort**: ~8h
- **Verdict**: Good for quick start, bad for long-term

### C: Indicator + cBot Combo (SELECTED)
- **Pros**: Extensible, modular, indicator reusable, can create cBot variants
- **Cons**: Higher initial complexity, 2 files to manage
- **Effort**: ~10h
- **Verdict**: Best long-term architecture

## Final Recommended Solution

### Architecture

```
LondonSweepIndicator.cs (Indicator)
├── London session range detection (03:00-09:00 EST)
├── H4 EMA trend filter (non-repainting)
├── H1 momentum filter (non-repainting)
├── Sweep detection (09:30-11:00 EST)
├── Visual drawings (range box, levels, dashboard)
├── Output properties:
│   ├── IsLongSignal (bool)
│   ├── IsShortSignal (bool)
│   ├── SignalEntryPrice (double)
│   ├── SignalSLPrice (double)
│   ├── SignalTPPrice (double)
│   ├── LondonHigh / LondonLow (double)
│   ├── RangeSize (double)
│   └── TradeResult (string)
└── State machine: IDLE → LONDON → RANGE_DONE → SWEEP_WINDOW → IN_TRADE → DONE

LondonSweepBot.cs (cBot)
├── References LondonSweepIndicator via GetIndicator<>()
├── Reads indicator output properties each bar
├── Pending order flow:
│   ├── Signal detected → Place limit order at entry price
│   ├── Set SL/TP on the pending order
│   ├── Fire notification (sound + popup + optional email)
│   └── User reviews → order fills or user cancels
├── Trade management:
│   ├── Monitor SL/TP (handled by broker via order params)
│   └── EOD cutoff at 15:00 EST → close position at market
├── Parameters:
│   ├── AutoTrade toggle (default: false, just alerts)
│   ├── Volume / lot size
│   ├── Max trades per day
│   └── Enable/disable email notifications
└── Logging: Print trade details to cTrader log
```

### Key Porting Differences

| Pine Concept | cTrader Equivalent |
|---|---|
| `request.security("240", ...)` | `MarketData.GetBars(TimeFrame.Hour4)` |
| `request.security("60", ...)` | `MarketData.GetBars(TimeFrame.Hour)` |
| `time(tf, session, tz)` | `TimeZones.EasternStandardTime` + manual hour/minute check |
| `var float x = na` | `private double _x = double.NaN;` |
| `ta.ema(close, 20)` | `Indicators.ExponentialMovingAverage(source, 20)` |
| `box.new(...)` | `Chart.DrawRectangle(...)` |
| `line.new(...)` | `Chart.DrawHorizontalLine(...)` or `Chart.DrawTrendLine(...)` |
| `label.new(...)` | `Chart.DrawText(...)` |
| `table.new(...)` | `Chart.DrawStaticText(...)` (simpler) or custom drawing |
| `alertcondition(...)` | `Notifications.PlaySound()` + `Notifications.SendEmail()` |
| `barstate.isconfirmed` | `IsLastBar && !IsBacktesting` or `OnBarClosed` event |
| State machine (`var int`) | C# class fields with enum |

### Pending Order Confirmation Flow

```
1. Indicator detects sweep → sets IsLongSignal=true, outputs entry/SL/TP
2. cBot reads signal on OnBar() event
3. cBot places PlaceLimitOrder(entry, volume, SL, TP)
4. cBot fires notification: "LONG signal at 42,150. Pending order placed. SL=42,092, TP=42,280"
5. User sees notification → checks chart → decides to let fill or cancel
6. If no fill by EOD → cancel pending order
7. If filled → monitor for EOD cutoff
```

### Non-Repainting Strategy

- H4 EMA: Use `Bars.ClosePrices.Last(1)` on H4 bars (confirmed bar only)
- H1 close: Same `Last(1)` pattern on H1 bars
- Sweep: Detect on `OnBarClosed` event (M15 bar confirmed)
- No intra-bar decisions — everything on bar close

## Implementation Considerations

### FxPro Specific
- Symbol: `#US30` — verify via `Symbol.Name` in code
- Spread: FxPro US30 spread typically 2-5 pts — factor into SL buffer
- Min volume: Check `Symbol.VolumeInUnitsMin`
- Trading hours: Verify FxPro US30 session matches expected EST hours

### Risks
1. **DST transitions**: `TimeZones.EasternStandardTime` should handle EST→EDT but test around March/November transitions
2. **Slippage**: Pending limit order may fill at worse price during volatility — add `SlippagePips` parameter
3. **Symbol differences**: `#US30` on FxPro may have different point value than TradingView — use `Symbol.PipSize` and `Symbol.TickSize` dynamically
4. **Network access**: cBot needs `[Robot(AccessRights = AccessRights.None)]` minimum; if email notifications needed, bump to `AccessRights.Internet`

### Testing Plan
1. Backtest on demo with historical M15 data
2. Forward-test on demo for at least 2 weeks
3. Verify London range matches TradingView visuals
4. Confirm H4/H1 filter values match between platforms
5. Test pending order placement + cancellation flow
6. Test EOD cutoff

## Success Metrics
- London range high/low within 1 pt of TradingView values
- H4 EMA and H1 momentum agree between platforms
- Sweep detection triggers on same bars as TradingView
- Pending orders placed with correct SL/TP levels
- EOD cutoff fires at 15:00 EST consistently
- No repainting on live chart

## File Deliverables

```
ctrader/
├── LondonSweepIndicator.cs   (~200 lines)
└── LondonSweepBot.cs         (~150 lines)
```

## Next Steps

1. Create implementation plan with phased approach
2. Phase 1: Indicator — session detection + London range + visuals
3. Phase 2: Indicator — MTF filters + sweep detection + outputs
4. Phase 3: cBot — read indicator + pending order logic + notifications
5. Phase 4: cBot — trade management + EOD cutoff
6. Phase 5: Testing on FxPro demo

## Unresolved Questions
- Exact point value / pip size for `#US30` on FxPro — needs runtime verification
- Whether FxPro allows `AccessRights.Internet` for email notifications or if restricted
- Preferred notification method: sound only, popup, or email?
