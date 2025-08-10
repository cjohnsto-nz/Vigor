using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools; // Corrected namespace for Vec3f and other math utilities
using Vigor.Config;
using System;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vigor.API;
using Vigor.Utils;
using Vigor.Core;

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
        
        // Batched tree attribute to prevent excessive MarkPathDirty calls
        private BatchedTreeAttribute _batchedStaminaTree;

        // Nutrition modifiers are calculated using the 'hunger' attribute tree.

        private float _timeSinceLastFatiguingAction = 0f;
        private float _updateCooldown = 0f;
        private bool _jumpCooldown = false;
        private bool _wasExhaustedLastTick = false; 
        private bool _isInitialExhaustion = false; // Tracks if the long exhaustion cooldown needs to be applied. 
        // private bool? _lastLoggedExhaustionState = null; // Unused, removed.
        private bool? _lastLoggedIdleBonusState = null; // To prevent log spam for idle bonus
        private bool _wasOverallRegenPreventedLastTick = false; // Tracks if regen was prevented by any means last tick for debug logging
        private bool _hasLoggedSwimStats = false; // To prevent log spam for swim stats
        private bool _hasLoggedWaterState = false; // To prevent log spam for water state
        
        // Obsolete batching variables removed - now handled by BatchedTreeAttribute
        
        public const string ATTR_EXHAUSTED_SINKING = "vigor:exhaustedSinking";

        private const string WALK_SPEED_DEBUFF_CODE = "vigorExhaustionWalkSpeedDebuff";
        // private const float DEFAULT_SINKING_VELOCITY_PER_SECOND = 0.1f; // Replaced by ExhaustedSinkVelocityY config

        /// <summary>
        /// Access to the stamina tree attribute with batching support
        /// </summary>
        private ITreeAttribute StaminaTree => entity.WatchedAttributes.GetTreeAttribute(Name);
        
        /// <summary>
        /// Initialize batched tree if needed
        /// </summary>
        private void EnsureBatchedTree()
        {
            if (_batchedStaminaTree == null && StaminaTree != null)
            {
                _batchedStaminaTree = new BatchedTreeAttribute(StaminaTree, entity.WatchedAttributes, Name, Config.DebugMode);
            }
        }

        public float MaxStamina
        {
            get {
                EnsureBatchedTree();
                float baseMaxStamina = _batchedStaminaTree?.GetFloat("maxStamina", Config.MaxStamina) ?? Config.MaxStamina;
                // Use the cached modifier from the new class
                return baseMaxStamina * _nutritionBonuses.MaxStaminaModifier;
            }
            set {
                EnsureBatchedTree();
                _batchedStaminaTree?.SetFloat("maxStamina", value);
            }
        }

        public float CurrentStamina
        {
            get {
                EnsureBatchedTree();
                return _batchedStaminaTree?.GetFloat("currentStamina", MaxStamina) ?? MaxStamina;
            }
            set {
                EnsureBatchedTree();
                _batchedStaminaTree?.SetFloat("currentStamina", value);
            }
        }

        public bool IsExhausted
        {
            get {
                EnsureBatchedTree();
                return _batchedStaminaTree?.GetBool("isExhausted", false) ?? false;
            }
            set {
                EnsureBatchedTree();
                _batchedStaminaTree?.SetBool("isExhausted", value);
            }
        }

        private float _lastReceivedStamina;
        private ICoreAPI api;

        public EntityBehaviorVigorStamina(Entity entity) : base(entity)
        {
            api = entity.Api;
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

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            Logger.Notification($"[{ModId} DEBUG] EntityBehaviorVigorStamina initialized for entity {entity.EntityId}.");
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

                // Update calculated max stamina for HUD display using batched tree
                EnsureBatchedTree();
                float oldCalculatedMax = _batchedStaminaTree?.GetFloat("calculatedMaxStamina", -1) ?? -1;
                float newCalculatedMax = Config.MaxStamina * _nutritionBonuses.MaxStaminaModifier;
                if (Math.Abs(oldCalculatedMax - newCalculatedMax) > 0.01f)
                {
                    // Update using batched tree to prevent immediate MarkPathDirty
                    _batchedStaminaTree?.SetFloat("calculatedMaxStamina", newCalculatedMax);
                    maxStaminaUpdated = true;
                }
            }

            if (entity.World.Side == EnumAppSide.Client) return; // Server-side logic only

            if (StaminaTree == null)
            {
                entity.WatchedAttributes.SetAttribute(Name, new TreeAttribute());
                // Initialize using batched tree with immediate sync for initialization
                EnsureBatchedTree();
                _batchedStaminaTree.SetFloat("maxStamina", Config.MaxStamina);
                _batchedStaminaTree.SetFloat("currentStamina", Config.MaxStamina);
                _batchedStaminaTree.SetBool("isExhausted", false);
                _batchedStaminaTree.SetFloat("calculatedMaxStamina", Config.MaxStamina * _nutritionBonuses.MaxStaminaModifier);
                _wasExhaustedLastTick = false;
                _batchedStaminaTree.ForceSync(); // Force immediate sync for initialization
                Logger.Notification($"[{ModId}] Initialized VigorStamina attributes for entity {entity.EntityId}. DebugMode: {Config.DebugMode}");
                return;
            }
            
            // Check if base max stamina needs updating due to config changes
            EnsureBatchedTree();
            float storedBaseMaxStamina = _batchedStaminaTree.GetFloat("maxStamina", -1);
            if (Math.Abs(storedBaseMaxStamina - Config.MaxStamina) > 0.01f)
            {
                Logger.Notification($"[{ModId}] Updating base max stamina for entity {entity.EntityId} from {storedBaseMaxStamina} to {Config.MaxStamina} due to config change");
                
                // Config updates using batched tree with immediate sync
                float currentStaminaRatio = storedBaseMaxStamina > 0 ? (_batchedStaminaTree.GetFloat("currentStamina", Config.MaxStamina) / storedBaseMaxStamina) : 1f;
                _batchedStaminaTree.SetFloat("maxStamina", Config.MaxStamina);
                _batchedStaminaTree.SetFloat("currentStamina", Config.MaxStamina * currentStaminaRatio);
                _batchedStaminaTree.SetFloat("calculatedMaxStamina", Config.MaxStamina * _nutritionBonuses.MaxStaminaModifier);
                
                maxStaminaUpdated = true;
                _batchedStaminaTree.ForceSync(); // Force immediate sync for config changes
            }

            if (entity is not EntityPlayer plr || plr.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return;
            }

            // Determine if player is effectively stationary. Calculated early for use in both cost and regen logic.
            bool isPlayerIdle = plr.ServerPos.Motion.LengthSq() < 0.0001; // Threshold for being considered idle (e.g., speed < 0.01 units/sec)

            bool isPlayerSitting = plr.ServerControls.FloorSitting; // True if player is sitting on the floor

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
            bool isSprinting = physicalSprintKeyHeldThisTick && plr.ServerPos.Motion.LengthSq() > Config.SprintDetectionSpeedThreshold;
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
                _isInitialExhaustion = true; // Set the one-time penalty flag.
            }


            // --- Stamina Regeneration & Exhaustion Recovery ---
            bool tryingToSprint = physicalSprintKeyHeldThisTick && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right) && !plr.ServerControls.Sneak;
            bool tryingToMoveInWater = isSwimming && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right || plr.ServerControls.Jump);

            bool activityPreventsRegenerationThisTick = tryingToSprint || tryingToMoveInWater;
            float requiredCooldown;
            if (_isInitialExhaustion)
            {
                requiredCooldown = Config.ExhaustionLossCooldownSeconds * _nutritionBonuses.RecoveryDelayModifier;
            }
            else
            {
                requiredCooldown = Config.StaminaLossCooldownSeconds * _nutritionBonuses.RecoveryDelayModifier;
            }

            bool cooldownActive = _timeSinceLastFatiguingAction < requiredCooldown;

            // If the initial long cooldown has just been served, clear the flag for the next fatiguing action.
            if (_isInitialExhaustion && !cooldownActive)
            {
                _isInitialExhaustion = false;
            }

            bool overallRegenPreventedThisTick = fatiguingActionThisTick || activityPreventsRegenerationThisTick || cooldownActive;

            // Calculate recovery threshold and gain rate once for both game logic and debug attributes.
            float modifiedRecoveryThreshold = MaxStamina * Config.StaminaRequiredToRecoverPercent * _nutritionBonuses.RecoveryThresholdModifier;
            float actualStaminaGainPerSecond = 0f;

            if (!overallRegenPreventedThisTick)
            {
                actualStaminaGainPerSecond = Config.StaminaGainPerSecond * _nutritionBonuses.RecoveryRateModifier;

                // If player is idle, apply idle regen multiplier
                if (isPlayerIdle && plr.OnGround && !plr.FeetInLiquid)
                {
                    actualStaminaGainPerSecond *= Config.IdleStaminaRegenMultiplier;

                    // If player is sitting, apply sitting regen multiplier
                    if (isPlayerSitting)
                    {
                        actualStaminaGainPerSecond *= Config.SittingStaminaRegenMultiplier;
                    }
                }
            }

            // --- Debug Logging ---
            if (Config.DebugMode)
            {
                if (overallRegenPreventedThisTick && !_wasOverallRegenPreventedLastTick)
                {
                    string reason = "";
                    if (fatiguingActionThisTick) reason += "fatiguingActionThisTick ";
                    if (activityPreventsRegenerationThisTick) reason += "activityPreventsRegen(sprint/swim) ";
                    if (cooldownActive) reason += "cooldown ";
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen PAUSED. Reason: {reason.Trim()}", EnumChatType.Notification);
                }
                else if (!overallRegenPreventedThisTick && _wasOverallRegenPreventedLastTick)
                {
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen RESUMED.", EnumChatType.Notification);
                }
                _wasOverallRegenPreventedLastTick = overallRegenPreventedThisTick;

                if (!overallRegenPreventedThisTick)
                {
                    if (isPlayerIdle && plr.OnGround && !plr.FeetInLiquid) {
                        if (_lastLoggedIdleBonusState != true) 
                        {
                            (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Idle stamina regen bonus ACTIVE. Rate: {actualStaminaGainPerSecond:F2}/s", EnumChatType.Notification);
                            _lastLoggedIdleBonusState = true;
                        }
                    } else {
                        if (_lastLoggedIdleBonusState == true)
                        {
                            (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Idle stamina regen bonus INACTIVE.", EnumChatType.Notification);
                            _lastLoggedIdleBonusState = false;
                        }
                    }
                }
            }

            // --- Apply Regeneration/Recovery Logic ---
            if (!overallRegenPreventedThisTick)
            {
                float clampedDeltaTime = Math.Min(deltaTime, 0.2f);
                if (IsExhausted)
                {
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

            // Use a larger threshold to avoid syncing tiny stamina changes that aren't visually meaningful
            bool staminaChanged = Math.Abs(staminaBefore - CurrentStamina) > 0.1f;
            bool exhaustionChanged = exhaustedBefore != IsExhausted;
            
            if (isSwimming)
            {
                if (!_hasLoggedWaterState)
                {
                    if (Config.DebugMode)
                    {
                        Logger.Notification($"[{ModId} DEBUG] Player swimming state check: IsExhausted={IsExhausted}, isSwimming={isSwimming}, FeetInLiquid={plr.FeetInLiquid}, OnGround={plr.OnGround}");
                    }
                    _hasLoggedWaterState = true;
                }
            }
            else
            {
                _hasLoggedWaterState = false;
            }

            bool shouldBeSinking = IsExhausted && isSwimming;

            if (shouldBeSinking)
            {
                var oxygenTree = entity.WatchedAttributes.GetTreeAttribute("oxygen");
                oxygenTree?.SetBool("hasair", false);

                if (Config.DebugMode)
                {
                    api.Logger.Debug($"Player {plr.Player.PlayerName} has no air due to exhaustion.");
                }
            }
            else
            {
                _hasLoggedSwimStats = false;
            }

            bool previousSinkingState = entity.WatchedAttributes.GetBool(ATTR_EXHAUSTED_SINKING, false);
            if (previousSinkingState != shouldBeSinking)
            {
                // Use batched tree for sinking state to prevent immediate MarkPathDirty
                _batchedStaminaTree?.SetBool(ATTR_EXHAUSTED_SINKING, shouldBeSinking);
            }

            // --- Update Debug Watched Attributes using batched tree ---
            // Debug attributes now use batched tree to prevent immediate MarkPathDirty calls
            // States
            _batchedStaminaTree?.SetBool("debug_isIdle", isPlayerIdle);
            _batchedStaminaTree?.SetBool("debug_isSitting", isPlayerSitting);
            _batchedStaminaTree?.SetBool("debug_isSprinting", isSprinting);
            _batchedStaminaTree?.SetBool("debug_isSwimming", isSwimming);
            _batchedStaminaTree?.SetBool("debug_isJumping", isJumping);
            _batchedStaminaTree?.SetBool("debug_fatiguingActionThisTick", fatiguingActionThisTick);
            _batchedStaminaTree?.SetBool("debug_regenPrevented", overallRegenPreventedThisTick);

            // Values
            _batchedStaminaTree?.SetFloat("debug_recoveryThreshold", modifiedRecoveryThreshold);
            _batchedStaminaTree?.SetFloat("debug_staminaGainPerSecond", actualStaminaGainPerSecond);
            _batchedStaminaTree?.SetFloat("debug_costPerSecond", costPerSecond);
            _batchedStaminaTree?.SetFloat("debug_timeSinceFatigue", _timeSinceLastFatiguingAction);

            // Final Modifiers
            _batchedStaminaTree?.SetFloat("debug_mod_maxStamina", _nutritionBonuses.MaxStaminaModifier);
            _batchedStaminaTree?.SetFloat("debug_mod_recoveryRate", _nutritionBonuses.RecoveryRateModifier);
            _batchedStaminaTree?.SetFloat("debug_mod_drainRate", _nutritionBonuses.DrainRateModifier);
            _batchedStaminaTree?.SetFloat("debug_mod_jumpCost", _nutritionBonuses.JumpCostModifier);
            _batchedStaminaTree?.SetFloat("debug_mod_recoveryThreshold", _nutritionBonuses.RecoveryThresholdModifier);
            _batchedStaminaTree?.SetFloat("debug_mod_recoveryDelay", _nutritionBonuses.RecoveryDelayModifier);
            
            // Batching is now handled by timer-based system, not per-tick TrySync
            // Always log profiling stats to verify batching effectiveness
            if (entity is EntityPlayer debugPlayer)
            {
                _batchedStaminaTree?.LogProfilingStats(debugPlayer.Player?.PlayerName ?? "Unknown", Config.DebugMode, api);
            }
        }

        // SetDebugBool and SetDebugFloat methods removed - now using batched tree interface directly

        // MarkDirty method removed - now handled by BatchedTreeAttribute.TrySync()
        
        // LogSyncStats method removed - profiling now handled by BatchedTreeAttribute
        
        /// <summary>
        /// Resets the fatigue timer, indicating a fatiguing action has occurred
        /// For use by the public API
        /// </summary>
        public void ResetFatigueTimer()
        {
            _timeSinceLastFatiguingAction = 0f;
        }

        public override string PropertyName()
        {
            return Name;
        }
    }
}
