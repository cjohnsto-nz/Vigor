using System;
using Vigor.Behaviors;
using Vigor.Client;
using Vigor.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Hud
{
    public class HudVigorBar : HudElement
    {
        // Linear bar UI elements (used when radial HUD is disabled)
        private GuiElementStatbar _staminaStatbar;
        
        // Radial HUD support
        private bool _useRadial;
        private StaminaRadialRenderer _radialRenderer;
        private Func<StaminaSnapshot> _snapshotProvider;
        private IClientStaminaPredictor _staminaPredictor;
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
        private float? _lastRecoveryMarkerValue;
        private long _recoveryMarkerVisibleSinceMs;
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

            VigorModSystem.Instance.LocalPlayerStaminaStateUpdated += OnLocalPlayerStaminaStateUpdated;

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
                _staminaPredictor = config.UseNewClientPredictionModel
                    ? new ClientStaminaPredictor(capi, config)
                    : new LegacyClientStaminaPredictor(capi, config);
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

            if (_staminaPredictor != null)
            {
                _displayedRecoveryThreshold = _staminaPredictor.CurrentRecoveryThreshold;
                return;
            }

            var syncedState = VigorModSystem.Instance?.GetClientStaminaState(player.PlayerUID);
            var staminaTree = player.Entity.WatchedAttributes.GetTreeAttribute(EntityBehaviorVigorStamina.Name);
            if (syncedState == null && staminaTree == null) return;

            float serverStamina;
            float serverMaxStamina;
            bool serverIsExhausted;

            if (syncedState != null)
            {
                serverStamina = syncedState.CurrentStamina;
                serverMaxStamina = syncedState.MaxStamina;
                serverIsExhausted = syncedState.IsExhausted;
            }
            else
            {
                serverStamina = staminaTree.GetFloat("currentStamina");
                serverMaxStamina = staminaTree.GetFloat("calculatedMaxStamina", staminaTree.GetFloat("maxStamina"));
                serverIsExhausted = staminaTree.GetBool("isExhausted");
            }

            var recoveryThreshold = staminaTree?.GetFloat("debug_recoveryThreshold", _displayedRecoveryThreshold) ?? _displayedRecoveryThreshold;
            
            // Update recovery threshold for debug display
            _displayedRecoveryThreshold = recoveryThreshold;

            // Fallback: direct server values when no predictor
            _displayedStamina = serverStamina;
            _displayedMaxStamina = serverMaxStamina;
            _displayedIsExhausted = serverIsExhausted;
        }
        
        /// <summary>
        /// Called when client-side prediction updates stamina values
        /// </summary>
        private void OnPredictedStaminaChanged(float stamina, float maxStamina, bool isExhausted)
        {
            _displayedStamina = stamina;
            _displayedMaxStamina = maxStamina;
            _displayedIsExhausted = isExhausted;
            _displayedRecoveryThreshold = _staminaPredictor?.CurrentRecoveryThreshold ?? _displayedRecoveryThreshold;
            
            // Values updated, visual update will happen on next OnVisualUpdate tick
        }

        /// <summary>
        /// Applies local-player packet updates immediately so the HUD does not wait for the next polling tick.
        /// </summary>
        private void OnLocalPlayerStaminaStateUpdated(StaminaStatePacket packet)
        {
            if (packet == null) return;

            var player = capi?.World?.Player;
            if (player?.PlayerUID != packet.PlayerUID) return;

            if (_staminaPredictor != null)
            {
                _staminaPredictor.ReconcileWithServer(packet.CurrentStamina, packet.MaxStamina, packet.IsExhausted);
            }
            else
            {
                _displayedStamina = packet.CurrentStamina;
                _displayedMaxStamina = packet.MaxStamina;
                _displayedIsExhausted = packet.IsExhausted;
                UpdateVigorDisplay();
            }
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

            // Create parent bounds and apply both X and Y offset at this level - the true parent container
            var barParentBounds = statsBarBounds.FlatCopy()
                .FixedGrow(0.0, statsBarHeight)
                .WithFixedOffset(0, -yOffset)
                .WithFixedAlignmentOffset(alignmentOffsetX, 0);
            
            // Set up bar colors with sufficient alpha for background visibility
            double[] staminaBarColor = { 0.85, 0.65, 0, 0.5 }; // Use alpha 0.5 to match HydrateOrDiedrate

            // Create composer using the parent bounds - exactly like HydrateOrDiedrate
            var composer = capi.Gui.CreateCompo("vigorhud", barParentBounds);
            
            // Begin with child elements in stats bounds
            composer.BeginChildElements(statsBarBounds);

            // Add main stamina bar
            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, isRight, false);
            _staminaStatbar.PreviousValueDisplayTime = 3600f; // Keep the recovery marker persistent while active
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

            if (_staminaStatbar == null) return;

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

            // Vanilla-style single-statbar overlay: use the statbar's previous-value layer
            // so the threshold inherits the bar's yellow/orange palette instead of the
            // hardcoded green future-value projection used by vanilla healing.
            float? recoveryMarkerValue = null;
            var config = VigorModSystem.Instance.CurrentConfig;
            if (!config.HideRecoveryThreshold)
            {
                if (_displayedIsExhausted && _displayedRecoveryThreshold > 0.01f)
                {
                    recoveryMarkerValue = Math.Clamp(_displayedRecoveryThreshold, 0f, max);
                }
            }
            _staminaStatbar.SetFutureValues(null, 0f);

            long nowMs = capi.InWorldEllapsedMilliseconds;
            if (recoveryMarkerValue.HasValue)
            {
                if (!_lastRecoveryMarkerValue.HasValue || Math.Abs(_lastRecoveryMarkerValue.Value - recoveryMarkerValue.Value) > 0.01f)
                {
                    _recoveryMarkerVisibleSinceMs = nowMs;
                    _lastRecoveryMarkerValue = recoveryMarkerValue.Value;
                }
            }
            else
            {
                _lastRecoveryMarkerValue = null;
                _recoveryMarkerVisibleSinceMs = nowMs;
            }
            _staminaStatbar.SetPrevValue(recoveryMarkerValue, _recoveryMarkerVisibleSinceMs, () => capi.InWorldEllapsedMilliseconds);

            // The line interval should also be based on the *current* max stamina
            // Draw a line every 100 stamina points, similar to the vanilla hunger bar.
            _staminaStatbar.SetLineInterval(1500f / max);
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

            if (VigorModSystem.Instance != null)
            {
                VigorModSystem.Instance.LocalPlayerStaminaStateUpdated -= OnLocalPlayerStaminaStateUpdated;
            }
            
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
