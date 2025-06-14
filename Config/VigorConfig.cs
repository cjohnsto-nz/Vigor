namespace Vigor.Config
{
    public class VigorConfig
    {
        // General Settings
        public bool DebugMode { get; set; } = true;

        // Stamina Mechanics
        public float MaxStamina { get; set; } = 100f;
        public float StaminaRegenRatePerSecond { get; set; } = 5f; // Points per second
        public float StaminaRegenDelaySeconds { get; set; } = 1.5f; // Delay after exertion before regen starts
        public float StaminaExhaustionThreshold { get; set; } = 0f; // Stamina level at which player is exhausted
        public float StaminaRecoveryDebounceThreshold { get; set; } = 0.25f;

        public float StaminaSyncIntervalSeconds { get; set; } = 0.2f; // Percentage of MaxStamina (e.g., 0.25 for 25%)

        // Action Costs (per second of action)
        public float SprintStaminaCostPerSecond { get; set; } = 10f;
        public float SwimStaminaCostPerSecond { get; set; } = 8f;

        // Sinking Mechanic
        public float SinkingForce { get; set; } = 0.05f; // Downward velocity applied per tick when sinking

        // Future: Add more settings as needed, e.g., for specific item effects on stamina

        public VigorConfig()
        {
            // Default values are set with property initializers
        }
    }
}
