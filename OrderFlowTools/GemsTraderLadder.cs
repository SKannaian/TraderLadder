// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//
#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Globalization;
using Gemify.OrderFlow;
using Trade = Gemify.OrderFlow.Trade;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
    public class GemsTraderLadder : SuperDomColumn
    {
		public enum PLType
	    {
	        Ticks,
	        Currency
	    }

	    public enum ColorScheme
	    {
	        Red_Green,
	        Red_Blue
	    }
		
        class ColumnDefinition
        {
            public ColumnDefinition(ColumnType columnType, ColumnSize columnSize, Brush backgroundColor, Func<double, double, FormattedText> calculate)
            {
                ColumnType = columnType;
                ColumnSize = columnSize;
                BackgroundColor = backgroundColor;
                Calculate = calculate;
            }
            public ColumnType ColumnType { get; set; }
            public ColumnSize ColumnSize { get; set; }
            public Brush BackgroundColor { get; set; }
            public Func<double, double, FormattedText> Calculate { get; set; }
            public FormattedText Text { get; set; }
            public void GenerateText(double renderWidth, double price)
            {
                Text = Calculate(renderWidth, price);
            }
        }        

        enum ColumnType
        {
            [Description("Vol")]
            VOLUME, 
            [Description("Acc Val")]
            ACCVAL, 
            [Description("Sess P/L")]
            TOTALPL, 
            [Description("P/L")]
            PL, 
            [Description("Price")]
            PRICE,
            [Description("Sells")]
            SELLS,
            [Description("Buys")]
            BUYS, 
            [Description("Last")]
            SELL_SIZE, 
            [Description("Last")]
            BUY_SIZE, 
            [Description("Sess Sells")]
            TOTAL_SELLS, 
            [Description("Sess Buys")]
            TOTAL_BUYS, 
            [Description("Bid")]
            BID, 
            [Description("Ask")]
            ASK, 
            [Description("+/-")]
            BID_CHANGE, 
            [Description("+/-")]
            ASK_CHANGE, 
            [Description("OFS")]
            OF_STRENGTH
        }

        enum ColumnSize
        {
            SMALL, MEDIUM, LARGE, XLARGE
        }         

        #region Variable Decls
        // VERSION
        private readonly string TraderLadderVersion = "v0.3.2";

        // UI variables
        private bool clearLoadingSent;
        private FontFamily fontFamily;
        private FontStyle fontStyle;
        private FontWeight fontWeight;
        private Pen gridPen;
        private double halfPenWidth;
        private bool heightUpdateNeeded;
        private double textHeight;
        private Point textPosition = new Point(10, 0);
        private static Typeface typeFace;

        // plumbing
        private readonly object barsSync = new object();
        private readonly double ImbalanceInvalidationThreshold = 5;
        private string tradingHoursData = TradingHours.UseInstrumentSettings;
        private bool mouseEventsSubscribed;
        private int lastMaxIndex = -1;

        // Orderflow variables
        private GemsOrderFlow orderFlow;

        private double commissionRT = 0.00;

        // Number of rows to display bid/ask size changes
        private long maxVolume = 0;
        private List<ColumnDefinition> columns;

        private Brush DefaultBackgroundColor = new SolidColorBrush(Color.FromRgb(25, 25, 25));
        private Brush CurrentPriceRowColor = new SolidColorBrush(Color.FromRgb(45, 45, 55));
        private Brush LongPositionRowColor = new SolidColorBrush(Color.FromRgb(10, 60, 10));
        private Brush ShortPositionRowColor = new SolidColorBrush(Color.FromRgb(70, 10, 10));
        private Brush SellColumnColor;
        private Brush BuyColumnColor;
        private Brush AskColumnColor;
        private Brush BidColumnColor;

        private static Indicator ind = new Indicator();
        private static CultureInfo culture = Core.Globals.GeneralOptions.CurrentCulture;
        private double pixelsPerDip;
        private long buysInSlidingWindow = 0;
        private long sellsInSlidingWindow = 0;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Free Trader Ladder (gemify) " + TraderLadderVersion;
                Description = @"Traders Ladder - (c) Gem Immanuel";
                DefaultWidth = 500;
                PreviousWidth = -1;
                IsDataSeriesRequired = true;

                // Orderflow var init
                orderFlow = new GemsOrderFlow(ImbalanceFactor);

                columns = new List<ColumnDefinition>();

                BidAskRows = 10;

                ImbalanceFactor = 2;
                TradeSlidingWindowSeconds = 60;
                OrderFlowStrengthThreshold = 65;

                DefaultTextColor = Brushes.Gray;
                VolumeColor = Brushes.MidnightBlue;
                VolumeTextColor = DefaultTextColor;
                BuyTextColor = Brushes.Lime;
                SellTextColor = Brushes.Red;
                SessionBuyTextColor = Brushes.Green;
                SessionSellTextColor = Brushes.Firebrick;
                BidAskRemoveColor = Brushes.Firebrick;
                BidAskAddColor = Brushes.Green;
                SellImbalanceColor = Brushes.Magenta;
                BuyImbalanceColor = Brushes.Cyan;
                LastTradeColor = Brushes.White;
                HeaderRowColor = Brushes.DimGray;
                HeadersTextColor = Brushes.White;

                DisplayVolume = true;
                DisplayVolumeText = true;
                DisplayPrice = true;
                DisplayAccountValue = false;
                DisplayPL = false;
                DisplaySessionPL = false;
                DisplayBidAsk = false;
                DisplayBidAskChange = false;
                DisplayLastSize = true;
                DisplaySlidingWindowBuysSells = true;
                DisplaySessionBuysSells = true;
                DisplayOrderFlowStrengthBar = true;

                ProfitLossType = PLType.Ticks;
                SelectedCurrency = Currency.UsDollar;

                ColorSchemeType = ColorScheme.Red_Blue;

            }
            else if (State == State.Configure)
            {
                switch (ColorSchemeType)
                {
                    case ColorScheme.Red_Blue:
                        {
                            SellColumnColor = new SolidColorBrush(Color.FromRgb(40, 15, 15));
                            AskColumnColor = new SolidColorBrush(Color.FromRgb(30, 15, 15));

                            BuyColumnColor = new SolidColorBrush(Color.FromRgb(15, 15, 30));
                            BidColumnColor = new SolidColorBrush(Color.FromRgb(20, 20, 30));
                            break;
                        }
                    case ColorScheme.Red_Green:
                    default:
                        {
                            SellColumnColor = new SolidColorBrush(Color.FromRgb(40, 15, 15));
                            AskColumnColor = new SolidColorBrush(Color.FromRgb(30, 15, 15));

                            BuyColumnColor = new SolidColorBrush(Color.FromRgb(15, 30, 15));
                            BidColumnColor = new SolidColorBrush(Color.FromRgb(15, 20, 15));
                            break;
                        }
                }

                #region Add Requested Columns
                // Add requested columns
                if (DisplayVolume)
                    columns.Add(new ColumnDefinition(ColumnType.VOLUME, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateVolumeText));
                if (DisplayPL)
                    columns.Add(new ColumnDefinition(ColumnType.PL, ColumnSize.MEDIUM, DefaultBackgroundColor, CalculatePL));
                if (DisplayPrice)
                    columns.Add(new ColumnDefinition(ColumnType.PRICE, ColumnSize.MEDIUM, DefaultBackgroundColor, GetPrice));
                if (DisplaySessionBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_SELLS, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateSessionSellsText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.BID_CHANGE, ColumnSize.SMALL, DefaultBackgroundColor, GenerateBidChangeText));
                if (DisplayBidAsk)
                    columns.Add(new ColumnDefinition(ColumnType.BID, ColumnSize.SMALL, DefaultBackgroundColor, GenerateBidText));
                if (DisplaySlidingWindowBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.SELLS, ColumnSize.SMALL, SellColumnColor, GenerateSlidingWindowSellsText));
                if (DisplayLastSize)
                {
                    columns.Add(new ColumnDefinition(ColumnType.SELL_SIZE, ColumnSize.SMALL, SellColumnColor, GenerateLastSellText));
                    columns.Add(new ColumnDefinition(ColumnType.BUY_SIZE, ColumnSize.SMALL, BuyColumnColor, GenerateLastBuyText));
                }
                if (DisplaySlidingWindowBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.BUYS, ColumnSize.SMALL, BuyColumnColor, GenerateSlidingWindowBuysText));
                if (DisplayBidAsk)
                    columns.Add(new ColumnDefinition(ColumnType.ASK, ColumnSize.SMALL, DefaultBackgroundColor, GenerateAskText));
                if (DisplayBidAskChange)
                    columns.Add(new ColumnDefinition(ColumnType.ASK_CHANGE, ColumnSize.SMALL, DefaultBackgroundColor, GenerateAskChangeText));
                if (DisplaySessionBuysSells)
                    columns.Add(new ColumnDefinition(ColumnType.TOTAL_BUYS, ColumnSize.MEDIUM, DefaultBackgroundColor, GenerateSessionBuysText));
                if (DisplayOrderFlowStrengthBar)
                    columns.Add(new ColumnDefinition(ColumnType.OF_STRENGTH, ColumnSize.SMALL, DefaultBackgroundColor, CalculateOFStrength));

                if (DisplaySessionPL)
                    columns.Add(new ColumnDefinition(ColumnType.TOTALPL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateTotalPL));
                if (DisplayAccountValue)
                    columns.Add(new ColumnDefinition(ColumnType.ACCVAL, ColumnSize.LARGE, DefaultBackgroundColor, CalculateAccValue));
                #endregion

                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    double dpiFactor = 1 / m.M11;
                    gridPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), dpiFactor);
                    halfPenWidth = gridPen.Thickness * 0.5;
                    pixelsPerDip = VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;
                }

                if (SuperDom.Instrument != null && SuperDom.IsConnected)
                {

                    BarsPeriod bp = new BarsPeriod
                    {
                        MarketDataType = MarketDataType.Last,
                        BarsPeriodType = BarsPeriodType.Tick,
                        Value = 1
                    };

                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.SetLoadingString());
                    clearLoadingSent = false;

                    if (BarsRequest != null)
                    {
                        BarsRequest.Update -= OnBarsUpdate;
                        BarsRequest = null;
                    }

                    BarsRequest = new BarsRequest(SuperDom.Instrument,
                        Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now,
                        Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now);

                    BarsRequest.BarsPeriod = bp;
                    BarsRequest.Update += OnBarsUpdate;

                    BarsRequest.Request((request, errorCode, errorMessage) =>
                    {
                        // Make sure this isn't a bars callback from another column instance
                        if (request != BarsRequest)
                        {
                            return;
                        }

                        lastMaxIndex = 0;
                        orderFlow.ClearAll();

                        if (State >= NinjaTrader.NinjaScript.State.Terminated)
                        {
                            return;
                        }

                        if (errorCode == Cbi.ErrorCode.UserAbort)
                        {
                            if (State <= NinjaTrader.NinjaScript.State.Terminated)
                                if (SuperDom != null && !clearLoadingSent)
                                {
                                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                    clearLoadingSent = true;
                                }

                            request.Update -= OnBarsUpdate;
                            request.Dispose();
                            request = null;
                            return;
                        }

                        if (errorCode != Cbi.ErrorCode.NoError)
                        {
                            request.Update -= OnBarsUpdate;
                            request.Dispose();
                            request = null;
                            if (SuperDom != null && !clearLoadingSent)
                            {
                                SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                clearLoadingSent = true;
                            }
                        }
                        else if (errorCode == Cbi.ErrorCode.NoError)
                        {

                            SessionIterator superDomSessionIt = new SessionIterator(request.Bars);
                            bool includesEndTimeStamp = request.Bars.BarsType.IncludesEndTimeStamp(false);

                            if (superDomSessionIt.IsInSession(Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now, includesEndTimeStamp, request.Bars.BarsType.IsIntraday))
                            {

                                for (int i = 0; i < request.Bars.Count; i++)
                                {
                                    DateTime time = request.Bars.BarsSeries.GetTime(i);
                                    if ((includesEndTimeStamp && time <= superDomSessionIt.ActualSessionBegin) || (!includesEndTimeStamp && time < superDomSessionIt.ActualSessionBegin))
                                        continue;

                                    // Get our datapoints
                                    double ask = request.Bars.BarsSeries.GetAsk(i);
                                    double bid = request.Bars.BarsSeries.GetBid(i);
                                    double close = request.Bars.BarsSeries.GetClose(i);
                                    long volume = request.Bars.BarsSeries.GetVolume(i);

                                    // Classify current volume as buy/sell
                                    // and add them to the buys/sells and totalBuys/totalSells collections
                                    orderFlow.ClassifyTrade(false, ask, bid, close, volume, time);

                                    // Calculate current max volume for session
                                    long totalVolume = orderFlow.GetVolumeAtPrice(close);
                                    maxVolume = totalVolume > maxVolume ? totalVolume : maxVolume;
                                }

                                lastMaxIndex = request.Bars.Count - 1;

                                // Repaint the column on the SuperDOM
                                OnPropertyChanged();
                            }

                            if (SuperDom != null && !clearLoadingSent)
                            {
                                SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                                clearLoadingSent = true;
                            }

                        }
                    });

                    // Repaint the column on the SuperDOM
                    OnPropertyChanged();

                }

            }
            else if (State == State.Active)
            {
                WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseDown", OnMouseClick);
                mouseEventsSubscribed = true;

            }
            else if (State == State.DataLoaded)
            {
                AccountItemEventArgs commissionAccountItem = SuperDom.Account.GetAccountItem(AccountItem.Commission, SelectedCurrency);
                if (commissionAccountItem != null)
                {
                    commissionRT = 2 * commissionAccountItem.Value;
                }

            }
            else if (State == State.Terminated)
            {
                if (BarsRequest != null)
                {
                    BarsRequest.Update -= OnBarsUpdate;
                    BarsRequest.Dispose();
                }

                BarsRequest = null;

                if (SuperDom != null && !clearLoadingSent)
                {
                    SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                    clearLoadingSent = true;
                }

                if (mouseEventsSubscribed)
                {
                    WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseDown", OnMouseClick);
                    mouseEventsSubscribed = false;
                }

                lastMaxIndex = 0;
                orderFlow.ClearAll();
            }
        }

        private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            if (State == State.Active && SuperDom != null && SuperDom.IsConnected)
            {
                if (SuperDom.IsReloading)
                {
                    OnPropertyChanged();
                    return;
                }

                BarsUpdateEventArgs barsUpdate = e;
                lock (barsSync)
                {
                    int currentMaxIndex = barsUpdate.MaxIndex;

                    for (int i = lastMaxIndex + 1; i <= currentMaxIndex; i++)
                    {
                        if (barsUpdate.BarsSeries.GetIsFirstBarOfSession(i))
                        {
                            // If a new session starts, clear out the old values and start fresh
                            maxVolume = 0;
                            orderFlow.ClearAll();
                        }

                        // Fetch our datapoints
                        double ask = barsUpdate.BarsSeries.GetAsk(i);
                        double bid = barsUpdate.BarsSeries.GetBid(i);
                        double close = barsUpdate.BarsSeries.GetClose(i);
                        long volume = barsUpdate.BarsSeries.GetVolume(i);
                        DateTime time = barsUpdate.BarsSeries.GetTime(i);

                        // Clear out data in buy / sell dictionaries based on a configurable
                        // sliding window of time (in seconds)
                        orderFlow.ClearTradesOutsideSlidingWindow(time, TradeSlidingWindowSeconds);

                        // Classify current volume as buy/sell
                        // and add them to the buys/sells and totalBuys/totalSells collections
                        orderFlow.ClassifyTrade(true, ask, bid, close, volume, time);

                        // Calculate current max volume for session
                        long totalVolume = orderFlow.GetVolumeAtPrice(close);
                        maxVolume = totalVolume > maxVolume ? totalVolume : maxVolume;
                    }

                    lastMaxIndex = barsUpdate.MaxIndex;
                    if (!clearLoadingSent)
                    {
                        SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
                        clearLoadingSent = true;
                    }
                }
            }
        }

        protected override void OnRender(DrawingContext dc, double renderWidth)
        {

            // This may be true if the UI for a column hasn't been loaded yet (e.g., restoring multiple tabs from workspace won't load each tab until it's clicked by the user)
            if (gridPen == null)
            {
                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    double dpiFactor = 1 / m.M11;
                    gridPen = new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
                    halfPenWidth = gridPen.Thickness * 0.5;
                }
            }

            double verticalOffset = -gridPen.Thickness;
            pixelsPerDip = VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

            if (fontFamily != SuperDom.Font.Family
                || (SuperDom.Font.Italic && fontStyle != FontStyles.Italic)
                || (!SuperDom.Font.Italic && fontStyle == FontStyles.Italic)
                || (SuperDom.Font.Bold && fontWeight != FontWeights.Bold)
                || (!SuperDom.Font.Bold && fontWeight == FontWeights.Bold))
            {
                // Only update this if something has changed
                fontFamily = SuperDom.Font.Family;
                fontStyle = SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal;
                fontWeight = SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal;
                typeFace = new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
                heightUpdateNeeded = true;
            }

            lock (SuperDom.Rows)
            { 
                foreach (PriceRow row in SuperDom.Rows)
                {
                    if (renderWidth - halfPenWidth >= 0)
                    {
                        if (SuperDom.IsConnected && !SuperDom.IsReloading && State == NinjaTrader.NinjaScript.State.Active)
                        {
                            // Generate cell text
                            for (int i = 0; i < columns.Count; i++)
                            {
                                double cellWidth = CalculateCellWidth(columns[i].ColumnSize, renderWidth);
                                columns[i].GenerateText(cellWidth, row.Price);
                            }

                            // Render the grid
                            DrawGrid(dc, renderWidth, verticalOffset, row);

                            verticalOffset += SuperDom.ActualRowHeight;
                        }
                    }
                }
            }
        }

        private double CalculateCellWidth(ColumnSize columnSize, double renderWidth)
        {
            double cellWidth = 0;
            int factor = 0;
            foreach (ColumnDefinition colDef in columns)
            {
                switch (colDef.ColumnSize)
                {
                    case ColumnSize.SMALL: factor += 1; break;
                    case ColumnSize.MEDIUM: factor += 2; break;
                    case ColumnSize.LARGE: factor += 3; break;
                    case ColumnSize.XLARGE: factor += 4; break;
                }
            }
            double unitCellWidth = renderWidth / factor;
            switch (columnSize)
            {
                case ColumnSize.XLARGE: cellWidth = 4 * unitCellWidth; break;
                case ColumnSize.LARGE: cellWidth = 3 * unitCellWidth; break;
                case ColumnSize.MEDIUM: cellWidth = 2 * unitCellWidth; break;
                default: cellWidth = unitCellWidth; break;
            }
            return cellWidth;
        }

        private void DrawGrid(DrawingContext dc, double renderWidth, double verticalOffset, PriceRow row)
        {
            double x = 0;

            for (int i = 0; i < columns.Count; i++)
            {
                ColumnDefinition colDef = columns[i];
                double cellWidth = CalculateCellWidth(colDef.ColumnSize, renderWidth);
                Brush cellColor = colDef.BackgroundColor;
                Rect rect = new Rect(x, verticalOffset, cellWidth, SuperDom.ActualRowHeight);

                // Create a guidelines set
                GuidelineSet guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(rect.Left + halfPenWidth);
                guidelines.GuidelinesX.Add(rect.Right + halfPenWidth);
                guidelines.GuidelinesY.Add(rect.Top + halfPenWidth);
                guidelines.GuidelinesY.Add(rect.Bottom + halfPenWidth);
                dc.PushGuidelineSet(guidelines);

                // BID column color
                if ((colDef.ColumnType == ColumnType.BID ||
                    colDef.ColumnType == ColumnType.BID_CHANGE) &&
                    row.Price < SuperDom.CurrentLast)
                {
                    cellColor = BidColumnColor;
                }

                // ASK column color
                if ((colDef.ColumnType == ColumnType.ASK ||
                    colDef.ColumnType == ColumnType.ASK_CHANGE) &&
                    row.Price > SuperDom.CurrentLast)
                {
                    cellColor = AskColumnColor;
                }

                // Position based row color
                if (SuperDom.Position != null && row.IsEntry)
                {
                    if (SuperDom.Position.MarketPosition == MarketPosition.Long)
                    {
                        cellColor = LongPositionRowColor;
                    }
                    else
                    {
                        cellColor = ShortPositionRowColor;
                    }
                }

                // Indicate current price
                if (row.Price == SuperDom.CurrentLast && colDef.ColumnType != ColumnType.OF_STRENGTH)
                {
                    cellColor = CurrentPriceRowColor;
                }

                // Headers row
                if (row.Price == SuperDom.UpperPrice) {
                    cellColor = HeaderRowColor;
                }

                // If in position, show MFE and MAE
                if (SuperDom.Position != null)
                {
                    Position position = SuperDom.Position;
                    // TODO:
                    ////////////////////////////////////////////////////////////////////////////////////////////                    
                }

                // Draw grid rectangle
                dc.DrawRectangle(cellColor, null, rect);
                dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
                dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
                
                if (row.Price == SuperDom.UpperPrice) {
                    FormattedText header = FormatText(colDef.ColumnType.GetEnumDescription(), renderWidth, HeadersTextColor, TextAlignment.Left);
                    dc.DrawText(header, new Point(rect.Left + 10, verticalOffset + (SuperDom.ActualRowHeight - header.Height) / 2));
                }
                else {
                    if (colDef.ColumnType == ColumnType.VOLUME && colDef.Text != null)
                    {
                        long volumeAtPrice = colDef.Text.Text == null ? 0 : long.Parse(colDef.Text.Text);
                        double totalWidth = cellWidth * ((double)volumeAtPrice / maxVolume);
                        double volumeWidth = totalWidth == cellWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - halfPenWidth;

                        if (volumeWidth >= 0)
                        {
                            double xc = x + (cellWidth - volumeWidth);
                            dc.DrawRectangle(VolumeColor, null, new Rect(xc, verticalOffset + halfPenWidth, volumeWidth, rect.Height - gridPen.Thickness));
                        }

                        if (!DisplayVolumeText)
                        {
                            colDef.Text = null;
                        }
                    }
                    // Write the column text
                    if (colDef.Text != null) 
                    {
                        dc.DrawText(colDef.Text, new Point(colDef.Text.TextAlignment == TextAlignment.Left ? rect.Left + 5 : rect.Right - 5, verticalOffset + (SuperDom.ActualRowHeight - colDef.Text.Height) / 2));
                    }
                }

                dc.Pop();

                x += cellWidth;
            }
        }

        #region Text utils
        private FormattedText FormatText(string text, double renderWidth, Brush color, TextAlignment alignment)
        {
            return new FormattedText(text.ToString(culture), culture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, color, pixelsPerDip) { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment };
        }

        private void Print(string s)
        {
            ind.Print(s);
        }
        #endregion        

        #region Column Text Calculation

        private FormattedText GenerateVolumeText(double renderWidth, double price)
        {
            long totalVolume = orderFlow.GetVolumeAtPrice(price);
            return totalVolume > 0 ? FormatText(totalVolume.ToString(), renderWidth, VolumeTextColor, TextAlignment.Right) : null;
        }
        
        private FormattedText CalculateOFStrength(double renderWidth, double price)
        {
            string text = "██";
            Brush color = Brushes.Transparent;

            OFStrength ofStrength = orderFlow.CalculateOrderFlowStrength(SuperDom.CurrentLast, SuperDom.Instrument.MasterInstrument.TickSize);

            double buyStrength = ofStrength.buyStrength;
            double sellStrength = ofStrength.sellStrength;

            double totalRows = Convert.ToDouble(SuperDom.Rows.Count);
            int nBuyRows = Convert.ToInt16(totalRows * (buyStrength/100.00));
            int nSellRows = Convert.ToInt16(totalRows - nBuyRows);

            if ((SuperDom.UpperPrice - price) < nSellRows*SuperDom.Instrument.MasterInstrument.TickSize)
            {
                if (sellStrength >= OrderFlowStrengthThreshold)
                {
                    color = Brushes.Red;
                }
                else
                {
                    color = Brushes.Maroon;
                }
                
                text = (nSellRows-1 == (SuperDom.UpperPrice - price)/SuperDom.Instrument.MasterInstrument.TickSize) ? Math.Round(sellStrength, 0, MidpointRounding.AwayFromZero).ToString() : text;
            }
            else
            {
                if (buyStrength >= OrderFlowStrengthThreshold)
                {
                    color = Brushes.Lime;
                }
                else {
                    color = Brushes.DarkGreen;
                }
                text = (nBuyRows-1 == (price -SuperDom.LowerPrice)/SuperDom.Instrument.MasterInstrument.TickSize) ? Math.Round(buyStrength, 0, MidpointRounding.AwayFromZero).ToString() : text;
            }

            return FormatText(string.Format("{0}", text), renderWidth, color, TextAlignment.Right);
        }

        private FormattedText GenerateSessionBuysText(double renderWidth, double buyPrice)
        {
            Brush brush = SessionBuyTextColor;

            double sellPrice = buyPrice - SuperDom.Instrument.MasterInstrument.TickSize;

            long totalBuys = orderFlow.GetBuyVolumeAtPrice(buyPrice);
            long totalSells = orderFlow.GetSellVolumeAtPrice(sellPrice);

            if (totalBuys > 0 && totalSells > 0 && totalBuys > totalSells * ImbalanceFactor)
            {
                brush = BuyImbalanceColor;
            }

            if (totalBuys != 0)
            {
                return FormatText(totalBuys.ToString(), renderWidth, brush, TextAlignment.Right);
            }

            return null;
        }

        private FormattedText GenerateSessionSellsText(double renderWidth, double sellPrice)
        {
            Brush brush = SessionSellTextColor;

            double buyPrice = sellPrice + SuperDom.Instrument.MasterInstrument.TickSize;

            long totalBuys = orderFlow.GetBuyVolumeAtPrice(buyPrice);
            long totalSells = orderFlow.GetSellVolumeAtPrice(sellPrice);

            if (totalBuys > 0 && totalSells > 0 && totalSells > totalBuys * ImbalanceFactor)
            {
                brush = SellImbalanceColor;
            }

            if (totalSells != 0)
            {
                return FormatText(totalSells.ToString(), renderWidth, brush, TextAlignment.Left);
            }

            return null;
        }

        private FormattedText GenerateAskChangeText(double renderWidth, double price)
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = SuperDom.MarketDepth.Asks;
            ladder = ladder.Count > BidAskRows ? ladder.GetRange(0, BidAskRows) : ladder;

            long currentSize = getBidAskSize(price, ladder);
            long change = orderFlow.GetAskChange(price, currentSize); 

            if (currentSize > 0 && change != 0)
            {
                Brush color = change > 0 ? BidAskAddColor : BidAskRemoveColor;
                return FormatText(change.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateBidChangeText(double renderWidth, double price)
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = SuperDom.MarketDepth.Bids;
            ladder = ladder.Count > BidAskRows ? ladder.GetRange(0, BidAskRows) : ladder;

            long currentSize = getBidAskSize(price, ladder);
            long change = orderFlow.GetBidChange(price, currentSize);

            if (currentSize > 0 && change != 0)
            {
                Brush color = change > 0 ? BidAskAddColor : BidAskRemoveColor;
                return FormatText(change.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }
        
        private FormattedText GenerateAskText(double renderWidth, double price)
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = SuperDom.MarketDepth.Asks;
            ladder = ladder.Count > BidAskRows ? ladder.GetRange(0, BidAskRows) : ladder;

            long currentSize = getBidAskSize(price, ladder);

            if (currentSize > 0) return FormatText(currentSize.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);

            return null;
        }

        private FormattedText GenerateBidText(double renderWidth, double price)
        {
            List<NinjaTrader.Gui.SuperDom.LadderRow> ladder = SuperDom.MarketDepth.Bids;
            ladder = ladder.Count > BidAskRows ? ladder.GetRange(0, BidAskRows) : ladder;

            long currentSize = getBidAskSize(price, ladder);

            if (currentSize > 0) return FormatText(currentSize.ToString(), renderWidth, DefaultTextColor, TextAlignment.Right);

            return null;
        }

        private FormattedText GenerateSlidingWindowBuysText(double renderWidth, double price)
        {
            double sellPrice = price - SuperDom.Instrument.MasterInstrument.TickSize;

            Trade buys = orderFlow.GetBuysInSlidingWindow(price);
            if (buys != null)
            {
                Brush color = SuperDom.CurrentAsk == price ? LastTradeColor : DefaultTextColor;
                
                Trade sells = orderFlow.GetSellsInSlidingWindow(sellPrice);
                if (sells != null && buys.Size > sells.Size * ImbalanceFactor)
                {
                    color = BuyImbalanceColor;
                }

                return FormatText(buys.Size.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateSlidingWindowSellsText(double renderWidth, double price)
        {
            double buyPrice = price + SuperDom.Instrument.MasterInstrument.TickSize;

            Trade sells = orderFlow.GetSellsInSlidingWindow(price);
            if (sells != null)
            {
                Brush color = SuperDom.CurrentBid == price ? LastTradeColor : DefaultTextColor;

                Trade buys = orderFlow.GetBuysInSlidingWindow(buyPrice);
                if (buys != null && sells.Size > buys.Size * ImbalanceFactor)
                {
                    color = SellImbalanceColor;
                }

                return FormatText(sells.Size.ToString(), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastBuyText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastBuySize(price);

            if (size > 0)
            {
                orderFlow.RemoveLastBuy(price);
                return FormatText(size.ToString(), renderWidth, BuyTextColor, TextAlignment.Right);
            }
            return null;
        }

        private FormattedText GenerateLastSellText(double renderWidth, double price)
        {
            long size = orderFlow.GetLastSellSize(price);

            if (size > 0)
            {
                orderFlow.RemoveLastSell(price);
                return FormatText(size.ToString(), renderWidth, SellTextColor, TextAlignment.Right);
            }

            return null;
        }

        private long getBidAskSize(double price, List<NinjaTrader.Gui.SuperDom.LadderRow> ladder)
        {
            foreach (NinjaTrader.Gui.SuperDom.LadderRow row in ladder)
            {
                if (row.Price == price)
                {
                    return row.Volume;
                }
            }
            return 0;
        }

        private FormattedText GetPrice(double renderWidth, double price)
        {
            return FormatText(SuperDom.Instrument.MasterInstrument.FormatPrice(price), renderWidth, Brushes.Gray, TextAlignment.Right);
        }

        private FormattedText CalculatePL(double renderWidth, double price)
        {
            FormattedText fpl = null;

            // Print P/L if position is open
            if (SuperDom.Position != null)
            {
                double pl = 0;

                if (ProfitLossType == PLType.Currency)
                {
                    pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity);
                    Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                    fpl = FormatText(string.Format("{0:0.00}", pl), renderWidth, color, TextAlignment.Right);
                }
                else
                {
                    pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Ticks, price);
                    Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                    fpl = FormatText(string.Format("{0}", Convert.ToInt32(pl)), renderWidth, color, TextAlignment.Right);
                }

                return fpl;
            }
            return fpl;
        }

        private FormattedText CalculateTotalPL(double renderWidth, double price)
        {
            // Print Total P/L if position is open
            if (SuperDom.Position != null)
            {
                double pl = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity) + SuperDom.Account.Get(AccountItem.RealizedProfitLoss, SelectedCurrency);
                Brush color = (pl > 0 ? (price == SuperDom.CurrentLast ? Brushes.Lime : Brushes.Green) : (pl < 0 ? (price == SuperDom.CurrentLast ? Brushes.Red : Brushes.Firebrick) : Brushes.DimGray));
                return FormatText(string.Format("{0:0.00}", pl), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }


        private FormattedText CalculateAccValue(double renderWidth, double price)
        {
            // Print Account Value if position is open
            if (SuperDom.Position != null)
            {
                double accVal = SuperDom.Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price) - (commissionRT * SuperDom.Position.Quantity) + SuperDom.Account.Get(AccountItem.CashValue, SelectedCurrency);
                Brush color = Brushes.DimGray;
                return FormatText(string.Format("{0:0.00}", accVal), renderWidth, color, TextAlignment.Right);
            }
            return null;
        }

        #endregion

        #region Event Handlers
        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                orderFlow.ClearSlidingWindow();

                OnPropertyChanged();
            }
        }
        #endregion

        #region Properties
        // =========== Price Column

        [NinjaScriptProperty]
        [Display(Name = "Price", Description = "Display price.", Order = 1, GroupName = "Price and Volume Columns")]
        public bool DisplayPrice
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram", Description = "Display volume.", Order = 2, GroupName = "Price and Volume Columns")]
        public bool DisplayVolume
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram Text", Description = "Display volume text.", Order = 3, GroupName = "Price and Volume Columns")]
        public bool DisplayVolumeText
        { get; set; }


        // =========== Buy / Sell Columns

        [NinjaScriptProperty]
        [Display(Name = "Trades (Sliding Window)", Description = "Display trades in a sliding window.", Order = 1, GroupName = "Buy / Sell Columns")]
        public bool DisplayLastSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Buy/Sell (Sliding Window)", Description = "Display Buys/Sells in a sliding window.", Order = 2, GroupName = "Buy / Sell Columns")]
        public bool DisplaySlidingWindowBuysSells
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Buy/Sell Sliding Window (Seconds)", Description = "Sliding Window (in seconds) used for displaying trades.", Order = 3, GroupName = "Buy / Sell Columns")]
        public int TradeSlidingWindowSeconds
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Buys / Sells", Description = "Display the total buys and sells columns.", Order = 4, GroupName = "Buy / Sell Columns")]
        public bool DisplaySessionBuysSells
        { get; set; }


        // =========== Visual

        [NinjaScriptProperty]
        [Display(Name = "Color Scheme", Description = "Color Scheme.", Order = 1, GroupName = "Visual")]
        public ColorScheme ColorSchemeType
        { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Default Text Color", Description = "Default text color.", Order = 2, GroupName = "Visual")]
        public Brush DefaultTextColor
        { get; set; }

        [Browsable(false)]
        public string DefaultTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(DefaultTextColor); }
            set { DefaultTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buys Text Color", Description = "Buys Text Color.", Order = 3, GroupName = "Visual")]
        public Brush BuyTextColor
        { get; set; }

        [Browsable(false)]
        public string BuyTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyTextColor); }
            set { BuyTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sells Text Color", Description = "Sells Text Color.", Order = 4, GroupName = "Visual")]
        public Brush SellTextColor
        { get; set; }

        [Browsable(false)]
        public string SellTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellTextColor); }
            set { SellTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Buys Text Color", Description = "Session Buys Text Color.", Order = 5, GroupName = "Visual")]
        public Brush SessionBuyTextColor
        { get; set; }

        [Browsable(false)]
        public string SessionBuyTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionBuyTextColor); }
            set { SessionBuyTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Sells Text Color", Description = "Session Sells Text Color.", Order = 6, GroupName = "Visual")]
        public Brush SessionSellTextColor
        { get; set; }

        [Browsable(false)]
        public string SessionSellTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SessionSellTextColor); }
            set { SessionSellTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Buy Imbalance Text Color", Description = "Buy Imbalance Text Color.", Order = 7, GroupName = "Visual")]
        public Brush BuyImbalanceColor
        { get; set; }

        [Browsable(false)]
        public string BuyImbalanceColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BuyImbalanceColor); }
            set { BuyImbalanceColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Sell Imbalance Text Color", Description = "Sell Imbalance Text Color.", Order = 8, GroupName = "Visual")]
        public Brush SellImbalanceColor
        { get; set; }

        [Browsable(false)]
        public string SellImbalanceColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(SellImbalanceColor); }
            set { SellImbalanceColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Volume Histogram Color", Description = "Volume Histogram Color.", Order = 9, GroupName = "Visual")]
        public Brush VolumeColor
        { get; set; }

        [Browsable(false)]
        public string VolumeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(VolumeColor); }
            set { VolumeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Volume Text Color", Description = "Volume Text Color.", Order = 10, GroupName = "Visual")]
        public Brush VolumeTextColor
        { get; set; }

        [Browsable(false)]
        public string VolumeTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(VolumeTextColor); }
            set { VolumeTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask (Add) Text Color", Description = "Bid/Ask orders added.", Order = 11, GroupName = "Visual")]
        public Brush BidAskAddColor
        { get; set; }

        [Browsable(false)]
        public string BidAskAddColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidAskAddColor); }
            set { BidAskAddColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask (Remove) Text Color", Description = "Bid/Ask orders removed.", Order = 12, GroupName = "Visual")]
        public Brush BidAskRemoveColor
        { get; set; }

        [Browsable(false)]
        public string BidAskRemoveColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(BidAskRemoveColor); }
            set { BidAskRemoveColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Last Trade Text Color", Description = "Last trade text color.", Order = 13, GroupName = "Visual")]
        public Brush LastTradeColor
        { get; set; }

        [Browsable(false)]
        public string LastTradeColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(LastTradeColor); }
            set { LastTradeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Header Row Color", Description = "Header row color.", Order = 14, GroupName = "Visual")]
        public Brush HeaderRowColor
        { get; set; }

        [Browsable(false)]
        public string HeaderRowColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HeaderRowColor); }
            set { HeaderRowColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }        

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Headers Text Color", Description = "Headers text color.", Order = 15, GroupName = "Visual")]
        public Brush HeadersTextColor
        { get; set; }

        [Browsable(false)]
        public string HeadersTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(HeadersTextColor); }
            set { HeadersTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }        


        // =========== P/L Columns


        [NinjaScriptProperty]
        [Display(Name = "P/L", Description = "Display P/L.", Order = 1, GroupName = "P/L Columns")]
        public bool DisplayPL
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "P/L display type", Description = "P/L display type.", Order = 2, GroupName = "P/L Columns")]
        public PLType ProfitLossType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "P/L display currency", Description = "P/L display currency.", Order = 3, GroupName = "P/L Columns")]
        public Currency SelectedCurrency
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session P/L", Description = "Display session P/L.", Order = 4, GroupName = "P/L Columns")]
        public bool DisplaySessionPL
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Cash Value", Description = "Display account value.", Order = 5, GroupName = "P/L Columns")]
        public bool DisplayAccountValue
        { get; set; }

        // =========== Bid / Ask Columns

        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask", Description = "Display the bid/ask.", Order = 1, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAsk
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Bid/Ask Rows", Description = "Bid/Ask Rows", Order = 2, GroupName = "Bid / Ask Columns")]
        public int BidAskRows
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bid/Ask Changes", Description = "Display the changes in bid/ask.", Order = 3, GroupName = "Bid / Ask Columns")]
        public bool DisplayBidAskChange
        { get; set; }

        

        // =========== OrderFlow Parameters

        [NinjaScriptProperty]
        [Range(1.5, double.MaxValue)]
        [Display(Name = "Imbalance Factor", Description = "Imbalance Factor", Order = 1, GroupName = "Order Flow Parameters")]
        public double ImbalanceFactor
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Overall OrderFlow Strength Bar", Description = "Display the overall OrderFlow strength bar, including data from imbalances.", Order = 2, GroupName = "Order Flow Parameters")]
        public bool DisplayOrderFlowStrengthBar
        { get; set; }

        [NinjaScriptProperty]
        [Range(51, 100)]
        [Display(Name = "OrderFlow Strength Threshold", Description = "Threshold for strength bar to light up (51-100)", Order = 3, GroupName = "Order Flow Parameters")]
        public int OrderFlowStrengthThreshold
        { get; set; }
        
        #endregion

    }
}
