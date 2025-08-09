using System;
using Vigor.Behaviors;
using Vigor.Config;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vigor.Utils;

namespace Vigor.Client
{
    /// <summary>
    /// Handles client-side stamina bar smoothing through server-based interpolation.
    /// Smooths small changes (gradual drain) but allows large instant jumps (jump costs, tools).
    /// </summary>
    public class ClientStaminaPredictor
    {
        public event Action<float, float, bool> OnStaminaChanged;
        
        private readonly ICoreClientAPI _api;
        private readonly VigorConfig _config;
        
        // Current display values (what the UI shows)
        private float _displayStamina;
        private float _displayMaxStamina;
        private bool _displayIsExhausted;
        
        // Server state tracking
        private float _serverStamina;
        private float _serverMaxStamina;
        private bool _serverIsExhausted;
        private long _lastServerUpdateTime;
        
        // Interpolation settings
        private float _smoothingThresholdUp; // Changes smaller than this get smoothed (recovery)
        private float _smoothingThresholdDown; // Changes smaller than this get smoothed (drains)
        private float _adaptiveSmoothingSpeed; // Adaptive interpolation speed based on recent change rate
        
        // Adaptive smoothing tracking
        private readonly Queue<float> _recentChangeRates = new Queue<float>();
        private const int MAX_CHANGE_SAMPLES = 3; // Track last 3 server updates for fast adaptation
        private float _lastServerStamina;
        private long _lastServerUpdateTimeMs;
        
        // Performance tracking
        private long _tickId = 0;
        
        public ClientStaminaPredictor(ICoreClientAPI capi, VigorConfig config)
        {
            _api = capi;
            _config = config;
            
            // Initialize display state
            _displayStamina = config.MaxStamina;
            _displayMaxStamina = config.MaxStamina;
            _displayIsExhausted = false;
            
            // Initialize server state tracking
            _serverStamina = config.MaxStamina;
            _serverMaxStamina = config.MaxStamina;
            _serverIsExhausted = false;
            _lastServerUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Configure interpolation from config
            _smoothingThresholdUp = config.InterpolationThresholdUp; // High threshold for recovery (going up) - smooth most recovery
            _smoothingThresholdDown = config.InterpolationThresholdDown; // Low threshold for drains (going down) - instant feedback for jumps/tools
            _adaptiveSmoothingSpeed = 8.0f; // Initial smoothing speed, will adapt quickly based on change rate
            
            // Initialize adaptive tracking
            _lastServerStamina = _serverStamina;
            _lastServerUpdateTimeMs = _lastServerUpdateTime;
            
            if (config.DebugMode)
            {
                _api.Logger.Debug($"[vigor] Interpolation initialized: display={_displayStamina:F2}, server={_serverStamina:F2}, thresholds=up:{_smoothingThresholdUp:F1}/down:{_smoothingThresholdDown:F1}");
            }
        }
        
        /// <summary>
        /// Updates the display stamina through server-based interpolation
        /// </summary>
        public void UpdatePrediction(float deltaTime)
        {
            var player = _api.World?.Player?.Entity as EntityPlayer;
            if (player == null) return;
            
            // Skip if in creative mode
            if (player.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            
            // Interpolate towards server values
            InterpolateTowardsServer(deltaTime);
            
            // Notify listeners of display changes
            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
            
            _tickId++;
        }
        
        /// <summary>
        /// Interpolates display values towards server values with smart smoothing
        /// </summary>
        private void InterpolateTowardsServer(float deltaTime)
        {
            // Calculate differences from server
            float staminaDiff = _serverStamina - _displayStamina;
            float maxStaminaDiff = _serverMaxStamina - _displayMaxStamina;
            
            // Handle large jumps instantly using directional thresholds
            float currentThreshold = staminaDiff > 0 ? _smoothingThresholdUp : _smoothingThresholdDown;
            if (Math.Abs(staminaDiff) > currentThreshold)
            {
                // Large change - apply instantly
                _displayStamina = _serverStamina;
                if (_config.DebugMode)
                {
                    _api.Logger.Debug($"[vigor] INSTANT: diff={staminaDiff:F2} > threshold={currentThreshold:F1} ({(staminaDiff > 0 ? "up" : "down")})");
                }
            }
            else if (Math.Abs(staminaDiff) > 0.01f)
            {
                // Small change - exponential smoothing for natural motion
                float smoothingFactor = Math.Min(1.0f, _adaptiveSmoothingSpeed * deltaTime / 10.0f); // Normalize speed to smoothing factor
                float moveAmount = staminaDiff * smoothingFactor;
                _displayStamina += moveAmount;
                
                if (_config.DebugMode)
                {
                    _api.Logger.Debug($"[vigor] SMOOTH: diff={staminaDiff:F2}, step={moveAmount:F2}, factor={smoothingFactor:F3}, speed={_adaptiveSmoothingSpeed:F2}");
                }
            }
            
            // Handle max stamina changes (usually from nutrition)
            if (Math.Abs(maxStaminaDiff) > 0.01f)
            {
                // Max stamina changes are typically gradual, so smooth them
                float maxInterpolationStep = _adaptiveSmoothingSpeed * deltaTime;
                float maxMoveAmount = Math.Sign(maxStaminaDiff) * Math.Min(Math.Abs(maxStaminaDiff), maxInterpolationStep);
                _displayMaxStamina += maxMoveAmount;
            }
            
            // Exhaustion state follows server immediately (important for gameplay)
            _displayIsExhausted = _serverIsExhausted;
            
            // Clamp display values
            _displayStamina = Math.Max(0, Math.Min(_displayStamina, _displayMaxStamina));
        }
        
        /// <summary>
        /// Updates server state for interpolation when new server data arrives
        /// </summary>
        public void ReconcileWithServer(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            long currentTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Calculate change rate for adaptive smoothing (ignore outliers like jumps)
            if (_lastServerUpdateTimeMs > 0)
            {
                float staminaChange = serverStamina - _lastServerStamina;
                float timeDeltaSeconds = (currentTimeMs - _lastServerUpdateTimeMs) / 1000f;
                
                float adaptiveThreshold = staminaChange > 0 ? _smoothingThresholdUp : _smoothingThresholdDown;
                if (timeDeltaSeconds > 0 && Math.Abs(staminaChange) <= adaptiveThreshold)
                {
                    // Only track gradual changes for adaptive speed (ignore jumps/outliers)
                    float changeRate = Math.Abs(staminaChange) / timeDeltaSeconds;
                    
                    // Debug: Log when we track a change for adaptive smoothing
                    _api.Logger.Debug($"[vigor] TRACKED: change={staminaChange:F2}, rate={changeRate:F2}/s, threshold={adaptiveThreshold:F1} ({(staminaChange > 0 ? "up" : "down")})");
                    
                    _recentChangeRates.Enqueue(changeRate);
                    if (_recentChangeRates.Count > MAX_CHANGE_SAMPLES)
                    {
                        _recentChangeRates.Dequeue();
                    }
                    
                    // Calculate adaptive smoothing speed based on recent average change rate
                    if (_recentChangeRates.Count > 0)
                    {
                        float averageChangeRate = 0f;
                        foreach (float rate in _recentChangeRates)
                        {
                            averageChangeRate += rate;
                        }
                        averageChangeRate /= _recentChangeRates.Count;
                        
                        // Set smoothing speed to match the average change rate (with buffer for responsiveness)
                        _adaptiveSmoothingSpeed = Math.Max(8.0f, averageChangeRate * 1.5f);
                        
                        if (_config.DebugMode && _tickId % 60 == 0)
                        {
                            _api.Logger.Debug($"[vigor] Adaptive speed: avg={averageChangeRate:F2}/s, speed={_adaptiveSmoothingSpeed:F2}/s, samples={_recentChangeRates.Count}");
                        }
                    }
                }
                else if (timeDeltaSeconds > 0)
                {
                    // Debug: Log when we ignore a change as an outlier
                    if (_config.DebugMode)
                    {
                        _api.Logger.Debug($"[vigor] OUTLIER: change={staminaChange:F2}, rate={Math.Abs(staminaChange)/timeDeltaSeconds:F2}/s, threshold={adaptiveThreshold:F1} ({(staminaChange > 0 ? "up" : "down")})");
                    }
                }
            }
            
            // Update server state tracking
            _lastServerStamina = _serverStamina; // Store previous value
            _lastServerUpdateTimeMs = currentTimeMs;
            _serverStamina = serverStamina;
            _serverMaxStamina = serverMaxStamina;
            _serverIsExhausted = serverIsExhausted;
            _lastServerUpdateTime = currentTimeMs;
            
            // Calculate change magnitude for debugging
            float displayStaminaChange = Math.Abs(_displayStamina - serverStamina);
            
            if (_config.DebugMode && displayStaminaChange > 0.1f)
            {
                System.Console.WriteLine($"[Vigor Server Update] New server values: stamina={serverStamina:F2}, max={serverMaxStamina:F2}, exhausted={serverIsExhausted}");
                System.Console.WriteLine($"[Vigor Server Update] Display delta: {displayStaminaChange:F2}, thresholds: up={_smoothingThresholdUp:F1}/down={_smoothingThresholdDown:F1}");
            }
        }
        
        /// <summary>
        /// Forces client state to match server state exactly (for major corrections)
        /// </summary>
        public void ForceSync(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            _displayStamina = serverStamina;
            _displayMaxStamina = serverMaxStamina;
            _displayIsExhausted = serverIsExhausted;
            
            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
        }
    }
}
