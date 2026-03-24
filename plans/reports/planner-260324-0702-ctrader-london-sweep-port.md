# Planner Report: cTrader London Sweep Port

- **Created**: 2026-03-24
- **Plan**: `plans/260324-0702-ctrader-london-sweep-port/`
- **Total Effort**: ~10h across 8 phases
- **Architecture**: 2-file approach — Indicator (~200 LOC) + cBot (~150 LOC)

## Summary

Created comprehensive implementation plan to port the PineScript v6 London Sweep Indicator to cTrader Automate (C# / cAlgo API). Plan targets FxPro broker with `#US30` symbol.

## Research Conducted

Validated cTrader API details via web search and official docs:
- `[Indicator]` / `[Robot]` attribute syntax and properties
- `MarketData.GetBars(TimeFrame.Hour4)` for MTF — returns `Bars` object, use with `Indicators.ExponentialMovingAverage()`
- `Chart.DrawRectangle(name, barIdx1, y1, barIdx2, y2, color)` returns `ChartRectangle` with `IsFilled`, `LineStyle` properties
- `Chart.DrawStaticText(name, text, vAlign, hAlign, color)` — single-color text block, replaces by name
- `[Output]` must be `IndicatorDataSeries` — indexed by bar, default NaN
- `PlaceLimitOrder(tradeType, symbol, volume, price, label, slPips, tpPips)` — pips-based overload; newer API has `ProtectionType` overload
- `Indicators.GetIndicator<T>(params)` — params in declaration order, requires Manage References in IDE
- Indicators use `Calculate(int index)` only (no OnBar); use `lastIndex` pattern for bar-close detection
- cBots have `OnBarClosed()` — fires on confirmed bar, safe for non-repainting decisions
- `Notifications.PlaySound()` / `.SendEmail()` — don't work during backtest
- `TimeZones.EasternStandardTime` handles DST automatically

## Phase Breakdown

| # | Phase | Effort | Key Deliverable |
|---|---|---|---|
| 1 | Project Setup | 1h | Boilerplate, all [Parameter] inputs, enum, field declarations |
| 2 | Session & Range | 1.5h | London range tracking, DrawRectangle box, level lines |
| 3 | MTF Filters | 1.5h | H4 EMA + H1 momentum with Last(1) non-repainting |
| 4 | Sweep Detection | 1.5h | Core strategy logic, trade levels, [Output] population |
| 5 | Dashboard | 1h | DrawStaticText status panel (6+ rows) |
| 6 | cBot Signals | 1.5h | GetIndicator<>, read outputs, PlaceLimitOrder |
| 7 | cBot Management | 1h | EOD cutoff, cleanup, notifications, event handlers |
| 8 | Testing | 1h | Backtest + forward test + cross-platform comparison |

## Key Technical Decisions

1. **Bar-close detection in indicator**: `if (index > _lastIndex)` pattern inside `Calculate()` — equivalent to Pine's `barstate.isconfirmed`
2. **Non-repainting**: All MTF reads use `.Last(1)` (confirmed bar), never `.Last(0)` or `.LastValue`
3. **Dashboard**: Single `DrawStaticText` with multi-line string + ASCII markers `[+][-][=]` to compensate for no per-cell coloring
4. **SL/TP conversion**: Indicator outputs absolute prices; cBot converts to pips via `|entry - sl| / Symbol.PipSize`
5. **London level lines**: `DrawHorizontalLine` (extends infinitely) — alternative is `DrawTrendLine` with capped endpoint if cleaner look preferred
6. **Output convention**: `1.0` = signal active, `double.NaN` = no signal in `IndicatorDataSeries`

## Unresolved Questions

1. Exact pip size for `#US30` on FxPro — runtime check needed via `Symbol.PipSize`
2. Whether FxPro restricts `AccessRights.FullAccess` for email — test on demo
3. DST edge cases around March/November transitions — manual validation
4. Which `PlaceLimitOrder` overload available on user's cTrader version (pips vs ProtectionType)
5. Minor EMA seeding differences between Pine and cTrader — may cause ~1-2 point drift on H4 EMA values
