# Research Report: cTrader Automation + Indicator Capabilities (for Pine/TradingView porting)

- Conducted: 2026-03-23
- Scope: practical implementation details only, no marketing

## TL;DR
cTrader Automate uses C# (plus newer Python support) on top of .NET APIs. cBots are primary vehicle for automation/trade execution; indicators are primary for analytics/visuals, but API surface overlaps more than most people expect. Multi-timeframe and multi-symbol access is first-class via `MarketData.GetBars(...)`. Alerts are code-driven notifications (popup/sound/email), not a direct Pine `alertcondition()` equivalent. External integration exists via Open API and HTTP/WebSocket from algos; inbound webhook endpoint inside algo runtime is not native.

## 1) cTrader Automate (cAlgo): language + SDK reality
- Yes, core language is **C#** (`cAlgo.API`).
- cTrader docs also show **Python algo support** (via .NET interop wrapper), but C# is the mainstream path.
- SDK style: event-driven classes inheriting from `Robot` (cBot) or `Indicator`.
- Typical cBot lifecycle hooks: `OnStart`, `OnTick`, `OnBar`, `OnBarClosed`, `OnStop`, `OnError`.
- Trade APIs are rich and overloaded (sync + async versions), but overload complexity is a footgun.

Practical implication:
- Pine porting to cTrader is not syntax translation only. Need architecture rewrite into OOP + events.

## 2) Indicators vs cBots; drawing capability
### Difference (practical)
- **cBot**: intended for automated execution + position/order management.
- **Indicator**: intended for analysis/visualization/signals.
- cTrader docs indicate indicator API can expose trading methods too, but platform workflow and product expectations still separate concerns: use cBots for execution.

### Drawing capability (TradingView-like objects)
Yes, indicators/cBots can draw chart objects, including:
- lines: horizontal, vertical, trend lines
- text/labels: text/static text
- arrows/icons/channels/fibonacci/shapes/risk-reward tools

Notes:
- Objects are scoped to the running algo instance.
- Objects are removed when algo stops.

## 3) Multi-timeframe access (e.g., H1/H4 from M15)
Supported natively.

Key API:
- `MarketData.GetBars(TimeFrame tf)`
- `MarketData.GetBars(TimeFrame tf, string symbol)`
- async variants: `GetBarsAsync(...)`

Example pattern:
```csharp
var h1 = MarketData.GetBars(TimeFrame.Hour);
var h4 = MarketData.GetBars(TimeFrame.Hour4);
```

Also supports cross-symbol MTF:
```csharp
var eurH1 = MarketData.GetBars(TimeFrame.Hour, "EURUSD");
```

Porting caveat vs Pine `request.security()`:
- cTrader gives direct series objects, but **bar alignment/synchronization logic is yours**.

## 4) Time/session handling (EST/EDT)
- Algo timezone can be set by attribute using `TimeZones.*` constants.
- `TimeZones.EasternStandardTime` exists.
- Docs do not clearly spell out DST handling semantics for every case in the reference page.
- Platform also has user-level time offset settings.

Practical approach:
- Set algo timezone explicitly.
- For session logic across DST boundaries, test around DST transitions, do not assume behavior.

## 5) Alert system vs TradingView `alertcondition()`
No direct 1:1 declarative `alertcondition()` equivalent in the API docs reviewed.

What you get in code:
- popup: `Notifications.ShowPopup(...)`
- sound: `Notifications.PlaySound(...)`
- email: `Notifications.SendEmail(...)`

Limitations:
- sound/email documented as not working during backtesting/optimization.
- alerts are imperative code checks, not declarative condition registration model like Pine.

## 6) Trade execution in cBots; SL/TP
Yes, cBots can place and manage orders with SL/TP.

Core methods include:
- `ExecuteMarketOrder(...)`
- `PlaceLimitOrder(...)`
- `PlaceStopOrder(...)`
- `PlaceStopLimitOrder(...)`
- position mgmt: `ModifyPosition`, `ClosePosition`, `ReversePosition`
- pending order mgmt: `ModifyPendingOrder`, `CancelPendingOrder`

Risk controls available in API surface:
- stop loss / take profit params
- trailing stop flags
- stop trigger method options

Compared to TradingView strategy:
- TradingView strategy = mostly backtest/signal framework + broker bridge depending setup.
- cBot = direct execution runtime inside cTrader terminal/cloud ecosystem, with native order/position objects.

## 7) cTrader Copy / sharing / selling automation
Two channels:
1. **cTrader Copy**: run as strategy provider; investors copy trades. Supports fee models (performance/management/volume with constraints).
2. **cTrader Store**: marketplace for cBots/indicators/plugins (free or paid), with product review/moderation, pricing rules, encrypted distribution.

Important constraints from docs:
- Provider must use live account and hedging account for copy provisioning.
- Store has moderation gates, disclosure requirements, and fixed commission model.

## 8) Key differences from PineScript that affect porting
Brutal list:
- Paradigm shift: declarative-ish Pine series model -> C# event-driven OOP.
- Type strictness: dynamic-feeling Pine vs strict C# types/classes/interfaces.
- MTF: Pine `request.security()` convenience vs manual Bars handling + sync discipline.
- Drawings: Pine convenience calls vs explicit object lifecycle management.
- Alerts: Pine `alertcondition()` vs imperative notifications and/or external HTTP integration.
- Execution model: Pine strategy often simulation-first; cBot executes real orders via broker account.
- Permissions: cTrader has `AccessRights` model (network/local restrictions).

## 9) cTrader API / web API / webhooks
- **Open API exists** for external apps (auth via cTrader ID/app registration; JSON or Protobuf message protocol).
- From inside algos, you can perform outbound HTTP + WebSocket communication.
- `AccessRights.None` is documented as sufficient for network functions in current docs.
- Native inbound webhook listener inside cBot/indicator runtime is not documented as first-class.
  - Practical workaround: host your own external webhook service, then push/pull via HTTP/WebSocket/Open API.

## 10) Community / marketplace equivalent to TradingView scripts
Yes, closest equivalents:
- **cTrader Store** for packaged cBots/indicators/plugins.
- **cTrader Copy** for copyable strategy products.
- There are also GitHub sample repos from Spotware for starter code.

## Practical limitations you should plan for
- API overload density can slow dev and increase parameter-order bugs.
- Indicator/cBot boundary is blurrier in API than in conceptual docs; keep architecture clean anyway.
- Notification behavior differs between live vs backtest.
- Session logic around DST needs explicit validation.
- If your TradingView workflow depends heavily on webhook inbound events, cTrader needs an external bridge design.

## Suggested porting strategy (minimal-risk)
1. Port indicator logic first into cTrader Indicator (visual parity).
2. Port execution/risk into cBot separately (clean separation).
3. Add MTF module with explicit bar sync checks.
4. Add session module with DST regression tests (US/Eastern transition weeks).
5. Add alert adapter layer (popup/sound/email + optional HTTP bridge).
6. Only then optimize execution and deploy copy/store packaging.

## Sources
- https://help.ctrader.com/ctrader-algo/references/General/Robot/
- https://help.ctrader.com/ctrader-algo/references/General/Indicator/
- https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/ChartObject/
- https://help.ctrader.com/ctrader-algo/references/MarketData/MarketData/
- https://help.ctrader.com/ctrader-algo/references/Period/TimeFrame/
- https://help.ctrader.com/ctrader-algo/references/Utility/TimeZones/
- https://help.ctrader.com/ctrader-algo/references/Notifications/INotifications/
- https://help.ctrader.com/ctrader-algo/documentation/all-algos/email-notifications/
- https://help.ctrader.com/ctrader-algo/guides/network-access/
- https://help.ctrader.com/ctrader-algo/guides/access-rights/
- https://help.ctrader.com/open-api/
- https://help.ctrader.com/open-api/api-application/
- https://openapi.ctrader.com/
- https://help.ctrader.com/ctrader-copy/becoming-a-strategy-provider/
- https://help.ctrader.com/ctrader-store/
- https://help.ctrader.com/ctrader-store/how-tos/publish-a-product/
- https://github.com/spotware/ctrader-algo-samples
- https://github.com/spotware/OpenApiPy

## Unresolved questions
1. Exact DST behavior contract for `TimeZones.EasternStandardTime` during historical backtests vs live runtime.
2. Current hard limits/rate limits for HTTP/WebSocket usage from Automate runtime (not clearly stated in pages reviewed).
3. Official best-practice from Spotware on indicator-initiated trade calls (possible in API surface, but likely discouraged architecturally).
