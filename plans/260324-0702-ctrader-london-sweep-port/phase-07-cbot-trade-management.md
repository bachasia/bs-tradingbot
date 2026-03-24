# Phase 7: cBot — Trade Management, EOD Cutoff, Notifications, Logging

## Context Links
- [Phase 6: cBot Signals & Orders](./phase-06-cbot-signals-orders.md)
- [PineScript Source — Trade Management](../../london-sweep-indicator.pine) (lines 227-278)
- [PineScript Source — Alerts](../../london-sweep-indicator.pine) (lines 318-329)
- [Brainstorm — Pending Order Flow](../../plans/reports/brainstorm-260323-2330-ctrader-london-sweep-port.md) (lines 96-104)
- [cTrader INotifications API](https://help.ctrader.com/ctrader-algo/references/Notifications/INotifications/)
- [cTrader Robot API](https://help.ctrader.com/ctrader-algo/references/General/Robot/)

## Overview
- **Priority**: P1 — completes the cBot execution cycle
- **Status**: pending
- **Effort**: 1h
- **Description**: Implement EOD position/order cleanup, pending order expiration management, sound/email notifications, comprehensive logging, and `OnStop()` cleanup. This completes the cBot's lifecycle.

## Key Insights

### EOD Cutoff Logic
- Pine indicator tracks EOD internally for visuals (Phase 4 handles this in indicator)
- cBot must independently close open positions AND cancel unfilled pending orders at EOD
- Check in `OnBarClosed()`: if current bar time >= `EodCutoffHHMM`, close everything
- Use `Positions` collection to find open positions by label, `PendingOrders` for unfilled orders

### Position vs Pending Order Management
- After `PlaceLimitOrder`, the order sits in `PendingOrders` until filled or cancelled
- Once filled, it moves to `Positions`
- cBot must handle both states at EOD:
  1. Cancel any unfilled pending orders labeled with `_botLabel`
  2. Close any open positions labeled with `_botLabel`

### Notifications API
- `Notifications.PlaySound(SoundType.Good)` — built-in success sound
- `Notifications.PlaySound(SoundType.Buzz)` — built-in alert/warning sound
- `Notifications.PlaySound("C:\\path\\to\\file.wav")` — custom sound file
- `Notifications.SendEmail(from, to, subject, body)` — requires email configured in cTrader Preferences
- Notifications do NOT work during backtesting — guard with `!IsBacktesting`

### Logging Best Practices
- `Print(format, args)` writes to cTrader's Log tab
- Prefix all messages with bot name for easy filtering
- Log every state transition and decision for debugging
- During backtest, Print still works (unlike notifications)

## Requirements

### Functional
- EOD cutoff: At `EodCutoffHHMM`, cancel all bot's pending orders and close all bot's positions
- Pending order timeout: If no fill by EOD, cancel the order (handled by EOD cutoff)
- Sound notifications: On signal detection, order placement, SL/TP fill, EOD close
- Logging: Every decision, order event, and error logged to Print
- OnStop cleanup: Cancel all bot's pending orders and close positions when bot is stopped manually

### Non-Functional
- Notifications gated by `!IsBacktesting` (don't play sounds during backtest)
- Email notifications optional — only if `AccessRights` upgraded and email configured
- All position/order operations wrapped in try/catch
- Clean shutdown: no orphaned orders left when bot stops

## Architecture

```
OnBarClosed()  (additions to Phase 6 flow)
├── ... existing signal reading + order placement ...
├── EOD Cutoff Check
│   ├── hhmm >= EodCutoffHHMM?
│   ├── Cancel all pending orders with _botLabel
│   ├── Close all open positions with _botLabel
│   └── Log results
└── Position Event Logging (SL/TP fills)

OnStart()  (additions)
├── Subscribe to Positions.Closed event for SL/TP logging
└── Subscribe to PendingOrders.Cancelled event for logging

OnStop()
├── Cancel all pending orders with _botLabel
├── Close all open positions with _botLabel
└── Log "Bot stopped"

Event Handlers
├── OnPositionClosed(PositionClosedEventArgs) — log SL/TP/manual close
└── OnPendingOrderCancelled(...) — log cancellation
```

## Related Code Files

### Files to Modify
- `ctrader/LondonSweepBot.cs` — `OnBarClosed()`, `OnStart()`, `OnStop()`, new event handlers

## Implementation Steps

### Step 1: Add position event subscriptions in OnStart()

Append to existing `OnStart()`:

```csharp
    // ─── SUBSCRIBE TO TRADE EVENTS ────────────────────────────────────
    // These fire when positions close (SL/TP hit, manual close, EOD close).
    // We use them to log trade outcomes.
    Positions.Closed += OnPositionClosed;
    PendingOrders.Filled += OnPendingOrderFilled;
```

### Step 2: Add EOD cutoff check in OnBarClosed()

Add this section at the END of `OnBarClosed()`, after the signal/order logic:

```csharp
    // ─── EOD CUTOFF ───────────────────────────────────────────────────
    // At end-of-day, cancel unfilled pending orders and close any
    // open positions from this bot. This prevents overnight exposure.
    //
    // Pine equivalent:
    //   if isEOD and not slHit and not tpHit
    //       eodClose := true
    int hhmm = Bars.OpenTimes.Last(1).Hour * 100 + Bars.OpenTimes.Last(1).Minute;
    if (hhmm >= EodCutoffHHMM)
    {
        CancelBotPendingOrders("EOD cutoff");
        CloseBotPositions("EOD cutoff");
    }
```

### Step 3: Implement helper — cancel all bot's pending orders

```csharp
// ─── HELPER: Cancel all pending orders placed by this bot ─────────────
// Finds orders by label prefix and cancels them.
private void CancelBotPendingOrders(string reason)
{
    // PendingOrders is a live collection — iterate a snapshot (ToList)
    // to avoid modification during enumeration.
    var botOrders = PendingOrders
        .Where(o => o.Label != null && o.Label.StartsWith(_botLabel))
        .ToList();

    foreach (var order in botOrders)
    {
        try
        {
            CancelPendingOrder(order);
            Print("[{0}] Cancelled pending order: {1} {2} at {3:F1} — Reason: {4}",
                _botLabel, order.TradeType, order.SymbolName, order.TargetPrice, reason);
        }
        catch (Exception ex)
        {
            Print("[{0}] ERROR cancelling order: {1}", _botLabel, ex.Message);
        }
    }
}
```

### Step 4: Implement helper — close all bot's positions

```csharp
// ─── HELPER: Close all open positions placed by this bot ──────────────
private void CloseBotPositions(string reason)
{
    var botPositions = Positions
        .Where(p => p.Label != null && p.Label.StartsWith(_botLabel))
        .ToList();

    foreach (var position in botPositions)
    {
        try
        {
            ClosePosition(position);
            Print("[{0}] Closed position: {1} {2}, P&L={3:F2} — Reason: {4}",
                _botLabel, position.TradeType, position.SymbolName,
                position.NetProfit, reason);

            // Sound notification (not during backtest)
            if (EnableSound && !IsBacktesting)
                Notifications.PlaySound(SoundType.Buzz);
        }
        catch (Exception ex)
        {
            Print("[{0}] ERROR closing position: {1}", _botLabel, ex.Message);
        }
    }
}
```

### Step 5: Implement position closed event handler

```csharp
// ─── EVENT: Position Closed (SL/TP/manual) ────────────────────────────
// Fires whenever any position closes. We filter to our bot's positions.
private void OnPositionClosed(PositionClosedEventArgs args)
{
    var pos = args.Position;

    // Only log our bot's trades
    if (pos.Label == null || !pos.Label.StartsWith(_botLabel))
        return;

    string closeReason = args.Reason.ToString();  // StopLoss, TakeProfit, Closed, StopOut

    Print("[{0}] === TRADE CLOSED ===", _botLabel);
    Print("[{0}]   Direction: {1}", _botLabel, pos.TradeType);
    Print("[{0}]   Entry: {1:F1}", _botLabel, pos.EntryPrice);
    Print("[{0}]   Close Reason: {1}", _botLabel, closeReason);
    Print("[{0}]   Net P&L: {1:F2}", _botLabel, pos.NetProfit);
    Print("[{0}]   Pips: {1:F1}", _botLabel, pos.Pips);

    // Sound notification
    if (EnableSound && !IsBacktesting)
    {
        if (args.Reason == PositionCloseReason.TakeProfit)
            Notifications.PlaySound(SoundType.Good);
        else
            Notifications.PlaySound(SoundType.Buzz);
    }
}
```

### Step 6: Implement pending order filled event handler

```csharp
// ─── EVENT: Pending Order Filled ──────────────────────────────────────
// Fires when a limit order fills (price reached the entry level).
private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
{
    var order = args.PendingOrder;

    if (order.Label == null || !order.Label.StartsWith(_botLabel))
        return;

    Print("[{0}] === ORDER FILLED ===", _botLabel);
    Print("[{0}]   Direction: {1} at {2:F1}", _botLabel, order.TradeType, order.TargetPrice);
    Print("[{0}]   Volume: {1}", _botLabel, order.VolumeInUnits);

    if (EnableSound && !IsBacktesting)
        Notifications.PlaySound(SoundType.Good);
}
```

### Step 7: Implement OnStop() — cleanup

```csharp
protected override void OnStop()
{
    // ─── CLEANUP ON BOT STOP ──────────────────────────────────────────
    // When the user stops the bot (or it crashes), clean up everything.
    // This prevents orphaned orders sitting in the account.
    Print("[{0}] Bot stopping — cleaning up...", _botLabel);

    CancelBotPendingOrders("Bot stopped");
    CloseBotPositions("Bot stopped");

    // Unsubscribe from events (good practice)
    Positions.Closed -= OnPositionClosed;
    PendingOrders.Filled -= OnPendingOrderFilled;

    Print("[{0}] Bot stopped.", _botLabel);
}
```

### Step 8: Gate notifications behind IsBacktesting check

Review all `Notifications.PlaySound()` calls in Phase 6 and wrap them:

```csharp
    // Sound alerts don't work during backtesting — guard them.
    if (EnableSound && !IsBacktesting)
        Notifications.PlaySound(SoundType.Good);
```

### Step 9 (Optional): Add email notification support

If user wants email alerts, upgrade `AccessRights` and add email parameter:

```csharp
// In class attribute:
[Robot("London Sweep Bot", TimeZone = TimeZones.EasternStandardTime,
    AccessRights = AccessRights.FullAccess)]  // Changed for email

// New parameter:
[Parameter("Email Address", Group = "Bot Settings", DefaultValue = "")]
public string EmailAddress { get; set; }

// In signal detection:
if (!string.IsNullOrEmpty(EmailAddress) && !IsBacktesting)
{
    Notifications.SendEmail(
        EmailAddress,  // from (cTrader uses configured SMTP)
        EmailAddress,  // to
        "London Sweep: " + directionStr + " Signal",
        string.Format("Entry: {0:F1}\nSL: {1:F1}\nTP: {2:F1}",
            entryPrice, slPrice, tpPrice)
    );
}
```

**Note**: Email requires SMTP configuration in cTrader → Preferences → Email Settings. Mark this as optional — sound alerts work out of the box.

## Todo List

- [ ] Subscribe to `Positions.Closed` event in OnStart
- [ ] Subscribe to `PendingOrders.Filled` event in OnStart
- [ ] Add EOD cutoff check at end of `OnBarClosed()`
- [ ] Implement `CancelBotPendingOrders(reason)` helper
- [ ] Implement `CloseBotPositions(reason)` helper
- [ ] Implement `OnPositionClosed` event handler with P&L logging
- [ ] Implement `OnPendingOrderFilled` event handler
- [ ] Implement `OnStop()` with cleanup and event unsubscribe
- [ ] Gate all `PlaySound` calls behind `!IsBacktesting`
- [ ] (Optional) Add email notification support with `AccessRights.FullAccess`
- [ ] Verify EOD cutoff cancels pending orders at 15:00 EST
- [ ] Verify OnStop cancels orders and closes positions

## Success Criteria
- At 15:00 EST: all bot's pending orders cancelled, all positions closed
- Stopping the bot: same cleanup behavior as EOD
- Backtest: no sound/email calls, but Print logging works
- Live: sound plays on signal, order fill, SL hit, TP hit, EOD close
- All trade outcomes logged with P&L, direction, prices
- No orphaned orders after bot stops

## Risk Assessment
- **Collection modification during enumeration**: `PendingOrders` and `Positions` are live — always `.ToList()` before iterating with modifications. Already handled in helpers above
- **PositionCloseReason enum values**: Need to verify exact enum members (`StopLoss`, `TakeProfit`, `Closed`, `StopOut`). If API differs, cast to string for safe logging
- **Event handler memory leak**: If OnStop doesn't unsubscribe, event handlers persist. Always unsubscribe in OnStop
- **EOD fires multiple times**: Every bar after 15:00 triggers the EOD check. Since positions/orders are already closed after first trigger, subsequent calls find nothing — safe but noisy in logs. Mitigation: add `_eodDone` flag, reset on new day
- **Concurrent events**: `OnBarClosed` and event handlers may fire in quick succession. cTrader processes them sequentially on the main thread — no race conditions

### Mitigation for repeated EOD logging

```csharp
private bool _eodDone = false;

// In day change reset:
_eodDone = false;

// In EOD check:
if (hhmm >= EodCutoffHHMM && !_eodDone)
{
    _eodDone = true;
    CancelBotPendingOrders("EOD cutoff");
    CloseBotPositions("EOD cutoff");
}
```

## Security Considerations
- `AccessRights.None` sufficient for trading + sound notifications
- Email requires `AccessRights.FullAccess` — upgrade only if user explicitly wants email
- No API keys or credentials stored in code
- Order labels contain only strategy name — no sensitive data

## Next Steps
- Phase 8: Testing on FxPro demo, validation checklist
