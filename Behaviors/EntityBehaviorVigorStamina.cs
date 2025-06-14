using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools; // Corrected namespace for Vec3f and other math utilities
using Vigor.Config;
using System;
using Vintagestory.API.Server;
using Vintagestory.API.Config;

namespace Vigor.Behaviors
{
    public class EntityBehaviorVigorStamina : EntityBehavior
    {
        public const string Name = "vigorstamina";
        private VigorConfig Config => VigorModSystem.Instance.CurrentConfig;
        private ILogger Logger => VigorModSystem.Instance.Logger;
        private string ModId => VigorModSystem.Instance.ModId;

        private float _timeSinceLastFatiguingAction = 0f;
        private float _updateCooldown = 0f;
        private bool _jumpCooldown = false;
        private bool _wasExhaustedLastTick = false; 
        // private bool? _lastLoggedExhaustionState = null; // Unused, removed.
        private bool? _lastLoggedIdleBonusState = null; // To prevent log spam for idle bonus
        private bool? _lastLoggedSwimCostSkippedState = null; // To prevent log spam for swim cost skip
        private bool _wasOverallRegenPreventedLastTick = false; // Tracks if regen was prevented by any means last tick for debug logging
        private bool _isCurrentlySinkingLogged = false; // To prevent log spam for sinking state

        private const string WALK_SPEED_DEBUFF_CODE = "vigorExhaustionWalkSpeedDebuff";
        // private const float DEFAULT_SINKING_VELOCITY_PER_SECOND = 0.1f; // Replaced by ExhaustedSinkVelocityY config

        public float MaxStamina
        {
            get => StaminaTree?.GetFloat("maxStamina", Config.MaxStamina) ?? Config.MaxStamina;
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
            if (entity.World.Side == EnumAppSide.Client) return; // Server-side logic only

            if (StaminaTree == null)
            {
                entity.WatchedAttributes.SetAttribute(Name, new TreeAttribute());
                MaxStamina = Config.MaxStamina;
                CurrentStamina = MaxStamina;
                IsExhausted = false;
                _wasExhaustedLastTick = false;
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
            // This ensures that logic for preventing regeneration is based on actual player intent for this tick,
            // before any server-side control overrides (like setting plr.Controls.Sprint = false due to exhaustion) might alter ServerControls.
            bool physicalSprintKeyHeldThisTick = plr.ServerControls.Sprint;

            if (IsExhausted && !_wasExhaustedLastTick) // Just became exhausted
            {
                // Apply walk speed debuff
                // Assuming Config.ExhaustionWalkSpeedMultiplier is the desired final speed (e.g., 0.5 for 50% speed)
                // If stat is additive percentage (e.g. -0.5 for -50% speed)
                float walkSpeedDebuffValue = -(1.0f - Config.ExhaustionWalkSpeedMultiplier);
                // Ensure the reduction doesn't go beyond -1 (which would be -100% speed, or 0 speed)
                // and also doesn't result in a speed increase if multiplier is > 1 by mistake.
                if (Config.ExhaustionWalkSpeedMultiplier < 0) walkSpeedDebuffValue = 0; // Prevent speed increase from negative multiplier
                else if (Config.ExhaustionWalkSpeedMultiplier >= 1.0f) walkSpeedDebuffValue = 0; // Prevent speed increase, effectively no change or slight reduction if base is not exactly 0 for 'no effect'
                else walkSpeedDebuffValue = Math.Max(-0.99f, -(1.0f - Config.ExhaustionWalkSpeedMultiplier)); // Cap at -99% speed reduction

                plr.Stats.Set("walkspeed", WALK_SPEED_DEBUFF_CODE, walkSpeedDebuffValue, false);
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Applied walk speed debuff (Category: walkspeed). TargetFactor: {Config.ExhaustionWalkSpeedMultiplier}, AppliedValue: {walkSpeedDebuffValue:F2}", EnumChatType.Notification);
            }
            else if (!IsExhausted && _wasExhaustedLastTick) // Just recovered from exhaustion
            {
                // Remove walk speed debuff
                plr.Stats.Remove("walkspeed", WALK_SPEED_DEBUFF_CODE);
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Removed walk speed debuff (Category: walkspeed).", EnumChatType.Notification);
            }
            _wasExhaustedLastTick = IsExhausted;

            if (IsExhausted)
            {
                // plr.Controls.Sprint = false; // Removed: Allow continued (debuffed) sprint attempts
                // plr.Controls.Jump = false; // Removed: Allow jumps during exhaustion to incur stamina cost

                if (plr.FeetInLiquid && !plr.OnGround)
                {
                    // Apply sinking effect only if in liquid and not on ground
                    plr.ServerPos.Motion.Y = Config.ExhaustedSinkVelocityY;
                    if (Config.DebugMode && !_isCurrentlySinkingLogged)
                    {
                        (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] SINKING STARTED (Not OnGround). Motion.Y set to: {Config.ExhaustedSinkVelocityY}", EnumChatType.Notification);
                        _isCurrentlySinkingLogged = true;
                    }
                }
                else if (plr.FeetInLiquid && plr.OnGround && !_isCurrentlySinkingLogged) // In liquid but on ground, log once if debug mode is on
                {
                    if (Config.DebugMode)
                    {
                         (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Exhausted in water but OnGround. Sinking not applied.", EnumChatType.Notification);
                        // We can set _isCurrentlySinkingLogged = true here if we don't want this message to repeat
                        // For now, let it repeat if the state flip-flops to see if OnGround is stable
                    }
                }
                else // Not exhausted or not in liquid, but was sinking
                {
                    if (_isCurrentlySinkingLogged) // Was sinking, now conditions not met
                    {
                        if (Config.DebugMode)
                        {
                            (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] SINKING STOPPED (no longer exhausted or not in liquid).", EnumChatType.Notification);
                        }
                        _isCurrentlySinkingLogged = false;
                    }
                }
            }

            _timeSinceLastFatiguingAction += deltaTime;
            _updateCooldown -= deltaTime;

            bool isSprinting = plr.Controls.Sprint && (plr.Controls.Forward || plr.Controls.Backward || plr.Controls.Left || plr.Controls.Right) && !plr.Controls.Sneak && !IsExhausted;
            bool isSwimming = plr.FeetInLiquid; // Player is in water (feet are in liquid)
            bool isJumping = plr.Controls.Jump && !_jumpCooldown && plr.OnGround;

            bool fatiguingActionThisTick = false;
            float staminaBefore = CurrentStamina;
            bool exhaustedBefore = IsExhausted;

            float costPerSecond = 0f;

            if (isSprinting && plr.OnGround)
            {
                costPerSecond += Config.SprintStaminaCostPerSecond;
            }

            if (isSwimming && !IsExhausted) // Potentially apply swim cost if in water and not exhausted
            {
                if (!isPlayerIdle) // Apply cost only if NOT idle
                {
                    costPerSecond += Config.SwimStaminaCostPerSecond;
                    if (Config.DebugMode && _lastLoggedSwimCostSkippedState == true)
                    {
                        (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Swim stamina cost RESUMED (player moving in water).", EnumChatType.Notification);
                        _lastLoggedSwimCostSkippedState = false;
                    }
                }
                else // Player is idle in water, and not exhausted: skip swim cost
                {
                    if (Config.DebugMode && _lastLoggedSwimCostSkippedState != true)
                    {
                        (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Swim stamina cost PAUSED (player idle in water).", EnumChatType.Notification);
                        _lastLoggedSwimCostSkippedState = true;
                    }
                }
            }
            else // Not swimming, or exhausted while swimming: reset the "skipped swim cost" log state
            {
                if (Config.DebugMode && _lastLoggedSwimCostSkippedState == true) // Was skipping, now not in a skippable swim context
                {
                     (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Swim stamina cost context ended (not swimming or exhausted).", EnumChatType.Notification);
                }
                _lastLoggedSwimCostSkippedState = null; 
            }

            if (costPerSecond > 0)
            {
                CurrentStamina -= costPerSecond * deltaTime;
                fatiguingActionThisTick = true;
            }

            if (isJumping)
            {
                CurrentStamina -= Config.JumpStaminaCost;
                _jumpCooldown = true;
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Jump stamina cost: {Config.JumpStaminaCost} applied (Category: jump).", EnumChatType.Notification);
            }

            if (fatiguingActionThisTick)
            {
                _timeSinceLastFatiguingAction = 0f;
            }

            if (!exhaustedBefore && CurrentStamina <= Config.StaminaExhaustionThreshold)
            {
                CurrentStamina = Config.StaminaExhaustionThreshold;
                IsExhausted = true;
            }


            // --- Stamina Regeneration & Exhaustion Recovery ---

            // Determine if player is *attempting* actions that should prevent regeneration, based on cached physical inputs.
            bool tryingToSprint = physicalSprintKeyHeldThisTick && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right) && !plr.ServerControls.Sneak;
            bool tryingToMoveInWater = plr.FeetInLiquid && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right || plr.ServerControls.Jump);
            // bool tryingToJump = physicalJumpKeyHeldThisTick && !plr.OnGround && !_jumpCooldown; // Player is holding jump, is in air, and jump isn't on mod cooldown.

            bool activityPreventsRegenerationThisTick = tryingToSprint || tryingToMoveInWater; // Removed tryingToJump

            // Overall condition: Regeneration is prevented if a fatiguing action just happened OR is currently being attempted.
            bool overallRegenPreventedThisTick = fatiguingActionThisTick || activityPreventsRegenerationThisTick;

            if (Config.DebugMode)
            {
                if (overallRegenPreventedThisTick && !_wasOverallRegenPreventedLastTick)
                {
                    string reason = "";
                    if (fatiguingActionThisTick) reason += "fatiguingActionThisTick=true ";
                    if (activityPreventsRegenerationThisTick)
                    {
                        reason += "activityPreventsRegenThisTick=true (";
                        if (tryingToSprint) reason += "tryingToSprint=true ";
                        if (tryingToMoveInWater) reason += "tryingToMoveInWater=true";
                        reason = reason.TrimEnd() + ") ";
                    }
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen PAUSED. Reason: {reason.Trim()}", EnumChatType.Notification);
                }
                else if (!overallRegenPreventedThisTick && _wasOverallRegenPreventedLastTick)
                {
                    (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Stamina regen RESUMED.", EnumChatType.Notification);
                }
                _wasOverallRegenPreventedLastTick = overallRegenPreventedThisTick; // Store state for next tick's comparison
            }

            if (!overallRegenPreventedThisTick)
            {
                float actualStaminaGainPerSecond = Config.StaminaGainPerSecond;
                float clampedDeltaTime = Math.Min(deltaTime, 0.2f); // Cap deltaTime at 0.2s (5 FPS) for regen calculation to prevent excessive gain during lag spikes

                // isPlayerIdle is now calculated earlier in the tick

                if (isPlayerIdle)
                {
                    actualStaminaGainPerSecond *= Config.IdleStaminaRegenMultiplier;
                    if (Config.DebugMode && actualStaminaGainPerSecond != Config.StaminaGainPerSecond && _lastLoggedIdleBonusState != true) 
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
                    if (CurrentStamina < Config.StaminaRequiredToRecover) CurrentStamina += actualStaminaGainPerSecond * clampedDeltaTime;
                    // else { IsExhausted already handled by the main check }

                    // Debug message for exhaustion recovery state change;
                    if (CurrentStamina >= Config.StaminaRequiredToRecover)
                    {
                        IsExhausted = false;
                        if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player RECOVERED from exhaustion. CurrentStamina: {CurrentStamina:F2}", EnumChatType.Notification);
                        _lastLoggedIdleBonusState = null; // Reset on exhaustion state change to ensure fresh log
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

            if (staminaChanged || exhaustionChanged)
            {
                MarkDirty();
            }
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
