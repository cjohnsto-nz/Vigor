using System;

namespace Vigor.Client
{
    public interface IClientStaminaPredictor
    {
        event Action<float, float, bool> OnStaminaChanged;

        float CurrentRecoveryThreshold { get; }
        string ModeName { get; }

        void UpdatePrediction(float deltaTime);
        void ReconcileWithServer(float serverStamina, float serverMaxStamina, bool serverIsExhausted);
        void ForceSync(float serverStamina, float serverMaxStamina, bool serverIsExhausted);
    }
}
