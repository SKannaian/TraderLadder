using NinjaTrader.Custom;
using System;

namespace Gemify.OrderFlow
{
    internal interface ITradeClassifier
    {
        /*
         * Classifies given trade as either buyer or seller initiated.
         */
        TradeAggressor ClassifyTrade(double ask, double bid, double close, long volume, DateTime time);
    }
}