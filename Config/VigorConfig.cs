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
        public float ExhaustedSinkVelocityY { get; set; } = -0.2f; // How fast player sinks when exhausted

        // Future: Add more settings as needed, e.g., for specific item effects on stamina

        public VigorConfig()
        {
            // Default values are set with property initializers
        }
    }
}
