// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Bot v2 | US30 | cTrader Automate (C#)
//
// Reads signals from LondonSweepIndicator, places market orders with
// 1% risk sizing, manages trades with EOD cutoff and FTMO risk guards.
//
// SETUP:
// 1. cTrader Desktop → Automate → New cBot → paste this code
// 2. Click "Manage References" → check "LondonSweepIndicator"
// 3. Build (Ctrl+B), attach to US30 M15 chart, set parameters, start.
// ══════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepBot : Robot
    {
        // ─── INPUTS: Passed through to indicator ─────────────────────────
        [Parameter("London Start (HHMM)", Group = "Session", DefaultValue = 300)]
        public int LondonStartHHMM { get; set; }

        [Parameter("London End (HHMM)", Group = "Session", DefaultValue = 900)]
        public int LondonEndHHMM { get; set; }

        [Parameter("Pre-Sweep End (HHMM)", Group = "Session", DefaultValue = 930)]
        public int PreSweepEndHHMM { get; set; }

        [Parameter("Sweep Window Start (HHMM)", Group = "Session", DefaultValue = 930)]
        public int SweepStartHHMM { get; set; }

        [Parameter("Sweep Window End (HHMM)", Group = "Session", DefaultValue = 1100)]
        public int SweepEndHHMM { get; set; }

        [Parameter("EOD Cutoff (HHMM)", Group = "Session", DefaultValue = 1500)]
        public int EodCutoffHHMM { get; set; }

        [Parameter("Min London Range (pts)", Group = "Trade", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade", DefaultValue = 0.65)]
        public double TpMultiplier { get; set; }

        [Parameter("H4 EMA Fast", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── INPUTS: Bot-Only ────────────────────────────────────────────
        [Parameter("Auto-Trade", Group = "Bot", DefaultValue = false)]
        public bool AutoTrade { get; set; }

        [Parameter("Risk Per Trade (%)", Group = "Bot", DefaultValue = 1.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Max Trades Per Day", Group = "Bot", DefaultValue = 1)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Enable Sound Alerts", Group = "Bot", DefaultValue = true)]
        public bool EnableSound { get; set; }

        // ─── INPUTS: FTMO Risk Guards ────────────────────────────────────
        [Parameter("Enable FTMO Guards", Group = "FTMO", DefaultValue = true)]
        public bool EnableFtmoGuards { get; set; }

        [Parameter("Daily Loss Limit ($)", Group = "FTMO", DefaultValue = 5000)]
        public double FtmoDailyLossLimit { get; set; }

        [Parameter("Max Drawdown ($)", Group = "FTMO", DefaultValue = 10000)]
        public double FtmoMaxDrawdown { get; set; }

        // ─── PRIVATE FIELDS ──────────────────────────────────────────────
        private LondonSweepIndicator _indicator;
        private int _tradesToday;
        private DateTime _lastDate;
        private bool _eodDone;
        private double _dayStartBalance;
        private double _highWaterBalance;
        private const string BotLabel = "LondonSweepV2";

        // ─── ON START ────────────────────────────────────────────────────
        protected override void OnStart()
        {
            // Initialize indicator with all params in declaration order
            _indicator = Indicators.GetIndicator<LondonSweepIndicator>(
                LondonStartHHMM, LondonEndHHMM, PreSweepEndHHMM,
                SweepStartHHMM, SweepEndHHMM, EodCutoffHHMM,
                MinRange, SweepThreshold, SlBuffer, TpMultiplier,
                H4EmaFastPeriod, H4EmaSlowPeriod, UseH4Filter, UseH1Filter,
                ShowDashboard, ShowTradeLines);

            _lastDate = DateTime.MinValue;
            _dayStartBalance = Account.Balance;
            _highWaterBalance = Account.Balance;

            Positions.Closed += OnPositionClosed;

            Print("[{0}] Started on {1} | AutoTrade={2} Risk={3}% FTMO={4}",
                BotLabel, Symbol.Name, AutoTrade, RiskPercent, EnableFtmoGuards);
            Print("[{0}] PipSize={1} TickSize={2} TickValue={3} MinVol={4}",
                BotLabel, Symbol.PipSize, Symbol.TickSize, Symbol.TickValue, Symbol.VolumeInUnitsMin);
        }

        // ─── ON BAR CLOSED ───────────────────────────────────────────────
        protected override void OnBarClosed()
        {
            DateTime barDate = Bars.OpenTimes.Last(1).Date;

            // ─── Day change reset ────────────────────────────────────
            if (barDate != _lastDate)
            {
                _lastDate = barDate;
                _tradesToday = 0;
                _eodDone = false;
                _dayStartBalance = Account.Balance;
                _highWaterBalance = Math.Max(_highWaterBalance, Account.Balance);
                CancelBotOrders("Day reset");
            }

            // ─── EOD cutoff ──────────────────────────────────────────
            int hhmm = Bars.OpenTimes.Last(1).Hour * 100 + Bars.OpenTimes.Last(1).Minute;
            if (hhmm >= EodCutoffHHMM && !_eodDone)
            {
                _eodDone = true;
                CancelBotOrders("EOD cutoff");
                CloseBotPositions("EOD cutoff");
            }

            // ─── Read indicator signals (explicit index for reliability) ──
            int sigIdx = Bars.Count - 2;  // just-closed bar index
            if (sigIdx < 0) return;

            double longVal  = _indicator.LongSignal[sigIdx];
            double shortVal = _indicator.ShortSignal[sigIdx];
            bool isLong  = !double.IsNaN(longVal) && longVal > 0.5;
            bool isShort = !double.IsNaN(shortVal) && shortVal > 0.5;

            if (!isLong && !isShort) return;

            double entryPrice = _indicator.EntryPriceOut[sigIdx];
            double slPrice    = _indicator.SlPriceOut[sigIdx];
            double tpPrice    = _indicator.TpPriceOut[sigIdx];

            if (double.IsNaN(entryPrice) || double.IsNaN(slPrice) || double.IsNaN(tpPrice))
            {
                Print("[{0}] Signal but prices NaN — skipping.", BotLabel);
                return;
            }

            TradeType tradeType = isLong ? TradeType.Buy : TradeType.Sell;
            string dir = isLong ? "LONG" : "SHORT";
            Print("[{0}] === SIGNAL: {1} === Entry={2:F1} SL={3:F1} TP={4:F1}",
                BotLabel, dir, entryPrice, slPrice, tpPrice);

            // ─── Guards ──────────────────────────────────────────────
            if (!PassesGuards()) return;

            PlayAlert();

            if (!AutoTrade)
            {
                Print("[{0}] AutoTrade OFF — signal logged only.", BotLabel);
                return;
            }

            // ─── Calculate volume (1% risk) ──────────────────────────
            // Use TickValue/TickSize for accurate index sizing (PipSize unreliable on indices)
            double slDistance = Math.Abs(entryPrice - slPrice);
            if (slDistance <= 0)
            {
                Print("[{0}] SL distance is 0 — skipping.", BotLabel);
                return;
            }

            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double costPerUnit = slDistance * (Symbol.TickValue / Symbol.TickSize);
            double volume = riskAmount / costPerUnit;
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            Print("[{0}] VOLUME CALC: Balance={1:F2} Risk={2:F2} SLDist={3:F1} CostPerUnit={4:F4} Volume={5}",
                BotLabel, Account.Balance, riskAmount, slDistance, costPerUnit, volume);

            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("[{0}] Volume {1} below min {2} — skipping.", BotLabel, volume, Symbol.VolumeInUnitsMin);
                return;
            }

            // ─── Place market order ──────────────────────────────────
            double slPips = slDistance / Symbol.PipSize;
            double tpPips = Math.Abs(tpPrice - entryPrice) / Symbol.PipSize;

            try
            {
                string label = BotLabel + "_" + dir;
                var result = ExecuteMarketOrder(tradeType, SymbolName, volume,
                    label, slPips, tpPips);

                if (result.IsSuccessful)
                {
                    Print("[{0}] ORDER FILLED: {1} Vol={2} SL={3:F1}pips TP={4:F1}pips",
                        BotLabel, dir, volume, slPips, tpPips);
                    _tradesToday++;
                    PlayAlert();

                    // Sync indicator visuals with actual fill prices.
                    // Fill price (next bar open) can differ from planned entry (signal bar close),
                    // so SL/TP levels set by cTrader are at different absolute prices than
                    // the indicator's visual lines. UpdateActualFill corrects the chart display
                    // and ManageTrade's SL/TP detection to use the real order levels.
                    var pos = result.Position;
                    if (pos != null && pos.StopLoss.HasValue && pos.TakeProfit.HasValue)
                    {
                        int sigBarIdx = Bars.Count - 2;
                        _indicator.UpdateActualFill(pos.EntryPrice, pos.StopLoss.Value, pos.TakeProfit.Value, sigBarIdx);
                        Print("[{0}] FILL SYNC: Entry={1:F1} SL={2:F1} TP={3:F1}",
                            BotLabel, pos.EntryPrice, pos.StopLoss.Value, pos.TakeProfit.Value);
                    }
                }
                else
                {
                    Print("[{0}] ORDER FAILED: {1}", BotLabel, result.Error);
                    PlayAlert();
                }
            }
            catch (Exception ex)
            {
                Print("[{0}] EXCEPTION: {1}", BotLabel, ex.Message);
            }
        }

        // ─── GUARDS ─────────────────────────────────────────────────────
        private bool PassesGuards()
        {
            // Max trades per day
            if (_tradesToday >= MaxTradesPerDay)
            {
                Print("[{0}] SKIP: Max trades reached ({1})", BotLabel, MaxTradesPerDay);
                PlayAlert();
                return false;
            }

            if (!EnableFtmoGuards) return true;

            // Daily loss limit — use Equity to include unrealized P&L (FTMO measures equity, not balance)
            double dailyPnL = Account.Equity - _dayStartBalance;
            if (dailyPnL <= -FtmoDailyLossLimit * 0.8)
            {
                Print("[{0}] FTMO: Daily loss near limit ({1:F2}$). Blocked.", BotLabel, dailyPnL);
                PlayAlert();
                return false;
            }

            // Max drawdown from peak balance (trigger at 80% threshold)
            _highWaterBalance = Math.Max(_highWaterBalance, Account.Balance);
            double drawdown = _highWaterBalance - Account.Equity;
            if (drawdown >= FtmoMaxDrawdown * 0.8)
            {
                Print("[{0}] FTMO: Drawdown near limit ({1:F2}$). Blocked.", BotLabel, drawdown);
                PlayAlert();
                return false;
            }

            return true;
        }

        // ─── ON STOP ─────────────────────────────────────────────────────
        protected override void OnStop()
        {
            CancelBotOrders("Bot stopped");
            CloseBotPositions("Bot stopped");
            Positions.Closed -= OnPositionClosed;
            Print("[{0}] Bot stopped.", BotLabel);
        }

        // ─── HELPERS ─────────────────────────────────────────────────────
        private void CancelBotOrders(string reason)
        {
            var orders = PendingOrders
                .Where(o => o.Label != null && o.Label.StartsWith(BotLabel))
                .ToList();
            foreach (var order in orders)
            {
                try
                {
                    CancelPendingOrder(order);
                    Print("[{0}] Cancelled order: {1} — {2}", BotLabel, order.TradeType, reason);
                }
                catch (Exception ex)
                {
                    Print("[{0}] Cancel error: {1}", BotLabel, ex.Message);
                }
            }
        }

        private void CloseBotPositions(string reason)
        {
            var positions = Positions
                .Where(p => p.Label != null && p.Label.StartsWith(BotLabel))
                .ToList();
            foreach (var pos in positions)
            {
                try
                {
                    ClosePosition(pos);
                    Print("[{0}] Closed {1} P&L={2:F2}$ — {3}", BotLabel, pos.TradeType, pos.NetProfit, reason);
                }
                catch (Exception ex)
                {
                    Print("[{0}] Close error: {1}", BotLabel, ex.Message);
                }
            }
        }

        private void PlayAlert()
        {
            if (EnableSound && !IsBacktesting)
                Notifications.PlaySound("C:\\Windows\\Media\\notify.wav");
        }

        // ─── EVENT HANDLERS ──────────────────────────────────────────────
        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            if (pos.Label == null || !pos.Label.StartsWith(BotLabel)) return;

            Print("[{0}] CLOSED: {1} Entry={2:F1} P&L={3:F2}$ Pips={4:F1} Reason={5}",
                BotLabel, pos.TradeType, pos.EntryPrice, pos.NetProfit, pos.Pips, args.Reason);

            PlayAlert();
        }
    }
}
