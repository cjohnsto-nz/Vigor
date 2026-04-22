using System;
using System.Collections.Generic;
using Vigor.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vigor.Client
{
    /// <summary>
    /// Original server-driven interpolation model retained as a compatibility fallback.
    /// </summary>
    public class LegacyClientStaminaPredictor : IClientStaminaPredictor
    {
        public event Action<float, float, bool> OnStaminaChanged;

        private readonly ICoreClientAPI _api;
        private readonly VigorConfig _config;

        private float _displayStamina;
        private float _displayMaxStamina;
        private bool _displayIsExhausted;

        private float _serverStamina;
        private float _serverMaxStamina;
        private bool _serverIsExhausted;
        private long _lastServerUpdateTime;

        private float _smoothingThresholdUp;
        private float _smoothingThresholdDown;
        private float _adaptiveSmoothingSpeed;

        private readonly Queue<float> _recentChangeRates = new();
        private const int MaxChangeSamples = 3;
        private float _lastServerStamina;
        private long _lastServerUpdateTimeMs;

        private long _tickId;

        public float CurrentRecoveryThreshold => _displayMaxStamina * _config.StaminaRequiredToRecoverPercent;
        public string ModeName => "LegacyInterpolation";

        public LegacyClientStaminaPredictor(ICoreClientAPI capi, VigorConfig config)
        {
            _api = capi;
            _config = config;

            _displayStamina = config.MaxStamina;
            _displayMaxStamina = config.MaxStamina;
            _displayIsExhausted = false;

            _serverStamina = config.MaxStamina;
            _serverMaxStamina = config.MaxStamina;
            _serverIsExhausted = false;
            _lastServerUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _smoothingThresholdUp = config.InterpolationThresholdUp;
            _smoothingThresholdDown = config.InterpolationThresholdDown;
            _adaptiveSmoothingSpeed = 8.0f;

            _lastServerStamina = _serverStamina;
            _lastServerUpdateTimeMs = _lastServerUpdateTime;

            if (config.DebugMode)
            {
                _api.Logger.Debug($"[vigor] Legacy interpolation initialized: display={_displayStamina:F2}, server={_serverStamina:F2}, thresholds=up:{_smoothingThresholdUp:F1}/down:{_smoothingThresholdDown:F1}");
            }
        }

        public void UpdatePrediction(float deltaTime)
        {
            var player = _api.World?.Player?.Entity as EntityPlayer;
            if (player == null) return;

            if (player.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative) return;

            InterpolateTowardsServer(deltaTime);
            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
            _tickId++;
        }

        private void InterpolateTowardsServer(float deltaTime)
        {
            float staminaDiff = _serverStamina - _displayStamina;
            float maxStaminaDiff = _serverMaxStamina - _displayMaxStamina;

            float currentThreshold = staminaDiff > 0 ? _smoothingThresholdUp : _smoothingThresholdDown;
            if (Math.Abs(staminaDiff) > currentThreshold)
            {
                _displayStamina = _serverStamina;
            }
            else if (Math.Abs(staminaDiff) > 0.01f)
            {
                float smoothingFactor = Math.Min(1.0f, _adaptiveSmoothingSpeed * deltaTime / 10.0f);
                _displayStamina += staminaDiff * smoothingFactor;
            }

            if (Math.Abs(maxStaminaDiff) > 0.01f)
            {
                float maxInterpolationStep = _adaptiveSmoothingSpeed * deltaTime;
                float maxMoveAmount = Math.Sign(maxStaminaDiff) * Math.Min(Math.Abs(maxStaminaDiff), maxInterpolationStep);
                _displayMaxStamina += maxMoveAmount;
            }

            _displayIsExhausted = _serverIsExhausted;
            _displayStamina = Math.Max(0, Math.Min(_displayStamina, _displayMaxStamina));
        }

        public void ReconcileWithServer(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            long currentTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (_lastServerUpdateTimeMs > 0)
            {
                float staminaChange = serverStamina - _lastServerStamina;
                float timeDeltaSeconds = (currentTimeMs - _lastServerUpdateTimeMs) / 1000f;

                float adaptiveThreshold = staminaChange > 0 ? _smoothingThresholdUp : _smoothingThresholdDown;
                if (timeDeltaSeconds > 0 && Math.Abs(staminaChange) <= adaptiveThreshold)
                {
                    float changeRate = Math.Abs(staminaChange) / timeDeltaSeconds;
                    _recentChangeRates.Enqueue(changeRate);
                    if (_recentChangeRates.Count > MaxChangeSamples)
                    {
                        _recentChangeRates.Dequeue();
                    }

                    if (_recentChangeRates.Count > 0)
                    {
                        float averageChangeRate = 0f;
                        foreach (float rate in _recentChangeRates)
                        {
                            averageChangeRate += rate;
                        }

                        averageChangeRate /= _recentChangeRates.Count;
                        _adaptiveSmoothingSpeed = Math.Max(8.0f, averageChangeRate * 1.5f);
                    }
                }
            }

            _lastServerStamina = _serverStamina;
            _lastServerUpdateTimeMs = currentTimeMs;
            _serverStamina = serverStamina;
            _serverMaxStamina = serverMaxStamina;
            _serverIsExhausted = serverIsExhausted;
            _lastServerUpdateTime = currentTimeMs;
        }

        public void ForceSync(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            _displayStamina = serverStamina;
            _displayMaxStamina = serverMaxStamina;
            _displayIsExhausted = serverIsExhausted;

            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
        }
    }
}
