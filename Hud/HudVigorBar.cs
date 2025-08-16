using System;
using Vigor.Behaviors;
using Vigor.Client;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Hud
{
    public class HudVigorBar : HudElement
    {
        // Linear bar UI elements (used when radial HUD is disabled)
        private GuiElementStatbar _staminaStatbar;
        private GuiElementStatbar _recoveryThresholdStatbar;
        
        // Radial HUD support
        private bool _useRadial;
        private StaminaRadialRenderer _radialRenderer;
        private Func<StaminaSnapshot> _snapshotProvider;
        private ClientStaminaPredictor _staminaPredictor;
        private long _clientTickListener;
        private long _serverSyncListener;
        private long _visualUpdateListener;
        
        // Client-side state for smooth updates
        private float _displayedStamina;
        private float _displayedMaxStamina;
        private bool _displayedIsExhausted;
        private float _displayedRecoveryThreshold;
        private bool _hideStaminaOnFull;
        // TEMP: hide stamina bar to verify recovery overlay independently
        private bool _debugHideStaminaBar = false;
        // Auto-hide controls
        private bool _autoHideEnabled;
        private bool _hudOpen;
        private double _fullElapsed;

        public HudVigorBar(ICoreClientAPI capi) : base(capi)
        {
            var config = VigorModSystem.Instance.CurrentConfig;
            _useRadial = config.UseRadialHud;
            _hideStaminaOnFull = config.HideStaminaOnFull;
            _autoHideEnabled = config.HideStaminaOnFull;

            // Only compose linear GUI when not using radial HUD
            if (!_useRadial)
            {
                ComposeGuis();
                _hudOpen = true;
            }
            else
            {
                // Prepare snapshot provider for the radial renderer
                _snapshotProvider = () =>
                {
                    // Clamp/snap values for radial to avoid > max and ensure visual full
                    float max = Math.Max(1f, _displayedMaxStamina);
                    float stam = _displayedStamina;
                    if (stam > max) stam = max;
                    if (stam < 0f) stam = 0f;
                    // Snap to max within small epsilon to guarantee full-hide behavior
                    float eps = Math.Max(0.001f * max, 0.01f);
                    if (max - stam <= eps) stam = max;
                    float rec = _displayedRecoveryThreshold;
                    if (rec < 0f) rec = 0f;
                    if (rec > max) rec = max;

                    return new StaminaSnapshot
                    {
                        Stamina = stam,
                        MaxStamina = max,
                        IsExhausted = _displayedIsExhausted,
                        RecoveryThreshold = rec
                    };
                };

                _radialRenderer = new StaminaRadialRenderer(capi, _snapshotProvider);
                capi.Event.RegisterRenderer(_radialRenderer, EnumRenderStage.Ortho, "vigor:staminaradial");
            }
            
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
            if (!_useRadial)
            {
                ComposeGuis(); // Recompose on player data received to ensure it's up to date
            }
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
            HandleAutoHide(dt);
            UpdateVigorDisplay();
        }

        private void HandleAutoHide(float dt)
        {
            if (_useRadial) return; // linear HUD only for now
            if (!_autoHideEnabled) return;
            // Determine current state
            float max = Math.Max(1f, _displayedMaxStamina);
            float stam = _displayedStamina;
            if (stam < 0f) stam = 0f;
            if (stam > max) stam = max;
            // small epsilon to treat near-max as full for UX
            float eps = Math.Max(0.001f * max, 0.01f);
            bool isFull = (max - stam) <= eps;

            if (isFull)
            {
                _fullElapsed += dt;
                if (_fullElapsed >= 1.0 && _hudOpen)
                {
                    base.TryClose();
                    _hudOpen = false;
                }
            }
            else
            {
                _fullElapsed = 0;
                if (!_hudOpen)
                {
                    TryOpen();
                    _hudOpen = true;
                }
            }
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
            if (_useRadial)
            {
                // Radial HUD pulls values from the snapshot provider during render. Nothing to do here.
                return;
            }

            if (_staminaStatbar == null || _recoveryThresholdStatbar == null) return;

            // Clamp/snap values to avoid > max and guarantee full-hide behavior
            float max = Math.Max(1f, _displayedMaxStamina);
            float stam = _displayedStamina;
            if (stam > max) stam = max;
            if (stam < 0f) stam = 0f;
            float eps = Math.Max(0.001f * max, 0.01f);
            if (max - stam <= eps) stam = max;

            // Note: HideWhenFull disabled; we control visibility via composer auto-hide

            // Update main stamina bar with predicted values (clamped)
            if (_debugHideStaminaBar)
            {
                // Force-hide: value == max with HideWhenFull on a hideable bar renders nothing
                _staminaStatbar.HideWhenFull = true;
                _staminaStatbar.SetValues(max, 0, max);
                _staminaStatbar.ShouldFlash = false;
            }
            else
            {
                _staminaStatbar.SetValues(stam, 0, max);
                _staminaStatbar.ShouldFlash = _displayedIsExhausted;
            }
            // The line interval should also be based on the *current* max stamina
            // Draw a line every 100 stamina points, similar to the vanilla hunger bar.
            _staminaStatbar.SetLineInterval(1500f / max);
             
            // Update recovery threshold bar
            // Respect config: optionally hide recovery threshold entirely
            float recoveryValue;
            if (VigorModSystem.Instance.CurrentConfig.HideRecoveryThreshold)
            {
                recoveryValue = 0f;
            }
            else
            {
                // If exhausted, show the threshold. If not exhausted, draw no fill (0) to avoid a full-width bar
                recoveryValue = _displayedIsExhausted ? _displayedRecoveryThreshold : 0f;
            }
            if (recoveryValue < 0f) recoveryValue = 0f;
            if (recoveryValue > max) recoveryValue = max;
            // Do NOT snap recovery to max; we want it visible throughout exhaustion, even if near max
            _recoveryThresholdStatbar.SetValues(recoveryValue, 0, max);
            _recoveryThresholdStatbar.SetLineInterval(1500f / max);

            // Note: Recovery bar HideWhenFull=true, setting value == max hides it internally
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

            // Unregister radial renderer if used
            if (_radialRenderer != null)
            {
                capi.Event.UnregisterRenderer(_radialRenderer, EnumRenderStage.Ortho);
                _radialRenderer.Dispose();
                _radialRenderer = null;
            }
        }

        public override bool TryClose() => base.TryClose();
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
