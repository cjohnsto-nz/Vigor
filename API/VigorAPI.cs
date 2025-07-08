using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vigor.Behaviors;
using Vigor.Utils;

namespace Vigor.API
{
    /// <summary>
    /// Implementation of the Vigor API for external mod consumption
    /// </summary>
    public class VigorAPI : IVigorAPI
    {
        private readonly ICoreAPI _api;
        private bool _lastExhaustedState = false; // Track last exhaustion state to avoid excessive logging

        public VigorAPI(ICoreAPI api)
        {
            _api = api;
        }

        /// <summary>
        /// Gets the EntityBehaviorVigorStamina for a player, or null if not found
        /// </summary>
        private EntityBehaviorVigorStamina GetStaminaBehavior(EntityPlayer player)
        {
            if (player == null) return null;
            
            return player.GetBehavior<EntityBehaviorVigorStamina>();
        }

        /// <inheritdoc />
        public float GetCurrentStamina(EntityPlayer player)
        {
            // On client side, use the synchronized state
            if (_api.Side == EnumAppSide.Client && player != null)
            {
                ICoreClientAPI capi = _api as ICoreClientAPI;
                if (capi?.World?.Player?.Entity == player)
                {
                    string playerUID = capi.World.Player.PlayerUID;
                    var syncedState = VigorModSystem.Instance.GetClientStaminaState(playerUID);
                    if (syncedState != null)
                    {
                        return syncedState.CurrentStamina;
                    }
                    // Fall through to behavior check if no sync data
                }
            }
            
            // Default/server behavior
            var behavior = GetStaminaBehavior(player);
            return behavior?.CurrentStamina ?? -1;
        }

        /// <inheritdoc />
        public float GetMaxStamina(EntityPlayer player)
        {
            // On client side, use the synchronized state
            if (_api.Side == EnumAppSide.Client && player != null)
            {
                ICoreClientAPI capi = _api as ICoreClientAPI;
                if (capi?.World?.Player?.Entity == player)
                {
                    string playerUID = capi.World.Player.PlayerUID;
                    var syncedState = VigorModSystem.Instance.GetClientStaminaState(playerUID);
                    if (syncedState != null)
                    {
                        return syncedState.MaxStamina;
                    }
                    // Fall through to behavior check if no sync data
                }
            }
            
            // Default/server behavior
            var behavior = GetStaminaBehavior(player);
            return behavior?.MaxStamina ?? -1;
        }

        /// <inheritdoc />
        public bool IsExhausted(EntityPlayer player)
        {
            // On client side, use the synchronized state
            if (_api.Side == EnumAppSide.Client && player != null)
            {
                ICoreClientAPI capi = _api as ICoreClientAPI;
                if (capi?.World?.Player?.Entity == player)
                {
                    string playerUID = capi.World.Player.PlayerUID;
                    var syncedState = VigorModSystem.Instance.GetClientStaminaState(playerUID);
                    if (syncedState != null)
                    {
                        // Log only when state changes to minimize log spam
                        if (syncedState.IsExhausted != _lastExhaustedState)
                        {
                            _api.Logger.Debug("[Vigor:API] Client IsExhausted changed: {0} -> {1}", _lastExhaustedState, syncedState.IsExhausted);
                            _lastExhaustedState = syncedState.IsExhausted;
                        }
                        return syncedState.IsExhausted;
                    }
                    // Fall through to behavior check if no sync data
                }
            }
            
            // Default/server behavior
            var behavior = GetStaminaBehavior(player);
            
            // Reduce logging to only log when behavior is null or exhausted state changes
            if (behavior == null)
            {
                _api.Logger.Debug("[Vigor:API] IsExhausted called for player {0}, behavior is null", (player != null ? player.ToString() : "null"));
                return false;
            }
            
            return behavior.IsExhausted;
        }

        /// <inheritdoc />
        public bool ConsumeStamina(EntityPlayer player, float amount, bool ignoreFatigue = false)
        {
            _api.Logger.Event("[Vigor:API] ConsumeStamina called for player {0}, amount {1}, ignoreFatigue {2}", (player != null ? player.ToString() : "null"), amount, ignoreFatigue);
            
            var behavior = GetStaminaBehavior(player);
            if (behavior == null)
            {
                _api.Logger.Warning("[Vigor:API] No stamina behavior found for player {0}", (player != null ? player.ToString() : "null"));
                return true; // Allow action if no behavior exists
            }
            
            _api.Logger.Event("[Vigor:API] Current stamina: {0}, Max stamina: {1}, Amount requested: {2}", behavior.CurrentStamina, behavior.MaxStamina, amount);
            
            // Verify player has enough stamina
            if (behavior.CurrentStamina < amount)
            {
                _api.Logger.Event("[Vigor:API] Not enough stamina for player {0} - have {1}, need {2}", (player != null ? player.ToString() : "null"), behavior.CurrentStamina, amount);
                return false;
            }
            
            // Consume stamina - server side only to avoid sync issues
            _api.Logger.Event("[Vigor:API] Current API side: {0}", _api.Side);
            if (_api.Side == EnumAppSide.Server)
            {
                _api.Logger.Event("[Vigor:API] Consuming {0} stamina on server for player {1}", amount, (player != null ? player.ToString() : "null"));
                behavior.CurrentStamina -= amount;
                
                // Mark player as having performed a fatiguing action
                if (!ignoreFatigue)
                {
                    _api.Logger.Event("[Vigor:API] Resetting fatigue timer for player {0}", (player != null ? player.ToString() : "null"));
                    behavior.ResetFatigueTimer();
                }
                
                _api.Logger.Event("[Vigor:API] Marking stamina dirty for player {0}, new value: {1}", (player != null ? player.ToString() : "null"), behavior.CurrentStamina);
                behavior.MarkDirty();
            }
            else
            {
                _api.Logger.Warning("[Vigor:API] Not consuming stamina on client side for player {0}", (player != null ? player.ToString() : "null"));
            }
            
            _api.Logger.Event("[Vigor:API] ConsumeStamina returning true for player {0}", (player != null ? player.ToString() : "null"));
            return true;
        }

        /// <inheritdoc />
        public bool DrainStamina(EntityPlayer player, float amountPerSecond, float deltaTime)
        {
            float amount = amountPerSecond * deltaTime;
            return ConsumeStamina(player, amount, true);
        }
    }
}
