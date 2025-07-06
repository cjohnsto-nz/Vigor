using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vigor.Config;

namespace Vigor.Utils
{
    /// <summary>
    /// Calculates and stores nutrition-based modifier values for stamina mechanics.
    /// This class caches the results to avoid expensive recalculations on every game tick.
    /// </summary>
    public class VigorNutritionBonuses
    {
        // --- Cached Modifier Values ---
        public float MaxStaminaModifier { get; private set; }
        public float RecoveryRateModifier { get; private set; }
        public float DrainRateModifier { get; private set; }
        public float JumpCostModifier { get; private set; }
        public float RecoveryThresholdModifier { get; private set; }

        /// <summary>
        /// Reads the entity's current nutrition levels and recalculates all cached modifier values.
        /// </summary>
        /// <param name="player">The player entity to read nutrition from.</param>
        /// <param name="config">The current Vigor mod configuration.</param>
        public void Update(EntityPlayer player, VigorConfig config)
        {
            if (player == null || config == null) return;

            // Read vanilla nutrition values
            float fruit = GetNutritionValue(player, "fruit");
            float grain = GetNutritionValue(player, "grain");
            float protein = GetNutritionValue(player, "protein");
            float vegetable = GetNutritionValue(player, "vegetable");
            float dairy = GetNutritionValue(player, "dairy");

            // Calculate and cache each modifier
            MaxStaminaModifier = 1f + (grain * config.GrainMaxStaminaModifierPerPoint) + (protein * config.ProteinMaxStaminaModifierPerPoint);
            RecoveryRateModifier = 1f + (protein * config.ProteinRecoveryRateModifierPerPoint) + (dairy * config.DairyRecoveryRateModifierPerPoint);
            DrainRateModifier = Math.Max(config.MinDrainRateModifier, 1f - (vegetable * config.VegetableDrainRateModifierPerPoint) - (fruit * config.FruitDrainRateModifierPerPoint));
            JumpCostModifier = Math.Max(config.MinJumpCostModifier, 1f - (fruit * config.FruitJumpCostModifierPerPoint) - (grain * config.GrainJumpCostModifierPerPoint));
            RecoveryThresholdModifier = Math.Max(config.MinRecoveryThresholdModifier, 1f - (dairy * config.DairyRecoveryThresholdModifierPerPoint) - (vegetable * config.VegetableRecoveryThresholdModifierPerPoint));
        }

        /// <summary>
        /// Helper method to safely get a specific nutrition value from the player's hunger attributes.
        /// </summary>
        private float GetNutritionValue(EntityPlayer player, string nutritionType)
        {
            return player.WatchedAttributes.GetTreeAttribute("hunger")?.GetFloat(nutritionType + "Level", 0f) ?? 0f;
        }
    }
}
