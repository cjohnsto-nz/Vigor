using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Vigor.API
{
    /// <summary>
    /// Event arguments for stamina cost calculation events
    /// </summary>
    public class StaminaCostEventArgs : EventArgs
    {
        /// <summary>
        /// The player entity
        /// </summary>
        public EntityPlayer Player { get; }
        
        /// <summary>
        /// The action type ID (e.g., "vigor:jump", "mymod:mining")
        /// </summary>
        public string ActionTypeId { get; }
        
        /// <summary>
        /// The original base amount of stamina cost or drain rate
        /// </summary>
        public float BaseAmount { get; }
        
        /// <summary>
        /// The current modified amount after applying modifiers
        /// </summary>
        public float FinalAmount { get; set; }
        
        /// <summary>
        /// Dictionary of modifier IDs to their impact on the final amount
        /// </summary>
        /// <remarks>
        /// Key: Modifier ID
        /// Value: The amount this modifier changed the stamina cost (positive = increased cost, negative = decreased cost)
        /// </remarks>
        public Dictionary<string, float> AppliedModifiers { get; } = new Dictionary<string, float>();
        
        /// <summary>
        /// If set to true by an event handler, the stamina cost will be canceled entirely
        /// </summary>
        public bool IsCancelled { get; set; } = false;
        
        /// <summary>
        /// Creates a new instance of stamina cost event args
        /// </summary>
        public StaminaCostEventArgs(EntityPlayer player, string actionTypeId, float baseAmount)
        {
            Player = player;
            ActionTypeId = actionTypeId;
            BaseAmount = baseAmount;
            FinalAmount = baseAmount;
        }
    }
}
