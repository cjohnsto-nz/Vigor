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
    }
}
