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
        public float StaminaRequiredToRecover { get; set; } = 50f; // Stamina points needed to recover from exhaustion
        public float IdleStaminaRegenMultiplier { get; set; } = 2f; // Multiplier for idle regen
        
        // Syncing interval (for client-server updates)
        public float StaminaSyncIntervalSeconds { get; set; } = 0.25f;
        
        // --- Action Costs ---
        public float SprintStaminaCostPerSecond { get; set; } = 10f;
        public float SwimStaminaCostPerSecond { get; set; } = 10f;
        public float JumpStaminaCost { get; set; } = 10f;
        
        // --- Exhaustion Effects ---
        public float ExhaustionWalkSpeedMultiplier { get; set; } = 0.5f; // Walking slower when exhausted (0.5 = 50% speed)
        public float ExhaustedSinkNudgeY { get; set; } = -0.05f; // Small downward velocity nudge per tick when exhausted in water.
        public float MaxExhaustedSinkSpeedY { get; set; } = -0.5f; // Terminal velocity for sinking when exhausted in water.
        
        // --- Nutrition Modifiers ---
        // Primary and secondary effects for each nutrition type
        
        // Grain effects
        public float GrainMaxStaminaModifierPerPoint { get; set; } = 0.005f; // +50% at 100% nutrition
        public float GrainJumpCostModifierPerPoint { get; set; } = 0.002f; // -20% at 100% nutrition
        
        // Protein effects
        public float ProteinRecoveryRateModifierPerPoint { get; set; } = 0.005f; // +50% at 100% nutrition
        public float ProteinMaxStaminaModifierPerPoint { get; set; } = 0.002f; // +20% at 100% nutrition
        
        // Vegetable effects
        public float VegetableDrainRateModifierPerPoint { get; set; } = 0.005f; // -50% at 100% nutrition
        public float VegetableRecoveryThresholdModifierPerPoint { get; set; } = 0.002f; // -20% at 100% nutrition
        
        // Dairy effects
        public float DairyRecoveryThresholdModifierPerPoint { get; set; } = 0.005f; // -50% at 100% nutrition
        public float DairyRecoveryRateModifierPerPoint { get; set; } = 0.002f; // +20% at 100% nutrition
        
        // Fruit effects
        public float FruitJumpCostModifierPerPoint { get; set; } = 0.005f; // -50% at 100% nutrition
        public float FruitDrainRateModifierPerPoint { get; set; } = 0.002f; // -20% at 100% nutrition
        
        // Minimum modifier values (to prevent extreme effects)
        public float MinDrainRateModifier { get; set; } = 0.1f; // Minimum 10% of normal drain rate
        public float MinJumpCostModifier { get; set; } = 0.1f; // Minimum 10% of normal jump cost
        public float MinRecoveryThresholdModifier { get; set; } = 0.1f; // Minimum 10% of normal recovery threshold
        

        public VigorConfig()
        {
            // Default values are set with property initializers
        }
    }
}
