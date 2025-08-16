using System;
using Vintagestory.API.Common;

namespace Vigor.API
{
    /// <summary>
    /// Public API for the Vigor mod's stamina system
    /// </summary>
    public interface IVigorAPI
    {
        /// <summary>
        /// Gets the current stamina value for an entity
        /// </summary>
        float GetCurrentStamina(EntityPlayer player);
        
        /// <summary>
        /// Gets the maximum stamina value for an entity
        /// </summary>
        float GetMaxStamina(EntityPlayer player);
        
        /// <summary>
        /// Checks if the player is exhausted (very low stamina)
        /// </summary>
        bool IsExhausted(EntityPlayer player);
        
        /// <summary>
        /// Attempts to consume stamina, returns true if successful
        /// </summary>
        /// <param name="player">The player entity</param>
        /// <param name="amount">Amount of stamina to consume</param>
        /// <param name="ignoreFatigue">If true, consuming stamina will not reset the fatigue timer</param>
        /// <returns>True if stamina was consumed, false otherwise</returns>
        bool ConsumeStamina(EntityPlayer player, float amount, bool ignoreFatigue = false);
        
        /// <summary>
        /// Continuously drains stamina over time (for sustained actions)
        /// </summary>
        /// <param name="player">The player entity</param>
        /// <param name="amountPerSecond">Amount of stamina to drain per second</param>
        /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
        /// <returns>True if stamina was drained, false if not enough stamina</returns>
        bool DrainStamina(EntityPlayer player, float amountPerSecond, float deltaTime);
        
        /// <summary>
        /// Starts continuous stamina drain at the specified rate
        /// </summary>
        /// <param name="player">The player entity</param>
        /// <param name="drainId">Unique identifier for this drain source (e.g., "climbing")</param>
        /// <param name="amountPerSecond">Amount of stamina to drain per second</param>
        /// <returns>True if drain was started successfully, false if player is already exhausted</returns>
        bool StartStaminaDrain(EntityPlayer player, string drainId, float amountPerSecond);
        
        /// <summary>
        /// Stops a previously started continuous stamina drain
        /// </summary>
        /// <param name="player">The player entity</param>
        /// <param name="drainId">Identifier of the drain to stop</param>
        void StopStaminaDrain(EntityPlayer player, string drainId);
        
        /// <summary>
        /// Checks if a player can perform an activity requiring stamina
        /// (e.g., start climbing) based on current exhaustion state
        /// </summary>
        /// <param name="player">The player entity</param>
        /// <returns>True if the player can perform the action, false if exhausted</returns>
        bool CanPerformStaminaAction(EntityPlayer player);
        
        #region Action Type Registration
        
        /// <summary>
        /// Registers a new stamina action type
        /// </summary>
        /// <param name="actionId">Unique identifier for this action type (should be namespaced, e.g. "mymod:mining")</param>
        /// <param name="displayName">Human-readable name for display purposes</param>
        /// <returns>The registered action type, or existing one if already registered</returns>
        StaminaActionType RegisterActionType(string actionId, string displayName);
        
        /// <summary>
        /// Gets a registered action type by its ID
        /// </summary>
        /// <param name="actionId">The action type ID</param>
        /// <returns>The action type if found, null otherwise</returns>
        StaminaActionType GetActionType(string actionId);
        
        /// <summary>
        /// Gets all registered action types
        /// </summary>
        /// <returns>Collection of registered action types</returns>
        StaminaActionType[] GetAllActionTypes();
        
        #endregion
        
        #region Modifier Registration
        
        /// <summary>
        /// Registers a new stamina modifier
        /// </summary>
        /// <param name="modifierId">Unique identifier for this modifier (should be namespaced, e.g. "mymod:mining_efficiency")</param>
        /// <param name="displayName">Human-readable name for display purposes</param>
        /// <param name="calculationDelegate">Function that calculates the modified stamina cost</param>
        /// <returns>The registered modifier, or existing one if already registered</returns>
        StaminaModifier RegisterModifier(string modifierId, string displayName, System.Func<EntityPlayer, string, float, float> calculationDelegate);
        
        /// <summary>
        /// Gets a registered modifier by its ID
        /// </summary>
        /// <param name="modifierId">The modifier ID</param>
        /// <returns>The modifier if found, null otherwise</returns>
        StaminaModifier GetModifier(string modifierId);
        
        /// <summary>
        /// Gets all registered modifiers
        /// </summary>
        /// <returns>Collection of registered modifiers</returns>
        StaminaModifier[] GetAllModifiers();
        
        #endregion
        
        #region Extended Stamina Consumption
        
        /// <summary>
        /// Consumes stamina for a specific action type, applying all registered modifiers
        /// </summary>
        /// <param name="actionTypeId">The action type ID</param>
        /// <param name="amount">Base amount of stamina to consume</param>
        /// <param name="player">The player entity</param>
        /// <param name="ignoreFatigue">If true, consuming stamina will not reset the fatigue timer</param>
        /// <returns>True if stamina was consumed, false otherwise</returns>
        bool ConsumeStamina(string actionTypeId, float amount, EntityPlayer player, bool ignoreFatigue = false);
        
        /// <summary>
        /// Starts continuous stamina drain for a specific action type, applying all registered modifiers
        /// </summary>
        /// <param name="actionTypeId">The action type ID</param>
        /// <param name="amountPerSecond">Base amount of stamina to drain per second</param>
        /// <param name="player">The player entity</param>
        /// <param name="drainId">Unique identifier for this drain source</param>
        /// <returns>True if drain was started successfully, false if player is already exhausted</returns>
        bool StartStaminaDrain(string actionTypeId, float amountPerSecond, EntityPlayer player, string drainId);
        
        /// <summary>
        /// Event raised when calculating stamina costs, allows for last-minute modifications
        /// </summary>
        event EventHandler<StaminaCostEventArgs> CalculatingStaminaCost;
        
        #endregion
    }
}
