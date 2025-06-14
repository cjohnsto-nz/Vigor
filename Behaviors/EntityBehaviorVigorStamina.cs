using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
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

        // --- Stamina Properties ---
        // These properties get/set values directly from the entity's WatchedAttributes tree.
        // This is the single source of truth for stamina state.
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



        public override void OnGameTick(float deltaTime)
        {
            // One-time initialization on the server
            if (StaminaTree == null)
            {
                entity.WatchedAttributes.SetAttribute(Name, new TreeAttribute());
                MaxStamina = Config.MaxStamina;
                CurrentStamina = MaxStamina;
                IsExhausted = false;
                MarkDirty();
                Logger.Warning($"[{ModId}] Initialized VigorStamina attributes for entity {entity.EntityId}. DebugMode: {Config.DebugMode}");
                return; // Do nothing else on this tick
            }

            if (entity.World.Side == EnumAppSide.Client) return;
            if (entity is not EntityPlayer plr || plr.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return;
            }

            // Reset jump cooldown when player is on the ground
            if (plr.OnGround)
            {
                _jumpCooldown = false;
            }

            // --- Exhaustion Effects ---
            if (IsExhausted)
            {
                plr.Controls.Sprint = false;
                plr.Controls.Jump = false;
                if (plr.FeetInLiquid)
                {
                    // Apply a downward force to simulate sinking
                    plr.Pos.Motion.Y -= Config.SinkingVelocity;
                }
            }

            _timeSinceLastFatiguingAction += deltaTime;
            _updateCooldown -= deltaTime;

            // --- Determine Player Actions for Stamina Calculation ---
            // Note: We use the potentially modified controls here. If exhausted, sprint/jump will be false.
            bool isSprinting = plr.Controls.Sprint && (plr.Controls.Forward || plr.Controls.Backward || plr.Controls.Left || plr.Controls.Right) && !plr.Controls.Sneak;
            bool isSwimming = plr.FeetInLiquid;
            bool isJumping = plr.Controls.Jump && !_jumpCooldown && plr.OnGround;

            bool fatiguingActionThisTick = false;
            float staminaBefore = CurrentStamina;
            bool exhaustedBefore = IsExhausted;

            // --- Stamina Depletion ---
            if (isSprinting || (isSwimming && !IsExhausted))
            {
                float costPerSecond = 0f;
                if (isSprinting) costPerSecond += Config.SprintStaminaCostPerSecond;
                if (isSwimming) costPerSecond += Config.SwimStaminaCostPerSecond;
                CurrentStamina -= costPerSecond * deltaTime;
                fatiguingActionThisTick = true;
            }

            if (isJumping) // isJumping is only true if not exhausted
            {
                CurrentStamina -= Config.JumpStaminaCost;
                fatiguingActionThisTick = true;
                _jumpCooldown = true; // Prevent continuous cost
            }

            if (fatiguingActionThisTick)
            {
                _timeSinceLastFatiguingAction = 0f;
            }

            // --- Update Exhaustion State (Post-Depletion) ---
            if (!exhaustedBefore && CurrentStamina <= Config.StaminaExhaustionThreshold)
            {
                CurrentStamina = Config.StaminaExhaustionThreshold;
                IsExhausted = true;
                if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player EXHAUSTED.", EnumChatType.Notification);
            }

            // --- Stamina Regeneration & Exhaustion Recovery ---
            // Check raw player input to see if they are *trying* to perform an action.
            bool tryingToSprint = plr.ServerControls.Sprint && (plr.ServerControls.Forward || plr.ServerControls.Backward || plr.ServerControls.Left || plr.ServerControls.Right);
            bool isTryingToPerformAction = tryingToSprint || isSwimming;

            if (!fatiguingActionThisTick && !isTryingToPerformAction)
            {
                if (IsExhausted)
                {
                    // Recovering from exhaustion
                    CurrentStamina += Config.StaminaRegenRatePerSecond * deltaTime;
                    if (CurrentStamina >= MaxStamina * Config.StaminaRecoveryDebounceThreshold)
                    {
                        IsExhausted = false;
                        if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player RECOVERED from exhaustion.", EnumChatType.Notification);
                    }
                }
                else if (_timeSinceLastFatiguingAction >= Config.StaminaRegenDelaySeconds)
                {
                    // Normal stamina regeneration
                    CurrentStamina += Config.StaminaRegenRatePerSecond * deltaTime;
                }
            }

            // Clamp stamina to max
            if (CurrentStamina > MaxStamina) CurrentStamina = MaxStamina;

            // --- Network Sync ---
            bool staminaChanged = Math.Abs(staminaBefore - CurrentStamina) > 0.001f;
            bool exhaustionChanged = exhaustedBefore != IsExhausted;

            if (staminaChanged || exhaustionChanged)
            {
                if (exhaustionChanged || _updateCooldown <= 0)
                {
                    MarkDirty();
                    _updateCooldown = Config.StaminaSyncIntervalSeconds;
                }
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
