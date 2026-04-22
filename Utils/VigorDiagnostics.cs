using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Vigor.Utils
{
    /// <summary>
    /// Lightweight in-memory diagnostics collector for profiling Vigor behavior
    /// without relying on continuous logging.
    /// </summary>
    public static class VigorDiagnostics
    {
        private static readonly ConcurrentDictionary<string, long> Counters = new();
        private static readonly ConcurrentDictionary<string, double> Gauges = new();
        private static readonly DateTime StartedAtUtc = DateTime.UtcNow;
        private static readonly object SessionLock = new();
        private static string _sessionDirectoryPath;
        private static int _snapshotSequence = 0;

        public static void Increment(string key, long amount = 1)
        {
            Counters.AddOrUpdate(key, amount, (_, current) => current + amount);
        }

        public static void SetGauge(string key, double value)
        {
            Gauges[key] = value;
        }

        public static long GetCounter(string key)
        {
            return Counters.TryGetValue(key, out var value) ? value : 0L;
        }

        public static double GetGauge(string key)
        {
            return Gauges.TryGetValue(key, out var value) ? value : 0d;
        }

        public static string DumpSnapshotToFile(string modId, string reason = "manual")
        {
            var snapshot = new DiagnosticsSnapshot
            {
                ModId = modId,
                Reason = reason,
                SnapshotSequence = Interlocked.Increment(ref _snapshotSequence),
                CreatedAtUtc = DateTime.UtcNow,
                SessionStartedAtUtc = StartedAtUtc,
                UptimeSeconds = (DateTime.UtcNow - StartedAtUtc).TotalSeconds,
                Counters = Counters.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Gauges = Gauges.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VintagestoryData",
                "Logs",
                "VigorDiagnostics"
            );

            Directory.CreateDirectory(logDir);
            string sessionDir = GetOrCreateSessionDirectory(logDir, modId);

            string filePath = Path.Combine(
                sessionDir,
                $"{snapshot.SnapshotSequence:D4}-{reason}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
            );

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
            return filePath;
        }

        private static string GetOrCreateSessionDirectory(string rootLogDir, string modId)
        {
            lock (SessionLock)
            {
                if (!string.IsNullOrEmpty(_sessionDirectoryPath))
                {
                    return _sessionDirectoryPath;
                }

                string sessionName = $"{modId}-session-{StartedAtUtc:yyyyMMdd-HHmmss}";
                _sessionDirectoryPath = Path.Combine(rootLogDir, sessionName);
                Directory.CreateDirectory(_sessionDirectoryPath);
                return _sessionDirectoryPath;
            }
        }

        private sealed class DiagnosticsSnapshot
        {
            public string ModId { get; set; }
            public string Reason { get; set; }
            public int SnapshotSequence { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime SessionStartedAtUtc { get; set; }
            public double UptimeSeconds { get; set; }
            public Dictionary<string, long> Counters { get; set; }
            public Dictionary<string, double> Gauges { get; set; }
        }
    }
}
