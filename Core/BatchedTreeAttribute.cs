using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vigor.Core
{
    /// <summary>
    /// Batched wrapper for TreeAttribute that collects all changes and applies them
    /// in a single batch to minimize MarkPathDirty calls and prevent rubberbanding
    /// </summary>
    public class BatchedTreeAttribute
    {
        private readonly ITreeAttribute _tree;
        private readonly SyncedTreeAttribute _watchedAttributes;
        private readonly string _pathName;
        private readonly int _minSyncIntervalMs;
        private readonly bool _debugMode;
        
        // Batched changes
        private readonly Dictionary<string, float> _pendingFloats = new();
        private readonly Dictionary<string, bool> _pendingBools = new();
        private readonly Dictionary<string, int> _pendingInts = new();
        private readonly Dictionary<string, string> _pendingStrings = new();
        
        // Thread safety lock
        private readonly object _pendingChangesLock = new object();
        
        // State tracking
        private bool _hasPendingChanges = false;
        private long _lastSyncTime = 0;
        
        // Profiling
        private int _totalSetCalls = 0;
        private int _actualSyncCalls = 0;
        private long _lastProfileLogTime = 0;
        private readonly Dictionary<string, int> _pathCallCounts = new Dictionary<string, int>();
        
        // Timer for batching
        private System.Threading.Timer _batchingTimer;
        
        public BatchedTreeAttribute(ITreeAttribute tree, SyncedTreeAttribute watchedAttributes, 
                                  string pathName, bool debugMode = false, int minSyncIntervalMs = 100)
        {
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _watchedAttributes = watchedAttributes ?? throw new ArgumentNullException(nameof(watchedAttributes));
            _pathName = pathName ?? throw new ArgumentNullException(nameof(pathName));
            _minSyncIntervalMs = minSyncIntervalMs;
            _debugMode = debugMode;
            _lastSyncTime = GetCurrentTimeMs();
            
            // Start the batching timer
            StartBatchingTimer();
        }
        
        private void StartBatchingTimer()
        {
            // Use a simple timer approach - check and sync every interval
            _batchingTimer = new System.Threading.Timer(BatchingTimerCallback, null, _minSyncIntervalMs, _minSyncIntervalMs);
        }
        
        private void BatchingTimerCallback(object state)
        {
            if (_hasPendingChanges)
            {
                CommitPendingChanges();
                _watchedAttributes.MarkPathDirty(_pathName);
                _actualSyncCalls++; // Track actual syncs performed by timer
                _lastSyncTime = GetCurrentTimeMs();
                _hasPendingChanges = false;
            }
        }
        
        /// <summary>
        /// Queue a float value change for batching
        /// </summary>
        public void SetFloat(string key, float value)
        {
            _totalSetCalls++;
            
            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode) return;
            
            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }
            
            // Check if value actually changed to avoid unnecessary updates
            float currentValue = _tree.GetFloat(key, float.MinValue);
            if (Math.Abs(currentValue - value) < 0.001f) return;
            
            lock (_pendingChangesLock)
            {
                _pendingFloats[key] = value;
                _hasPendingChanges = true;
            }
        }
        
        /// <summary>
        /// Queue a bool value change for batching
        /// </summary>
        public void SetBool(string key, bool value)
        {
            _totalSetCalls++;
            
            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode) return;
            
            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }
            
            // Check if value actually changed
            bool currentValue = _tree.GetBool(key, !value); // Use opposite as default to force change detection
            if (currentValue == value) return;
            
            lock (_pendingChangesLock)
            {
                _pendingBools[key] = value;
                _hasPendingChanges = true;
            }
        }
        
        /// <summary>
        /// Queue an int value change for batching
        /// </summary>
        public void SetInt(string key, int value)
        {
            _totalSetCalls++;
            
            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode) return;
            
            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }
            
            // Check if value actually changed
            int currentValue = _tree.GetInt(key, int.MinValue);
            if (currentValue == value) return;
            
            lock (_pendingChangesLock)
            {
                _pendingInts[key] = value;
                _hasPendingChanges = true;
            }
        }
        
        /// <summary>
        /// Queue a string value change for batching
        /// </summary>
        public void SetString(string key, string value)
        {
            _totalSetCalls++;
            
            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode) return;
            
            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }
            
            // Check if value actually changed
            string currentValue = _tree.GetString(key, null);
            if (string.Equals(currentValue, value)) return;
            
            lock (_pendingChangesLock)
            {
                _pendingStrings[key] = value;
                _hasPendingChanges = true;
            }
        }
        
        /// <summary>
        /// Get float value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public float GetFloat(string key, float defaultValue = 0f)
        {
            // Check pending changes first, then tree
            lock (_pendingChangesLock)
            {
                if (_pendingFloats.TryGetValue(key, out float pendingValue))
                    return pendingValue;
            }
            return _tree.GetFloat(key, defaultValue);
        }
        
        /// <summary>
        /// Get bool value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public bool GetBool(string key, bool defaultValue = false)
        {
            // Check pending changes first, then tree
            lock (_pendingChangesLock)
            {
                if (_pendingBools.TryGetValue(key, out bool pendingValue))
                    return pendingValue;
            }
            return _tree.GetBool(key, defaultValue);
        }
        
        /// <summary>
        /// Get int value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public int GetInt(string key, int defaultValue = 0)
        {
            // Check pending changes first, then tree
            if (_pendingInts.TryGetValue(key, out int pendingValue))
                return pendingValue;
            return _tree.GetInt(key, defaultValue);
        }
        
        /// <summary>
        /// Get string value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public string GetString(string key, string defaultValue = null)
        {
            // Check pending changes first, then tree
            if (_pendingStrings.TryGetValue(key, out string pendingValue))
                return pendingValue;
            return _tree.GetString(key, defaultValue);
        }
        
        /// <summary>
        /// Force immediate sync of all pending changes (for initialization, config changes, etc.)
        /// </summary>
        public void ForceSync()
        {
            if (!_hasPendingChanges) return;
            
            CommitPendingChanges();
            _watchedAttributes.MarkPathDirty(_pathName);
            _actualSyncCalls++; // Track ForceSync calls too!
            _lastSyncTime = GetCurrentTimeMs();
            _hasPendingChanges = false;
        }
        
        // TrySync method removed - now using proper timer-based batching
        
        /// <summary>
        /// Apply all pending changes to the actual tree
        /// </summary>
        private void CommitPendingChanges()
        {
            lock (_pendingChangesLock)
            {
                foreach (var kvp in _pendingFloats)
                {
                    _tree.SetFloat(kvp.Key, kvp.Value);
                }
                
                foreach (var kvp in _pendingBools)
                {
                    _tree.SetBool(kvp.Key, kvp.Value);
                }
                
                foreach (var kvp in _pendingInts)
                {
                    _tree.SetInt(kvp.Key, kvp.Value);
                }
                
                foreach (var kvp in _pendingStrings)
                {
                    _tree.SetString(kvp.Key, kvp.Value);
                }
                
                // Clear pending changes
                _pendingFloats.Clear();
                _pendingBools.Clear();
                _pendingInts.Clear();
                _pendingStrings.Clear();
            }
        }
        
        /// <summary>
        /// Get profiling statistics for debugging
        /// </summary>
        public void LogProfilingStats(string playerName, bool debugMode, ICoreAPI api = null)
        {
            // Always show profiling logs to verify batching effectiveness
            
            long currentTime = GetCurrentTimeMs();
            if (currentTime - _lastProfileLogTime < 30000) return; // Log every 30 seconds
            
            float batchingEffectiveness = _totalSetCalls > 0 ? ((_totalSetCalls - _actualSyncCalls) * 100.0f / _totalSetCalls) : 0f;
            float actualSyncRate = (currentTime - _lastProfileLogTime) > 0 ? 
                _actualSyncCalls * 1000.0f / (currentTime - _lastProfileLogTime) : 0f;
            
            // Get top 10 most called paths for detailed analysis
            string pathDetails = "";
            lock (_pendingChangesLock)
            {
                var topPaths = _pathCallCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(10)
                    .ToList();
                
                if (topPaths.Any())
                {
                    pathDetails = " | Top paths: " + string.Join(", ", topPaths.Select(kvp => $"{kvp.Key}({kvp.Value})"));
                }
            }
            
            string logMessage = $"[Vigor BATCHED SYNC] Player {playerName}: " +
                              $"Total calls: {_totalSetCalls}, Actual syncs: {_actualSyncCalls} (Batching: {batchingEffectiveness:F1}%), " +
                              $"Sync rate: {actualSyncRate:F1}/sec{pathDetails}";
            
            // Use Vintage Story logging system
            if (api?.Logger != null)
            {
                api.Logger.Notification(logMessage);
            }
            else
            {
                // Fallback to VigorModSystem logger if available
                VigorModSystem.Instance?.Logger?.Notification(logMessage);
            }
            
            _lastProfileLogTime = currentTime;
            _totalSetCalls = 0;
            _actualSyncCalls = 0;
            
            // Reset path call counts for next interval
            lock (_pendingChangesLock)
            {
                _pathCallCounts.Clear();
            }
        }
        
        /// <summary>
        /// Get current time in milliseconds (mockable for testing)
        /// </summary>
        protected virtual long GetCurrentTimeMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        
        /// <summary>
        /// Check if there are pending changes
        /// </summary>
        public bool HasPendingChanges => _hasPendingChanges;
        
        /// <summary>
        /// Get the underlying tree (for read-only operations)
        /// </summary>
        public ITreeAttribute UnderlyingTree => _tree;
    }
}
