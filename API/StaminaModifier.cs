using System;
using Vintagestory.API.Common;

namespace Vigor.API
{
    /// <summary>
    /// Represents a modifier that affects stamina costs or drain rates
    /// </summary>
    public class StaminaModifier
    {
        /// <summary>
        /// Unique identifier for this modifier (e.g., "vigor:nutrition_jump", "mymod:mining_efficiency")
        /// </summary>
        public string ModifierId { get; }
        
        /// <summary>
        /// ID of the mod that registered this modifier
        /// </summary>
        public string ModId { get; }
        
        /// <summary>
        /// Human-readable name for display purposes
        /// </summary>
        public string DisplayName { get; }
        
        /// <summary>
        /// Delegate that calculates the modified stamina cost or drain rate
        /// </summary>
        /// <remarks>
        /// Parameters:
        /// - EntityPlayer player: The player entity
        /// - string actionTypeId: The ID of the action type being modified
        /// - float baseAmount: The base stamina cost or drain rate
        /// Returns: The modified stamina cost or drain rate
        /// </remarks>
        public System.Func<EntityPlayer, string, float, float> CalculationDelegate { get; }
        
        /// <summary>
        /// Creates a new stamina modifier
        /// </summary>
        public StaminaModifier(string modifierId, string modId, string displayName, 
                            System.Func<EntityPlayer, string, float, float> calculationDelegate)
        {
            ModifierId = modifierId;
            ModId = modId;
            DisplayName = displayName;
            CalculationDelegate = calculationDelegate ?? throw new ArgumentNullException(nameof(calculationDelegate));
        }
        
        /// <summary>
        /// Applies this modifier to the given stamina cost
        /// </summary>
        public float Apply(EntityPlayer player, string actionTypeId, float baseAmount)
        {
            try
            {
                return CalculationDelegate(player, actionTypeId, baseAmount);
            }
            catch (Exception)
            {
                // If there's an error in the calculation, return the original amount
                return baseAmount;
            }
        }
        
        public override string ToString()
        {
            return $"{DisplayName} ({ModifierId})";
        }
    }
}
