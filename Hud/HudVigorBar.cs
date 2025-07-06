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
            ElementBounds dialogBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = 348,
                fixedHeight = 20
            }.WithFixedAlignmentOffset(249, -89.5);

            ElementBounds statbarBounds = ElementBounds.Fixed(0, 5, 348, 10);
            double[] staminaBarColor = { 0.85, 0.65, 0, 0.9 };
            // For the recovery bar, use a darker, more transparent version of the main stamina color
            double[] recoveryBarColor = { staminaBarColor[0] * 0.6, staminaBarColor[1] * 0.6, staminaBarColor[2], 0.5 };

            var composer = capi.Gui.CreateCompo("vigorhud", dialogBounds.FlatCopy());
            
            // Add recovery bar first (to be in the background)
            _recoveryThresholdStatbar = new GuiElementStatbar(composer.Api, statbarBounds, recoveryBarColor, true, false);
            _recoveryThresholdStatbar.HideWhenFull = true;
            composer.AddInteractiveElement(_recoveryThresholdStatbar, "recoverybar");

            // Add main stamina bar
            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, true, false);
            composer.AddInteractiveElement(_staminaStatbar, "staminabar");

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
