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

        private float _currentStamina;
        private float _maxStamina;
        private bool _isExhausted;
        private float _timeSinceLastFatiguingAction = 0f;
        private float _updateCooldown = 0f;

        private ITreeAttribute StaminaTree => entity.WatchedAttributes.GetTreeAttribute(Name);

        public EntityBehaviorVigorStamina(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            _maxStamina = Config.MaxStamina;
            _currentStamina = _maxStamina;
            _isExhausted = false;

            // Create the tree and populate it for the client
            entity.WatchedAttributes.SetAttribute(Name, new TreeAttribute());
            UpdateStaminaTree();

            // This log is critical for diagnostics, so use a high-visibility level.
            Logger.Warning($"[{ModId}] Initializing VigorStamina behavior for entity {entity.EntityId}. DebugMode: {Config.DebugMode}");
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side == EnumAppSide.Client) return;
            if (entity is not EntityPlayer plr || plr.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return;
            }

            _timeSinceLastFatiguingAction += deltaTime;
            _updateCooldown -= deltaTime;

            bool isSprinting = plr.Controls.Sprint && (plr.Controls.Forward || plr.Controls.Backward || plr.Controls.Left || plr.Controls.Right) && !plr.Controls.Sneak;
            bool isSwimming = plr.FeetInLiquid;
            bool isPerformingFatiguingAction = isSprinting || isSwimming;

            float staminaBefore = _currentStamina;

            // --- Stamina Depletion ---
            if (isPerformingFatiguingAction && !_isExhausted)
            {
                float costPerSecond = 0f;
                if (isSprinting) costPerSecond += Config.SprintStaminaCostPerSecond;
                if (isSwimming) costPerSecond += Config.SwimStaminaCostPerSecond;

                _currentStamina -= costPerSecond * deltaTime;
                _timeSinceLastFatiguingAction = 0f;

                if (_currentStamina <= Config.StaminaExhaustionThreshold)
                {
                    _currentStamina = Config.StaminaExhaustionThreshold;
                    _isExhausted = true;
                    if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player EXHAUSTED.", EnumChatType.Notification);
                }
            }
            // --- Stamina Regeneration ---
            else if (!_isExhausted && _timeSinceLastFatiguingAction >= Config.StaminaRegenDelaySeconds)
            {
                if (_currentStamina < _maxStamina)
                {
                    _currentStamina += Config.StaminaRegenRatePerSecond * deltaTime;
                }
            }

            // --- Exhaustion Recovery ---
            if (_isExhausted)
            {
                if (_currentStamina < _maxStamina)
                {
                    _currentStamina += Config.StaminaRegenRatePerSecond * deltaTime;
                }

                if (_currentStamina >= _maxStamina * Config.StaminaRecoveryDebounceThreshold)
                {
                    _isExhausted = false;
                    if (Config.DebugMode) (plr.Player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"[{ModId} DEBUG] Player RECOVERED from exhaustion.", EnumChatType.Notification);
                }
            }

            // Clamp stamina to max
            if (_currentStamina > _maxStamina) _currentStamina = _maxStamina;

            // If stamina has changed, update the watched attribute
            bool staminaChanged = Math.Abs(staminaBefore - _currentStamina) > 0.001f;
            bool exhaustionChanged = StaminaTree.GetBool("isExhausted") != _isExhausted;

            if (staminaChanged || exhaustionChanged)
            {
                // Update immediately if exhaustion state changes, otherwise update on a cooldown
                // to prevent sending network packets every single tick, which causes severe lag.
                if (exhaustionChanged || _updateCooldown <= 0)
                {
                    UpdateStaminaTree();
                    _updateCooldown = Config.StaminaSyncIntervalSeconds;
                }
            }
        }

        private void UpdateStaminaTree()
        {
            var tree = StaminaTree;
            if (tree == null) return; // Should not happen after Initialize

            tree.SetFloat("currentStamina", _currentStamina);
            tree.SetFloat("maxStamina", _maxStamina);
            tree.SetBool("isExhausted", _isExhausted);
            entity.WatchedAttributes.MarkPathDirty(Name);
        }

        public override string PropertyName()
        {
            return Name;
        }
    }
}
