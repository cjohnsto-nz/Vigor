using Newtonsoft.Json;

namespace Vigor.Config
{
    public class VigorConfig
    {
        // General Settings
        public bool DebugMode { get; set; } = true; // Disabled by default

        // Stamina Mechanics
        public float MaxStamina { get; set; } = 150f;
        public float StaminaGainPerSecond { get; set; } = 12.5f; // Points per second
        public float StaminaLossCooldownSeconds { get; set; } = 1f; // Delay after exertion before regen starts
        public float StaminaExhaustionThreshold { get; set; } = 0f; // Stamina level at which player is exhausted
        public float StaminaRequiredToRecoverPercent { get; set; } = 0.4f; // Percentage of max stamina needed to recover from exhaustion (e.g., 0.33 for 33%)
        public float IdleStaminaRegenMultiplier { get; set; } = 2f; // Multiplier for idle regen
        public float SittingStaminaRegenMultiplier { get; set; } = 1.5f; // Multiplier for sitting regen
        public float SprintDetectionSpeedThreshold { get; set; } = 0.005f; // Threshold for sprint detection

        
        // Syncing interval (for client-server updates)
        public float StaminaSyncIntervalSeconds { get; set; } = 0.25f;
        
        // --- Action Costs ---
        public float SprintStaminaCostPerSecond { get; set; } = 8f;
        public float SwimStaminaCostPerSecond { get; set; } = 3f;
        public float JumpStaminaCost { get; set; } = 10f;
        
        // --- Exhaustion Effects ---
        public float ExhaustionWalkSpeedMultiplier { get; set; } = 0.5f; // Walking slower when exhausted (0.5 = 50% speed)
        public float ExhaustedSwimOxygenDebuff { get; set; } = 1.0f; // Rate of oxygen drain when swimming while exhausted
        public float ExhaustedSinkNudgeY { get; set; } = -0.05f; // Small downward velocity nudge per tick when exhausted in water.
        public float MaxExhaustedSinkSpeedY { get; set; } = -0.5f; // Terminal velocity for sinking when exhausted in water.
        public float ExhaustionLossCooldownSeconds { get; set; } = 3f; // Delay after exertion before regen starts
        
        // --- Nutrition Modifiers ---
        // These values represent the total bonus percentage when a nutrition bar is full (at 1500).
        // For example, a value of 0.5 means a +50% bonus or a -50% cost reduction at max nutrition.

        // Grain effects
        public float GrainMaxStaminaBonusAtMax { get; set; } = 0.3f; // +50% max stamina
        public float GrainJumpCostBonusAtMax { get; set; } = 0.2f; // -20% jump cost

        // Protein effects
        public float ProteinRecoveryRateBonusAtMax { get; set; } = 0.3f; // +50% recovery rate
        public float ProteinMaxStaminaBonusAtMax { get; set; } = 0.2f; // +20% max stamina

        // Vegetable effects
        public float VegetableDrainRateBonusAtMax { get; set; } = 0.3f; // -50% drain rate
        public float VegetableRecoveryThresholdBonusAtMax { get; set; } = 0.2f; // -20% recovery threshold

        // Dairy effects
        public float DairyRecoveryThresholdBonusAtMax { get; set; } = 0.3f; // -50% recovery threshold
        public float DairyRecoveryRateBonusAtMax { get; set; } = 0.2f; // +20% recovery rate

        // Fruit effects
        public float FruitJumpCostBonusAtMax { get; set; } = 0.3f; // -50% jump cost
        public float FruitDrainRateBonusAtMax { get; set; } = 0.2f; // -20% drain rate
        
        // --- Pooled Nutrition Effects ---
        // These effects are based on the combined nutrition levels across all categories
        public float PooledNutritionRecoveryDelayReductionAtMax { get; set; } = 0.5f; // -50% recovery delay when all nutrition maxed
        
        // Minimum modifier values (to prevent extreme effects)
        public float MinDrainRateModifier { get; set; } = 0.1f; // Minimum 10% of normal drain rate
        public float MinJumpCostModifier { get; set; } = 0.1f; // Minimum 10% of normal jump cost
        public float MinRecoveryThresholdModifier { get; set; } = 0.1f; // Minimum 10% of normal recovery threshold
        public float MinRecoveryDelayModifier { get; set; } = 0.5f; // Minimum 50% of normal recovery delay
        

        public VigorConfig()
        {
            // Default values are set with property initializers
        }
    }
}
