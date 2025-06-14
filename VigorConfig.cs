namespace Vigor
{
    public class VigorConfig
    {
        public float MaxStamina = 250f;
        public float StaminaGainPerSecond = 10f;
        public float StaminaLossCooldownSeconds = 1f;
        public float SprintStaminaCostPerSecond = 5f;
        public float SwimStaminaCostPerSecond = 5f;
        public float JumpStaminaCost = 3f;
        public float StaminaExhaustionThreshold = 0f;
        public float StaminaRequiredToRecover = 30f;

        public bool DebugMode = true; // Enable/disable debug logging

        // New settings for exhaustion debuffs
        public float ExhaustionWalkSpeedMultiplier = 0.5f; // e.g., 0.5f for 50% speed
        public float IdleStaminaRegenMultiplier = 2f; // e.g., 2f for double regen when idle
        public float ExhaustedSinkVelocityY = -10000f; // Extremely high value for testing sinking

        public static VigorConfig Loaded { get; set; } = new VigorConfig();
    }
}
