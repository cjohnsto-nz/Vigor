using System;
using Vintagestory.API.Common;

namespace Vigor.API
{
    /// <summary>
    /// Represents a type of action that consumes stamina
    /// </summary>
    public class StaminaActionType
    {
        /// <summary>
        /// Unique identifier for this action type (e.g., "vigor:jump", "mymod:mining")
        /// </summary>
        public string ActionId { get; }
        
        /// <summary>
        /// ID of the mod that registered this action type
        /// </summary>
        public string ModId { get; }
        
        /// <summary>
        /// Human-readable name for display purposes
        /// </summary>
        public string DisplayName { get; }
        
        /// <summary>
        /// Creates a new stamina action type
        /// </summary>
        public StaminaActionType(string actionId, string modId, string displayName)
        {
            ActionId = actionId;
            ModId = modId;
            DisplayName = displayName;
        }
        
        public override string ToString()
        {
            return $"{DisplayName} ({ActionId})";
        }
    }
}
