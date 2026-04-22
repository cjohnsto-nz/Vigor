using System;
using Vigor.Config;
using Vigor.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vigor.Client
{
    /// <summary>
    /// Predicts the local player's stamina using the same stamina rules as the server,
    /// then reconciles gently against authoritative server packets.
    /// </summary>
    public class ClientStaminaPredictor : IClientStaminaPredictor
    {
        private const float IdleMotionThresholdSq = 0.0001f;
        private const float LowerBoundaryEpsilon = 0.05f;
        private const int JumpCorrectionGraceMs = 175;

        public event Action<float, float, bool> OnStaminaChanged;

        private readonly ICoreClientAPI _api;
        private readonly VigorConfig _config;
        private readonly VigorNutritionBonuses _nutritionBonuses = new();

        private float _displayStamina;
        private float _displayMaxStamina;
        private bool _displayIsExhausted;
        private float _displayRecoveryThreshold;

        private float _serverStamina;
        private float _serverMaxStamina;
        private bool _serverIsExhausted;
        private bool _hasAuthoritativeState;
        private long _lastServerUpdateTimeMs;

        private float _timeSinceLastFatiguingAction;
        private bool _isInitialExhaustion;
        private bool _jumpCooldown;
        private float _nutritionUpdateAccumulatorSeconds;
        private long _lastPredictedJumpAtMs;
        private float _pendingStaminaCorrection;
        private float _pendingMaxStaminaCorrection;
        private bool _predictedRecovering;
        private float _lastLocalStaminaDelta;
        private float _lastLocalRecoveryGain;
        private float _lastQueuedStaminaCorrection;
        private float _lastAppliedStaminaCorrection;
        private float _lastServerPacketDelta;
        private float _lastRecoveryCooldownRemaining;
        private long _lastServerPacketIntervalMs;

        private const float GenericCorrectionDeadzone = 0.15f;
        private const float RecoveryUpwardCorrectionDeadzone = 0.9f;
        private const float RecoveryUpwardCorrectionScale = 0.25f;

        public float CurrentRecoveryThreshold => _displayRecoveryThreshold;
        public string ModeName => "LocalSimulation";

        public ClientStaminaPredictor(ICoreClientAPI capi, VigorConfig config)
        {
            _api = capi;
            _config = config;

            _displayStamina = config.MaxStamina;
            _displayMaxStamina = config.MaxStamina;
            _displayIsExhausted = false;
            _displayRecoveryThreshold = config.MaxStamina * config.StaminaRequiredToRecoverPercent;

            _serverStamina = _displayStamina;
            _serverMaxStamina = _displayMaxStamina;
            _serverIsExhausted = _displayIsExhausted;
            _lastServerUpdateTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void UpdatePrediction(float deltaTime)
        {
            var player = _api.World?.Player?.Entity as EntityPlayer;
            if (player == null) return;

            if (player.Player?.WorldData.CurrentGameMode == Vintagestory.API.Common.EnumGameMode.Creative)
            {
                _displayMaxStamina = _config.MaxStamina;
                _displayStamina = _displayMaxStamina;
                _displayIsExhausted = false;
                _displayRecoveryThreshold = _displayMaxStamina * _config.StaminaRequiredToRecoverPercent;
                EmitDiagnostics();
                OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
                return;
            }

            UpdateNutritionBonuses(player, deltaTime);
            TickLocalSimulation(player, deltaTime);
            ApplyPendingServerCorrection(deltaTime);
            EmitDiagnostics();

            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
        }

        public void ReconcileWithServer(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastServerPacketIntervalMs = _lastServerUpdateTimeMs > 0 ? Math.Max(0, nowMs - _lastServerUpdateTimeMs) : 0;
            _lastServerPacketDelta = serverStamina - _serverStamina;
            _serverStamina = serverStamina;
            _serverMaxStamina = serverMaxStamina;
            _serverIsExhausted = serverIsExhausted;
            _lastServerUpdateTimeMs = nowMs;

            bool wasAuthoritative = _hasAuthoritativeState;
            _hasAuthoritativeState = true;

            VigorDiagnostics.Increment("prediction.serverPackets");

            bool boundarySnap = false;
            if (!wasAuthoritative || ShouldSnapToServer(serverStamina, serverMaxStamina, serverIsExhausted, out boundarySnap))
            {
                SnapToServer(boundarySnap);
                _pendingStaminaCorrection = 0f;
                _pendingMaxStaminaCorrection = 0f;
                _lastQueuedStaminaCorrection = 0f;
            }
            else
            {
                QueueSoftCorrection();
            }
        }

        public void ForceSync(float serverStamina, float serverMaxStamina, bool serverIsExhausted)
        {
            _serverStamina = serverStamina;
            _serverMaxStamina = serverMaxStamina;
            _serverIsExhausted = serverIsExhausted;
            _lastServerUpdateTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _hasAuthoritativeState = true;
            _pendingStaminaCorrection = 0f;
            _pendingMaxStaminaCorrection = 0f;
            _lastQueuedStaminaCorrection = 0f;
            _lastAppliedStaminaCorrection = 0f;

            ApplySnap(serverStamina, serverMaxStamina, serverIsExhausted);
            VigorDiagnostics.Increment("prediction.forcedSyncs");
            EmitDiagnostics();
            OnStaminaChanged?.Invoke(_displayStamina, _displayMaxStamina, _displayIsExhausted);
        }

        private void QueueSoftCorrection()
        {
            float staminaCorrection = _serverStamina - _displayStamina;
            float maxCorrection = _serverMaxStamina - _displayMaxStamina;

            if (Math.Abs(staminaCorrection) < GenericCorrectionDeadzone)
            {
                staminaCorrection = 0f;
            }

            if (_predictedRecovering && staminaCorrection > 0f)
            {
                if (staminaCorrection < RecoveryUpwardCorrectionDeadzone)
                {
                    staminaCorrection = 0f;
                    VigorDiagnostics.Increment("prediction.recoverySuppressions");
                }
                else
                {
                    staminaCorrection *= RecoveryUpwardCorrectionScale;
                }
            }

            _pendingStaminaCorrection = staminaCorrection;
            _pendingMaxStaminaCorrection = Math.Abs(maxCorrection) < GenericCorrectionDeadzone ? 0f : maxCorrection;
            _lastQueuedStaminaCorrection = _pendingStaminaCorrection;

            if (IsWithinJumpGraceWindow() && _pendingStaminaCorrection > 0f)
            {
                VigorDiagnostics.Increment("prediction.jumpGraceSuppressions");
            }

            if (_predictedRecovering)
            {
                if (_pendingStaminaCorrection < -0.01f)
                {
                    VigorDiagnostics.Increment("prediction.recoveryNegativeCorrections");
                }
                else if (_pendingStaminaCorrection > 0.01f)
                {
                    VigorDiagnostics.Increment("prediction.recoveryPositiveCorrections");
                }
            }

            if (Math.Abs(_pendingStaminaCorrection) > 0.01f || Math.Abs(_pendingMaxStaminaCorrection) > 0.01f)
            {
                VigorDiagnostics.Increment("prediction.softCorrections");
            }
        }

        private void UpdateNutritionBonuses(EntityPlayer player, float deltaTime)
        {
            _nutritionUpdateAccumulatorSeconds += deltaTime;
            float nutritionUpdateSeconds = Math.Max(0.05f, _config.NutritionUpdateRate / 1000f);

            if (_nutritionUpdateAccumulatorSeconds < nutritionUpdateSeconds && _displayMaxStamina > 0f)
            {
                return;
            }

            _nutritionBonuses.Update(player, _config);
            _nutritionUpdateAccumulatorSeconds = 0f;
        }

        private void TickLocalSimulation(EntityPlayer player, float deltaTime)
        {
            bool isOnGround = player.OnGround;
            float staminaAtTickStart = _displayStamina;

            if (isOnGround)
            {
                _jumpCooldown = false;
            }

            bool isPlayerIdle = player.Pos.Motion.LengthSq() < IdleMotionThresholdSq;
            bool isPlayerSitting = player.Controls.FloorSitting;
            bool physicalSprintKeyHeldThisTick = player.Controls.Sprint;
            bool isJumping = player.Controls.Jump && !isOnGround && !_jumpCooldown;
            bool isSprinting = physicalSprintKeyHeldThisTick && player.Pos.Motion.LengthSq() > _config.SprintDetectionSpeedThreshold;
            bool isSwimming = player.FeetInLiquid && !isOnGround;

            float predictedMaxStamina = _config.MaxStamina * _nutritionBonuses.MaxStaminaModifier;
            _displayMaxStamina = predictedMaxStamina;

            _timeSinceLastFatiguingAction += deltaTime;
            bool fatiguingActionThisTick = false;

            float costPerSecond = 0f;
            if (isSprinting && player.OnGround)
            {
                costPerSecond += _config.SprintStaminaCostPerSecond * _nutritionBonuses.DrainRateModifier;
            }

            if (isSwimming && !isPlayerIdle)
            {
                costPerSecond += _config.SwimStaminaCostPerSecond * _nutritionBonuses.DrainRateModifier;
            }

            if (costPerSecond > 0f)
            {
                _displayStamina -= costPerSecond * deltaTime;
                fatiguingActionThisTick = true;
            }

            if (isJumping)
            {
                _displayStamina -= _config.JumpStaminaCost * _nutritionBonuses.JumpCostModifier;
                _jumpCooldown = true;
                fatiguingActionThisTick = true;
                _lastPredictedJumpAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                VigorDiagnostics.Increment("prediction.jumpPredictions");
            }

            if (fatiguingActionThisTick)
            {
                _timeSinceLastFatiguingAction = 0f;
            }

            if (!_displayIsExhausted && _displayStamina <= 0f)
            {
                _displayStamina = 0f;
                _displayIsExhausted = true;
                _isInitialExhaustion = true;
            }

            bool tryingToSprint = physicalSprintKeyHeldThisTick &&
                                  (player.Controls.Forward || player.Controls.Backward || player.Controls.Left || player.Controls.Right) &&
                                  !player.Controls.Sneak;

            bool tryingToMoveInWater = isSwimming &&
                                       (player.Controls.Forward || player.Controls.Backward || player.Controls.Left || player.Controls.Right || player.Controls.Jump);

            bool activityPreventsRegenerationThisTick = tryingToSprint || tryingToMoveInWater;

            float requiredCooldown = (_isInitialExhaustion ? _config.ExhaustionLossCooldownSeconds : _config.StaminaLossCooldownSeconds) *
                                     _nutritionBonuses.RecoveryDelayModifier;

            bool cooldownActive = _timeSinceLastFatiguingAction < requiredCooldown;
            _lastRecoveryCooldownRemaining = Math.Max(0f, requiredCooldown - _timeSinceLastFatiguingAction);
            if (_isInitialExhaustion && !cooldownActive)
            {
                _isInitialExhaustion = false;
            }

            bool overallRegenPreventedThisTick = fatiguingActionThisTick || activityPreventsRegenerationThisTick || cooldownActive;

            _displayRecoveryThreshold = _displayMaxStamina *
                                        _config.StaminaRequiredToRecoverPercent *
                                        _nutritionBonuses.RecoveryThresholdModifier;

            float actualStaminaGainPerSecond = 0f;
            bool regenAppliedThisTick = false;
            if (!overallRegenPreventedThisTick)
            {
                actualStaminaGainPerSecond = _config.StaminaGainPerSecond * _nutritionBonuses.RecoveryRateModifier;

                if (isPlayerIdle && player.OnGround && !player.FeetInLiquid)
                {
                    actualStaminaGainPerSecond *= _config.IdleStaminaRegenMultiplier;

                    if (isPlayerSitting)
                    {
                        actualStaminaGainPerSecond *= _config.SittingStaminaRegenMultiplier;
                    }
                }
            }

            if (!overallRegenPreventedThisTick)
            {
                float clampedDeltaTime = Math.Min(deltaTime, 0.2f);
                if (_displayIsExhausted)
                {
                    if (_displayStamina < _displayRecoveryThreshold)
                    {
                        _displayStamina += actualStaminaGainPerSecond * clampedDeltaTime;
                        regenAppliedThisTick = true;
                    }
                    else
                    {
                        _displayIsExhausted = false;
                    }
                }
                else if (_timeSinceLastFatiguingAction >= _config.StaminaLossCooldownSeconds)
                {
                    _displayStamina += actualStaminaGainPerSecond * clampedDeltaTime;
                    regenAppliedThisTick = true;
                }
            }

            _displayStamina = Math.Clamp(_displayStamina, 0f, _displayMaxStamina);
            _predictedRecovering = regenAppliedThisTick;
            _lastLocalRecoveryGain = regenAppliedThisTick ? Math.Max(0f, _displayStamina - staminaAtTickStart) : 0f;
            _lastLocalStaminaDelta = _displayStamina - staminaAtTickStart;
        }

        private void ApplyPendingServerCorrection(float deltaTime)
        {
            if (Math.Abs(_pendingStaminaCorrection) <= 0.001f && Math.Abs(_pendingMaxStaminaCorrection) <= 0.001f)
            {
                _lastAppliedStaminaCorrection = 0f;
                return;
            }

            float correctionBlendSeconds = _predictedRecovering && _pendingStaminaCorrection > 0f
                ? Math.Max(0.18f, _config.StaminaSyncIntervalSeconds * 4f)
                : Math.Max(0.06f, _config.StaminaSyncIntervalSeconds * 2f);
            float factor = Math.Min(1f, deltaTime / correctionBlendSeconds);
            float staminaStep = 0f;
            if (!(IsWithinJumpGraceWindow() && _pendingStaminaCorrection > 0f))
            {
                staminaStep = _pendingStaminaCorrection * factor;
            }

            float maxStep = _pendingMaxStaminaCorrection * factor;
            _displayStamina += staminaStep;
            _displayMaxStamina += maxStep;
            _displayStamina = Math.Clamp(_displayStamina, 0f, Math.Max(_displayMaxStamina, _serverMaxStamina));
            _pendingStaminaCorrection -= staminaStep;
            _pendingMaxStaminaCorrection -= maxStep;
            _lastAppliedStaminaCorrection = staminaStep;
        }

        private bool ShouldSnapToServer(float serverStamina, float serverMaxStamina, bool serverIsExhausted, out bool boundarySnap)
        {
            boundarySnap = false;
            bool withinJumpGrace = IsWithinJumpGraceWindow();
            bool stalePreJumpRecovery = withinJumpGrace && serverStamina > _displayStamina;

            float upperBoundaryEpsilon = Math.Max(LowerBoundaryEpsilon, serverMaxStamina * 0.01f);
            bool nearEmpty = serverStamina <= LowerBoundaryEpsilon || _displayStamina <= LowerBoundaryEpsilon;
            bool nearFull = serverMaxStamina > 0f &&
                            (serverStamina >= serverMaxStamina - upperBoundaryEpsilon ||
                             _displayStamina >= _displayMaxStamina - upperBoundaryEpsilon);

            if (stalePreJumpRecovery && (_displayStamina <= LowerBoundaryEpsilon || (_displayIsExhausted && !serverIsExhausted)))
            {
                VigorDiagnostics.Increment("prediction.jumpGraceSuppressions");
                return false;
            }

            if (nearEmpty || serverIsExhausted != _displayIsExhausted)
            {
                boundarySnap = true;
                return true;
            }

            if (nearFull && !stalePreJumpRecovery)
            {
                boundarySnap = true;
                return true;
            }

            if (stalePreJumpRecovery)
            {
                VigorDiagnostics.Increment("prediction.jumpGraceSuppressions");
                return false;
            }

            if (Math.Abs(serverMaxStamina - _displayMaxStamina) > 0.5f)
            {
                return true;
            }

            if (Math.Abs(serverStamina - _displayStamina) >= Math.Max(0.5f, _config.ReconciliationThreshold))
            {
                return true;
            }

            return false;
        }

        private void SnapToServer(bool boundarySnap)
        {
            ApplySnap(_serverStamina, _serverMaxStamina, _serverIsExhausted);

            if (boundarySnap)
            {
                VigorDiagnostics.Increment("prediction.boundarySnaps");
            }
            else
            {
                VigorDiagnostics.Increment("prediction.hardSnaps");
            }
        }

        private bool IsWithinJumpGraceWindow()
        {
            if (_lastPredictedJumpAtMs <= 0) return false;
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastPredictedJumpAtMs <= JumpCorrectionGraceMs;
        }

        private void ApplySnap(float stamina, float maxStamina, bool isExhausted)
        {
            _displayStamina = stamina;
            _displayMaxStamina = maxStamina;
            _displayIsExhausted = isExhausted;

            if (_displayIsExhausted || _displayStamina <= LowerBoundaryEpsilon)
            {
                _timeSinceLastFatiguingAction = 0f;
                _isInitialExhaustion = _displayIsExhausted;
            }
            else if (_displayMaxStamina > 0f && _displayStamina >= _displayMaxStamina - Math.Max(LowerBoundaryEpsilon, _displayMaxStamina * 0.01f))
            {
                _timeSinceLastFatiguingAction = _config.StaminaLossCooldownSeconds;
                _isInitialExhaustion = false;
            }

            _displayRecoveryThreshold = _displayMaxStamina *
                                        _config.StaminaRequiredToRecoverPercent *
                                        Math.Max(_config.MinRecoveryThresholdModifier, _nutritionBonuses.RecoveryThresholdModifier <= 0f ? 1f : _nutritionBonuses.RecoveryThresholdModifier);
        }

        private void EmitDiagnostics()
        {
            VigorDiagnostics.SetGauge("prediction.displayStamina", _displayStamina);
            VigorDiagnostics.SetGauge("prediction.serverStamina", _serverStamina);
            VigorDiagnostics.SetGauge("prediction.error", _serverStamina - _displayStamina);
            VigorDiagnostics.SetGauge("prediction.errorAbs", Math.Abs(_serverStamina - _displayStamina));
            VigorDiagnostics.SetGauge("prediction.displayMaxStamina", _displayMaxStamina);
            VigorDiagnostics.SetGauge("prediction.recoveryThreshold", _displayRecoveryThreshold);
            VigorDiagnostics.SetGauge("prediction.pendingCorrection", _pendingStaminaCorrection);
            VigorDiagnostics.SetGauge("prediction.pendingMaxCorrection", _pendingMaxStaminaCorrection);
            VigorDiagnostics.SetGauge("prediction.isRecovering", _predictedRecovering ? 1 : 0);
            VigorDiagnostics.SetGauge("prediction.localDelta", _lastLocalStaminaDelta);
            VigorDiagnostics.SetGauge("prediction.localRecoveryGain", _lastLocalRecoveryGain);
            VigorDiagnostics.SetGauge("prediction.lastQueuedCorrection", _lastQueuedStaminaCorrection);
            VigorDiagnostics.SetGauge("prediction.lastAppliedCorrection", _lastAppliedStaminaCorrection);
            VigorDiagnostics.SetGauge("prediction.lastServerPacketDelta", _lastServerPacketDelta);
            VigorDiagnostics.SetGauge("prediction.cooldownRemaining", _lastRecoveryCooldownRemaining);
            VigorDiagnostics.SetGauge("prediction.lastServerPacketIntervalMs", _lastServerPacketIntervalMs);
            VigorDiagnostics.SetGauge("prediction.syncIntervalMs", _config.StaminaSyncIntervalSeconds * 1000f);

            if (_hasAuthoritativeState)
            {
                long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastServerUpdateTimeMs;
                VigorDiagnostics.SetGauge("prediction.lastPacketAgeMs", Math.Max(0, ageMs));
            }
        }
    }
}
