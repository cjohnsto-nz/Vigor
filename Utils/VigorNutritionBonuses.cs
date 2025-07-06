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
        private const float MAX_NUTRITION_VALUE = 1500f;

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

            // Read vanilla nutrition values and normalize them to a 0-1 scale.
            float fruit = GetNutritionValue(player, "fruit") / MAX_NUTRITION_VALUE;
            float grain = GetNutritionValue(player, "grain") / MAX_NUTRITION_VALUE;
            float protein = GetNutritionValue(player, "protein") / MAX_NUTRITION_VALUE;
            float vegetable = GetNutritionValue(player, "vegetable") / MAX_NUTRITION_VALUE;
            float dairy = GetNutritionValue(player, "dairy") / MAX_NUTRITION_VALUE;

            // Calculate and cache each modifier using the normalized values and the total bonus from config.
            MaxStaminaModifier = 1f + (grain * config.GrainMaxStaminaBonusAtMax) + (protein * config.ProteinMaxStaminaBonusAtMax);
            RecoveryRateModifier = 1f + (protein * config.ProteinRecoveryRateBonusAtMax) + (dairy * config.DairyRecoveryRateBonusAtMax);
            DrainRateModifier = Math.Max(config.MinDrainRateModifier, 1f - (vegetable * config.VegetableDrainRateBonusAtMax) - (fruit * config.FruitDrainRateBonusAtMax));
            JumpCostModifier = Math.Max(config.MinJumpCostModifier, 1f - (fruit * config.FruitJumpCostBonusAtMax) - (grain * config.GrainJumpCostBonusAtMax));
            RecoveryThresholdModifier = Math.Max(config.MinRecoveryThresholdModifier, 1f - (dairy * config.DairyRecoveryThresholdBonusAtMax) - (vegetable * config.VegetableRecoveryThresholdBonusAtMax));
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
