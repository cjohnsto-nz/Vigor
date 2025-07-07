using System;
using Vigor.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Hud
{
    public class HudVigorBar : HudElement
    {
        private GuiElementStatbar _staminaStatbar;
        private GuiElementStatbar _recoveryThresholdStatbar;


        public HudVigorBar(ICoreClientAPI capi) : base(capi)
        {
            ComposeGuis();
            capi.Event.RegisterGameTickListener(OnGameTick, 10);
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();
            ComposeGuis(); // Recompose on player data received to ensure it's up to date
        }

        private void OnGameTick(float dt)
        {
            var player = capi?.World?.Player;
            if (player?.Entity == null) return;

            var staminaTree = player.Entity.WatchedAttributes.GetTreeAttribute(EntityBehaviorVigorStamina.Name);
            if (staminaTree == null) return;

            var currentStamina = staminaTree.GetFloat("currentStamina");
            var maxStamina = staminaTree.GetFloat("calculatedMaxStamina", VigorModSystem.Instance.CurrentConfig.MaxStamina);
            var isExhausted = staminaTree.GetBool("isExhausted");
            var recoveryThreshold = staminaTree.GetFloat("debug_recoveryThreshold");

            UpdateVigor(currentStamina, maxStamina, isExhausted, recoveryThreshold);
        }

        private void ComposeGuis()
        {
            // Match HydrateOrDiedrate's bar layout exactly
            const float statsBarParentWidth = 850f;
            const float statsBarWidth = statsBarParentWidth * 0.41f; // Same ratio as HydrateOrDiedrate
            
            // Position exactly like HydrateOrDiedrate
            double yOffset = -5; // Exactly where HydrateOrDiedrate bar would be
            
            // If HydrateOrDiedrate is loaded, offset to avoid overlap
            if (VigorModSystem.Instance.IsHydrateOrDiedrateLoaded)
            {
                yOffset = -17; // Offset to avoid overlap with HydrateOrDiedrate bar
            }
            
            // Create the parent bounds exactly like HydrateOrDiedrate
            var statsBarBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = statsBarParentWidth,
                fixedHeight = 100
            }.WithFixedOffset(0, yOffset);

            // Apply the same horizontal offset as HydrateOrDiedrate
            bool isRight = true;
            double alignmentOffsetX = isRight ? -2.0 : 1.0;
            
            // Create SEPARATE bar bounds with different alignment - exactly like HydrateOrDiedrate
            var statbarBounds = ElementStdBounds.Statbar(
                isRight ? EnumDialogArea.RightTop : EnumDialogArea.LeftTop, 
                statsBarWidth
            )
            .WithFixedAlignmentOffset(alignmentOffsetX, 5)
            .WithFixedHeight(10); // statbar height verified
            
            // Create recovery bar bounds - same alignment as main bar
            var recoveryBarBounds = statbarBounds.FlatCopy();

            // Create parent bounds same as HydrateOrDiedrate
            var barParentBounds = statsBarBounds.FlatCopy().FixedGrow(0.0, 20.0);
            
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

        public void UpdateVigor(float current, float max, bool isExhausted, float recoveryThreshold)
        {
            if (_staminaStatbar == null || _recoveryThresholdStatbar == null) return;

            // Update main stamina bar
            _staminaStatbar.SetValues(current, 0, max);
            _staminaStatbar.ShouldFlash = isExhausted;
            // The line interval should also be based on the *current* max stamina
            // Draw a line every 100 stamina points, similar to the vanilla hunger bar.
            _staminaStatbar.SetLineInterval(1500f / max);
            
            // Update recovery threshold bar
            // If exhausted, show the threshold. If not, set value to max, which hides it because HideWhenFull is true.
            float recoveryValue = isExhausted ? recoveryThreshold : max;
            _recoveryThresholdStatbar.SetValues(recoveryValue, 0, max);
            _recoveryThresholdStatbar.SetLineInterval(1500f / max);

        }

        public override bool TryClose() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
