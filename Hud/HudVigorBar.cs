using System;
using Vigor.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Hud
{
    public class HudVigorBar : HudElement
    {
        private GuiElementStatbar _staminaStatbar;


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

            UpdateVigor(currentStamina, maxStamina, isExhausted);
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

            var composer = capi.Gui.CreateCompo("vigorhud", dialogBounds.FlatCopy());
            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, true, false);
            composer.AddInteractiveElement(_staminaStatbar, "staminabar");
            Composers["vigorhud"] = composer.Compose();

            TryOpen();
        }

        public void UpdateVigor(float current, float max, bool isExhausted)
        {
            if (_staminaStatbar == null) return;

            _staminaStatbar.SetValues(current, 0, max);
            _staminaStatbar.ShouldFlash = isExhausted;
            // The line interval should also be based on the *current* max stamina
            // Draw a line every 100 stamina points, similar to the vanilla hunger bar.
            _staminaStatbar.SetLineInterval(1500f / max);
        }

        public override bool TryClose() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
