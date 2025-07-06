using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools; // Corrected namespace for Vec3f and other math utilities
using Vigor.Config;
using System;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using Vigor.Utils;

namespace Vigor.Behaviors
{
    public class EntityBehaviorVigorStamina : EntityBehavior
    {
        public const string Name = "vigorstamina";
        private VigorConfig Config => VigorModSystem.Instance.CurrentConfig;
        private ILogger Logger => VigorModSystem.Instance.Logger;
        private string ModId => VigorModSystem.Instance.ModId;
        
                // New fields for the refactored nutrition bonus system
        private VigorNutritionBonuses _nutritionBonuses;
        private float _timeSinceBonusUpdate = 1f; // Start > 1 to force immediate update on first tick

        // Nutrition modifiers are calculated using the 'hunger' attribute tree.

        private float _timeSinceLastFatiguingAction = 0f;
        private float _updateCooldown = 0f;
        private bool _jumpCooldown = false;
        private bool _wasExhaustedLastTick = false; 
        // private bool? _lastLoggedExhaustionState = null; // Unused, removed.
        private bool? _lastLoggedIdleBonusState = null; // To prevent log spam for idle bonus
        private bool? _lastLoggedSwimCostSkippedState = null; // To prevent log spam for swim cost skip
        private bool _wasOverallRegenPreventedLastTick = false; // Tracks if regen was prevented by any means last tick for debug logging
        private bool _isCurrentlySinkingLogged = false; // To prevent log spam for sinking state
        public const string ATTR_EXHAUSTED_SINKING = "vigor:exhaustedSinking";

        private const string WALK_SPEED_DEBUFF_CODE = "vigorExhaustionWalkSpeedDebuff";
        // private const float DEFAULT_SINKING_VELOCITY_PER_SECOND = 0.1f; // Replaced by ExhaustedSinkVelocityY config

        public float MaxStamina
        {
            get {
                float baseMaxStamina = StaminaTree?.GetFloat("maxStamina", Config.MaxStamina) ?? Config.MaxStamina;
                // Use the cached modifier from the new class
                return baseMaxStamina * _nutritionBonuses.MaxStaminaModifier;
            }
            set => StaminaTree?.SetFloat("maxStamina", value);
        }

        public float CurrentStamina
        {
            get => StaminaTree?.GetFloat("currentStamina", MaxStamina) ?? MaxStamina;
            set => StaminaTree?.SetFloat("currentStamina", value);
        }

        public bool IsExhausted
        {
            get => StaminaTree?.GetBool("isExhausted", false) ?? false;
            set => StaminaTree?.SetBool("isExhausted", value);
        }

        private ITreeAttribute StaminaTree => entity.WatchedAttributes.GetTreeAttribute(Name);

        public EntityBehaviorVigorStamina(Entity entity) : base(entity)
        {
            _nutritionBonuses = new VigorNutritionBonuses();
        }

        public override void OnFallToGround(Vec3d lastTerrainContact, double withYMotion)
        {
            base.OnFallToGround(lastTerrainContact, withYMotion);
            
            // Only apply jump costs when on the server side
            if (entity.World.Side != EnumAppSide.Server) return;
            
            // Calculate approximate fall height from velocity
            float fallSpeed = (float)withYMotion;
            
            // Get access to EntityPlayer which has Controls
            EntityPlayer player = entity as EntityPlayer;
            if (player == null) return;

            // Reset jump cooldown
            _jumpCooldown = false;
            
            // Debug messaging
            if (Config.DebugMode)
            {
                IServerPlayer serverPlayer = entity.World.PlayerByUid(player.PlayerUID) as IServerPlayer;
                serverPlayer?.SendMessage(GlobalConstants.GeneralChatGroup,
                    $"[{ModId} DEBUG] Landing detected via OnFallToGround. Velocity: {fallSpeed:F1}",
                    EnumChatType.Notification);
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            // Update nutrition bonuses periodically.
            _timeSinceBonusUpdate += deltaTime;
            bool maxStaminaUpdated = false;
            if (_timeSinceBonusUpdate >= 1f) // Update every second
            {
                if (entity is EntityPlayer playerForBonuses)
                {
                    _nutritionBonuses.Update(playerForBonuses, Config);
                }
                _timeSinceBonusUpdate = 0f;

                // Check if the calculated max stamina has changed and update the watched attribute if it has.
                float oldCalculatedMax = StaminaTree?.GetFloat("calculatedMaxStamina", -1) ?? -1;
                float newCalculatedMax = Config.MaxStamina * _nutritionBonuses.MaxStaminaModifier;
                if (Math.Abs(oldCalculatedMax - newCalculatedMax) > 0.01f)
                {
                    StaminaTree?.SetFloat("calculatedMaxStamina", newCalculatedMax);
                    maxStaminaUpdated = true;
                }
            }

            if (entity.World.Side == EnumAppSide.Client) return; // Server-side logic only

            if (StaminaTree == null)
            {
                entity.WatchedAttributes.SetAttribute(Name, new TreeAttribute());
                MaxStamina = Config.MaxStamina;
                CurrentStamina = MaxStamina;
                IsExhausted = false;
                _wasExhaustedLastTick = false;
                StaminaTree.SetFloat("calculatedMaxStamina", Config.MaxStamina * _nutritionBonuses.MaxStaminaModifier); // Set initial value
                MarkDirty();
                Logger.Notification($"[{ModId}] Initialized VigorStamina attributes for entity {entity.EntityId}. DebugMode: {Config.DebugMode}");
                return;
            }

            if (entity is not EntityPlayer plr || plr.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return;
            }

            // Determine if player is effectively stationary. Calculated early for use in both cost and regen logic.
            bool isPlayerIdle = plr.ServerPos.Motion.LengthSq() < 0.0001; // Threshold for being considered idle (e.g., speed < 0.01 units/sec)

            // Cache raw player inputs at the beginning of the tick.
            bool physicalSprintKeyHeldThisTick = plr.ServerControls.Sprint;

            if (IsExhausted && !_wasExhaustedLastTick) // Just became exhausted
            {
                float walkSpeedDebuffValue = -(1.0f - Config.ExhaustionWalkSpeedMultiplier);
                if (Config.ExhaustionWalkSpeedMultiplier < 0) walkSpeedDebuffValue = 0;
                else if (Config.ExhaustionWalkSpeedMultiplier >= 1.0f) walkSpeedDebuffValue = 0;
                else walkSpeedDebuffValue = Math.Max(-0.99f, -(1.0f - Config.ExhaustionWalkSpeedMultiplier));

                plr.Stats.Set("walkspeed", WALK_SPEED_DEBUFF_CODE, walkSpeedDebuffValue, false);
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Applied walk speed debuff. TargetFactor: {Config.ExhaustionWalkSpeedMultiplier}, AppliedValue: {walkSpeedDebuffValue:F2}", EnumChatType.Notification);
            }
            else if (!IsExhausted && _wasExhaustedLastTick) // Just recovered from exhaustion
            {
                plr.Stats.Remove("walkspeed", WALK_SPEED_DEBUFF_CODE);
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Removed walk speed debuff.", EnumChatType.Notification);
            }
            _wasExhaustedLastTick = IsExhausted;

            _timeSinceLastFatiguingAction += deltaTime;
            _updateCooldown -= deltaTime;

            float staminaBefore = CurrentStamina;
            bool exhaustedBefore = IsExhausted;
            bool fatiguingActionThisTick = false;
            
            // Check for fatiguing actions
            bool isJumping = plr.Controls.Jump && !plr.OnGround && !_jumpCooldown;
            bool isSprinting = physicalSprintKeyHeldThisTick && plr.ServerPos.Motion.LengthSq() > 0.01;
            bool isSwimming = plr.FeetInLiquid && !plr.OnGround;

            float costPerSecond = 0f;

            if (isSprinting && plr.OnGround)
            {
                costPerSecond += Config.SprintStaminaCostPerSecond * _nutritionBonuses.DrainRateModifier;
            }

            if (isSwimming)
            {
                if (!isPlayerIdle)
                {
                    costPerSecond += Config.SwimStaminaCostPerSecond * _nutritionBonuses.DrainRateModifier;
                }
            }

            if (costPerSecond > 0)
            {
                CurrentStamina -= costPerSecond * deltaTime;
                fatiguingActionThisTick = true;
            }

            if (isJumping)
            {
                CurrentStamina -= Config.JumpStaminaCost * _nutritionBonuses.JumpCostModifier;
                _jumpCooldown = true;
                fatiguingActionThisTick = true;
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Jump stamina cost: {Config.JumpStaminaCost * _nutritionBonuses.JumpCostModifier:F2} applied.", EnumChatType.Notification);
            }

            if (fatiguingActionThisTick)
            {
                _timeSinceLastFatiguingAction = 0f;
            }

            if (!exhaustedBefore && CurrentStamina <= 0)
            {
                CurrentStamina = 0;
                IsExhausted = true;
            }


            // --- Stamina Regeneration & Exhaustion Recovery ---
            bool tryingToSprint = physicalSprintKeyHeldThisTick && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right) && !plr.ServerControls.Sneak;
            bool tryingToMoveInWater = isSwimming && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right || plr.ServerControls.Jump);

            bool activityPreventsRegenerationThisTick = tryingToSprint || tryingToMoveInWater;
            bool overallRegenPreventedThisTick = fatiguingActionThisTick || activityPreventsRegenerationThisTick;

            if (Config.DebugMode)
            {
                if (overallRegenPreventedThisTick && !_wasOverallRegenPreventedLastTick)
                {
                    string reason = "";
                    if (fatiguingActionThisTick) reason += "fatiguingActionThisTick ";
                    if (activityPreventsRegenerationThisTick) reason += "activityPreventsRegen(sprint/swim) ";
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen PAUSED. Reason: {reason.Trim()}", EnumChatType.Notification);
                }
                else if (!overallRegenPreventedThisTick && _wasOverallRegenPreventedLastTick)
                {
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen RESUMED.", EnumChatType.Notification);
                }
                _wasOverallRegenPreventedLastTick = overallRegenPreventedThisTick;
            }

            if (!overallRegenPreventedThisTick)
            {
                float actualStaminaGainPerSecond = Config.StaminaGainPerSecond * _nutritionBonuses.RecoveryRateModifier;
                float clampedDeltaTime = Math.Min(deltaTime, 0.2f);

                if (isPlayerIdle && plr.OnGround && !plr.FeetInLiquid)
                {
                    actualStaminaGainPerSecond *= Config.IdleStaminaRegenMultiplier;
                    if (Config.DebugMode && _lastLoggedIdleBonusState != true) 
                    {
                        (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Idle stamina regen bonus ACTIVE. Rate: {actualStaminaGainPerSecond:F2}/s", EnumChatType.Notification);
                        _lastLoggedIdleBonusState = true;
                    }
                }
                else
                {
                    if (Config.DebugMode && _lastLoggedIdleBonusState == true)
                    {
                        (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Idle stamina regen bonus INACTIVE.", EnumChatType.Notification);
                        _lastLoggedIdleBonusState = false;
                    }
                }

                if (IsExhausted)
                {
                    float modifiedRecoveryThreshold = Config.StaminaRequiredToRecover * _nutritionBonuses.RecoveryThresholdModifier;
                    if (CurrentStamina < modifiedRecoveryThreshold)
                    {
                        CurrentStamina += actualStaminaGainPerSecond * clampedDeltaTime;
                    }
                    else
                    {
                        IsExhausted = false;
                        if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player RECOVERED from exhaustion. CurrentStamina: {CurrentStamina:F2}", EnumChatType.Notification);
                        _lastLoggedIdleBonusState = null;
                    }
                }
                else if (_timeSinceLastFatiguingAction >= Config.StaminaLossCooldownSeconds)
                {
                    CurrentStamina += actualStaminaGainPerSecond * clampedDeltaTime;
                }
            }

            if (CurrentStamina > MaxStamina) CurrentStamina = MaxStamina;
            if (CurrentStamina < 0) CurrentStamina = 0;

            bool staminaChanged = Math.Abs(staminaBefore - CurrentStamina) > 0.001f;
            bool exhaustionChanged = exhaustedBefore != IsExhausted;
            
            bool shouldBeSinking = IsExhausted && isSwimming;
            bool previousSinkingState = entity.WatchedAttributes.GetBool(ATTR_EXHAUSTED_SINKING, false);
            if (previousSinkingState != shouldBeSinking)
            {
                entity.WatchedAttributes.SetBool(ATTR_EXHAUSTED_SINKING, shouldBeSinking);
            }

            bool debugStateChanged = false;
            if (Config.DebugMode)
            {
                // States
                debugStateChanged |= SetDebugBool("debug_isIdle", isPlayerIdle);
                debugStateChanged |= SetDebugBool("debug_isSprinting", isSprinting);
                debugStateChanged |= SetDebugBool("debug_isSwimming", isSwimming);
                debugStateChanged |= SetDebugBool("debug_isJumping", isJumping);
                debugStateChanged |= SetDebugBool("debug_fatiguingActionThisTick", fatiguingActionThisTick);
                debugStateChanged |= SetDebugBool("debug_regenPrevented", overallRegenPreventedThisTick);

                // Values
                float modifiedRecoveryThreshold = Config.StaminaRequiredToRecover * _nutritionBonuses.RecoveryThresholdModifier;
                float actualStaminaGainPerSecond = Config.StaminaGainPerSecond * _nutritionBonuses.RecoveryRateModifier * (isPlayerIdle && plr.OnGround && !plr.FeetInLiquid ? Config.IdleStaminaRegenMultiplier : 1f);
                
                debugStateChanged |= SetDebugFloat("debug_recoveryThreshold", modifiedRecoveryThreshold);
                debugStateChanged |= SetDebugFloat("debug_staminaGainPerSecond", overallRegenPreventedThisTick ? 0 : actualStaminaGainPerSecond);
                debugStateChanged |= SetDebugFloat("debug_costPerSecond", costPerSecond);
                debugStateChanged |= SetDebugFloat("debug_timeSinceFatigue", _timeSinceLastFatiguingAction);

                // Final Modifiers
                debugStateChanged |= SetDebugFloat("debug_mod_maxStamina", _nutritionBonuses.MaxStaminaModifier);
                debugStateChanged |= SetDebugFloat("debug_mod_recoveryRate", _nutritionBonuses.RecoveryRateModifier);
                debugStateChanged |= SetDebugFloat("debug_mod_drainRate", _nutritionBonuses.DrainRateModifier);
                debugStateChanged |= SetDebugFloat("debug_mod_jumpCost", _nutritionBonuses.JumpCostModifier);
                debugStateChanged |= SetDebugFloat("debug_mod_recoveryThreshold", _nutritionBonuses.RecoveryThresholdModifier);
            }

            if (staminaChanged || exhaustionChanged || maxStaminaUpdated || previousSinkingState != shouldBeSinking || debugStateChanged)
            {
                MarkDirty();
            }
        }

        private bool SetDebugBool(string key, bool value)
        {
            if (StaminaTree == null) return false;
            bool oldValue = StaminaTree.GetBool(key);
            if (oldValue == value) return false;
            StaminaTree.SetBool(key, value);
            return true;
        }

        private bool SetDebugFloat(string key, float value)
        {
            if (StaminaTree == null) return false;
            float oldValue = StaminaTree.GetFloat(key, -999f);
            if (Math.Abs(oldValue - value) < 0.01f) return false;
            StaminaTree.SetFloat(key, value);
            return true;
        }

        private void MarkDirty()
        {
            entity.WatchedAttributes.MarkPathDirty(Name);
        }

        public override string PropertyName()
        {
            return Name;
        }
    }
}
