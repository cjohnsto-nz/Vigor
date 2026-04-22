using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vigor.Utils;

namespace Vigor.Core
{
    /// <summary>
    /// Batched wrapper for TreeAttribute that collects all changes and applies them
    /// in a single batch to minimize MarkPathDirty calls and prevent rubberbanding
    /// </summary>
    public class BatchedTreeAttribute : IDisposable
    {
        private static long _nextInstanceId = 0;

        private readonly ITreeAttribute _tree;
        private readonly SyncedTreeAttribute _watchedAttributes;
        private readonly string _pathName;
        private readonly int _minSyncIntervalMs;
        private readonly bool _debugMode;
        private readonly long _instanceId;
        
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
        private bool _disposed = false;
        private int _disposeState = 0;
        
        // Profiling
        private int _totalSetCalls = 0;
        private int _stagedSetCalls = 0;
        private int _actualSyncCalls = 0;
        private int _forcedSyncCalls = 0;
        private int _deferredFlushCalls = 0;
        private long _lastProfileLogTime = 0;
        private readonly Dictionary<string, int> _pathCallCounts = new Dictionary<string, int>();
        
        public BatchedTreeAttribute(ITreeAttribute tree, SyncedTreeAttribute watchedAttributes, 
                                  string pathName, bool debugMode = false, int minSyncIntervalMs = 100)
        {
            _instanceId = System.Threading.Interlocked.Increment(ref _nextInstanceId);
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _watchedAttributes = watchedAttributes ?? throw new ArgumentNullException(nameof(watchedAttributes));
            _pathName = pathName ?? throw new ArgumentNullException(nameof(pathName));
            _minSyncIntervalMs = minSyncIntervalMs;
            _debugMode = debugMode;
            _lastSyncTime = GetCurrentTimeMs();

            VigorDiagnostics.Increment("batchedTree.created");
            VigorDiagnostics.SetGauge("batchedTree.lastInstanceId", _instanceId);
            VigorDiagnostics.SetGauge("batchedTree.activeApprox", VigorDiagnostics.GetGauge("batchedTree.activeApprox") + 1);
        }

        ~BatchedTreeAttribute()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            _disposed = true;

            lock (_pendingChangesLock)
            {
                _pendingFloats.Clear();
                _pendingBools.Clear();
                _pendingInts.Clear();
                _pendingStrings.Clear();
                _pathCallCounts.Clear();
                _hasPendingChanges = false;
                UpdatePendingCountGaugesLocked();
            }

            if (disposing)
            {
                VigorDiagnostics.Increment("batchedTree.disposed");
            }
            else
            {
                VigorDiagnostics.Increment("batchedTree.finalized");
            }

            VigorDiagnostics.SetGauge("batchedTree.activeApprox", Math.Max(0, VigorDiagnostics.GetGauge("batchedTree.activeApprox") - 1));
        }

        /// <summary>
        /// Queue a float value change for batching
        /// </summary>
        public void SetFloat(string key, float value)
        {
            if (_disposed) return;

            _totalSetCalls++;
            VigorDiagnostics.Increment("batchedTree.setCalls");

            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode)
            {
                VigorDiagnostics.Increment("batchedTree.setDebugFiltered");
                return;
            }

            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }

            // Compare against the effective current value, including staged changes from this tick.
            float currentValue = GetFloat(key, float.MinValue);
            if (Math.Abs(currentValue - value) < 0.001f)
            {
                VigorDiagnostics.Increment("batchedTree.setNoOpSkipped");
                return;
            }

            lock (_pendingChangesLock)
            {
                _pendingFloats[key] = value;
                _hasPendingChanges = true;
                _stagedSetCalls++;
                VigorDiagnostics.Increment("batchedTree.setStaged");
                UpdatePendingCountGaugesLocked();
            }
        }

        /// <summary>
        /// Queue a bool value change for batching
        /// </summary>
        public void SetBool(string key, bool value)
        {
            if (_disposed) return;

            _totalSetCalls++;
            VigorDiagnostics.Increment("batchedTree.setCalls");

            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode)
            {
                VigorDiagnostics.Increment("batchedTree.setDebugFiltered");
                return;
            }

            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }

            // Compare against the effective current value, including staged changes from this tick.
            bool currentValue = GetBool(key, !value);
            if (currentValue == value)
            {
                VigorDiagnostics.Increment("batchedTree.setNoOpSkipped");
                return;
            }

            lock (_pendingChangesLock)
            {
                _pendingBools[key] = value;
                _hasPendingChanges = true;
                _stagedSetCalls++;
                VigorDiagnostics.Increment("batchedTree.setStaged");
                UpdatePendingCountGaugesLocked();
            }
        }

        /// <summary>
        /// Queue an int value change for batching
        /// </summary>
        public void SetInt(string key, int value)
        {
            if (_disposed) return;

            _totalSetCalls++;
            VigorDiagnostics.Increment("batchedTree.setCalls");

            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode)
            {
                VigorDiagnostics.Increment("batchedTree.setDebugFiltered");
                return;
            }

            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }

            // Compare against the effective current value, including staged changes from this tick.
            int currentValue = GetInt(key, int.MinValue);
            if (currentValue == value)
            {
                VigorDiagnostics.Increment("batchedTree.setNoOpSkipped");
                return;
            }

            lock (_pendingChangesLock)
            {
                _pendingInts[key] = value;
                _hasPendingChanges = true;
                _stagedSetCalls++;
                VigorDiagnostics.Increment("batchedTree.setStaged");
                UpdatePendingCountGaugesLocked();
            }
        }

        /// <summary>
        /// Queue a string value change for batching
        /// </summary>
        public void SetString(string key, string value)
        {
            if (_disposed) return;

            _totalSetCalls++;
            VigorDiagnostics.Increment("batchedTree.setCalls");

            // Skip debug attributes if debug mode is disabled
            if (key.StartsWith("debug_") && !_debugMode)
            {
                VigorDiagnostics.Increment("batchedTree.setDebugFiltered");
                return;
            }

            // Track path call counts for profiling (only for non-filtered paths)
            lock (_pendingChangesLock)
            {
                _pathCallCounts[key] = _pathCallCounts.GetValueOrDefault(key, 0) + 1;
            }

            // Compare against the effective current value, including staged changes from this tick.
            string currentValue = GetString(key, null);
            if (string.Equals(currentValue, value))
            {
                VigorDiagnostics.Increment("batchedTree.setNoOpSkipped");
                return;
            }

            lock (_pendingChangesLock)
            {
                _pendingStrings[key] = value;
                _hasPendingChanges = true;
                _stagedSetCalls++;
                VigorDiagnostics.Increment("batchedTree.setStaged");
                UpdatePendingCountGaugesLocked();
            }
        }

        /// <summary>
        /// Get float value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public float GetFloat(string key, float defaultValue = 0f)
        {
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
            lock (_pendingChangesLock)
            {
                if (_pendingInts.TryGetValue(key, out int pendingValue))
                    return pendingValue;
            }
            return _tree.GetInt(key, defaultValue);
        }

        /// <summary>
        /// Get string value (reads from tree immediately for gameplay consistency)
        /// </summary>
        public string GetString(string key, string defaultValue = null)
        {
            lock (_pendingChangesLock)
            {
                if (_pendingStrings.TryGetValue(key, out string pendingValue))
                    return pendingValue;
            }
            return _tree.GetString(key, defaultValue);
        }

        /// <summary>
        /// Attempts to flush pending changes on the current thread if the batching interval has elapsed.
        /// </summary>
        public bool TryFlush()
        {
            return TryFlushInternal(force: false);
        }

        /// <summary>
        /// Alias retained for older call sites / terminology.
        /// </summary>
        public bool TrySync()
        {
            return TryFlush();
        }

        /// <summary>
        /// Force immediate sync of all pending changes (for initialization, config changes, etc.)
        /// </summary>
        public void ForceSync()
        {
            TryFlushInternal(force: true);
        }

        private bool TryFlushInternal(bool force)
        {
            if (_disposed)
            {
                return false;
            }

            long nowMs = GetCurrentTimeMs();

            lock (_pendingChangesLock)
            {
                if (!_hasPendingChanges)
                {
                    VigorDiagnostics.Increment("batchedTree.flushSkippedNoChanges");
                    return false;
                }

                if (!force && nowMs - _lastSyncTime < _minSyncIntervalMs)
                {
                    VigorDiagnostics.Increment("batchedTree.flushSkippedNotDue");
                    return false;
                }

                CommitPendingChangesLocked();
                _lastSyncTime = nowMs;
                _hasPendingChanges = false;
            }

            _watchedAttributes.MarkPathDirty(_pathName);
            _actualSyncCalls++;
            VigorDiagnostics.Increment("batchedTree.markPathDirty");
            VigorDiagnostics.Increment("batchedTree.flushPerformed");

            if (force)
            {
                _forcedSyncCalls++;
                VigorDiagnostics.Increment("batchedTree.forceSync");
            }
            else
            {
                _deferredFlushCalls++;
                VigorDiagnostics.Increment("batchedTree.flushInterval");
            }

            return true;
        }
        
        /// <summary>
        /// Apply all pending changes to the actual tree
        /// </summary>
        private void CommitPendingChangesLocked()
        {
            UpdatePendingCountGaugesLocked();

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

            _pendingFloats.Clear();
            _pendingBools.Clear();
            _pendingInts.Clear();
            _pendingStrings.Clear();
            UpdatePendingCountGaugesLocked();
        }

        private void UpdatePendingCountGaugesLocked()
        {
            VigorDiagnostics.SetGauge("batchedTree.pendingFloatCount", _pendingFloats.Count);
            VigorDiagnostics.SetGauge("batchedTree.pendingBoolCount", _pendingBools.Count);
            VigorDiagnostics.SetGauge("batchedTree.pendingIntCount", _pendingInts.Count);
            VigorDiagnostics.SetGauge("batchedTree.pendingStringCount", _pendingStrings.Count);
        }
        
        /// <summary>
        /// Get profiling statistics for debugging
        /// </summary>
        public void LogProfilingStats(string playerName, bool debugMode, ICoreAPI api = null)
        {
            if (!debugMode) return;
            
            long currentTime = GetCurrentTimeMs();
            if (currentTime - _lastProfileLogTime < 30000) return; // Log every 30 seconds
            
            float stagingRate = _totalSetCalls > 0 ? (_stagedSetCalls * 100.0f / _totalSetCalls) : 0f;
            float batchingEffectiveness = _stagedSetCalls > 0 ? ((_stagedSetCalls - _actualSyncCalls) * 100.0f / _stagedSetCalls) : 0f;
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
                              $"Set attempts: {_totalSetCalls}, Staged changes: {_stagedSetCalls} ({stagingRate:F1}% of attempts), " +
                              $"MarkDirty calls: {_actualSyncCalls} (Coalesced: {batchingEffectiveness:F1}%), " +
                              $"Forced: {_forcedSyncCalls}, Deferred: {_deferredFlushCalls}, Sync rate: {actualSyncRate:F1}/sec{pathDetails}";
            
            // Use Vintage Story logging system
            if (api?.Logger != null)
            {
                api.Logger.Debug(logMessage);
            }
            else
            {
                // Fallback to VigorModSystem logger if available
                VigorModSystem.Instance?.Logger?.Debug(logMessage);
            }
            
            _lastProfileLogTime = currentTime;
            _totalSetCalls = 0;
            _stagedSetCalls = 0;
            _actualSyncCalls = 0;
            _forcedSyncCalls = 0;
            _deferredFlushCalls = 0;
            
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
        public bool HasPendingChanges
        {
            get
            {
                lock (_pendingChangesLock)
                {
                    return _hasPendingChanges;
                }
            }
        }
        
        /// <summary>
        /// Get the underlying tree (for read-only operations)
        /// </summary>
        public ITreeAttribute UnderlyingTree => _tree;
    }
}
