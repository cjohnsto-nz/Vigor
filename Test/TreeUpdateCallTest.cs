using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vigor.Test
{
    /// <summary>
    /// Test class to instrument and count all tree update calls in OnGameTick
    /// This will help identify exactly how many MarkPathDirty calls are being triggered
    /// </summary>
    public class TreeUpdateCallTracker
    {
        public static List<string> CallLog = new List<string>();
        public static int SetFloatCallCount = 0;
        public static int SetBoolCallCount = 0;
        public static int MarkPathDirtyCallCount = 0;
        
        public static void Reset()
        {
            CallLog.Clear();
            SetFloatCallCount = 0;
            SetBoolCallCount = 0;
            MarkPathDirtyCallCount = 0;
        }
        
        public static void LogSetFloat(string key, float value, string source)
        {
            SetFloatCallCount++;
            string logEntry = $"SetFloat: {key}={value} (from {source})";
            CallLog.Add(logEntry);
            Console.WriteLine($"[TREE UPDATE TEST] {logEntry}");
        }
        
        public static void LogSetBool(string key, bool value, string source)
        {
            SetBoolCallCount++;
            string logEntry = $"SetBool: {key}={value} (from {source})";
            CallLog.Add(logEntry);
            Console.WriteLine($"[TREE UPDATE TEST] {logEntry}");
        }
        
        public static void LogMarkPathDirty(string path, string source)
        {
            MarkPathDirtyCallCount++;
            string logEntry = $"MarkPathDirty: {path} (from {source})";
            CallLog.Add(logEntry);
            Console.WriteLine($"[TREE UPDATE TEST] {logEntry}");
        }
        
        public static void PrintSummary()
        {
            Console.WriteLine($"\n=== TREE UPDATE CALL TEST SUMMARY ===");
            Console.WriteLine($"Total SetFloat calls: {SetFloatCallCount}");
            Console.WriteLine($"Total SetBool calls: {SetBoolCallCount}");
            Console.WriteLine($"Total MarkPathDirty calls: {MarkPathDirtyCallCount}");
            Console.WriteLine($"TOTAL CALLS THAT TRIGGER SYNC: {SetFloatCallCount + SetBoolCallCount + MarkPathDirtyCallCount}");
            Console.WriteLine($"\nDetailed call log:");
            foreach (var call in CallLog)
            {
                Console.WriteLine($"  {call}");
            }
            Console.WriteLine($"=====================================\n");
        }
    }
    
    /// <summary>
    /// Wrapper class to intercept and count tree attribute calls
    /// </summary>
    public class InstrumentedTreeAttribute : TreeAttribute
    {
        private readonly string _source;
        
        public InstrumentedTreeAttribute(string source) : base()
        {
            _source = source;
        }
        
        public override void SetFloat(string key, float value)
        {
            TreeUpdateCallTracker.LogSetFloat(key, value, _source);
            base.SetFloat(key, value);
        }
        
        public override void SetBool(string key, bool value)
        {
            TreeUpdateCallTracker.LogSetBool(key, value, _source);
            base.SetBool(key, value);
        }
    }
    
    /// <summary>
    /// Wrapper class to intercept and count WatchedAttributes calls
    /// </summary>
    public class InstrumentedWatchedAttributes
    {
        private readonly ITreeAttribute _attributes;
        private readonly string _source;
        
        public InstrumentedWatchedAttributes(ITreeAttribute attributes, string source)
        {
            _attributes = attributes;
            _source = source;
        }
        
        public void MarkPathDirty(string path)
        {
            TreeUpdateCallTracker.LogMarkPathDirty(path, _source);
            // Note: We can't actually call the real MarkPathDirty in a test
            // but we can count how many times it would be called
        }
        
        public void SetBool(string key, bool value)
        {
            TreeUpdateCallTracker.LogSetBool(key, value, _source);
            // This would also trigger MarkPathDirty internally
            TreeUpdateCallTracker.LogMarkPathDirty(key, $"{_source}-internal");
        }
    }
}
