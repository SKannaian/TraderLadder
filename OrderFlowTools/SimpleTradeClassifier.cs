using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gemify.OrderFlow
{
    class SimpleTradeClassifier : ITradeClassifier
    {
        /*
         * Simple trade classifier implementation. 
         */
        TradeAggressor ITradeClassifier.ClassifyTrade(double ask, double bid, double close, long volume, DateTime time)
        {
            TradeAggressor aggressor;
            
            double midpoint = (ask + bid) / 2.0;

            if (ask == bid)
            {
                if (close > ask)
                {
                    aggressor = TradeAggressor.BUYER;
                }
                else 
                {
                    aggressor = TradeAggressor.SELLER;
                }
            }
            else
            {
                if (close > midpoint)
                {
                    aggressor = TradeAggressor.BUYER;
                }
                else 
                {
                    aggressor = TradeAggressor.SELLER;
                }
            }

            return aggressor;
        }
    }
}
