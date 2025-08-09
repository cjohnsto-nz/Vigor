using Newtonsoft.Json;

namespace Vigor.Config
{
    public class VigorConfig
    {
        // General Settings
        public bool DebugMode { get; set; } = false; // Disabled by default

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
        
        // --- Client-Side Prediction Settings ---
        public bool EnableClientSidePrediction { get; set; } = true; // Enable client-side stamina prediction for responsiveness
        public float ClientPredictionUpdateRate { get; set; } = 16f; // Client prediction update interval in milliseconds (~60 FPS)
        public float ServerReconciliationRate { get; set; } = 100f; // Server reconciliation interval in milliseconds (10 FPS)
        public bool EnableServerReconciliation { get; set; } = false; // Enable server reconciliation for UI (can cause jankiness)
        public float ReconciliationThreshold { get; set; } = 5.0f; // Threshold for server reconciliation
        public float NutritionUpdateRate { get; set; } = 500f; // How often to update nutrition bonuses (ms)
        
        // Directional interpolation thresholds
        public float InterpolationThresholdUp { get; set; } = 24.0f; // Threshold for smoothing stamina recovery (going up)
        public float InterpolationThresholdDown { get; set; } = 5.0f; // Threshold for smoothing stamina drains (going down)
        
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
        
        // --- HUD Options ---
        // When true, replaces the linear statbar with a centered radial element (purely a rendering change)
        public bool UseRadialHud { get; set; } = true;
        // Radial appearance options (used only when UseRadialHud is true)
        public bool HideStaminaOnFull { get; set; } = true;
        public float RadialInnerRadius { get; set; } = 0.6f; // normalized ring inner radius
        public float RadialOuterRadius { get; set; } = 0.8f; // normalized ring outer radius
        public float RadialScale { get; set; } = 1.0f;       // global scale multiplier
        

        public VigorConfig()
        {
            // Default values are set with property initializers
        }
    }
}
