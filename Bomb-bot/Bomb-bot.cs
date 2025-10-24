using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None)]
    public class Bombbot : Robot
    {
        [Parameter(DefaultValue = "Hello world!")]
        public string Message { get; set; }

        private const int StructureLookback = 2; // candles before/after for swing detection
        private Bars bars;

        // Track mini-holder extremes
        private double miniHolderLow = double.MaxValue;
        private double miniHolderHigh = double.MinValue;
        private bool waitingForNextTrendCandle = false;

        protected override void OnStart()
        {
            Print(Message);
            bars = MarketData.GetBars(TimeFrame.Minute5);
        }

        protected override void OnTick()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName != Symbol.Name)
                    continue;

                var index = bars.Count - 1;
                var current = bars[index];
                var previous = bars[index - 1];

                // --- Calculate all potential stop-losses ---
                double? initialStop = position.TradeType == TradeType.Buy ? previous.Low : previous.High;
                double? breakoutStop = null;
                double? structureStop = null;
                double? miniHolderStop = null;
                double? approachingTPStop = null;

                // --- Breakout stop (first candle after entry) ---
                if (position.Comment == "FirstCandleMoved")
                {
                    if (position.TradeType == TradeType.Buy && current.High > previous.High)
                        breakoutStop = current.Low;
                    else if (position.TradeType == TradeType.Sell && current.Low < previous.Low)
                        breakoutStop = current.High;
                }

                // --- Mini-holder stop ---
                if (position.TradeType == TradeType.Buy)
                {
                    if (current.Low < miniHolderLow)
                        miniHolderLow = current.Low;

                    if (current.Close > current.Open && !waitingForNextTrendCandle)
                        miniHolderStop = miniHolderLow;
                }
                else if (position.TradeType == TradeType.Sell)
                {
                    if (current.High > miniHolderHigh)
                        miniHolderHigh = current.High;

                    if (current.Close < current.Open && !waitingForNextTrendCandle)
                        miniHolderStop = miniHolderHigh;
                }

                // --- Approaching TP stop ---
                if (position.TakeProfit.HasValue)
                {
                    double candleLength = current.High - current.Low;
                    if (position.TradeType == TradeType.Buy)
                    {
                        double distanceToTP = position.TakeProfit.Value - Symbol.Bid;
                        if (distanceToTP <= candleLength)
                            approachingTPStop = current.Low;
                    }
                    else if (position.TradeType == TradeType.Sell)
                    {
                        double distanceToTP = Symbol.Ask - position.TakeProfit.Value;
                        if (distanceToTP <= candleLength)
                            approachingTPStop = current.High;
                    }
                }

                // --- Structure stop (swing highs/lows) ---
                for (int i = 2; i < bars.Count - 2; i++)
                {
                    bool isSwingHigh = bars[i].High > bars[i - 1].High && bars[i].High > bars[i - 2].High &&
                                       bars[i].High > bars[i + 1].High && bars[i].High > bars[i + 2].High;

                    bool isSwingLow = bars[i].Low < bars[i - 1].Low && bars[i].Low < bars[i - 2].Low &&
                                      bars[i].Low < bars[i + 1].Low && bars[i].Low < bars[i + 2].Low;

                    if (position.TradeType == TradeType.Buy && isSwingHigh && current.High > bars[i].High)
                        structureStop = current.Low;

                    if (position.TradeType == TradeType.Sell && isSwingLow && current.Low < bars[i].Low)
                        structureStop = current.High;
                }

                // --- Determine the tightest stop-loss ---
                double tightestStop = initialStop.Value;

                if (position.TradeType == TradeType.Buy)
                {
                    if (breakoutStop.HasValue && breakoutStop.Value > tightestStop)
                        tightestStop = breakoutStop.Value;
                    if (structureStop.HasValue && structureStop.Value > tightestStop)
                        tightestStop = structureStop.Value;
                    if (miniHolderStop.HasValue && miniHolderStop.Value > tightestStop)
                        tightestStop = miniHolderStop.Value;
                    if (approachingTPStop.HasValue && approachingTPStop.Value > tightestStop)
                        tightestStop = approachingTPStop.Value;

                    // ✅ Only apply if stop is below current Bid
                    if (tightestStop < Symbol.Bid)
                        ModifyPosition(position, tightestStop, position.TakeProfit);
                }
                else // Sell
                {
                    if (breakoutStop.HasValue && breakoutStop.Value < tightestStop)
                        tightestStop = breakoutStop.Value;
                    if (structureStop.HasValue && structureStop.Value < tightestStop)
                        tightestStop = structureStop.Value;
                    if (miniHolderStop.HasValue && miniHolderStop.Value < tightestStop)
                        tightestStop = miniHolderStop.Value;
                    if (approachingTPStop.HasValue && approachingTPStop.Value < tightestStop)
                        tightestStop = approachingTPStop.Value;

                    // ✅ Only apply if stop is above current Ask
                    if (tightestStop > Symbol.Ask)
                        ModifyPosition(position, tightestStop, position.TakeProfit);
                }
            }
        }

        protected override void OnStop()
        {
        }
    }
}
