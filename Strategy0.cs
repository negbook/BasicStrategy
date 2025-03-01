// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace Main
{
    /// <summary>
    /// An example of blank strategy. Add your code, compile it and run via Strategy Runner panel in the assigned trading terminal.
    /// Information about API you can find here: http://api.quantower.com
    /// Code samples: https://github.com/Quantower/Examples 
    /// </summary>
    public sealed class Strategy0 : Strategy, ICurrentAccount, ICurrentSymbol
    {
        

        /// <summary>
        /// Account to place orders
        /// </summary>
        [InputParameter("Account", 1)]
        public Account CurrentAccount { get; set; }

        [InputParameter("Symbol", 0)]
        public Symbol CurrentSymbol { get; set; }


        /// <summary>
        /// Period to load history
        /// </summary>
        [InputParameter("Period", 3)]
        public Period Period { get; set; }

        /// <summary>
        /// Start point to load history
        /// </summary>
        [InputParameter("Start point", 4)]
        public DateTime StartPoint { get; set; }

        public override string[] MonitoringConnectionsIds => new string[] { this.CurrentSymbol?.ConnectionId, this.CurrentAccount?.ConnectionId };

        
        private bool waitOpenPosition;
        private bool waitClosePosition;

        /// <summary>
        /// Strategy's constructor. Contains general information: name, description etc. 
        /// </summary>
        public Strategy0()
            : base()
        {
            // Defines strategy's name and description.
            this.Name = "Strategy0";
            this.Description = "My strategy's annotation";
            this.Period = Period.MIN5;
            this.StartPoint = Core.TimeUtils.DateTimeUtcNow.AddDays(-100);
        }

        /// <summary>
        /// This function will be called after running a strategy
        /// </summary>
        protected override void OnRun()
        {
            // 使用TradingUtils檢查賬戶和交易品種
            Symbol updatedSymbol;
            Account updatedAccount;
            if (!TradingUtils.CheckAccountAndSymbol(this.CurrentSymbol, this.CurrentAccount, this.Log, 
                                                   out updatedSymbol, out updatedAccount))
            {
                this.StrategyQuit("Seems like the symbol or account is not valid. Please check the input parameters.");
                return;
            }
            
            // 更新當前使用的Symbol和Account
            this.CurrentSymbol = updatedSymbol;
            this.CurrentAccount = updatedAccount;

            // 初始化交易工具類並設置事件處理
            TradingUtils.Init(
                Core,
                this.CurrentSymbol,
                this.CurrentAccount,
                this.Log,
                OnStrategyPositionAdded,
                OnStrategyPositionRemoved,
                OnAllPositionsClosed,
                OnOrderRefused,
                OnTradeAdded
            );
            
            // 初始化訂單類型
            if (!TradingUtils.InitOrderTypes("Market", "Limit", "Stop"))
            {
                this.Log("Failed to initialize required order types", StrategyLoggingLevel.Error);
                return;
            }
            
            // 檢查市場訂單類型是否有效
            if (!TradingUtils.IsOrderTypeValid("Market"))
            {
                this.Log("Connection of selected symbol has not support market orders", StrategyLoggingLevel.Error);
                return;
            }

            // 添加歷史數據，由TradingUtils統一管理
            TradingUtils.AddHistory(
                this.CurrentSymbol,
                this.Period, 
                this.CurrentSymbol.HistoryType, 
                this.StartPoint,
                this.OnUpdate,
                this.OnNewBar
            );
        }

        /// <summary>
        /// This function will be called after stopping a strategy
        /// </summary>
        protected override void OnStop()
        {
            try
            {
                // 清理所有歷史數據
                TradingUtils.CleanupHistories();
                
                // 清理交易工具類
                TradingUtils.Cleanup(Core);

                // 重置交易統計數據
                TradingUtils.ResetTradeStatistics();
            }
            catch (Exception ex)
            {
                // 記錄異常但不中斷清理流程
                this.Log($"Error during cleanup: {ex.Message}", StrategyLoggingLevel.Error);
            }
            finally
            {
                // 確保基類的OnStop總是被調用
                base.OnStop();
            }
        }

        private void OnUpdate(object sender, HistoryEventArgs e)
        {
            // 處理歷史數據更新事件
            
        }

        private void OnNewBar(object sender, HistoryEventArgs e)
        {
            // 處理新Bar事件
            // 如果需要獲取歷史數據，可以使用 TradingUtils.GetSymbolHistory
            var history = TradingUtils.GetSymbolHistory(this.CurrentSymbol);
            if (history != null)
            {
                // 使用歷史數據
                var lastBar = history[1] as HistoryItemBar;
                if (lastBar != null)
                {
                    this.Log($"[{this.CurrentSymbol.Name}] New Bar Update - Last bar - Open: {lastBar.Open}, High: {lastBar.High}, Low: {lastBar.Low}, Close: {lastBar.Close}", StrategyLoggingLevel.Trading);
                    
                    // 執行交易邏輯
                    ExecTradingLogic(history);
                }
            }
        }

        /// <summary>
        /// 執行交易邏輯，根據當前市場情況決定是否開倉或平倉
        /// </summary>
        /// <param name="history">歷史數據</param>
        private void ExecTradingLogic(HistoricalData history)
        {
            // 檢查是否有正在等待的交易操作
            if (this.waitOpenPosition || this.waitClosePosition)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Already waiting for position operation to complete. waitOpen: {this.waitOpenPosition}, waitClose: {this.waitClosePosition}", StrategyLoggingLevel.Trading);
                return;
            }

            // 獲取當前倉位狀態
            int longCount = TradingUtils.LongPositionsCount;
            int shortCount = TradingUtils.ShortPositionsCount;
            
            // 檢查是否有平倉條件
            if (longCount > 0 || shortCount > 0)
            {
                // 如果有倉位，檢查是否應該平倉
                if (ShouldClosePosition(history))
                {
                    StrategyClosePosition();
                }
            }
            // 檢查是否有開倉條件
            else if (longCount == 0 && shortCount == 0)
            {
                // 如果沒有倉位，檢查是否應該開倉
                Side? side = ShouldOpenPosition(history);
                if (side.HasValue)
                {
                    StrategyOpenPosition(side.Value);
                }
            }

            
        }

        /// <summary>
        /// 判斷是否應該開倉
        /// </summary>
        /// <param name="history">歷史數據</param>
        /// <returns>開倉方向，如果不應該開倉則返回null</returns>
        private Side? ShouldOpenPosition(HistoricalData history)
        {
            if (history == null || history.Count < 3)
                return null;

            // 獲取最近的三根K線
            var bar1 = history[1] as HistoryItemBar; // 最近的已完成K線
            var bar2 = history[2] as HistoryItemBar; // 前一根K線
            var bar3 = history[3] as HistoryItemBar; // 再前一根K線

            if (bar1 == null || bar2 == null || bar3 == null)
                return null;

            // 簡單策略：三根連續上漲K線則做多，三根連續下跌K線則做空
            // 這只是一個示例，您可以根據自己的策略修改這裡的邏輯
            
            bool isThreeConsecutiveUp = bar1.Close > bar1.Open && bar2.Close > bar2.Open && bar3.Close > bar3.Open;
            bool isThreeConsecutiveDown = bar1.Close < bar1.Open && bar2.Close < bar2.Open && bar3.Close < bar3.Open;

            if (isThreeConsecutiveUp)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Detected three consecutive bullish bars, considering long position", StrategyLoggingLevel.Trading);
                return Side.Buy;
            }
            else if (isThreeConsecutiveDown)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Detected three consecutive bearish bars, considering short position", StrategyLoggingLevel.Trading);
                return Side.Sell;
            }

            return null;
        }

        /// <summary>
        /// 判斷是否應該平倉
        /// </summary>
        /// <param name="history">歷史數據</param>
        /// <returns>如果應該平倉則返回true，否則返回false</returns>
        private bool ShouldClosePosition(HistoricalData history)
        {
            if (history == null || history.Count < 2)
                return false;

            // 獲取最近的兩根K線
            var bar1 = history[1] as HistoryItemBar; // 最近的已完成K線
            var bar2 = history[2] as HistoryItemBar; // 前一根K線

            if (bar1 == null || bar2 == null)
                return false;

            // 檢查當前倉位方向
            bool hasLongPosition = TradingUtils.LongPositionsCount > 0;
            bool hasShortPosition = TradingUtils.ShortPositionsCount > 0;

            // 簡單策略：如果持有多頭倉位且最近K線為陰線，則平倉
            // 如果持有空頭倉位且最近K線為陽線，則平倉
            // 這只是一個示例，您可以根據自己的策略修改這裡的邏輯
            
            if (hasLongPosition && bar1.Close < bar1.Open)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Detected bearish bar while in long position, considering closing", StrategyLoggingLevel.Trading);
                return true;
            }
            else if (hasShortPosition && bar1.Close > bar1.Open)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Detected bullish bar while in short position, considering closing", StrategyLoggingLevel.Trading);
                return true;
            }

            

            return false;
        }

        /// <summary>
        /// 開倉操作
        /// </summary>
        /// <param name="side">開倉方向</param>
        private void StrategyOpenPosition(Side side)
        {
            // 設置等待狀態
            this.waitOpenPosition = true;
            
            // 獲取市場訂單類型ID
            string orderTypeId = TradingUtils.GetOrderType("Market");
            if (string.IsNullOrEmpty(orderTypeId))
            {
                this.Log($"[{this.CurrentSymbol.Name}] Market order type not available", StrategyLoggingLevel.Error);
                this.waitOpenPosition = false;
                return;
            }

            // 計算下單數量（這裡使用固定數量，您可以根據需要修改）
            double quantity = 1.0;
            
            // 創建下單請求
            var request = new PlaceOrderRequestParameters
            {
                Symbol = this.CurrentSymbol,
                Account = this.CurrentAccount,
                Side = side,
                OrderTypeId = orderTypeId,
                Quantity = quantity,
                TimeInForce = TimeInForce.GTC,
                // 可以根據需要添加止損止盈
                // StopLoss = SlTpHolder.CreateSL(price * 0.98),  // 2%止損
                // TakeProfit = SlTpHolder.CreateTP(price * 1.05) // 5%止盈
            };

            // 記錄下單信息
            this.Log($"[{this.CurrentSymbol.Name}] Placing {side} order, Quantity: {quantity}", StrategyLoggingLevel.Trading);
            
            // 發送下單請求
            var result = Core.Instance.PlaceOrder(request);
            
            // 檢查下單結果
            if (result != null && result.Status == TradingOperationResultStatus.Failure)
            {
                this.Log($"[{this.CurrentSymbol.Name}] Order placement failed: {result.Message}", StrategyLoggingLevel.Error);
                this.waitOpenPosition = false;
            }
            else
            {
                this.Log($"[{this.CurrentSymbol.Name}] Order placement sent successfully", StrategyLoggingLevel.Trading);
                // waitOpenPosition 狀態將在 OnStrategyPositionAdded 回調中重置
            }
        }

        /// <summary>
        /// 關閉所有倉位
        /// </summary>
        private void StrategyClosePosition()
        {
            // 獲取所有當前倉位
            var positions = TradingUtils.GetPositions(this.CurrentAccount, this.CurrentSymbol);
            if (positions.Length == 0)
            {
                this.Log($"[{this.CurrentSymbol.Name}] No positions to close", StrategyLoggingLevel.Trading);
                return;
            }

            // 設置等待狀態
            this.waitClosePosition = true;
            
            // 關閉每個倉位
            foreach (var position in positions)
            {
                // 記錄平倉信息
                this.Log($"[{this.CurrentSymbol.Name}] Closing position - Side: {position.Side}, OpenPrice: {position.OpenPrice}, Quantity: {position.Quantity}", StrategyLoggingLevel.Trading);
                
                // 發送平倉請求
                var result = Core.Instance.ClosePosition(position);
                
                // 檢查平倉結果
                if (result != null && result.Status == TradingOperationResultStatus.Failure)
                {
                    this.Log($"[{this.CurrentSymbol.Name}] Position closing failed: {result.Message}", StrategyLoggingLevel.Error);
                    // 不重置waitClosePosition，因為我們可能有多個倉位需要關閉
                }
                else
                {
                    this.Log($"[{this.CurrentSymbol.Name}] Position closing request sent successfully", StrategyLoggingLevel.Trading);
                    // waitClosePosition 狀態將在 OnAllPositionsClosed 回調中重置
                }
            }
        }

        /// <summary>
        /// 處理已確認為本策略的新增倉位
        /// </summary>
        /// <param name="position">匹配的倉位</param>
        private void OnStrategyPositionAdded(Position position)
        {
            // 記錄實際的開倉價格
            double entryPrice = TradingUtils.GetEntryPrice(position);
            this.Log($"[{this.CurrentSymbol.Name}] Position opened at price: {entryPrice}", StrategyLoggingLevel.Trading);

            // 更新等待狀態
            this.waitOpenPosition = false;
            this.Log($"[{this.CurrentSymbol.Name}] WaitOpen set to false", StrategyLoggingLevel.Trading);
        }
        
        /// <summary>
        /// 處理已確認為本策略的移除倉位
        /// </summary>
        /// <param name="position">匹配的倉位</param>
        private void OnStrategyPositionRemoved(Position position)
        {
            // 處理單個倉位關閉邏輯
            this.Log($"[{this.CurrentSymbol.Name}] Position closed - Symbol: {position.Symbol.Name}, Side: {position.Side}", StrategyLoggingLevel.Trading);
            

        }
        
        /// <summary>
        /// 當所有倉位關閉時的處理
        /// </summary>
        private void OnAllPositionsClosed()
        {
            this.waitClosePosition = false;
            this.ResetAllRecords();
        }

        /// <summary>
        /// 處理新增交易
        /// </summary>
        /// <param name="trade">交易對象</param>
        private void OnTradeAdded(Trade trade)
        {
            // 盈虧統計已由TradingUtils處理，這裡可以添加額外邏輯
            this.Log($"[{this.CurrentSymbol.Name}] Trade event - ID: {trade.Id}", StrategyLoggingLevel.Trading);
        }

        private void ResetAllRecords()
        {
            this.waitOpenPosition = false;
            this.waitClosePosition = false;
            
            
        }

        /// <summary>
        /// Use this method to provide run time information about your strategy. You will see it in StrategyRunner panel in trading terminal
        /// </summary>
        [Obsolete("Use OnInitializeMetrics() method to initialize System.Diagnostics.Metrics")]
        protected override List<StrategyMetric> OnGetMetrics()
        {
            List<StrategyMetric> result = base.OnGetMetrics();
            
            // 創建臨時列表存儲所有指標
            var metrics = new List<(string name, string value)>();
            
            // 按原順序添加指標到臨時列表
            metrics.Add(("Symbol", this.CurrentSymbol.Name));
            metrics.Add(("Account", this.CurrentAccount.Name));
            metrics.Add(("Period", this.Period.ToString()));

            // 從TradingUtils直接獲取倉位計數
            metrics.Add(("Long positions", TradingUtils.LongPositionsCount.ToString()));
            metrics.Add(("Short positions", TradingUtils.ShortPositionsCount.ToString()));
            
            // 從TradingUtils獲取盈虧統計
            metrics.Add(("Net P/L", TradingUtils.TotalNetPl.ToString("F2")));
            metrics.Add(("Gross P/L", TradingUtils.TotalGrossPl.ToString("F2")));
            metrics.Add(("Total Fee", TradingUtils.TotalFee.ToString("F2")));
            
            // 交易統計
            metrics.Add(("Total Trades", TradingUtils.TotalTrades.ToString()));
            metrics.Add(("Total Long Trades", TradingUtils.TotalLongTrades.ToString()));
            metrics.Add(("Total Short Trades", TradingUtils.TotalShortTrades.ToString()));
            
            // 翻轉指標列表順序
            metrics.Reverse();
            
            // 將翻轉後的指標添加到結果中
            foreach (var (name, value) in metrics)
            {
                result.Add(name, value);
            }

            return result;
        }

        /// <summary>
        /// New method to initialize metrics using System.Diagnostics.Metrics
        /// This replaces the obsolete OnGetMetrics method
        /// </summary>
        protected override void OnInitializeMetrics(Meter meter)
        {
            base.OnInitializeMetrics(meter);
            
            // 创建一个计数器来记录所有指标
            var counter = meter.CreateCounter<int>("strategy_metrics");
            
            // 创建指标列表（与旧方法中相同）
            var metrics = new List<(string name, object value)>();
            
            // 添加所有指标（按照之前相同的顺序）
            metrics.Add(("Symbol", this.CurrentSymbol?.Name ?? "None"));
            metrics.Add(("Account", this.CurrentAccount?.Name ?? "None"));
            metrics.Add(("Period", this.Period.ToString()));
            metrics.Add(("Long positions", TradingUtils.LongPositionsCount));
            metrics.Add(("Short positions", TradingUtils.ShortPositionsCount));
            metrics.Add(("Net P/L", TradingUtils.TotalNetPl));
            metrics.Add(("Gross P/L", TradingUtils.TotalGrossPl));
            metrics.Add(("Total Fee", TradingUtils.TotalFee));
            metrics.Add(("Total Trades", TradingUtils.TotalTrades));
            metrics.Add(("Total Long Trades", TradingUtils.TotalLongTrades));
            metrics.Add(("Total Short Trades", TradingUtils.TotalShortTrades));
            
            // 反转列表顺序（与旧方法一致）
            metrics.Reverse();
            
            // 遍历添加所有指标
            foreach (var (name, value) in metrics)
            {
                if (value is int intValue)
                {
                    counter.Add(intValue, new KeyValuePair<string, object>("metric", name));
                }
                else if (value is double doubleValue)
                {
                    // 对于浮点数，乘以100以保留两位小数的精度
                    counter.Add((int)(doubleValue * 100), 
                               new KeyValuePair<string, object>("metric", name),
                               new KeyValuePair<string, object>("scale", 0.01));
                }
                else
                {
                    // 对于字符串或其他类型，使用1作为计数器值，并将实际值作为标签
                    counter.Add(1, 
                               new KeyValuePair<string, object>("metric", name),
                               new KeyValuePair<string, object>("value", value?.ToString() ?? "None"));
                }
            }
        }

        #region 次要函數
        /// <summary>
        /// 當訂單被拒絕時的處理
        /// </summary>
        /// <param name="errorMessage">錯誤信息</param>
        private void OnOrderRefused(string errorMessage)
        {
            this.StrategyQuit("Strategy have received refuse for trading action. It should be stopped");
        }

        private void StrategyQuit(string message = null)
        {
            if(message != null)
            {
                this.Log(message, StrategyLoggingLevel.Error);
            }
            this.Stop();
        }
        /// <summary>
        /// This function will be called after creating a strategy
        /// </summary>
        protected override void OnCreated()
        {
            // Add your code here
        }
        /// <summary>
        /// This function will be called after removing a strategy
        /// </summary>
        protected override void OnRemove()
        {
            // Add your code here
        }

        

        #endregion
    }
}