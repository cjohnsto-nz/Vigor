using System;
using Vigor.Behaviors;
using Vigor.Client;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Hud
{
    public class HudVigorBar : HudElement
    {
        private GuiElementStatbar _staminaStatbar;
        private GuiElementStatbar _recoveryThresholdStatbar;
        private ClientStaminaPredictor _staminaPredictor;
        private long _clientTickListener;
        private long _serverSyncListener;
        private long _visualUpdateListener;
        
        // Client-side state for smooth updates
        private float _displayedStamina;
        private float _displayedMaxStamina;
        private bool _displayedIsExhausted;
        private float _displayedRecoveryThreshold;

        public HudVigorBar(ICoreClientAPI capi) : base(capi)
        {
            var config = VigorModSystem.Instance.CurrentConfig;
            
            ComposeGuis();
            
            // Initialize client-side predictor if enabled
            if (config.EnableClientSidePrediction)
            {
                _staminaPredictor = new ClientStaminaPredictor(capi, config);
                _staminaPredictor.OnStaminaChanged += OnPredictedStaminaChanged;
                
                // High-frequency client-side updates for smooth prediction
                _clientTickListener = capi.Event.RegisterGameTickListener(OnClientTick, (int)config.ClientPredictionUpdateRate);
                
                // Lower-frequency server sync for reconciliation
                _serverSyncListener = capi.Event.RegisterGameTickListener(OnServerSync, (int)config.ServerReconciliationRate);
                
                // High-frequency visual updates for smooth UI (independent of value changes)
                _visualUpdateListener = capi.Event.RegisterGameTickListener(OnVisualUpdate, 16); // Fixed 60+ FPS visual updates
            }
            else
            {
                // Fallback to traditional server-only updates
                _serverSyncListener = capi.Event.RegisterGameTickListener(OnServerSync, 50); // 20 FPS fallback
                _visualUpdateListener = capi.Event.RegisterGameTickListener(OnVisualUpdate, 50); // 20 FPS visual fallback
            }
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();
            ComposeGuis(); // Recompose on player data received to ensure it's up to date
        }

        /// <summary>
        /// High-frequency client-side tick for smooth stamina prediction
        /// </summary>
        private void OnClientTick(float dt)
        {
            if (_staminaPredictor == null) return;
            
            // Update client-side prediction
            _staminaPredictor.UpdatePrediction(dt);
        }
        
        /// <summary>
        /// Server sync to provide server values for interpolation
        /// </summary>
        private void OnServerSync(float dt)
        {
            var player = capi?.World?.Player;
            if (player?.Entity == null) return;

            var staminaTree = player.Entity.WatchedAttributes.GetTreeAttribute(EntityBehaviorVigorStamina.Name);
            if (staminaTree == null) return;

            // Get server values for interpolation
            var serverStamina = staminaTree.GetFloat("currentStamina");
            var serverMaxStamina = staminaTree.GetFloat("calculatedMaxStamina");
            var serverIsExhausted = staminaTree.GetBool("isExhausted");
            var recoveryThreshold = staminaTree.GetFloat("debug_recoveryThreshold");
            
            // Update recovery threshold for debug display
            _displayedRecoveryThreshold = recoveryThreshold;
            
            // Provide server values to interpolation system
            if (_staminaPredictor != null)
            {
                _staminaPredictor.ReconcileWithServer(serverStamina, serverMaxStamina, serverIsExhausted);
            }
            else
            {
                // Fallback: direct server values when no predictor
                _displayedStamina = serverStamina;
                _displayedMaxStamina = serverMaxStamina;
                _displayedIsExhausted = serverIsExhausted;
            }
        }
        
        /// <summary>
        /// Called when client-side prediction updates stamina values
        /// </summary>
        private void OnPredictedStaminaChanged(float stamina, float maxStamina, bool isExhausted)
        {
            _displayedStamina = stamina;
            _displayedMaxStamina = maxStamina;
            _displayedIsExhausted = isExhausted;
            
            // Values updated, visual update will happen on next OnVisualUpdate tick
        }
        
        /// <summary>
        /// High-frequency visual updates for smooth UI regardless of value changes
        /// </summary>
        private void OnVisualUpdate(float dt)
        {
            UpdateVigorDisplay();
        }

        private void ComposeGuis()
        {
            // Match HydrateOrDiedrate's bar layout exactly
            const float statsBarParentWidth = 850f;
            const float statsBarWidth = statsBarParentWidth * 0.41f; // Same ratio as HydrateOrDiedrate
            
            // Position exactly like HydrateOrDiedrate
            double yOffset = 96; // Exactly where HydrateOrDiedrate bar would be
            double statsBarHeight = 10;
            
            // If HydrateOrDiedrate is loaded, offset to avoid overlap
            if (VigorModSystem.Instance.IsHydrateOrDiedrateLoaded)
            {
                yOffset += 22; // Offset to avoid overlap with HydrateOrDiedrate bar
            }
            
            // Create the parent bounds with CenterBottom alignment
            var statsBarBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = statsBarParentWidth,
                fixedHeight = 10
            };

            // Set up alignment
            bool isRight = true;
            double alignmentOffsetX = isRight ? -2.0 : 1.0;
            
            // Create bar bounds WITHOUT horizontal offset (will be applied to parent container)
            var statbarBounds = ElementStdBounds.Statbar(
                isRight ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle, 
                statsBarWidth
            )
            .WithFixedHeight(10); // statbar height verified
            
            // Create recovery bar bounds - same alignment as main bar
            var recoveryBarBounds = statbarBounds.FlatCopy();

            // Create parent bounds and apply both X and Y offset at this level - the true parent container
            var barParentBounds = statsBarBounds.FlatCopy()
                .FixedGrow(0.0, statsBarHeight)
                .WithFixedOffset(0, -yOffset)
                .WithFixedAlignmentOffset(alignmentOffsetX, 0);
            
            // Set up bar colors with sufficient alpha for background visibility
            double[] staminaBarColor = { 0.85, 0.65, 0, 0.5 }; // Use alpha 0.5 to match HydrateOrDiedrate
            double[] recoveryBarColor = { staminaBarColor[0] * 0.6, staminaBarColor[1] * 0.6, staminaBarColor[2], 0.5 };

            // Create composer using the parent bounds - exactly like HydrateOrDiedrate
            var composer = capi.Gui.CreateCompo("vigorhud", barParentBounds);
            
            // Begin with child elements in stats bounds
            composer.BeginChildElements(statsBarBounds);
            
            // Add recovery bar first (to be in the background)
            _recoveryThresholdStatbar = new GuiElementStatbar(composer.Api, recoveryBarBounds, recoveryBarColor, isRight, false);
            _recoveryThresholdStatbar.HideWhenFull = true;
            composer.AddInteractiveElement(_recoveryThresholdStatbar, "recoverybar");

            // Add main stamina bar
            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, isRight, false);
            composer.AddInteractiveElement(_staminaStatbar, "staminabar");
            
            // End child elements and compose
            composer.EndChildElements();
            Composers["vigorhud"] = composer.Compose();

            TryOpen();
        }

        /// <summary>
        /// Updates the visual display using current stamina values
        /// </summary>
        private void UpdateVigorDisplay()
        {
            if (_staminaStatbar == null || _recoveryThresholdStatbar == null) return;

            // Update main stamina bar with predicted values
            _staminaStatbar.SetValues(_displayedStamina, 0, _displayedMaxStamina);
            _staminaStatbar.ShouldFlash = _displayedIsExhausted;
            // The line interval should also be based on the *current* max stamina
            // Draw a line every 100 stamina points, similar to the vanilla hunger bar.
            _staminaStatbar.SetLineInterval(1500f / _displayedMaxStamina);
            
            // Update recovery threshold bar
            // If exhausted, show the threshold. If not, set value to max, which hides it because HideWhenFull is true.
            float recoveryValue = _displayedIsExhausted ? _displayedRecoveryThreshold : _displayedMaxStamina;
            _recoveryThresholdStatbar.SetValues(recoveryValue, 0, _displayedMaxStamina);
            _recoveryThresholdStatbar.SetLineInterval(1500f / _displayedMaxStamina);
        }
        
        /// <summary>
        /// Legacy method for backward compatibility - now uses display system
        /// </summary>
        public void UpdateVigor(float current, float max, bool isExhausted, float recoveryThreshold)
        {
            // Force sync with provided values (for external calls)
            _staminaPredictor?.ForceSync(current, max, isExhausted);
            _displayedRecoveryThreshold = recoveryThreshold;
            UpdateVigorDisplay();
        }
        
        public override void Dispose()
        {
            base.Dispose();
            
            // Unregister event listeners
            if (_staminaPredictor != null)
            {
                _staminaPredictor.OnStaminaChanged -= OnPredictedStaminaChanged;
            }
            
            // Unregister tick listeners
            capi.Event.UnregisterGameTickListener(_clientTickListener);
            capi.Event.UnregisterGameTickListener(_serverSyncListener);
            capi.Event.UnregisterGameTickListener(_visualUpdateListener);
        }

        public override bool TryClose() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
