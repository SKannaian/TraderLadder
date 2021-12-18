using NinjaTrader.Cbi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;

namespace Gemify.OrderFlow
{
    class OFStrength
    {
        internal double buyStrength = 0.0;
        internal double sellStrength = 0.0;
    }

    class Trade
    {
        internal long Size { get; set; }
        internal double Ask { get; set; }
        internal double Bid { get; set; }
        internal DateTime Time { get; set; }
    }
	
	enum TradeAggressor
    {
        BUYER,
        SELLER
    }

    class GemsOrderFlow
    {

        protected ITradeClassifier tradeClassifier;

        private ConcurrentDictionary<double, Trade> SlidingWindowBuys;
        private ConcurrentDictionary<double, Trade> SlidingWindowSells;
        private ConcurrentDictionary<double, long> LastBuy;
        private ConcurrentDictionary<double, long> LastSell;
        private ConcurrentDictionary<double, long> TotalBuys;
        private ConcurrentDictionary<double, long> TotalSells;
        private ConcurrentDictionary<double, long> PrevBid;
        private ConcurrentDictionary<double, long> PrevAsk;
        
        private double imbalanceFactor;
        private long imbalanceInvalidateDistance;

        // To support Print
        private Indicator ind;

        internal GemsOrderFlow (double imbalanceFactor)
        {
            ind = new Indicator();

            tradeClassifier = new SimpleTradeClassifier();

            this.imbalanceFactor = imbalanceFactor;
            this.imbalanceInvalidateDistance = 10;

            SlidingWindowBuys = new ConcurrentDictionary<double, Trade>();
            SlidingWindowSells = new ConcurrentDictionary<double, Trade>();
            TotalBuys = new ConcurrentDictionary<double, long>();
            TotalSells = new ConcurrentDictionary<double, long>();

            LastBuy = new ConcurrentDictionary<double, long>();
            LastSell = new ConcurrentDictionary<double, long>();
            PrevAsk = new ConcurrentDictionary<double, long>();
            PrevBid = new ConcurrentDictionary<double, long>();
        }

        internal void ClearAll()
        {

            ClearSlidingWindow();

            TotalBuys.Clear();
            TotalSells.Clear();

            PrevBid.Clear();
            PrevAsk.Clear();
        }

        internal void ClearSlidingWindow()
        {
            SlidingWindowBuys.Clear();
            SlidingWindowSells.Clear();
            LastBuy.Clear();
            LastSell.Clear();
        }

        private void Print(string s)
        {
            ind.Print(s);
        }

        /*
         * Classifies given trade as either buyer or seller initiated based on configured classifier.
         */
        internal void ClassifyTrade(bool updateSlidingWindow, double ask, double bid, double close, long volume, DateTime time)
        {
            TradeAggressor aggressor = tradeClassifier.ClassifyTrade(ask, bid, close, volume, time);

            // Classification - buyers vs. sellers
            if (aggressor == TradeAggressor.BUYER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowBuys.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.Ask = ask;
                trade.Time = time;

                if (gotOldTrade)
                {
                    trade.Size = oldTrade.Size + volume;
                }
                else
                {
                    trade.Size = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowBuys.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                }
                TotalBuys.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
            else if (aggressor == TradeAggressor.SELLER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowSells.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.Bid = bid;
                trade.Time = time;

                if (gotOldTrade)
                {
                    trade.Size = oldTrade.Size + volume;
                }
                else
                {
                    trade.Size = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowSells.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                }
                TotalSells.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
        }

        /*
        * Gets total buy volume in the sliding window
        */
        internal long GetBuysInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowBuys.Values)
            {
                total += trade.Size;
            }
            return total;
        }

        /*
        * Gets total sell volume in the sliding window
        */
        internal long GetSellsInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowSells.Values)
            {
                total += trade.Size;
            }
            return total;
        }

        /*
         * Gets total volume transacted (buyers + sellers) at given price.
         */
        internal long GetVolumeAtPrice(double price)
        {
            long buyVolume = 0, sellVolume = 0;
            TotalBuys.TryGetValue(price, out buyVolume);
            TotalSells.TryGetValue(price, out sellVolume);
            long totalVolume = buyVolume + sellVolume;
            return totalVolume;
        }

        /* 
         * Clear out trades from the buys and sells collection if the trade entries 
         * fall outside (older trades) of a sliding time window (seconds), 
         * thus preserving only the latest trades based on the time window.
         */
        internal void ClearTradesOutsideSlidingWindow(DateTime time, int TradeSlidingWindowSeconds)
        {
            foreach (double price in SlidingWindowBuys.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowBuys.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowBuys.TryRemove(price, out trade);
                        long oldVolume;
                        LastBuy.TryRemove(price, out oldVolume);
                    }
                }
            }

            foreach (double price in SlidingWindowSells.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowSells.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowSells.TryRemove(price, out trade);
                        long oldVolume;
                        LastSell.TryRemove(price, out oldVolume);
                    }
                }
            }
        }

        internal long GetImbalancedBuys(double currentPrice, double tickSize)
        {
            long buyImbalance = 0;

            foreach (double buyPrice in SlidingWindowBuys.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice < buyPrice - (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(buyPrice, out buyTrade);
                long buySize = gotBuy ? buyTrade.Size : 0;

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(buyPrice - tickSize, out sellTrade);
                long sellSize = gotSell ? sellTrade.Size : 0;

                if (gotSell && buySize >= sellSize * imbalanceFactor)
                    buyImbalance += buySize;
            }

            return buyImbalance;
        }

        internal long GetImbalancedSells(double currentPrice, double tickSize)
        {
            long sellImbalance = 0;

            foreach (double sellPrice in SlidingWindowSells.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice > sellPrice + (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(sellPrice, out sellTrade);
                long sellSize = gotSell ? sellTrade.Size : 0;

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(sellPrice + tickSize, out buyTrade);
                long buySize = gotBuy ? buyTrade.Size : 0;

                if (gotBuy && sellSize >= buySize * imbalanceFactor)
                    sellImbalance += sellSize;
            }

            return sellImbalance;
        }

        internal OFStrength CalculateOrderFlowStrength(double price, double tickSize)
        {
            OFStrength orderFlowStrength = new OFStrength();

            // Imbalance data
            long buyImbalance = GetImbalancedBuys(price, tickSize);
            long sellImbalance = GetImbalancedSells(price, tickSize);

            if (buyImbalance + sellImbalance == 0)
            {
                buyImbalance = sellImbalance = 1;
            }

            long totalImbalance = buyImbalance + sellImbalance;

            // Buy/Sell data in sliding window
            long buysInSlidingWindow = GetBuysInSlidingWindow();
            long sellsInSlidingWindow = GetSellsInSlidingWindow();

            if (buysInSlidingWindow + sellsInSlidingWindow == 0)
            {
                buysInSlidingWindow = sellsInSlidingWindow = 1;
            }

            long totalVolume = sellsInSlidingWindow + buysInSlidingWindow;

            orderFlowStrength.buyStrength = (Convert.ToDouble(buysInSlidingWindow + buyImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.00;
            orderFlowStrength.sellStrength = (Convert.ToDouble(sellsInSlidingWindow + sellImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.8;

            return orderFlowStrength;
        }

        internal long GetBuyVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalBuys.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetSellVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalSells.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetAskChange(double price, long currentSize)
        {
            long change = 0;
            long prevSize = 0;
            bool gotPrevAsk = PrevAsk.TryGetValue(price, out prevSize);

            // Replace with current size
            if (currentSize > 0)
            {
                // Clear out old size
                long old;
                PrevAsk.TryRemove(price, out old);

                PrevAsk.TryAdd(price, currentSize);
            }

            if (gotPrevAsk)
            {
                change = currentSize - prevSize;

            }

            return change;
        }

        internal long GetBidChange(double price, long currentSize)
        {
            long change = 0;
            long prevSize = 0;
            bool gotPrevBid = PrevBid.TryGetValue(price, out prevSize);

            // Replace with current size
            if (currentSize > 0)
            {
                // Clear out old size
                long old;
                PrevBid.TryRemove(price, out old);

                PrevBid.TryAdd(price, currentSize);
            }

            if (gotPrevBid)
            {
                change = currentSize - prevSize;

            }

            return change;
        }

        internal Trade GetBuysInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowBuys.TryGetValue(price, out trade);
            return trade;
        }

        internal Trade GetSellsInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowSells.TryGetValue(price, out trade);
            return trade;
        }

        internal long GetLastBuySize(double price)
        {
            long lastSize = 0;

            Trade current = GetBuysInSlidingWindow(price);
            if (current != null && current.Size > 0) {
                
                long prevSize;
                if (LastBuy.TryGetValue(price, out prevSize))
                {
                    lastSize = current.Size - prevSize;
                }

                long oldValue;
                LastBuy.TryRemove(price, out oldValue);
                LastBuy.TryAdd(price, current.Size);                
            }
            return lastSize;
        }

        internal long GetLastSellSize(double price)
        {
            long lastSize = 0;

            Trade current = GetSellsInSlidingWindow(price);
            if (current != null && current.Size > 0)
            {

                long prevSize;
                if (LastSell.TryGetValue(price, out prevSize))
                {
                    lastSize = current.Size - prevSize;
                }

                long oldValue;
                LastSell.TryRemove(price, out oldValue);
                LastSell.TryAdd(price, current.Size);                
            }
            return lastSize;
        }

        internal void RemoveLastBuy(double price)
        {
            long lastSize;
            LastBuy.TryRemove(price, out lastSize);
        }

        internal void RemoveLastSell(double price)
        {
            long lastSize;
            LastSell.TryRemove(price, out lastSize);            
        }
    }
}
