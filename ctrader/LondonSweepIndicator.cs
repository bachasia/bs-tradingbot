// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Indicator | US30 | cTrader Automate (C#)
// Detects liquidity sweeps of London session range during NY open
// with H4 trend + H1 momentum filters.
// Target: FTMO cTrader, symbol US30, M15 chart.
// ══════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    // State machine tracking daily strategy cycle.
    public enum SessionState
    {
        Idle, London, RangeDone, SweepWindow, InTrade, Done
    }

    [Indicator("London Sweep | US30", IsOverlay = true,
        TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepIndicator : Indicator
    {
        // ─── INPUTS: Session Times ───────────────────────────────────────────
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

        // ─── INPUTS: Trade Parameters ────────────────────────────────────────
        [Parameter("Min London Range (pts)", Group = "Trade Parameters", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade Parameters", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade Parameters", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade Parameters", DefaultValue = 0.65, Step = 0.05)]
        public double TpMultiplier { get; set; }

        // ─── INPUTS: Filters ─────────────────────────────────────────────────
        [Parameter("H4 EMA Fast Period", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow Period", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        // ─── INPUTS: Visual ──────────────────────────────────────────────────
        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── OUTPUT PROPERTIES (for cBot to read) ────────────────────────────
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

        // ─── PRIVATE STATE ───────────────────────────────────────────────────
        private SessionState _state = SessionState.Idle;
        private double _londonHigh = double.NaN;
        private double _londonLow = double.NaN;
        private double _rangeSize = double.NaN;
        private bool _tradedToday;
        private double _h1Close0300 = double.NaN;
        private double _h1Close0900 = double.NaN;
        private int _tradeDir;
        private double _entryPrice = double.NaN;
        private double _slPrice = double.NaN;
        private double _tpPrice = double.NaN;
        private string _tradeResult = "";
        private DateTime _lastDate = DateTime.MinValue;
        private int _lastIndex = -1;
        private int _dayCount;
        private int _londonBoxStartIndex;

        // MTF references
        private Bars _h4Bars;
        private Bars _h1Bars;
        private ExponentialMovingAverage _h4EmaFast;
        private ExponentialMovingAverage _h4EmaSlow;

        // Filter results (recalculated each bar)
        private bool _h4Bullish;
        private bool _h4Bearish;
        private bool _longAllowed;
        private bool _shortAllowed;

        // ─── HELPERS ─────────────────────────────────────────────────────────
        private int GetHHMM(DateTime dt) => dt.Hour * 100 + dt.Minute;

        private bool IsInSession(int hhmm, int startHHMM, int endHHMM)
            => hhmm >= startHHMM && hhmm < endHHMM;

        // ─── INITIALIZE ─────────────────────────────────────────────────────
        protected override void Initialize()
        {
            _h4Bars = MarketData.GetBars(TimeFrame.Hour4);
            _h1Bars = MarketData.GetBars(TimeFrame.Hour);
            _h4EmaFast = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaFastPeriod);
            _h4EmaSlow = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaSlowPeriod);
        }

        // ─── CALCULATE ──────────────────────────────────────────────────────
        public override void Calculate(int index)
        {
            // Dashboard updates on every tick for responsive display
            if (ShowDashboard)
                UpdateDashboard();
            else
                Chart.RemoveObject("Dashboard");

            // Bar-close detection: only run strategy logic when a new bar opens
            bool isNewBar = index > _lastIndex;
            _lastIndex = index;
            if (!isNewBar) return;

            int prevIndex = index - 1;
            if (prevIndex < 0) return;

            DateTime barTime = Bars.OpenTimes[prevIndex];
            int hhmm = GetHHMM(barTime);

            // ─── DAILY RESET ─────────────────────────────────────────────
            DateTime barDate = barTime.Date;
            if (barDate != _lastDate)
            {
                _lastDate = barDate;
                _dayCount++;
                if (barTime.DayOfWeek == DayOfWeek.Saturday || barTime.DayOfWeek == DayOfWeek.Sunday)
                    return;
                _state = SessionState.Idle;
                _londonHigh = double.NaN;
                _londonLow = double.NaN;
                _rangeSize = double.NaN;
                _tradedToday = false;
                _h1Close0300 = double.NaN;
                _h1Close0900 = double.NaN;
                _tradeDir = 0;
                _entryPrice = double.NaN;
                _slPrice = double.NaN;
                _tpPrice = double.NaN;
                _tradeResult = "";
            }

            // Weekend guard
            if (barTime.DayOfWeek == DayOfWeek.Saturday || barTime.DayOfWeek == DayOfWeek.Sunday)
                return;

            // ─── H4 TREND FILTER (non-repainting) ────────────────────────
            double h4FastVal = _h4EmaFast.Result.Last(1);
            double h4SlowVal = _h4EmaSlow.Result.Last(1);
            _h4Bullish = !double.IsNaN(h4FastVal) && !double.IsNaN(h4SlowVal) && h4FastVal > h4SlowVal;
            _h4Bearish = !double.IsNaN(h4FastVal) && !double.IsNaN(h4SlowVal) && h4FastVal < h4SlowVal;

            // ─── LONDON SESSION TRACKING ──────────────────────────────────
            bool inLondon = IsInSession(hhmm, LondonStartHHMM, LondonEndHHMM);

            if (_state == SessionState.Idle && inLondon)
            {
                _state = SessionState.London;
                _londonHigh = Bars.HighPrices[prevIndex];
                _londonLow = Bars.LowPrices[prevIndex];
                _londonBoxStartIndex = prevIndex;
                _h1Close0300 = _h1Bars.ClosePrices.Last(1);

                Chart.DrawRectangle("LondonBox_" + _dayCount,
                    prevIndex, _londonHigh, prevIndex, _londonLow,
                    Color.FromArgb(40, 0, 100, 255)).IsFilled = true;
            }

            if (_state == SessionState.London && inLondon)
            {
                _londonHigh = Math.Max(_londonHigh, Bars.HighPrices[prevIndex]);
                _londonLow = Math.Min(_londonLow, Bars.LowPrices[prevIndex]);
                var box = Chart.DrawRectangle("LondonBox_" + _dayCount,
                    _londonBoxStartIndex, _londonHigh, prevIndex, _londonLow,
                    Color.FromArgb(40, 0, 100, 255));
                box.IsFilled = true;
            }

            if (_state == SessionState.London && !inLondon)
            {
                _state = SessionState.RangeDone;
                _rangeSize = _londonHigh - _londonLow;
                _h1Close0900 = _h1Bars.ClosePrices.Last(1);

                var hiLine = Chart.DrawHorizontalLine("LondonHi_" + _dayCount, _londonHigh, Color.Blue);
                hiLine.LineStyle = LineStyle.Dots;
                var loLine = Chart.DrawHorizontalLine("LondonLo_" + _dayCount, _londonLow, Color.Blue);
                loLine.LineStyle = LineStyle.Dots;
            }

            // ─── H1 MOMENTUM + COMBINED FILTER ───────────────────────────
            bool momPos = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300) && _h1Close0900 > _h1Close0300;
            bool momNeg = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300) && _h1Close0900 < _h1Close0300;

            _longAllowed  = (UseH4Filter ? _h4Bullish : true) && (UseH1Filter ? momPos : true);
            _shortAllowed = (UseH4Filter ? _h4Bearish : true) && (UseH1Filter ? momNeg : true);

            // ─── SWEEP WINDOW ────────────────────────────────────────────
            bool inSweepWindow = IsInSession(hhmm, SweepStartHHMM, SweepEndHHMM);
            bool rangeValid = !double.IsNaN(_rangeSize) && _rangeSize >= MinRange;

            if (_state == SessionState.RangeDone && inSweepWindow && rangeValid)
                _state = SessionState.SweepWindow;

            if (_state == SessionState.SweepWindow && !inSweepWindow)
                _state = SessionState.Done;

            // ─── SWEEP DETECTION ─────────────────────────────────────────
            bool longSweep = false;
            bool shortSweep = false;

            if (_state == SessionState.SweepWindow && !_tradedToday)
            {
                double prevHigh  = Bars.HighPrices[prevIndex];
                double prevLow   = Bars.LowPrices[prevIndex];
                double prevClose = Bars.ClosePrices[prevIndex];

                // Long: wick below London low, close back above
                if (_longAllowed)
                {
                    double sweepDepth = _londonLow - prevLow;
                    if (sweepDepth >= SweepThreshold && prevClose > _londonLow)
                    {
                        longSweep = true;
                        _tradeDir = 1;
                        _state = SessionState.InTrade;
                        _tradedToday = true;
                        _entryPrice = prevClose;
                        _slPrice = prevLow - SlBuffer;
                        _tpPrice = prevClose + (TpMultiplier * _rangeSize);
                    }
                }

                // Short: wick above London high, close back below (only if no long)
                if (_shortAllowed && !longSweep)
                {
                    double sweepHeight = prevHigh - _londonHigh;
                    if (sweepHeight >= SweepThreshold && prevClose < _londonHigh)
                    {
                        shortSweep = true;
                        _tradeDir = -1;
                        _state = SessionState.InTrade;
                        _tradedToday = true;
                        _entryPrice = prevClose;
                        _slPrice = prevHigh + SlBuffer;
                        _tpPrice = prevClose - (TpMultiplier * _rangeSize);
                    }
                }
            }

            // ─── TRADE LEVEL VISUALS ─────────────────────────────────────
            if ((longSweep || shortSweep) && ShowTradeLines)
            {
                string sfx = "_" + _dayCount;
                var eLine = Chart.DrawTrendLine("Entry" + sfx, prevIndex, _entryPrice, prevIndex + 50, _entryPrice, Color.DodgerBlue);
                eLine.LineStyle = LineStyle.Lines; eLine.Thickness = 2;
                var sLine = Chart.DrawTrendLine("SL" + sfx, prevIndex, _slPrice, prevIndex + 50, _slPrice, Color.Red);
                sLine.LineStyle = LineStyle.Lines; sLine.Thickness = 2;
                var tLine = Chart.DrawTrendLine("TP" + sfx, prevIndex, _tpPrice, prevIndex + 50, _tpPrice, Color.Green);
                tLine.LineStyle = LineStyle.Lines; tLine.Thickness = 2;

                Chart.DrawText("EntryLbl" + sfx, "Entry: " + _entryPrice.ToString("F1"), prevIndex + 5, _entryPrice, Color.DodgerBlue);
                Chart.DrawText("SLLbl" + sfx, "SL: " + _slPrice.ToString("F1"), prevIndex + 5, _slPrice, Color.Red);
                Chart.DrawText("TPLbl" + sfx, "TP: " + _tpPrice.ToString("F1"), prevIndex + 5, _tpPrice, Color.Green);
            }

            // ─── TRADE MANAGEMENT (SL / TP / EOD) ────────────────────────
            bool slHit = false, tpHit = false, eodClose = false;

            if (_state == SessionState.InTrade)
            {
                double prevHigh = Bars.HighPrices[prevIndex];
                double prevLow  = Bars.LowPrices[prevIndex];

                if (_tradeDir == 1)
                {
                    if (prevLow <= _slPrice) slHit = true;
                    else if (prevHigh >= _tpPrice) tpHit = true;
                }
                else if (_tradeDir == -1)
                {
                    if (prevHigh >= _slPrice) slHit = true;
                    else if (prevLow <= _tpPrice) tpHit = true;
                }

                if (hhmm >= EodCutoffHHMM && !slHit && !tpHit)
                    eodClose = true;

                if (slHit) { _tradeResult = "SL Hit"; _state = SessionState.Done; }
                else if (tpHit) { _tradeResult = "TP Hit"; _state = SessionState.Done; }
                else if (eodClose) { _tradeResult = "EOD Close"; _state = SessionState.Done; }
            }

            // Stop extending lines on trade resolution
            if (slHit || tpHit || eodClose)
            {
                string sfx = "_" + _dayCount;
                var eLine = Chart.FindObject("Entry" + sfx) as ChartTrendLine;
                if (eLine != null) eLine.Time2 = Bars.OpenTimes[prevIndex];
                var sLine = Chart.FindObject("SL" + sfx) as ChartTrendLine;
                if (sLine != null) sLine.Time2 = Bars.OpenTimes[prevIndex];
                var tLine = Chart.FindObject("TP" + sfx) as ChartTrendLine;
                if (tLine != null) tLine.Time2 = Bars.OpenTimes[prevIndex];
                Chart.RemoveObject("LondonHi_" + _dayCount);
                Chart.RemoveObject("LondonLo_" + _dayCount);
            }

            // ─── OUTPUT PROPERTIES (for cBot) ────────────────────────────
            LongSignal[prevIndex]       = longSweep ? 1.0 : double.NaN;
            ShortSignal[prevIndex]      = shortSweep ? 1.0 : double.NaN;
            EntryPriceOutput[prevIndex] = (longSweep || shortSweep) ? _entryPrice : double.NaN;
            SlPriceOutput[prevIndex]    = (longSweep || shortSweep) ? _slPrice : double.NaN;
            TpPriceOutput[prevIndex]    = (longSweep || shortSweep) ? _tpPrice : double.NaN;
        }

        // ─── DASHBOARD ──────────────────────────────────────────────────
        private void UpdateDashboard()
        {
            string h4St = _h4Bullish ? "Bullish [+]" : _h4Bearish ? "Bearish [-]" : "Neutral [=]";

            bool mp = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300) && _h1Close0900 > _h1Close0300;
            bool mn = !double.IsNaN(_h1Close0900) && !double.IsNaN(_h1Close0300) && _h1Close0900 < _h1Close0300;
            string momSt = mp ? "Positive [+]" : mn ? "Negative [-]" : "Flat [=]";

            string rngSt = double.IsNaN(_rangeSize) ? "N/A" : _rangeSize.ToString("F1") + " pts";
            bool rngOk = !double.IsNaN(_rangeSize) && _rangeSize >= MinRange;
            string sweepSt = _tradedToday ? (_tradeDir == 1 ? "LONG" : "SHORT") : "Watching";
            string tradeSt = _state == SessionState.InTrade ? "Active"
                : !string.IsNullOrEmpty(_tradeResult) ? _tradeResult : "None";

            string txt =
                "══ London Sweep ══\n" +
                "H4 Trend:  " + h4St + "\n" +
                "H1 Mom:    " + momSt + "\n" +
                "Range:     " + rngSt + (rngOk ? " OK" : " LOW") + "\n" +
                "Sweep:     " + sweepSt + "\n" +
                "Trade:     " + tradeSt + "\n" +
                "State:     " + _state;

            if (_tradedToday && !double.IsNaN(_entryPrice))
            {
                txt += "\n───────────────\n" +
                    "Entry: " + _entryPrice.ToString("F1") + "\n" +
                    "SL:    " + _slPrice.ToString("F1") + "\n" +
                    "TP:    " + _tpPrice.ToString("F1");
            }

            Chart.DrawStaticText("Dashboard", txt,
                VerticalAlignment.Top, HorizontalAlignment.Right, Color.White);
        }
    }
}
