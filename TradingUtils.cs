// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace Main
{
    /// <summary>
    /// 通用交易工具類，提供各種與交易相關的實用函數
    /// </summary>
    public static class TradingUtils
    {
        // 策略環境配置
        private static Symbol _strategySymbol;
        private static Account _strategyAccount;
        private static Action<string, StrategyLoggingLevel> _logger;
        private static Action<Position> _onPositionAdded;
        private static Action<Position> _onPositionRemoved;
        private static Action _onAllPositionsClosed;
        private static Action<string> _onOrderRefused;
        private static Action<Trade> _onTradeAdded;
        
        // 倉位計數器
        private static int _longPositionsCount;
        private static int _shortPositionsCount;
        
        // 交易盈虧統計
        private static double _totalNetPl;
        private static double _totalGrossPl;
        private static double _totalFee;

        // 交易統計
        private static int _totalTrades;
        private static int _totalLongTrades;
        private static int _totalShortTrades;
        
        // 訂單類型管理
        private static Dictionary<string, string> _orderTypeIds = new Dictionary<string, string>();
        
        // 歷史數據管理
        private static List<(HistoricalData history, HistoryEventHandler updateHandler, HistoryEventHandler newBarHandler)> _histories = 
            new List<(HistoricalData, HistoryEventHandler, HistoryEventHandler)>();
        
        /// <summary>
        /// 獲取當前多頭倉位數量
        /// </summary>
        public static int LongPositionsCount => _longPositionsCount;
        
        /// <summary>
        /// 獲取當前空頭倉位數量
        /// </summary>
        public static int ShortPositionsCount => _shortPositionsCount;
        
        /// <summary>
        /// 獲取總淨盈虧
        /// </summary>
        public static double TotalNetPl => _totalNetPl;
        
        /// <summary>
        /// 獲取總毛盈虧
        /// </summary>
        public static double TotalGrossPl => _totalGrossPl;
        
        /// <summary>
        /// 獲取總手續費
        /// </summary>
        public static double TotalFee => _totalFee;

        /// <summary>       
        /// 獲取總交易數量
        /// </summary>
        public static int TotalTrades => _totalTrades;

        /// <summary>
        /// 獲取總多頭交易數量
        /// </summary>
        public static int TotalLongTrades => _totalLongTrades;

        /// <summary>
        /// 獲取總空頭交易數量
        /// </summary>
        public static int TotalShortTrades => _totalShortTrades;

        /// <summary>
        /// 初始化交易工具類，設置全局事件處理
        /// </summary>
        /// <param name="core">核心對象</param>
        /// <param name="symbol">策略使用的交易品種</param>
        /// <param name="account">策略使用的賬戶</param>
        /// <param name="logger">日誌記錄器</param>
        /// <param name="onPositionAdded">當倉位添加時的回調</param>
        /// <param name="onPositionRemoved">當倉位移除時的回調</param>
        /// <param name="onAllPositionsClosed">當所有倉位關閉時的回調</param>
        /// <param name="onOrderRefused">當訂單被拒絕時的回調</param>
        /// <param name="onTradeAdded">當交易產生時的回調</param>
        public static void Init(
            Core core,
            Symbol symbol,
            Account account,
            Action<string, StrategyLoggingLevel> logger,
            Action<Position> onPositionAdded = null,
            Action<Position> onPositionRemoved = null,
            Action onAllPositionsClosed = null,
            Action<string> onOrderRefused = null,
            Action<Trade> onTradeAdded = null)
        {
            // 保存策略環境
            _strategySymbol = symbol;
            _strategyAccount = account;
            _logger = logger;
            _onPositionAdded = onPositionAdded;
            _onPositionRemoved = onPositionRemoved;
            _onAllPositionsClosed = onAllPositionsClosed;
            _onOrderRefused = onOrderRefused;
            _onTradeAdded = onTradeAdded;
            
            // 初始化倉位計數
            _longPositionsCount = 0;
            _shortPositionsCount = 0;
            
            // 初始化盈虧統計
            _totalNetPl = 0;
            _totalGrossPl = 0;
            _totalFee = 0;

            // 初始化交易統計
            _totalTrades = 0;
            _totalLongTrades = 0;
            _totalShortTrades = 0;
            
            // 清空訂單類型字典
            _orderTypeIds.Clear();
            
            // 初始化歷史數據列表
            _histories = new List<(HistoricalData, HistoryEventHandler, HistoryEventHandler)>();
            
            // 獲取當前倉位數量
            if (symbol != null && account != null)
            {
                var positions = GetPositions(account, symbol);
                _longPositionsCount = positions.Count(x => x.Side == Side.Buy);
                _shortPositionsCount = positions.Count(x => x.Side == Side.Sell);
            }

            // 訂閱事件
            core.PositionAdded += Core_PositionAdded;
            core.PositionRemoved += Core_PositionRemoved;
            core.OrdersHistoryAdded += Core_OrdersHistoryAdded;
            core.TradeAdded += Core_TradeAdded;
        }

        /// <summary>
        /// 清理交易工具類，移除事件處理
        /// </summary>
        /// <param name="core">核心對象</param>
        public static void Cleanup(Core core)
        {
            // 檢查參數
            if (core != null)
            {
                // 取消訂閱事件
                core.PositionAdded -= Core_PositionAdded;
                core.PositionRemoved -= Core_PositionRemoved;
                core.OrdersHistoryAdded -= Core_OrdersHistoryAdded;
                core.TradeAdded -= Core_TradeAdded;
            }
            else
            {
                _logger?.Invoke("Warning: Core object is null during cleanup", StrategyLoggingLevel.Error);
            }

            // 清理所有歷史數據
            CleanupAllHistories();
            
            // 清除引用
            _strategySymbol = null;
            _strategyAccount = null;
            _logger = null;
            _onPositionAdded = null;
            _onPositionRemoved = null;
            _onAllPositionsClosed = null;
            _onOrderRefused = null;
            _onTradeAdded = null;
            
            // 重置倉位計數
            _longPositionsCount = 0;
            _shortPositionsCount = 0;
            
            // 重置盈虧統計
            _totalNetPl = 0;
            _totalGrossPl = 0;
            _totalFee = 0;

            // 重置交易統計
            _totalTrades = 0;
            _totalLongTrades = 0;
            _totalShortTrades = 0;
            
            // 清空訂單類型字典
            _orderTypeIds.Clear();
        }
        
        /// <summary>
        /// 添加歷史數據源，可選擇是否訂閱事件
        /// </summary>
        /// <param name="history">歷史數據對象</param>
        /// <param name="updateHandler">歷史數據更新事件處理器，如果為null則不訂閱</param>
        /// <param name="newBarHandler">新K線事件處理器，如果為null則不訂閱</param>
        /// <returns>添加的歷史數據對象</returns>
        public static HistoricalData AddHistory(HistoricalData history, HistoryEventHandler updateHandler = null, HistoryEventHandler newBarHandler = null)
        {
            if (history == null)
                return null;
                
            // 檢查歷史數據的Symbol是否為null
            if (history.Symbol == null)
            {
                _logger?.Invoke("Cannot add historical data with null Symbol", StrategyLoggingLevel.Error);
                return null;
            }
                
            // 訂閱事件（如果處理器不為null）
            if (updateHandler != null)
                history.HistoryItemUpdated += updateHandler;
                
            if (newBarHandler != null)
                history.NewHistoryItem += newBarHandler;
                
            // 將歷史數據添加到列表中
            _histories.Add((history, updateHandler, newBarHandler));
            
            _logger?.Invoke($"Added historical data: {history.Symbol.Name}, Items: {history.Count}", StrategyLoggingLevel.Info);
            
            return history;
        }
        
        /// <summary>
        /// 添加歷史數據源，直接通過參數創建並添加
        /// </summary>
        /// <param name="symbol">交易品種</param>
        /// <param name="period">K線周期</param>
        /// <param name="historyType">歷史數據類型</param>
        /// <param name="startPoint">起始時間點</param>
        /// <param name="updateHandler">歷史數據更新事件處理器，如果為null則不訂閱</param>
        /// <param name="newBarHandler">新K線事件處理器，如果為null則不訂閱</param>
        /// <returns>添加的歷史數據對象</returns>
        public static HistoricalData AddHistory(
            Symbol symbol, 
            Period period, 
            HistoryType historyType, 
            DateTime startPoint, 
            HistoryEventHandler updateHandler = null, 
            HistoryEventHandler newBarHandler = null)
        {
            if (symbol == null)
                return null;
                
            // 創建歷史數據對象
            var history = symbol.GetHistory(period, historyType, startPoint);
            
            // 使用現有方法添加歷史數據
            return AddHistory(history, updateHandler, newBarHandler);
        }
        
        /// <summary>
        /// 獲取指定交易品種和周期的歷史數據
        /// </summary>
        /// <param name="symbol">交易品種</param>
        /// <param name="period">K線周期，如果為null則返回找到的第一個匹配交易品種的歷史數據</param>
        /// <returns>匹配的歷史數據，如果沒有找到則返回null</returns>
        public static HistoricalData GetSymbolHistory(Symbol symbol, Period? period = null)
        {
            if (symbol == null || _histories.Count == 0)
                return null;
                
            // 尋找匹配的歷史數據
            foreach (var (history, _, _) in _histories)
            {
                if (history != null && IsSameSymbol(history.Symbol, symbol))
                {
                    // 如果周期為null或者周期匹配，則返回該歷史數據
                    // 注意: HistoricalData 可能沒有直接訪問 Period 的方法
                    // 我們暫時只根據交易品種匹配
                    if (period == null)
                        return history;
                }
            }
            
            // 如果沒有找到匹配的歷史數據，則返回null
            return null;
        }
        
        /// <summary>
        /// 清理所有添加的歷史數據，可從外部調用
        /// </summary>
        public static void CleanupHistories()
        {
            int count = _histories.Count;
            CleanupAllHistories();
            
            _logger?.Invoke($"All historical data have been cleaned up. Total items: {count}", StrategyLoggingLevel.Info);
        }
        
        /// <summary>
        /// 清理所有添加的歷史數據
        /// </summary>
        private static void CleanupAllHistories()
        {
            foreach (var (history, updateHandler, newBarHandler) in _histories)
            {
                if (history != null)
                {
                    // 在釋放資源前先保存Symbol名稱
                    string symbolName = "unknown";
                    if (history.Symbol != null)
                    {
                        symbolName = history.Symbol.Name;
                    }
                    
                    // 取消訂閱事件
                    if (updateHandler != null)
                        history.HistoryItemUpdated -= updateHandler;
                        
                    if (newBarHandler != null)
                        history.NewHistoryItem -= newBarHandler;
                        
                    // 釋放資源
                    history.Dispose();
                    
                    // 使用預先保存的Symbol名稱
                    _logger?.Invoke($"Cleaned up historical data: {symbolName}", StrategyLoggingLevel.Info);
                }
            }
            
            // 清空列表
            _histories.Clear();
        }

        // 內部事件處理方法
        private static void Core_PositionAdded(Position position)
        {
            if (_strategySymbol == null || _strategyAccount == null)
                return;

            var result = HandlePositionAdded(
                position,
                _strategySymbol,
                _strategyAccount,
                _logger,
                _onPositionAdded
            );

            // 更新倉位計數
            if (result.HasValue)
            {
                _longPositionsCount = result.Value.longCount;
                _shortPositionsCount = result.Value.shortCount;
            }
        }

        private static void Core_PositionRemoved(Position position)
        {
            if (_strategySymbol == null || _strategyAccount == null)
                return;

            var counts = HandlePositionRemoved(
                position,
                _strategySymbol,
                _strategyAccount,
                _logger,
                _onPositionRemoved,
                _onAllPositionsClosed
            );

            // 更新倉位計數
            _longPositionsCount = counts.longCount;
            _shortPositionsCount = counts.shortCount;
        }

        private static void Core_OrdersHistoryAdded(OrderHistory orderHistory)
        {
            if (_strategySymbol == null || _strategyAccount == null)
                return;

            HandleOrdersHistoryAdded(
                orderHistory,
                _strategySymbol,
                _strategyAccount,
                _logger,
                _onOrderRefused
            );
        }

        private static void Core_TradeAdded(Trade trade)
        {
            // 檢查是否是本策略的交易
            if (_strategySymbol != null && _strategyAccount != null && 
                IsSameSymbol(trade.Symbol, _strategySymbol) && 
                IsSameAccount(trade.Account, _strategyAccount))
            {
                // 更新盈虧統計
                if (trade.NetPnl != null)
                    _totalNetPl += trade.NetPnl.Value;

                if (trade.GrossPnl != null)
                    _totalGrossPl += trade.GrossPnl.Value;

                if (trade.Fee != null)
                    _totalFee += trade.Fee.Value;

                if(trade.GrossPnl != null )
                {
                    _totalTrades++;

                    if (trade.Side == Side.Buy )
                        _totalLongTrades++;
                    else if (trade.Side == Side.Sell)
                        _totalShortTrades++;
                }
                
                // 記錄交易信息
                _logger?.Invoke($"[{_strategySymbol.Name}] Trade added - ID: {trade.Id}, Net P/L: {trade.NetPnl}, Gross P/L: {trade.GrossPnl}, Fee: {trade.Fee}", StrategyLoggingLevel.Trading);
            }
            
            // 調用回調函數
            _onTradeAdded?.Invoke(trade);
        }

        /// <summary>
        /// 比較兩個Symbol對象是否代表相同的交易商品
        /// </summary>
        /// <param name="a">第一個Symbol對象</param>
        /// <param name="b">第二個Symbol對象</param>
        /// <returns>如果兩個Symbol代表相同的交易商品，則返回true，否則返回false</returns>
        public static bool IsSameSymbol(Symbol a, Symbol b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            // 獲取基礎商品代碼
            string aId = GetTradableSymbolId(a);
            string bId = GetTradableSymbolId(b);
            
            // 嘗試多種匹配方式
            bool isMatch = (a.Id == b.Id) ||  // 通過完整ID匹配
                          (a.Name == b.Name && a.ConnectionId == b.ConnectionId) ||  // 通過完整名稱和連接ID匹配
                          (a.CreateInfo().Equals(b.CreateInfo())) ||  // 通過CreateInfo匹配
                          (aId == bId && a.ConnectionId == b.ConnectionId);  // 通過基礎商品代碼匹配

            return isMatch;
        }

        /// <summary>
        /// 從Symbol對象中提取可交易的商品ID
        /// </summary>
        /// <param name="symbol">Symbol對象</param>
        /// <returns>可交易的商品ID</returns>
        public static string GetTradableSymbolId(Symbol symbol)
        {
            var symbolId = symbol.AdditionalInfo != null && 
                          symbol.AdditionalInfo.TryGetItem(Symbol.TRADING_SYMBOL_ID, out var item) ? 
                          item.Value.ToString() : 
                          symbol.Id;
            int atIndex = symbolId.IndexOf('@');
            string tradableSymbolId = atIndex > 0 ? symbolId.Substring(0, atIndex) : symbolId;
            return tradableSymbolId;
        }

        /// <summary>
        /// 獲取倉位開倉價格
        /// </summary>
        /// <param name="position">倉位對象</param>
        /// <returns>開倉價格</returns>
        public static double GetEntryPrice(Position position)
        {
            return position.OpenPrice;
        }
        

        /// <summary>
        /// 比較兩個Account對象是否代表相同的賬戶
        /// </summary>
        /// <param name="a">第一個Account對象</param>
        /// <param name="b">第二個Account對象</param>
        /// <returns>如果兩個Account代表相同的賬戶，則返回true，否則返回false</returns>
        public static bool IsSameAccount(Account a, Account b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            
            // 嘗試多種匹配方式
            bool isMatch = (a.Id == b.Id) ||  // 通過ID匹配
                          (a.Name == b.Name && a.ConnectionId == b.ConnectionId) ||  // 通過名稱和連接ID匹配
                          (a.CreateInfo().Equals(b.CreateInfo()));  // 通過CreateInfo匹配
            
            return isMatch;
        }

        /// <summary>
        /// 獲取指定賬戶和交易品種的所有倉位
        /// </summary>
        /// <param name="account">賬戶對象</param>
        /// <param name="symbol">交易品種對象</param>
        /// <returns>匹配的倉位數組</returns>
        public static Position[] GetPositions(Account account, Symbol symbol)
        {
            // 先獲取所有倉位
            var allPositions = Core.Instance.Positions.ToArray();

            // 過濾倉位
            var filteredPositions = allPositions
                .Where(x => IsSameSymbol(x.Symbol, symbol) && 
                           IsSameAccount(x.Account, account))
                .ToArray();

            return filteredPositions;
        }

        /// <summary>
        /// 獲取當前倉位數量
        /// </summary>
        /// <param name="account">賬戶對象</param>
        /// <param name="symbol">交易品種對象</param>
        /// <param name="longCount">多頭倉位數量(輸出參數)</param>
        /// <param name="shortCount">空頭倉位數量(輸出參數)</param>
        public static void GetPositionCounts(Account account, Symbol symbol, out int longCount, out int shortCount)
        {
            var positions = GetPositions(account, symbol);
            longCount = positions.Count(x => x.Side == Side.Buy);
            shortCount = positions.Count(x => x.Side == Side.Sell);
        }

        /// <summary>
        /// 處理新增倉位事件，檢查倉位是否匹配指定的賬戶和交易品種
        /// </summary>
        /// <param name="position">新增的倉位</param>
        /// <param name="strategySymbol">策略使用的交易品種</param>
        /// <param name="strategyAccount">策略使用的賬戶</param>
        /// <param name="logger">日誌記錄器</param>
        /// <param name="onPositionMatched">當倉位匹配時執行的回調函數，接收倉位參數</param>
        /// <returns>包含長短倉數量的元組，如果倉位不匹配則返回null</returns>
        public static (int longCount, int shortCount)? HandlePositionAdded(
            Position position, 
            Symbol strategySymbol, 
            Account strategyAccount, 
            Action<string, StrategyLoggingLevel> logger,
            Action<Position> onPositionMatched = null)
        {
            // 記錄新增倉位的詳細信息
            logger?.Invoke($"[{strategySymbol.Name}] Position added event - Symbol: {position.Symbol.Name}({position.Symbol.Id}/{position.Symbol.ConnectionId}), Account: {position.Account.Name}({position.Account.Id}/{position.Account.ConnectionId})", StrategyLoggingLevel.Trading);
            logger?.Invoke($"[{strategySymbol.Name}] Current strategy - Symbol: {strategySymbol.Name}({strategySymbol.Id}/{strategySymbol.ConnectionId}), Account: {strategyAccount.Name}({strategyAccount.Id}/{strategyAccount.ConnectionId})", StrategyLoggingLevel.Trading);

            // 檢查是否為本策略的倉位
            if (!IsSameSymbol(position.Symbol, strategySymbol) || 
                !IsSameAccount(position.Account, strategyAccount))
            {
                logger?.Invoke($"[{strategySymbol.Name}] Ignoring position for different symbol/account", StrategyLoggingLevel.Trading);
                return null;
            }

            // 獲取匹配的倉位
            var positions = GetPositions(strategyAccount, strategySymbol);
            int longPositionsCount = positions.Count(x => x.Side == Side.Buy);
            int shortPositionsCount = positions.Count(x => x.Side == Side.Sell);

            // 記錄倉位統計
            logger?.Invoke($"[{strategySymbol.Name}] Position added - Long: {longPositionsCount}, Short: {shortPositionsCount}", StrategyLoggingLevel.Trading);
            
            // 如果有回調函數，調用它
            onPositionMatched?.Invoke(position);
            
            return (longPositionsCount, shortPositionsCount);
        }

        /// <summary>
        /// 處理移除倉位事件
        /// </summary>
        /// <param name="position">被移除的倉位</param>
        /// <param name="strategySymbol">策略使用的交易品種</param>
        /// <param name="strategyAccount">策略使用的賬戶</param>
        /// <param name="logger">日誌記錄器</param>
        /// <param name="onPositionRemoved">當倉位被移除時執行的回調函數</param>
        /// <param name="onAllPositionsClosed">當所有倉位被關閉時執行的回調函數</param>
        /// <returns>包含長短倉數量的元組</returns>
        public static (int longCount, int shortCount) HandlePositionRemoved(
            Position position,
            Symbol strategySymbol,
            Account strategyAccount,
            Action<string, StrategyLoggingLevel> logger,
            Action<Position> onPositionRemoved = null,
            Action onAllPositionsClosed = null)
        {
            // 檢查是否為本策略的倉位
            bool isStrategyPosition = IsSameSymbol(position.Symbol, strategySymbol) && 
                                     IsSameAccount(position.Account, strategyAccount);
            
            // 如果是本策略的倉位，執行倉位移除回調
            if (isStrategyPosition && onPositionRemoved != null)
            {
                logger?.Invoke($"[{strategySymbol.Name}] Strategy position removed - Side: {position.Side}, Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);
                onPositionRemoved(position);
            }
            
            // 獲取當前剩餘的倉位
            var positions = GetPositions(strategyAccount, strategySymbol);
            int longPositionsCount = positions.Count(x => x.Side == Side.Buy);
            int shortPositionsCount = positions.Count(x => x.Side == Side.Sell);

            // 如果已經沒有倉位，執行特定回調
            if (!positions.Any())
            {
                logger?.Invoke($"[{strategySymbol.Name}] All positions closed, resetting states", StrategyLoggingLevel.Trading);
                onAllPositionsClosed?.Invoke();
            }

            // 記錄倉位統計
            logger?.Invoke($"[{strategySymbol.Name}] Position removed - Long: {longPositionsCount}, Short: {shortPositionsCount}", StrategyLoggingLevel.Trading);
            
            return (longPositionsCount, shortPositionsCount);
        }

        /// <summary>
        /// 處理訂單歷史事件，特別是處理拒絕訂單的情況
        /// </summary>
        /// <param name="orderHistory">訂單歷史記錄</param>
        /// <param name="strategySymbol">策略使用的交易品種</param>
        /// <param name="strategyAccount">策略使用的賬戶</param>
        /// <param name="logger">日誌記錄器</param>
        /// <param name="onOrderRefused">當訂單被拒絕時執行的回調函數</param>
        /// <returns>是否是被拒絕的策略訂單</returns>
        public static bool HandleOrdersHistoryAdded(OrderHistory orderHistory,
                                                Symbol strategySymbol,
                                                Account strategyAccount,
                                                Action<string, StrategyLoggingLevel> logger,
                                                Action<string> onOrderRefused)
        {
            // 只處理拒絕訂單的情況
            if (orderHistory.Status != OrderStatus.Refused)
                return false;

            // 檢查是否是本策略的訂單
            if (!IsSameSymbol(orderHistory.Symbol, strategySymbol) || 
                !IsSameAccount(orderHistory.Account, strategyAccount))
                return false;

            // 記錄拒絕訂單信息
            string errorMessage = $"Order refused for {strategySymbol.Name}: {orderHistory.Id}";
            logger?.Invoke(errorMessage, StrategyLoggingLevel.Error);
            
            // 執行回調
            onOrderRefused?.Invoke(errorMessage);
            
            return true;
        }
        
        /// <summary>
        /// 重置所有交易統計數據
        /// </summary>
        public static void ResetTradeStatistics()
        {
            _totalNetPl = 0;
            _totalGrossPl = 0;
            _totalFee = 0;
        }

        /// <summary>
        /// 初始化訂單類型
        /// </summary>
        /// <param name="orderTypeBehaviors">要初始化的訂單類型行為，如"Market"、"Limit"、"Stop"等</param>
        /// <returns>是否成功初始化所有請求的訂單類型</returns>
        public static bool InitOrderTypes(params string[] orderTypeBehaviors)
        {
            if (_strategySymbol == null || orderTypeBehaviors == null || orderTypeBehaviors.Length == 0)
            {
                _logger?.Invoke("Cannot initialize order types: Symbol is null or no order types specified", StrategyLoggingLevel.Error);
                return false;
            }
            
            bool allTypesFound = true;
            
            // 清空現有字典
            _orderTypeIds.Clear();
            
            foreach (var behavior in orderTypeBehaviors)
            {
                OrderTypeBehavior typeBehavior;
                if (!Enum.TryParse(behavior, true, out typeBehavior))
                {
                    _logger?.Invoke($"Invalid order type behavior: {behavior}", StrategyLoggingLevel.Error);
                    allTypesFound = false;
                    continue;
                }
                
                var orderType = Core.Instance.OrderTypes
                    .FirstOrDefault(x => x.ConnectionId == _strategySymbol.ConnectionId && x.Behavior == typeBehavior);
                
                if (orderType != null)
                {
                    _orderTypeIds[behavior] = orderType.Id;
                    _logger?.Invoke($"Initialized {behavior} order type: {orderType.Id}", StrategyLoggingLevel.Info);
                }
                else
                {
                    _logger?.Invoke($"Connection of selected symbol has not support {behavior} orders", StrategyLoggingLevel.Error);
                    allTypesFound = false;
                }
            }
            
            return allTypesFound;
        }
        
        /// <summary>
        /// 獲取指定訂單類型的ID
        /// </summary>
        /// <param name="orderTypeBehavior">訂單類型行為，如"Market"、"Limit"、"Stop"等</param>
        /// <returns>訂單類型ID，如果未找到則返回null</returns>
        public static string GetOrderType(string orderTypeBehavior)
        {
            if (string.IsNullOrEmpty(orderTypeBehavior) || !_orderTypeIds.ContainsKey(orderTypeBehavior))
            {
                return null;
            }
            
            return _orderTypeIds[orderTypeBehavior];
        }
        
        /// <summary>
        /// 檢查指定訂單類型是否有效
        /// </summary>
        /// <param name="orderTypeBehavior">訂單類型行為，如"Market"、"Limit"、"Stop"等</param>
        /// <returns>如果訂單類型有效則返回true，否則返回false</returns>
        public static bool IsOrderTypeValid(string orderTypeBehavior)
        {
            return !string.IsNullOrEmpty(GetOrderType(orderTypeBehavior));
        }

        /// <summary>
        /// 檢查賬戶和交易品種是否有效，並恢復它們的正確狀態
        /// </summary>
        /// <param name="symbol">要檢查的交易品種</param>
        /// <param name="account">要檢查的賬戶</param>
        /// <param name="logger">日誌記錄器</param>
        /// <param name="updatedSymbol">返回更新後的交易品種</param>
        /// <param name="updatedAccount">返回更新後的賬戶</param>
        /// <returns>如果賬戶和交易品種都有效且來自同一連接，則返回true，否則返回false</returns>
        public static bool CheckAccountAndSymbol(Symbol symbol, Account account, Action<string, StrategyLoggingLevel> logger, 
                                               out Symbol updatedSymbol, out Account updatedAccount)
        {
            updatedSymbol = symbol;
            updatedAccount = account;
            
            // 檢查並恢復Symbol對象
            if (updatedSymbol != null && updatedSymbol.State == BusinessObjectState.Fake)
                updatedSymbol = Core.Instance.GetSymbol(updatedSymbol.CreateInfo());

            if (updatedSymbol == null)
            {
                logger?.Invoke("Incorrect input parameters... Symbol have not specified.", StrategyLoggingLevel.Error);
                return false;
            }

            // 檢查並恢復Account對象
            if (updatedAccount != null && updatedAccount.State == BusinessObjectState.Fake)
                updatedAccount = Core.Instance.GetAccount(updatedAccount.CreateInfo());

            if (updatedAccount == null)
            {
                logger?.Invoke("Incorrect input parameters... Account have not specified.", StrategyLoggingLevel.Error);
                return false;
            }

            // 檢查Symbol和Account是否來自同一連接
            if (updatedSymbol.ConnectionId != updatedAccount.ConnectionId)
            {
                logger?.Invoke("Incorrect input parameters... Symbol and Account from different connections.", StrategyLoggingLevel.Error);
                return false;
            }

            return true;
        }
    }
}