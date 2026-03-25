// ══════════════════════════════════════════════════════════════════════════════
// London Session Sweep Indicator v2 | US30 | cTrader Automate (C#)
//
// Detects liquidity sweeps of London session range during NY open,
// filtered by H4 EMA trend + H1 momentum. Outputs signals for cBot.
//
// v2 improvements:
//   - Pre-sweep invalidation (09:00-09:29)
//   - Market order at sweep candle close (not limit)
//   - 1% risk auto-calculation
//
// SETUP: cTrader Desktop → Automate → New Indicator → paste → Build (Ctrl+B)
// Attach to US30 M15 chart.
// ══════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    /// <summary>Daily strategy cycle states.</summary>
    public enum SessionState
    {
        Idle,            // Waiting for London session start
        London,          // Tracking London range (03:00-09:00 EST)
        RangeDone,       // London closed, range calculated
        PreSweepCheck,   // Checking 09:00-09:29 for early sweeps
        SweepWindow,     // Monitoring for sweep (09:30-11:00 EST)
        InTrade,         // Trade active, monitoring SL/TP/EOD
        Done             // Day completed — no more trading
    }

    [Indicator(IsOverlay = true,
        TimeZone = TimeZones.EasternStandardTime,
        AccessRights = AccessRights.None)]
    public class LondonSweepIndicator : Indicator
    {
        // ─── INPUTS: Session Times ───────────────────────────────────────
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

        // ─── INPUTS: Trade Parameters ────────────────────────────────────
        [Parameter("Min London Range (pts)", Group = "Trade", DefaultValue = 50.0)]
        public double MinRange { get; set; }

        [Parameter("Sweep Threshold (pts)", Group = "Trade", DefaultValue = 5.0)]
        public double SweepThreshold { get; set; }

        [Parameter("SL Buffer (pts)", Group = "Trade", DefaultValue = 8.0)]
        public double SlBuffer { get; set; }

        [Parameter("TP Multiplier (x range)", Group = "Trade", DefaultValue = 0.65, Step = 0.05)]
        public double TpMultiplier { get; set; }

        // ─── INPUTS: Filters ─────────────────────────────────────────────
        [Parameter("H4 EMA Fast", Group = "Filters", DefaultValue = 20)]
        public int H4EmaFastPeriod { get; set; }

        [Parameter("H4 EMA Slow", Group = "Filters", DefaultValue = 50)]
        public int H4EmaSlowPeriod { get; set; }

        [Parameter("Enable H4 Trend Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH4Filter { get; set; }

        [Parameter("Enable H1 Momentum Filter", Group = "Filters", DefaultValue = true)]
        public bool UseH1Filter { get; set; }

        // ─── INPUTS: Visual ──────────────────────────────────────────────
        [Parameter("Show Dashboard", Group = "Visual", DefaultValue = true)]
        public bool ShowDashboard { get; set; }

        [Parameter("Show Trade Lines", Group = "Visual", DefaultValue = true)]
        public bool ShowTradeLines { get; set; }

        // ─── OUTPUTS (for cBot to read) ──────────────────────────────────
        [Output("Long Signal", LineColor = "Transparent")]
        public IndicatorDataSeries LongSignal { get; set; }

        [Output("Short Signal", LineColor = "Transparent")]
        public IndicatorDataSeries ShortSignal { get; set; }

        [Output("Entry Price", LineColor = "Transparent")]
        public IndicatorDataSeries EntryPriceOut { get; set; }

        [Output("SL Price", LineColor = "Transparent")]
        public IndicatorDataSeries SlPriceOut { get; set; }

        [Output("TP Price", LineColor = "Transparent")]
        public IndicatorDataSeries TpPriceOut { get; set; }

        // ─── PRIVATE STATE ───────────────────────────────────────────────
        private SessionState _state;
        private double _londonHigh, _londonLow, _rangeSize;
        private bool _tradedToday, _preSweepInvalid;
        private double _h1CloseAtLondonStart, _h1CloseAtLondonEnd;
        private int _tradeDir;  // +1 long, -1 short, 0 none
        private double _entryPrice, _slPrice, _tpPrice;
        private string _tradeResult;
        private DateTime _lastDate;
        private int _lastIndex;
        private int _dayCount;
        private int _londonBoxStartIdx;

        // MTF data
        private Bars _h4Bars, _h1Bars;
        private ExponentialMovingAverage _h4EmaFast, _h4EmaSlow;

        // Filter results
        private bool _h4Bullish, _h4Bearish;
        private bool _longAllowed, _shortAllowed;

        // ─── HELPERS ─────────────────────────────────────────────────────
        private static int GetHHMM(DateTime dt) => dt.Hour * 100 + dt.Minute;
        private static bool InRange(int hhmm, int start, int end) => hhmm >= start && hhmm < end;

        // ─── INITIALIZE ──────────────────────────────────────────────────
        protected override void Initialize()
        {
            _h4Bars = MarketData.GetBars(TimeFrame.Hour4);
            _h1Bars = MarketData.GetBars(TimeFrame.Hour);
            _h4EmaFast = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaFastPeriod);
            _h4EmaSlow = Indicators.ExponentialMovingAverage(_h4Bars.ClosePrices, H4EmaSlowPeriod);

            _lastDate = DateTime.MinValue;
            _lastIndex = -1;
            _debugBarCount = 0;
            ResetDay();
        }

        // Debug counter — only log first N bars per day to avoid spam
        private int _debugBarCount;

        /// <summary>Reset all daily state variables.</summary>
        private void ResetDay()
        {
            _state = SessionState.Idle;
            _londonHigh = double.NaN;
            _londonLow = double.NaN;
            _rangeSize = double.NaN;
            _tradedToday = false;
            _preSweepInvalid = false;
            _h1CloseAtLondonStart = double.NaN;
            _h1CloseAtLondonEnd = double.NaN;
            _tradeDir = 0;
            _entryPrice = double.NaN;
            _slPrice = double.NaN;
            _tpPrice = double.NaN;
            _tradeResult = "";
        }

        // ─── MAIN CALCULATE ──────────────────────────────────────────────
        public override void Calculate(int index)
        {
            // Dashboard updates on every tick
            if (ShowDashboard) UpdateDashboard();
            else Chart.RemoveObject("Dashboard");

            // Only run strategy on new bar close
            bool isNewBar = index > _lastIndex;
            _lastIndex = index;
            if (!isNewBar) return;

            int prev = index - 1;
            if (prev < 0) return;

            DateTime barTime = Bars.OpenTimes[prev];
            int hhmm = GetHHMM(barTime);

            // ─── DAILY RESET ─────────────────────────────────────────
            if (barTime.Date != _lastDate)
            {
                _lastDate = barTime.Date;
                _dayCount++;
                if (barTime.DayOfWeek == DayOfWeek.Saturday ||
                    barTime.DayOfWeek == DayOfWeek.Sunday) return;
                ResetDay();
            }

            // Weekend guard
            if (barTime.DayOfWeek == DayOfWeek.Saturday ||
                barTime.DayOfWeek == DayOfWeek.Sunday) return;

            // ─── H4 TREND FILTER (time-synced, non-repainting) ──────
            UpdateH4Filter(barTime);

            // ─── STATE: IDLE → LONDON ────────────────────────────────
            if (_state == SessionState.Idle && InRange(hhmm, LondonStartHHMM, LondonEndHHMM))
            {
                _state = SessionState.London;
                _londonHigh = Bars.HighPrices[prev];
                _londonLow = Bars.LowPrices[prev];
                _londonBoxStartIdx = prev;
                CaptureH1CloseAtLondonStart(barTime);
                DrawLondonBox(prev);
            }

            // ─── STATE: LONDON — track range ─────────────────────────
            if (_state == SessionState.London && InRange(hhmm, LondonStartHHMM, LondonEndHHMM))
            {
                _londonHigh = Math.Max(_londonHigh, Bars.HighPrices[prev]);
                _londonLow = Math.Min(_londonLow, Bars.LowPrices[prev]);
                DrawLondonBox(prev);
            }

            // ─── STATE: LONDON → RANGEDONE (London closes) ──────────
            if (_state == SessionState.London && !InRange(hhmm, LondonStartHHMM, LondonEndHHMM))
            {
                _rangeSize = _londonHigh - _londonLow;
                CaptureH1CloseAtLondonEnd(barTime);

                // Range too small → done for the day
                if (_rangeSize < MinRange)
                {
                    _state = SessionState.Done;
                    return;
                }

                _state = SessionState.RangeDone;
                DrawRangeLines();
            }

            // ─── H1 MOMENTUM FILTER ──────────────────────────────────
            UpdateDirectionFilter();

            // ─── STATE: RANGEDONE → PRESWEEPCHECK ────────────────────
            if (_state == SessionState.RangeDone &&
                InRange(hhmm, LondonEndHHMM, PreSweepEndHHMM))
            {
                _state = SessionState.PreSweepCheck;
            }

            // ─── STATE: PRESWEEPCHECK — check for early sweep ────────
            if (_state == SessionState.PreSweepCheck &&
                InRange(hhmm, LondonEndHHMM, PreSweepEndHHMM))
            {
                double prevHigh = Bars.HighPrices[prev];
                double prevLow = Bars.LowPrices[prev];

                // If price already swept past London High or Low by threshold → invalidate
                if (prevHigh > _londonHigh + SweepThreshold || prevLow < _londonLow - SweepThreshold)
                {
                    _preSweepInvalid = true;
                    _state = SessionState.Done;
                    return;
                }
            }

            // ─── STATE: PRESWEEPCHECK → SWEEPWINDOW ──────────────────
            if ((_state == SessionState.PreSweepCheck || _state == SessionState.RangeDone) &&
                InRange(hhmm, SweepStartHHMM, SweepEndHHMM) &&
                !_preSweepInvalid)
            {
                _state = SessionState.SweepWindow;
            }

            // ─── STATE: SWEEPWINDOW timeout (checked BEFORE detect) ──────────
            // Must run first so the 11:00 candle (open at 11:00, close at 11:15)
            // does not generate a signal after the cutoff.
            if (_state == SessionState.SweepWindow && hhmm >= SweepEndHHMM)
            {
                _state = SessionState.Done;
            }

            // ─── STATE: SWEEPWINDOW — detect sweep ───────────────────
            if (_state == SessionState.SweepWindow && !_tradedToday)
            {
                DetectSweep(prev);
            }

            // ─── STATE: INTRADE — monitor SL/TP/EOD ──────────────────
            if (_state == SessionState.InTrade)
            {
                ManageTrade(prev, hhmm);
            }

            // ─── WRITE OUTPUTS ───────────────────────────────────────
            // Signals already written in DetectSweep; default NaN
        }

        // ─── H4 FILTER (time-synced for correct backtesting) ─────────────
        private void UpdateH4Filter(DateTime barTime)
        {
            // Find H4 bar that corresponds to current M15 bar time
            int h4Idx = _h4Bars.OpenTimes.GetIndexByTime(barTime);
            if (h4Idx < 1) { _h4Bullish = false; _h4Bearish = false; return; }

            // Use previous closed H4 bar (h4Idx-1) for non-repainting
            double fast = _h4EmaFast.Result[h4Idx - 1];
            double slow = _h4EmaSlow.Result[h4Idx - 1];
            bool valid = !double.IsNaN(fast) && !double.IsNaN(slow);
            _h4Bullish = valid && fast > slow;
            _h4Bearish = valid && fast < slow;
        }

        // ─── DIRECTION FILTER (H4 + H1 combined) ────────────────────────
        private void UpdateDirectionFilter()
        {
            bool momPos = !double.IsNaN(_h1CloseAtLondonEnd) && !double.IsNaN(_h1CloseAtLondonStart)
                          && _h1CloseAtLondonEnd > _h1CloseAtLondonStart;
            bool momNeg = !double.IsNaN(_h1CloseAtLondonEnd) && !double.IsNaN(_h1CloseAtLondonStart)
                          && _h1CloseAtLondonEnd < _h1CloseAtLondonStart;

            _longAllowed  = (UseH4Filter ? _h4Bullish : true) && (UseH1Filter ? momPos : true);
            _shortAllowed = (UseH4Filter ? _h4Bearish : true) && (UseH1Filter ? momNeg : true);
        }

        // ─── H1 CLOSE CAPTURES (time-synced for correct backtesting) ─────
        /// <summary>Capture H1 close at London start. Uses time-based lookup
        /// to get the H1 bar that closed at ~03:00 EST (the 02:00-03:00 candle).</summary>
        private void CaptureH1CloseAtLondonStart(DateTime barTime)
        {
            int h1Idx = _h1Bars.OpenTimes.GetIndexByTime(barTime);
            if (h1Idx > 0)
                _h1CloseAtLondonStart = _h1Bars.ClosePrices[h1Idx - 1];
        }

        /// <summary>Capture H1 close at London end. Uses time-based lookup
        /// to get the H1 bar that closed at ~08:00 EST (the 07:00-08:00 candle).
        /// Strategy spec: compare close@08:00 vs close@03:00 for H1 momentum.
        /// Called at 09:00 EST: h1Idx points to the 09:00-10:00 bar, so
        /// h1Idx-2 gives the 07:00-08:00 bar that closed at 08:00.</summary>
        private void CaptureH1CloseAtLondonEnd(DateTime barTime)
        {
            int h1Idx = _h1Bars.OpenTimes.GetIndexByTime(barTime);
            if (h1Idx > 1)
                _h1CloseAtLondonEnd = _h1Bars.ClosePrices[h1Idx - 2];
        }

        // ─── SWEEP DETECTION ─────────────────────────────────────────────
        private void DetectSweep(int prev)
        {
            double prevHigh  = Bars.HighPrices[prev];
            double prevLow   = Bars.LowPrices[prev];
            double prevClose = Bars.ClosePrices[prev];

            // ─── BREAKOUT CHECK (must come FIRST) ───────────────────────
            // Strategy rule: "Nếu giá phá vỡ High/Low nhưng đóng cửa ngoài
            // vùng → Đó là Breakout, không phải Sweep → Không vào lệnh."
            // Once ANY candle closes outside the London range during sweep
            // window, the day is invalidated — no more sweep opportunities.
            bool closedBelowRange = prevClose < _londonLow;
            bool closedAboveRange = prevClose > _londonHigh;
            if (closedBelowRange || closedAboveRange)
            {
                _state = SessionState.Done;
                return;
            }

            // ─── SWEEP CHECK (close must be INSIDE range) ───────────────
            bool longSweep = false;
            bool shortSweep = false;

            // LONG: wick dips below London Low by threshold, closes back above
            if (_longAllowed)
            {
                double sweepDepth = _londonLow - prevLow;
                if (sweepDepth >= SweepThreshold && prevClose > _londonLow)
                {
                    longSweep = true;
                    _tradeDir = 1;
                    _entryPrice = prevClose;
                    _slPrice = prevLow - SlBuffer;
                    _tpPrice = prevClose + (TpMultiplier * _rangeSize);
                }
            }

            // SHORT: wick spikes above London High by threshold, closes back below
            if (_shortAllowed && !longSweep)
            {
                double sweepHeight = prevHigh - _londonHigh;
                if (sweepHeight >= SweepThreshold && prevClose < _londonHigh)
                {
                    shortSweep = true;
                    _tradeDir = -1;
                    _entryPrice = prevClose;
                    _slPrice = prevHigh + SlBuffer;
                    _tpPrice = prevClose - (TpMultiplier * _rangeSize);
                }
            }

            if (longSweep || shortSweep)
            {
                _state = SessionState.InTrade;
                _tradedToday = true;

                // Write output signals for cBot
                LongSignal[prev]   = longSweep ? 1.0 : double.NaN;
                ShortSignal[prev]  = shortSweep ? 1.0 : double.NaN;
                EntryPriceOut[prev] = _entryPrice;
                SlPriceOut[prev]   = _slPrice;
                TpPriceOut[prev]   = _tpPrice;

                DrawTradeLines(prev);
                DrawSweepArrow(prev, longSweep);
            }
        }

        // ─── TRADE MANAGEMENT (visual tracking for backtest) ─────────────
        private void ManageTrade(int prev, int hhmm)
        {
            double prevHigh = Bars.HighPrices[prev];
            double prevLow  = Bars.LowPrices[prev];

            bool slHit = false, tpHit = false, eodClose = false;

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

            if (!slHit && !tpHit && hhmm >= EodCutoffHHMM)
                eodClose = true;

            if (slHit)      { _tradeResult = "SL Hit"; _state = SessionState.Done; }
            else if (tpHit) { _tradeResult = "TP Hit"; _state = SessionState.Done; }
            else if (eodClose) { _tradeResult = "EOD Close"; _state = SessionState.Done; }

            // Stop extending trade lines on resolution
            if (slHit || tpHit || eodClose)
                StopTradeLines(prev);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  VISUALS
        // ═══════════════════════════════════════════════════════════════════

        private void DrawLondonBox(int endIdx)
        {
            var box = Chart.DrawRectangle("LondonBox_" + _dayCount,
                _londonBoxStartIdx, _londonHigh, endIdx, _londonLow,
                Color.FromArgb(40, 0, 100, 255));
            box.IsFilled = true;
        }

        private void DrawRangeLines()
        {
            var hi = Chart.DrawHorizontalLine("LondonHi_" + _dayCount, _londonHigh, Color.Blue);
            hi.LineStyle = LineStyle.Dots;
            var lo = Chart.DrawHorizontalLine("LondonLo_" + _dayCount, _londonLow, Color.Blue);
            lo.LineStyle = LineStyle.Dots;
        }

        private void DrawTradeLines(int startIdx)
        {
            if (!ShowTradeLines) return;

            string sfx = "_" + _dayCount;
            int endIdx = startIdx + 50;

            var eLine = Chart.DrawTrendLine("Entry" + sfx, startIdx, _entryPrice, endIdx, _entryPrice, Color.DodgerBlue);
            eLine.LineStyle = LineStyle.Lines; eLine.Thickness = 2;

            var sLine = Chart.DrawTrendLine("SL" + sfx, startIdx, _slPrice, endIdx, _slPrice, Color.Red);
            sLine.LineStyle = LineStyle.Lines; sLine.Thickness = 2;

            var tLine = Chart.DrawTrendLine("TP" + sfx, startIdx, _tpPrice, endIdx, _tpPrice, Color.Green);
            tLine.LineStyle = LineStyle.Lines; tLine.Thickness = 2;

            Chart.DrawText("EntryLbl" + sfx, "Entry: " + _entryPrice.ToString("F1"), startIdx + 5, _entryPrice, Color.DodgerBlue);
            Chart.DrawText("SLLbl" + sfx, "SL: " + _slPrice.ToString("F1"), startIdx + 5, _slPrice, Color.Red);
            Chart.DrawText("TPLbl" + sfx, "TP: " + _tpPrice.ToString("F1"), startIdx + 5, _tpPrice, Color.Green);
        }

        private void DrawSweepArrow(int idx, bool isLong)
        {
            string name = "SweepArrow_" + _dayCount;
            if (isLong)
                Chart.DrawIcon(name, ChartIconType.UpArrow, idx, Bars.LowPrices[idx] - 10, Color.Lime);
            else
                Chart.DrawIcon(name, ChartIconType.DownArrow, idx, Bars.HighPrices[idx] + 10, Color.Red);
        }

        private void StopTradeLines(int endIdx)
        {
            string sfx = "_" + _dayCount;
            DateTime endTime = Bars.OpenTimes[endIdx];

            var eLine = Chart.FindObject("Entry" + sfx) as ChartTrendLine;
            if (eLine != null) eLine.Time2 = endTime;

            var sLine = Chart.FindObject("SL" + sfx) as ChartTrendLine;
            if (sLine != null) sLine.Time2 = endTime;

            var tLine = Chart.FindObject("TP" + sfx) as ChartTrendLine;
            if (tLine != null) tLine.Time2 = endTime;

            Chart.RemoveObject("LondonHi_" + _dayCount);
            Chart.RemoveObject("LondonLo_" + _dayCount);
        }

        // ─── DASHBOARD ──────────────────────────────────────────────────
        private void UpdateDashboard()
        {
            string h4St = _h4Bullish ? "Bullish [+]" : _h4Bearish ? "Bearish [-]" : "Neutral [=]";

            bool mp = !double.IsNaN(_h1CloseAtLondonEnd) && !double.IsNaN(_h1CloseAtLondonStart) && _h1CloseAtLondonEnd > _h1CloseAtLondonStart;
            bool mn = !double.IsNaN(_h1CloseAtLondonEnd) && !double.IsNaN(_h1CloseAtLondonStart) && _h1CloseAtLondonEnd < _h1CloseAtLondonStart;
            string momSt = mp ? "Positive [+]" : mn ? "Negative [-]" : "Flat [=]";

            string rngSt = double.IsNaN(_rangeSize) ? "N/A" : _rangeSize.ToString("F1") + " pts";
            bool rngOk = !double.IsNaN(_rangeSize) && _rangeSize >= MinRange;

            string preSweepSt = _preSweepInvalid ? "INVALID" : "Clear";
            string sweepSt = _tradedToday ? (_tradeDir == 1 ? "LONG" : "SHORT") : "Watching";
            string tradeSt = _state == SessionState.InTrade ? "Active"
                : !string.IsNullOrEmpty(_tradeResult) ? _tradeResult : "None";

            string txt =
                "══ London Sweep v2 ══\n" +
                "H4 Trend:   " + h4St + "\n" +
                "H1 Mom:     " + momSt + "\n" +
                "Range:      " + rngSt + (rngOk ? " OK" : " LOW") + "\n" +
                "Pre-Sweep:  " + preSweepSt + "\n" +
                "Sweep:      " + sweepSt + "\n" +
                "Trade:      " + tradeSt + "\n" +
                "State:      " + _state;

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
