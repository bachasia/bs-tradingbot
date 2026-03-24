// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Bot | US30 | cTrader Automate (C#)
// Reads signals from LondonSweepIndicator, places pending limit orders,
// manages trades with EOD cutoff and FTMO risk guards.
// Target: FTMO cTrader, symbol US30, M15 chart.
//
// SETUP:
// 1. Open cTrader Desktop → Automate tab
// 2. Create new cBot → paste this code
// 3. Click "Manage References" → check "LondonSweepIndicator"
// 4. Build (Ctrl+B), attach to US30 M15 chart, set parameters, start.
// ══════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot("London Sweep Bot", TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepBot : Robot
    {
        // ─── INPUTS: Passed through to indicator (must match declaration order) ──
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

        [Parameter("Min London Range (pts)", Group = "Trade Parameters", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade Parameters", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade Parameters", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade Parameters", DefaultValue = 0.65)]
        public double TpMultiplier { get; set; }

        [Parameter("H4 EMA Fast Period", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow Period", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── INPUTS: Bot-Only ────────────────────────────────────────────────
        [Parameter("Auto-Trade (place orders)", Group = "Bot Settings", DefaultValue = false)]
        public bool AutoTrade { get; set; }

        [Parameter("Volume (units)", Group = "Bot Settings", DefaultValue = 1.0)]
        public double TradeVolume { get; set; }

        [Parameter("Max Trades Per Day", Group = "Bot Settings", DefaultValue = 1)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Enable Sound Alerts", Group = "Bot Settings", DefaultValue = true)]
        public bool EnableSound { get; set; }

        // ─── INPUTS: FTMO Risk Guards ────────────────────────────────────────
        [Parameter("Enable FTMO Guards", Group = "FTMO Risk", DefaultValue = true)]
        public bool EnableFtmoGuards { get; set; }

        [Parameter("FTMO Daily Loss Limit ($)", Group = "FTMO Risk", DefaultValue = 5000)]
        public double FtmoDailyLossLimit { get; set; }

        [Parameter("FTMO Max Drawdown ($)", Group = "FTMO Risk", DefaultValue = 10000)]
        public double FtmoMaxDrawdown { get; set; }

        // ─── PRIVATE FIELDS ──────────────────────────────────────────────────
        private LondonSweepIndicator _indicator;
        private int _tradesToday;
        private DateTime _lastDate = DateTime.MinValue;
        private bool _eodDone;
        private double _dayStartBalance;
        private double _highWaterBalance;  // FTMO drawdown measured from peak balance
        private const string _botLabel = "LondonSweep";

        // ─── ON START ────────────────────────────────────────────────────────
        protected override void OnStart()
        {
            // Initialize indicator — params passed in EXACT declaration order
            _indicator = Indicators.GetIndicator<LondonSweepIndicator>(
                LondonStartHHMM, LondonEndHHMM, SweepStartHHMM, SweepEndHHMM, EodCutoffHHMM,
                MinRange, SweepThreshold, SlBuffer, TpMultiplier,
                H4EmaFastPeriod, H4EmaSlowPeriod, UseH4Filter, UseH1Filter,
                ShowDashboard, ShowTradeLines);

            Print("[{0}] Bot started on {1}", _botLabel, Symbol.Name);
            Print("[{0}]   PipSize={1}  TickSize={2}  MinVolume={3}",
                _botLabel, Symbol.PipSize, Symbol.TickSize, Symbol.VolumeInUnitsMin);
            Print("[{0}]   AutoTrade={1}  FTMO Guards={2}",
                _botLabel, AutoTrade, EnableFtmoGuards);

            _dayStartBalance = Account.Balance;
            _highWaterBalance = Account.Balance;

            // Subscribe to trade events for logging
            Positions.Closed += OnPositionClosed;
            PendingOrders.Filled += OnPendingOrderFilled;
        }

        // ─── ON BAR CLOSED ───────────────────────────────────────────────────
        protected override void OnBarClosed()
        {
            // ─── Day change reset ────────────────────────────────────────
            DateTime barDate = Bars.OpenTimes.Last(1).Date;
            if (barDate != _lastDate)
            {
                _lastDate = barDate;
                _tradesToday = 0;
                _eodDone = false;
                _dayStartBalance = Account.Balance;
                _highWaterBalance = Math.Max(_highWaterBalance, Account.Balance);
                CancelBotPendingOrders("Day reset — stale orders");
            }

            // ─── EOD cutoff ──────────────────────────────────────────────
            int hhmm = Bars.OpenTimes.Last(1).Hour * 100 + Bars.OpenTimes.Last(1).Minute;
            if (hhmm >= EodCutoffHHMM && !_eodDone)
            {
                _eodDone = true;
                CancelBotPendingOrders("EOD cutoff");
                CloseBotPositions("EOD cutoff");
            }

            // ─── Read indicator signals ──────────────────────────────────
            double longVal  = _indicator.LongSignal.Last(1);
            double shortVal = _indicator.ShortSignal.Last(1);
            bool isLong  = !double.IsNaN(longVal) && longVal > 0.5;
            bool isShort = !double.IsNaN(shortVal) && shortVal > 0.5;

            if (!isLong && !isShort) return;

            double entryPrice = _indicator.EntryPriceOutput.Last(1);
            double slPrice    = _indicator.SlPriceOutput.Last(1);
            double tpPrice    = _indicator.TpPriceOutput.Last(1);

            if (double.IsNaN(entryPrice) || double.IsNaN(slPrice) || double.IsNaN(tpPrice))
            {
                Print("[{0}] Signal detected but prices are NaN — skipping.", _botLabel);
                return;
            }

            TradeType tradeType = isLong ? TradeType.Buy : TradeType.Sell;
            string dir = isLong ? "LONG" : "SHORT";
            Print("[{0}] === SIGNAL: {1} === Entry={2:F1} SL={3:F1} TP={4:F1}",
                _botLabel, dir, entryPrice, slPrice, tpPrice);

            // ─── Max trades guard ────────────────────────────────────────
            if (_tradesToday >= MaxTradesPerDay)
            {
                Print("[{0}] SKIPPED: Max trades reached ({1})", _botLabel, MaxTradesPerDay);
                PlaySound(SoundType.Negative);
                return;
            }

            // ─── FTMO risk guards ────────────────────────────────────────
            if (EnableFtmoGuards)
            {
                double dailyPnL = Account.Balance - _dayStartBalance;
                if (dailyPnL <= -FtmoDailyLossLimit * 0.8)
                {
                    Print("[{0}] FTMO GUARD: Daily loss near limit ({1:F2}). No trade.", _botLabel, dailyPnL);
                    PlaySound(SoundType.Negative);
                    return;
                }

                // FTMO measures drawdown from highest balance, not current balance
                _highWaterBalance = Math.Max(_highWaterBalance, Account.Balance);
                double drawdown = _highWaterBalance - Account.Equity;
                if (drawdown >= FtmoMaxDrawdown * 0.8)
                {
                    Print("[{0}] FTMO GUARD: Drawdown near limit ({1:F2}). No trade.", _botLabel, drawdown);
                    PlaySound(SoundType.Negative);
                    return;
                }
            }

            PlaySound(SoundType.Positive);

            // ─── Alert-only mode ─────────────────────────────────────────
            if (!AutoTrade)
            {
                Print("[{0}] AutoTrade OFF — signal logged only.", _botLabel);
                return;
            }

            // ─── Place pending limit order ───────────────────────────────
            double slPips = Math.Abs(entryPrice - slPrice) / Symbol.PipSize;
            double tpPips = Math.Abs(tpPrice - entryPrice) / Symbol.PipSize;
            double volume = Symbol.NormalizeVolumeInUnits(TradeVolume, RoundingMode.ToNearest);

            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("[{0}] Volume {1} below min {2}", _botLabel, volume, Symbol.VolumeInUnitsMin);
                return;
            }

            try
            {
                string label = _botLabel + "_" + dir;
                var result = PlaceLimitOrder(tradeType, SymbolName, volume,
                    entryPrice, label, slPips, tpPips);

                if (result.IsSuccessful)
                {
                    Print("[{0}] ORDER PLACED: {1} Limit@{2:F1} Vol={3} SL={4:F1}pips TP={5:F1}pips",
                        _botLabel, dir, entryPrice, volume, slPips, tpPips);
                    _tradesToday++;
                }
                else
                {
                    Print("[{0}] ORDER FAILED: {1}", _botLabel, result.Error);
                }
            }
            catch (Exception ex)
            {
                Print("[{0}] EXCEPTION: {1}", _botLabel, ex.Message);
            }
        }

        // ─── ON STOP ─────────────────────────────────────────────────────────
        protected override void OnStop()
        {
            Print("[{0}] Bot stopping — cleaning up...", _botLabel);
            CancelBotPendingOrders("Bot stopped");
            CloseBotPositions("Bot stopped");
            Positions.Closed -= OnPositionClosed;
            PendingOrders.Filled -= OnPendingOrderFilled;
            Print("[{0}] Bot stopped.", _botLabel);
        }

        // ─── HELPERS ─────────────────────────────────────────────────────────
        private void CancelBotPendingOrders(string reason)
        {
            var orders = PendingOrders
                .Where(o => o.Label != null && o.Label.StartsWith(_botLabel))
                .ToList();
            foreach (var order in orders)
            {
                try
                {
                    CancelPendingOrder(order);
                    Print("[{0}] Cancelled: {1} {2}@{3:F1} — {4}",
                        _botLabel, order.TradeType, order.SymbolName, order.TargetPrice, reason);
                }
                catch (Exception ex)
                {
                    Print("[{0}] Cancel error: {1}", _botLabel, ex.Message);
                }
            }
        }

        private void CloseBotPositions(string reason)
        {
            var positions = Positions
                .Where(p => p.Label != null && p.Label.StartsWith(_botLabel))
                .ToList();
            foreach (var pos in positions)
            {
                try
                {
                    ClosePosition(pos);
                    Print("[{0}] Closed: {1} P&L={2:F2} — {3}",
                        _botLabel, pos.TradeType, pos.NetProfit, reason);
                    PlaySound(SoundType.Negative);
                }
                catch (Exception ex)
                {
                    Print("[{0}] Close error: {1}", _botLabel, ex.Message);
                }
            }
        }

        private void PlaySound(SoundType sound)
        {
            if (EnableSound && !IsBacktesting)
                Notifications.PlaySound(sound);
        }

        // ─── EVENT HANDLERS ──────────────────────────────────────────────────
        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            if (pos.Label == null || !pos.Label.StartsWith(_botLabel)) return;

            Print("[{0}] TRADE CLOSED: {1} Entry={2:F1} Reason={3} P&L={4:F2} Pips={5:F1}",
                _botLabel, pos.TradeType, pos.EntryPrice, args.Reason, pos.NetProfit, pos.Pips);

            PlaySound(args.Reason == PositionCloseReason.TakeProfit ? SoundType.Positive : SoundType.Negative);
        }

        private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
        {
            var order = args.PendingOrder;
            if (order.Label == null || !order.Label.StartsWith(_botLabel)) return;

            Print("[{0}] ORDER FILLED: {1}@{2:F1} Vol={3}",
                _botLabel, order.TradeType, order.TargetPrice, order.VolumeInUnits);
            PlaySound(SoundType.Positive);
        }
    }
}
