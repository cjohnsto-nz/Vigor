namespace Vigor
{
    public class VigorConfig
    {
        public float MaxStamina = 100f;
        public float StaminaGainPerSecond = 10f;
        public float StaminaLossCooldownSeconds = 1.5f;
        public float SprintStaminaCostPerSecond = 10f;
        public float SwimStaminaCostPerSecond = 10f;
        public float JumpStaminaCost = 2f;
        public float StaminaExhaustionThreshold = 0f;
        public float StaminaRequiredToRecover = 25f;

        public bool DebugMode = true; // Enable/disable debug logging

        // New settings for exhaustion debuffs
        public float ExhaustionWalkSpeedMultiplier = 0.5f; // e.g., 0.5f for 50% speed
        public float ExhaustionJumpPowerMultiplier = 0.3f; // e.g., 0.3f for 30% jump power

        public static VigorConfig Loaded { get; set; } = new VigorConfig();
    }
}
