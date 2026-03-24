# cTrader Automate C# API Research Report
**Date:** March 24, 2026
**Scope:** Multi-timeframe data, indicators, time/session handling, order execution, chart visualization, cBot/Indicator architecture, scheduling, FTMO compliance

---

## 1. Multi-Timeframe Data Access

### MarketData.GetBars() API

**Core Method:**
```csharp
// Get bars for any timeframe on current symbol
Bars h4Bars = MarketData.GetBars(TimeFrame.Hour4);
Bars h1Bars = MarketData.GetBars(TimeFrame.Hour);
Bars m15Bars = MarketData.GetBars(TimeFrame.Minute15);

// Get bars for different symbol
Bars eurusdBars = MarketData.GetBars(TimeFrame.Hour4, "EURUSD");
```

**DataSeries Access:**
```csharp
// Extract close prices from multi-timeframe bars
DataSeries h4Closes = h4Bars.ClosePrices;
DataSeries h1Highs = h1Bars.HighPrices;
DataSeries m15Lows = m15Bars.LowPrices;

// Get specific bar element
double lastH4Close = h4Bars.ClosePrices.Last(0); // Current bar
double prevH4Close = h4Bars.ClosePrices.Last(1); // Previous bar
```

**Async Support:**
```csharp
// Asynchronous loading for large datasets
Bars barsAsync = await MarketData.GetBarsAsync(TimeFrame.Daily);
```

**Index Alignment Issue:**
When timeframes don't align (e.g., H4 vs M15), use time-based indexing:
```csharp
// Find H4 bar index corresponding to current M15 bar time
int h4Index = h4Bars.OpenTimes.GetIndexByTime(m15Bars.OpenTimes.Last(0));
if (h4Index >= 0)
{
    double h4CloseAtM15Time = h4Bars.ClosePrices[h4Index];
}
```

**Important Limitation:**
- Other timeframes may not load same historical data as chart timeframe
- Use `LoadMoreHistory()` on alternate bars if needed:
```csharp
// Load same amount of history as main chart
h4Bars.LoadMoreHistory();
```

**Best Practice for London Sweep:**
```csharp
// In OnStart() or OnBar()
private Bars h4Bars;
private Bars h1Bars;
private Bars m15Bars;

protected override void OnStart()
{
    // Initialize all timeframes
    h4Bars = MarketData.GetBars(TimeFrame.Hour4);
    h1Bars = MarketData.GetBars(TimeFrame.Hour);
    m15Bars = MarketData.GetBars(TimeFrame.Minute15);

    // Sync history if needed
    h4Bars.LoadMoreHistory();
    h1Bars.LoadMoreHistory();
}

protected override void OnBar()
{
    // Access current bars safely
    if (h4Bars.Count > 0 && h1Bars.Count > 0 && m15Bars.Count > 0)
    {
        // Strategy logic here
    }
}
```

---

## 2. EMA Indicators on Multiple Timeframes

### Built-in ExponentialMovingAverage API

**Basic Usage:**
```csharp
// On main chart timeframe (Bars.ClosePrices)
var ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
var ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);

// Access values
double currentEma20 = ema20.Result.Last(0);
double previousEma20 = ema20.Result.Last(1);

// Check if rising/falling
bool ema20Rising = ema20.Result.IsRising();
bool ema20Falling = ema20.Result.IsFalling();
```

**Multi-Timeframe Pattern:**
```csharp
// Declare in class
private Bars h4Bars;
private IndicatorDataSeries ema20H4;
private IndicatorDataSeries ema50H4;

protected override void OnStart()
{
    h4Bars = MarketData.GetBars(TimeFrame.Hour4);

    // Create indicators on H4 bars
    var ema20Indicator = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 20);
    var ema50Indicator = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 50);

    ema20H4 = ema20Indicator.Result;
    ema50H4 = ema50Indicator.Result;
}

protected override void OnBar()
{
    // Check H4 trend
    bool h4Bullish = ema20H4.Last(0) > ema50H4.Last(0);
    bool h4Bearish = ema20H4.Last(0) < ema50H4.Last(0);
}
```

**Indicator Method Signatures:**
```csharp
// Available parameters
Indicators.ExponentialMovingAverage(
    DataSeries source,      // HighPrices, LowPrices, ClosePrices, or custom
    int period              // e.g., 20, 50, 200
)

// Returns IndicatorResult with:
// - Result: The EMA values (DataSeries)
// - IsRising(): True if last value > previous
// - IsFalling(): True if last value < previous
```

**London Sweep Implementation:**
```csharp
private Bars h4Bars;
private IndicatorDataSeries ema20H4, ema50H4;
private Bars h1Bars;
private IndicatorDataSeries ema20H1, ema50H1;

protected override void OnStart()
{
    h4Bars = MarketData.GetBars(TimeFrame.Hour4);
    h1Bars = MarketData.GetBars(TimeFrame.Hour);

    ema20H4 = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 20).Result;
    ema50H4 = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 50).Result;

    ema20H1 = Indicators.ExponentialMovingAverage(h1Bars.ClosePrices, 20).Result;
    ema50H1 = Indicators.ExponentialMovingAverage(h1Bars.ClosePrices, 50).Result;
}

protected override void OnBar()
{
    // Step 3: Determine H4 trend
    bool h4Bullish = ema20H4.Last(0) > ema50H4.Last(0);
    bool h4Bearish = ema20H4.Last(0) < ema50H4.Last(0);

    // Would filter entry direction
}
```

---

## 3. Time & Session Handling (EST/DST)

### Server Time Access

**Basic Time API:**
```csharp
// Server time (typically UTC or broker server time)
DateTime serverTime = Server.Time;
DateTime serverTimeUtc = Server.TimeInUtc;

// Current bar time
DateTime barOpenTime = Bars.OpenTimes.Last(0);

// Hour/minute checks
if (Server.Time.Hour == 9 && Server.Time.Minute == 0)
{
    // Triggered at 09:00 server time
}
```

**Convert Server Time to EST (Eastern Standard Time):**
```csharp
// Create EST timezone
TimeZoneInfo estTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

// Convert from UTC to EST
DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(Server.TimeInUtc, estTimeZone);
int estHour = estTime.Hour;
int estMinute = estTime.Minute;

// Check for 09:00 EST (London close)
if (estTime.Hour == 9 && estTime.Minute == 0)
{
    OnLondonSessionClose();
}
```

**DST Handling (Automatic):**
`TimeZoneInfo.ConvertTimeFromUtc()` automatically handles DST transitions:
- Summer (EDT): UTC-4
- Winter (EST): UTC-5

**⚠️ Important Note on Aggregation:**
cTrader aggregates daily candles at **17:00 EST** (fixed), which means:
- Summer: 21:00 UTC (EDT)
- Winter: 22:00 UTC (EST)

**London Session Time Detection (03:00–09:00 EST):**
```csharp
// Class-level variables
private TimeZoneInfo estTimeZone;
private DateTime londonSessionStart;  // 03:00 EST
private DateTime londonSessionEnd;    // 09:00 EST

protected override void OnStart()
{
    estTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
}

protected override void OnBar()
{
    // Convert current bar time to EST
    DateTime barTimeEst = TimeZoneInfo.ConvertTimeFromUtc(
        Bars.OpenTimes.Last(0).ToUniversalTime(),
        estTimeZone
    );

    // Check if bar closed during London session (03:00–09:00)
    int barHourEst = barTimeEst.Hour;
    bool inLondonSession = barHourEst >= 3 && barHourEst < 9;
}
```

**Session Detection Best Practice:**
```csharp
private bool IsLondonSessionOpen()
{
    DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(
        Server.TimeInUtc,
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")
    );

    int hour = estTime.Hour;
    int minute = estTime.Minute;

    // Exact: 03:00–09:00 EST
    return (hour > 3 || (hour == 3 && minute >= 0)) &&
           (hour < 9 || (hour == 9 && minute < 0));
}

private bool IsLondonSessionClosed()
{
    DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(
        Server.TimeInUtc,
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")
    );

    return estTime.Hour == 9 && estTime.Minute == 0;
}
```

---

## 4. Order Execution & Position Management

### Market Order with SL/TP

**ExecuteMarketOrder() Syntax:**
```csharp
// Simple market order
var result = ExecuteMarketOrder(TradeType.Buy, Symbol, Volume, "TradeLabel");

// With Stop Loss & Take Profit (in pips)
var result = ExecuteMarketOrder(
    TradeType.Buy,           // TradeType.Buy or TradeType.Sell
    Symbol,                  // e.g., "#US30"
    Volume,                  // Position size (lots)
    "LondonSweep",          // Label for tracking
    stopLossPips,           // SL distance in pips from entry
    takeProfitPips          // TP distance in pips from entry
);
```

**Full Example with Error Handling:**
```csharp
protected override void OnBar()
{
    // When sweep is confirmed
    if (IsSweepConfirmed())
    {
        double entryPrice = Bars.ClosePrices.Last(0);
        double stopLossPips = 8;  // As per strategy
        double londonRangeSize = londonHigh - londonLow;
        double takeProfitPips = londonRangeSize * 0.65;

        var result = ExecuteMarketOrder(
            TradeType.Buy,
            Symbol,
            Volume,
            "LondonSweep",
            stopLossPips,
            takeProfitPips
        );

        if (result.IsSuccessful)
        {
            Print($"Position opened: {result.Position.Id}");
            tradeCount++;
        }
        else
        {
            Print($"Order failed: {result.Error}");
        }
    }
}
```

### Position Management

**Access Open Positions:**
```csharp
// Iterate all open positions
foreach (var position in Positions)
{
    Print($"Position ID: {position.Id}, Pips: {position.Pips}");
}

// Find position by label
var sweepPosition = Positions.Find("LondonSweep");
if (sweepPosition != null)
{
    Print($"Label: {sweepPosition.Label}, Volume: {sweepPosition.Volume}");
}
```

**Modify Position (SL/TP):**
```csharp
var position = Positions.Find("LondonSweep");
if (position != null)
{
    // Modify stop loss & take profit
    ModifyPosition(position,
        position.EntryPrice - (10 * Symbol.PipSize),  // New SL
        position.EntryPrice + (50 * Symbol.PipSize)   // New TP
    );
}
```

**Close Position:**
```csharp
// By label
var position = Positions.Find("LondonSweep");
if (position != null)
{
    ClosePosition(position);
}

// At market price
var closeResult = ExecuteMarketOrder(
    position.TradeType == TradeType.Buy ? TradeType.Sell : TradeType.Buy,
    Symbol,
    position.Volume,
    "LondonSweep-Close"
);
```

**Force Close Before 15:00 EST:**
```csharp
protected override void OnTick()
{
    // Check if past 15:00 EST
    DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(
        Server.TimeInUtc,
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")
    );

    if (estTime.Hour >= 15)
    {
        // Close all open London Sweep positions
        foreach (var position in Positions.Where(p => p.Label == "LondonSweep"))
        {
            ClosePosition(position);
        }
    }
}
```

### Async Execution

```csharp
// Async market order
var result = await ExecuteMarketOrderAsync(
    TradeType.Buy,
    Symbol,
    Volume,
    "LondonSweep",
    stopLossPips,
    takeProfitPips
);
```

---

## 5. Drawing on Chart (Visualization in Indicator)

### Horizontal Lines for London Range

**DrawHorizontalLine() Usage:**
```csharp
// In Indicator OnBar() method
protected override void OnBar()
{
    if (IsFirstBarOfLondonSession())
    {
        // Get high/low from previous London session or H4 bars
        double londonHigh = GetLondonHigh();
        double londonLow = GetLondonLow();

        // Draw horizontal lines
        var lineLow = Chart.DrawHorizontalLine(
            "LondonLow",           // Unique name
            londonLow,             // Y value (price)
            Color.Red,             // Color
            2,                     // Thickness
            LineStyle.Solid        // Style
        );

        var lineHigh = Chart.DrawHorizontalLine(
            "LondonHigh",
            londonHigh,
            Color.Blue,
            2,
            LineStyle.Solid
        );

        // Make interactive (users can move them)
        lineLow.IsInteractive = true;
        lineHigh.IsInteractive = true;
    }
}
```

### Rectangle for London Range Zone

**DrawRectangle() Usage:**
```csharp
protected override void OnBar()
{
    // Draw rectangle from 03:00 to 09:00 EST bars
    int barIndex03EST = FindBarIndexAtTime(03, 0);  // 03:00 EST
    int barIndex09EST = FindBarIndexAtTime(09, 0);  // 09:00 EST

    if (barIndex03EST >= 0 && barIndex09EST > barIndex03EST)
    {
        double londonLow = GetLondonLow();
        double londonHigh = GetLondonHigh();

        var rectangle = Chart.DrawRectangle(
            "LondonRange",           // Name
            barIndex03EST,           // Start bar index
            londonLow,               // Bottom price
            barIndex09EST,           // End bar index
            londonHigh,              // Top price
            Color.Yellow             // Border color
        );

        // Semi-transparent fill
        rectangle.IsFilled = true;
        rectangle.FillColor = Color.FromArgb(30, Color.Yellow);  // 30/255 opacity
        rectangle.IsInteractive = false;
    }
}
```

### Complete Visualization Example for London Sweep Indicator

```csharp
[Indicator(IsOverlay = true, TimeZone = TimeZoneInfo.Utc, AccessRights = AccessRights.None)]
public class LondonSweepIndicator : Indicator
{
    private double londonHigh, londonLow;
    private int londonSessionStartBar, londonSessionEndBar;

    protected override void OnBar()
    {
        // Detect London session (03:00–09:00 EST)
        DateTime barTimeEst = ConvertToEST(Bars.OpenTimes.Last(0));

        // **First bar of London session (03:00 EST)**
        if (IsNewBar && barTimeEst.Hour == 3 && barTimeEst.Minute == 0)
        {
            londonSessionStartBar = Bars.Count - 1;

            // Clear old drawings
            Chart.RemoveAllObjects();

            // Initialize high/low trackers
            londonHigh = Bars.HighPrices.Last(0);
            londonLow = Bars.LowPrices.Last(0);
        }

        // **Track high/low during session**
        if (IsInLondonSession(barTimeEst))
        {
            londonHigh = Math.Max(londonHigh, Bars.HighPrices.Last(0));
            londonLow = Math.Min(londonLow, Bars.LowPrices.Last(0));
        }

        // **London session close (09:00 EST)**
        if (barTimeEst.Hour == 9 && barTimeEst.Minute == 0)
        {
            londonSessionEndBar = Bars.Count - 1;

            // Draw range zone
            Chart.DrawRectangle(
                "LondonRange",
                londonSessionStartBar,
                londonLow,
                londonSessionEndBar,
                londonHigh,
                Color.Yellow
            );

            // Draw high/low lines
            Chart.DrawHorizontalLine("LondonHigh", londonHigh, Color.Blue, 2, LineStyle.Solid);
            Chart.DrawHorizontalLine("LondonLow", londonLow, Color.Red, 2, LineStyle.Solid);

            // Add range size label
            double rangeSize = londonHigh - londonLow;
            ChartText text = Chart.DrawText(
                "RangeText",
                $"Range: {rangeSize:F2}",
                Bars.Count - 1,
                londonHigh + (Symbol.PipSize * 50),
                Color.Black
            );
            text.BackgroundColor = Color.White;
        }
    }

    private DateTime ConvertToEST(DateTime utcTime)
    {
        TimeZoneInfo est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime.ToUniversalTime(), est);
    }

    private bool IsInLondonSession(DateTime estTime)
    {
        return estTime.Hour >= 3 && estTime.Hour < 9;
    }
}
```

---

## 6. cBot vs Indicator Architecture & Sharing Logic

### Two-File Architecture Pattern (Recommended)

**File 1: LondonSweepIndicator.cs** (Visualization only)
```csharp
[Indicator(IsOverlay = true, TimeZone = TimeZoneInfo.Utc, AccessRights = AccessRights.None)]
public class LondonSweepIndicator : Indicator
{
    // ONLY chart drawing + signal visualization
    // NO order execution

    private double londonHigh, londonLow;

    protected override void OnBar()
    {
        // 1. Detect London session
        // 2. Track high/low
        // 3. Draw rectangle/lines
        // 4. Emit visual signals (arrows, colors)
    }
}
```

**File 2: LondonSweepBot.cs** (Execution only)
```csharp
[Cbot(TimeZone = TimeZoneInfo.Utc, Label = "London Sweep Bot", AccessRights = AccessRights.None)]
public class LondonSweepBot : Robot
{
    // ONLY trading logic + execution
    // Mirrors strategy rules from strategy doc

    protected override void OnStart()
    {
        // Initialize all bars/indicators
    }

    protected override void OnBar()
    {
        // 1. Check London Range size
        // 2. Verify H4 trend (EMA20 > EMA50)
        // 3. Verify H1 momentum
        // 4. Detect sweep in M15
        // 5. Execute trade if all signals align
        // 6. Manage positions
    }
}
```

### Can a cBot Reference an Indicator?

**YES, but with limitations:**

**Approach 1: Direct Reference (Recommended)**
```csharp
// In cBot
private Indicators.GetIndicator<LondonSweepIndicator>() indicator;

protected override void OnStart()
{
    // Reference the indicator by class name
    indicator = Indicators.GetIndicator<LondonSweepIndicator>();

    // Can access indicator's output series
    // indicator.Result; // if indicator exposes Output series
}
```

**Approach 2: Copy Logic (Avoid if Possible)**
Don't copy-paste logic. Instead:
- Indicator: Visual signals only
- Bot: Business logic (with duplication accepted for now)
- Future: Extract to shared `StrategyLogic.cs` static class

**Approach 3: Shared Static Utility Class (Best Practice)**
```csharp
// File: LondonSweepLogic.cs
public static class LondonSweepLogic
{
    // Shared calculations (no cTrader dependencies)
    public static bool IsValidRange(double high, double low)
    {
        return (high - low) >= 50 * 0.0001;  // 50 pips
    }

    public static bool IsH4Bullish(double ema20, double ema50)
    {
        return ema20 > ema50;
    }
}

// Used in both Indicator and cBot
if (LondonSweepLogic.IsValidRange(londonHigh, londonLow))
{
    // Proceed with trade
}
```

**⚠️ Important Limitations:**
- Indicator runs on chart timeframe (visual updates)
- cBot runs independently (can run without chart open)
- No direct event communication between indicator & bot
- cBot should NOT rely on indicator being loaded
- Indicator cannot execute trades

**Recommended Pattern Summary:**
```
LondonSweepLogic.cs (static utility, shared calculations)
    ↓
    ├─ LondonSweepIndicator.cs (uses LondonSweepLogic, draws visuals)
    └─ LondonSweepBot.cs (uses LondonSweepLogic, executes trades)
```

---

## 7. Timer & Scheduling (Time-Specific Execution)

### Timer API Basics

**Initialize Timer in OnStart():**
```csharp
private Timer tradingTimer;

protected override void OnStart()
{
    // Create timer that fires every 1 second
    tradingTimer = new Timer(1000);  // 1000ms = 1 second
    tradingTimer.TimerTick += OnTimerTick;
    tradingTimer.Start();
}

protected override void OnStop()
{
    if (tradingTimer != null)
    {
        tradingTimer.Stop();
    }
}

private void OnTimerTick(TimerTickEventArgs args)
{
    // This fires every 1 second, regardless of price ticks
}
```

### Time-Specific Execution Pattern

**Execute at 09:00 EST (London Close):**
```csharp
private Timer dailyTimer;
private bool londonCloseProcessed = false;

protected override void OnStart()
{
    // Timer every 10 seconds (check frequently)
    dailyTimer = new Timer(10000);
    dailyTimer.TimerTick += OnTimerCheck;
    dailyTimer.Start();
}

private void OnTimerCheck(TimerTickEventArgs args)
{
    DateTime estTime = ConvertToEST(Server.TimeInUtc);

    // Execute exactly once per day at 09:00 EST
    if (estTime.Hour == 9 && estTime.Minute == 0 && !londonCloseProcessed)
    {
        OnLondonSessionClose();
        londonCloseProcessed = true;
    }

    // Reset flag at midnight EST
    if (estTime.Hour == 0 && estTime.Minute == 0)
    {
        londonCloseProcessed = false;
    }
}

private void OnLondonSessionClose()
{
    Print("London session closed at 09:00 EST");
    // Perform daily analysis
}
```

### Why NOT OnTick() for Time-Specific Logic

❌ **Problem with OnTick():**
```csharp
protected override void OnTick()
{
    // This only fires when price updates
    // If no tick at 09:00 EST, this misses the time!

    if (Server.Time.Hour == 9 && Server.Time.Minute == 0)
    {
        // May NEVER fire if there's no price movement at exactly 09:00
    }
}
```

✅ **Use Timer instead:**
```csharp
// Timer fires independent of price ticks
// Guarantees execution at time interval
```

### Complete Scheduling Example for London Sweep

```csharp
[Cbot(TimeZone = TimeZoneInfo.Utc, Label = "London Sweep Bot")]
public class LondonSweepBot : Robot
{
    private Timer sessionTimer;
    private TimeZoneInfo estTimeZone;
    private int tradeCount = 0;
    private bool tradeExecutedToday = false;

    // Session tracking
    private DateTime lastLondonOpen = DateTime.MinValue;
    private DateTime lastLondonClose = DateTime.MinValue;

    protected override void OnStart()
    {
        estTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // Check time every 5 seconds
        sessionTimer = new Timer(5000);
        sessionTimer.TimerTick += OnSessionCheck;
        sessionTimer.Start();
    }

    private void OnSessionCheck(TimerTickEventArgs args)
    {
        DateTime estTime = ConvertToEST(Server.TimeInUtc);

        // **03:00 EST - London opens**
        if (estTime.Hour == 3 && estTime.Minute == 0 &&
            lastLondonOpen.Date < estTime.Date)
        {
            OnLondonSessionOpen(estTime);
            lastLondonOpen = estTime;
            tradeExecutedToday = false;  // Reset trade counter
        }

        // **09:00 EST - London closes, check for sweep**
        if (estTime.Hour == 9 && estTime.Minute == 0 &&
            lastLondonClose.Date < estTime.Date)
        {
            OnLondonSessionClose(estTime);
            lastLondonClose = estTime;
        }

        // **09:30 EST - Trading window opens**
        if (estTime.Hour == 9 && estTime.Minute == 30)
        {
            // Start monitoring for sweeps in M15
        }

        // **15:00 EST - Force close all positions**
        if (estTime.Hour >= 15 && estTime.Minute == 0)
        {
            OnForcedClose(estTime);
        }
    }

    private void OnLondonSessionOpen(DateTime estTime)
    {
        Print($"[{estTime:HH:mm:ss}] London session opened");
        // Reset daily counters, prepare for trading
    }

    private void OnLondonSessionClose(DateTime estTime)
    {
        Print($"[{estTime:HH:mm:ss}] London session closed");
        // Capture London high/low, verify range >= 50 pips
    }

    private void OnForcedClose(DateTime estTime)
    {
        Print($"[{estTime:HH:mm:ss}] Forced close: end of trading window");

        foreach (var position in Positions.Where(p => p.Label.Contains("LondonSweep")))
        {
            ClosePosition(position);
        }
    }

    private DateTime ConvertToEST(DateTime utcTime)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, estTimeZone);
    }

    protected override void OnStop()
    {
        if (sessionTimer != null)
        {
            sessionTimer.Stop();
        }
    }
}
```

---

## 8. FTMO Compliance & Risk Management

### Official FTMO Requirements (2026)

**Key Rules:**
- **Daily Loss Limit:** Max 5% of account balance per day
- **Maximum Drawdown:** Max 10% from account peak
- **Leverage Cap:** 1:100 maximum
- **Risk Per Trade:** Standard recommendation is 0.5–2% per trade
- **Day Trader Risk:** 0.5–1% per trade typical

**⚠️ Note on "1 Trade/Day":**
Official FTMO rules do NOT mandate max 1 trade per day. This may be:
- Self-imposed discipline (London Sweep strategy design)
- Custom rule from different prop firm
- Trading plan optimization (reduce overtrading)

### Risk Management Implementation

**1% Risk Per Trade Calculation:**
```csharp
private double CalculateVolume(double accountBalance, double stopLossPips)
{
    // Risk = 1% of account
    double riskAmount = accountBalance * 0.01;

    // Stop loss in currency (pips * pip value * volume)
    double pipValue = Symbol.PipValue;
    double volume = riskAmount / (stopLossPips * pipValue);

    return Math.Round(volume, 2);
}

// Example: $100k account, 8 pip SL
// Risk = $1,000
// PipValue for US30 ≈ $0.10
// Volume = $1,000 / (8 * $0.10) = 1,250 (10.0 lots in cTrader)
```

**Daily Loss Tracking:**
```csharp
private DateTime lastTradeDate = DateTime.MinValue;
private double dailyLoss = 0;
private const double MAX_DAILY_LOSS_PERCENT = 0.05;  // 5% FTMO limit
private double maxDailyLoss;

protected override void OnStart()
{
    maxDailyLoss = Account.Balance * MAX_DAILY_LOSS_PERCENT;
}

protected override void OnBar()
{
    DateTime estTime = ConvertToEST(Server.TimeInUtc);

    // Reset daily loss at midnight EST
    if (estTime.Date > lastTradeDate.Date)
    {
        dailyLoss = 0;
        lastTradeDate = estTime;
    }

    // Calculate current daily loss
    dailyLoss = ClosedPositions
        .Where(p => p.Label.Contains("LondonSweep") &&
                    p.ClosingTime.Date == estTime.Date)
        .Sum(p => p.NetProfit);

    if (Math.Abs(dailyLoss) >= maxDailyLoss)
    {
        Print("⚠️ Daily loss limit reached. No more trades today.");
        // Block new trades
        canTrade = false;
    }
}
```

**Drawdown Monitoring:**
```csharp
private double peakBalance;
private const double MAX_DRAWDOWN_PERCENT = 0.10;  // 10% FTMO limit

protected override void OnStart()
{
    peakBalance = Account.Balance;
}

protected override void OnBar()
{
    if (Account.Balance > peakBalance)
    {
        peakBalance = Account.Balance;
    }

    double drawdown = (peakBalance - Account.Balance) / peakBalance;

    if (drawdown >= MAX_DRAWDOWN_PERCENT)
    {
        Print("⚠️ Maximum drawdown reached!");
        // Emergency stop all trading
        StopBot();
    }
}
```

**One-Trade-Per-Day Enforcement (Strategy Design):**
```csharp
private int tradesExecutedToday = 0;
private DateTime lastTradeDate = DateTime.MinValue;
private const int MAX_TRADES_PER_DAY = 1;  // London Sweep design

protected override void OnBar()
{
    DateTime estTime = ConvertToEST(Server.TimeInUtc);

    // Reset counter at midnight EST
    if (estTime.Date > lastTradeDate.Date)
    {
        tradesExecutedToday = 0;
        lastTradeDate = estTime;
    }

    // Only trade if haven't executed today's limit
    if (tradesExecutedToday < MAX_TRADES_PER_DAY && IsSweepConfirmed())
    {
        ExecuteMarketOrder(...);
        tradesExecutedToday++;
    }
    else if (tradesExecutedToday >= MAX_TRADES_PER_DAY)
    {
        Print("Today's trade limit reached. Waiting for next session.");
    }
}
```

### Position Sizing Best Practice for London Sweep

```csharp
private double CalculateLondonSweepVolume()
{
    // London Sweep: Risk 1% per trade
    double accountRisk = Account.Balance * 0.01;

    // SL = 8 pips (from strategy)
    double stopLossPips = 8;

    // US30: ~0.1 pip value
    double pipValue = Symbol.PipValue;

    // Volume = Risk / (SL distance * pip value)
    double rawVolume = accountRisk / (stopLossPips * pipValue);

    // Round to nearest 0.01 lot (minimum unit)
    double finalVolume = Math.Round(rawVolume, 2);

    // Safety: never exceed max position size
    double maxPositionSize = Account.Balance * 0.05;  // Max 5% per position
    double maxVolume = maxPositionSize / Symbol.PipValue;

    return Math.Min(finalVolume, maxVolume);
}
```

**Complete FTMO-Compliant Bot Fragment:**
```csharp
[Cbot(Label = "London Sweep FTMO-Compliant")]
public class LondonSweepBotFtmo : Robot
{
    // FTMO Constants
    private const double DAILY_LOSS_LIMIT = 0.05;      // 5%
    private const double DRAWDOWN_LIMIT = 0.10;        // 10%
    private const double RISK_PER_TRADE = 0.01;        // 1%
    private const int MAX_TRADES_PER_DAY = 1;

    // Tracking
    private double peakBalance;
    private double dailyLoss = 0;
    private int tradesExecutedToday = 0;
    private DateTime lastCheckDate;

    protected override void OnStart()
    {
        peakBalance = Account.Balance;
        lastCheckDate = Server.Time.Date;
    }

    protected override void OnBar()
    {
        // Reset daily counters
        if (Server.Time.Date > lastCheckDate)
        {
            lastCheckDate = Server.Time.Date;
            tradesExecutedToday = 0;
            dailyLoss = 0;
        }

        // Check drawdown limit
        double currentDrawdown = (peakBalance - Account.Balance) / peakBalance;
        if (currentDrawdown >= DRAWDOWN_LIMIT)
        {
            Print("❌ Drawdown limit breached. Stopping bot.");
            Stop();
            return;
        }

        // Update peak balance if new high
        if (Account.Balance > peakBalance)
            peakBalance = Account.Balance;

        // Check daily loss limit
        dailyLoss = ClosedPositions
            .Where(p => p.ClosingTime.Date == Server.Time.Date)
            .Sum(p => p.NetProfit);

        double maxDailyLoss = Account.Balance * DAILY_LOSS_LIMIT;
        if (Math.Abs(dailyLoss) >= maxDailyLoss)
        {
            Print($"❌ Daily loss limit reached. Loss: {dailyLoss}");
            return;
        }

        // Check daily trade count
        if (tradesExecutedToday >= MAX_TRADES_PER_DAY)
        {
            Print("⏸️ Max 1 trade per day limit reached");
            return;
        }

        // If all checks pass, proceed with trading logic
        if (IsSweepSignal())
        {
            ExecuteTrade();
            tradesExecutedToday++;
        }
    }

    private void ExecuteTrade()
    {
        // Calculate volume based on 1% risk rule
        double volume = CalculateVolume(8);  // 8 pip SL
        ExecuteMarketOrder(TradeType.Buy, Symbol, volume, "LondonSweep", 8, 50);
    }

    private double CalculateVolume(double slPips)
    {
        double riskAmount = Account.Balance * RISK_PER_TRADE;
        double pipValue = Symbol.PipValue;
        return Math.Round(riskAmount / (slPips * pipValue), 2);
    }

    private bool IsSweepSignal()
    {
        // Strategy logic here
        return false;
    }
}
```

---

## 9. Code Examples Summary

### London Sweep Indicator (Visual Only)
- Multi-timeframe bar access (H4, H1, M15)
- EMA20/EMA50 calculation on H4
- Chart drawing: Rectangle for range, horizontal lines for H/L
- No order execution

### London Sweep cBot (Execution Only)
- Initialize all bars in OnStart()
- Use Timer for time-specific checks (09:00 EST, 15:00 EST)
- Verify: range >= 50 pips, H4 trend, H1 momentum, M15 sweep
- Execute with SL=8 pips, TP=0.65× range
- Force close at 15:00 EST
- FTMO compliance: 1 trade/day, 1% risk, 5% daily loss, 10% drawdown

### Key Takeaways
1. **Multi-timeframe:** Use `MarketData.GetBars(TimeFrame.X)` for each timeframe
2. **EMA:** `Indicators.ExponentialMovingAverage(dataSource, period)` returns `IndicatorResult`
3. **Time:** Convert `Server.Time` to EST using `TimeZoneInfo.ConvertTimeFromUtc()`
4. **Orders:** `ExecuteMarketOrder()` with SL/TP in pips
5. **Drawing:** `Chart.DrawHorizontalLine()` + `Chart.DrawRectangle()` for visualization
6. **Architecture:** Indicator (visual) + cBot (execution), share logic via static utility class
7. **Scheduling:** Use `Timer` with `TimerTick` event, NOT `OnTick()` for time-specific logic
8. **FTMO:** Track peak balance, daily loss, position count; enforce 1%/trade, 5% daily loss, 10% drawdown

---

## Unresolved Questions

1. **FxPro Specific:** Does FxPro cTrader have custom API extensions or limitations beyond Spotware standard?
2. **PipValue for US30:** Exact `Symbol.PipValue` numeric on FxPro (affects volume calculation)
3. **Indicator → cBot Communication:** Should indicator expose output series for cBot to reference, or duplicate logic?
4. **Bar Loading:** How many bars should London Sweep Indicator load for proper H4/H1/M15 alignment?
5. **Non-Repainting:** What's the safest pattern to avoid repainting in multi-timeframe scenarios?

---

## Sources

- [cTrader Automate API - Multi-Timeframe Strategies](https://help.ctrader.com/ctrader-algo/how-tos/cbots/code-multitimeframe-strategies/)
- [cTrader MarketData Reference](https://help.ctrader.com/ctrader-algo/references/MarketData/MarketData/)
- [cTrader Chart Objects & Drawings](https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/)
- [cTrader Timer API](https://help.ctrader.com/ctrader-algo/references/Timer/Timer/)
- [cTrader cBot Lifecycle](https://help.ctrader.com/ctrader-algo/how-tos/cbots/cbot-lifecycle/)
- [Using Custom Indicators in cBots](https://help.ctrader.com/ctrader-algo/how-tos/indicators/use-custom-indicators-in-cbots/)
- [TimeZones - cTrader Algo](https://help.ctrader.com/ctrader-algo/references/Utility/TimeZones/)
- [FTMO Trading Objectives](https://ftmo.com/en/trading-objectives/)
- [cTrader Algo Code Samples](https://github.com/spotware/ctrader-algo-samples)
