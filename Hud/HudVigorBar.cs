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
            if (staminaTree == null)
            {
                // This can happen briefly on startup, not necessarily an error.
                return;
            }

            var currentStamina = staminaTree.GetFloat("currentStamina");
            var maxStamina = staminaTree.GetFloat("maxStamina");
            var isExhausted = staminaTree.GetBool("isExhausted");

            UpdateVigor(currentStamina, maxStamina, isExhausted, maxStamina);
        }

        private void ComposeGuis()
        {
            // The overall container for our HUD element
            ElementBounds dialogBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = 348,
                fixedHeight = 20
            }.WithFixedAlignmentOffset(249, -85); // Reset X offset

            // The specific bounds for the statbar *within* the container
            ElementBounds statbarBounds = ElementBounds.Fixed(0, 5, 348, 10);
            double[] staminaBarColor = { 0.85, 0.65, 0, 0.9 };

            // 1. Create the main composer with the overall dialogBounds
            var composer = capi.Gui.CreateCompo("vigorhud", dialogBounds.FlatCopy());

            // 2. Create the statbar element itself, using the *inner* statbarBounds
            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, true, false);

            // 3. Begin child elements within the composer, but add the statbar using its own bounds.
            //    This avoids the self-reference crash.
            composer.AddInteractiveElement(_staminaStatbar, "staminabar");

            Composers["vigorhud"] = composer.Compose();

            TryOpen();
        }

        public void UpdateVigor(float current, float max, bool isExhausted, float maxStamina)
        {
            if (_staminaStatbar == null) return;

            _staminaStatbar.SetValues(current, 0, max);
            _staminaStatbar.ShouldFlash = isExhausted;
            _staminaStatbar.SetLineInterval(1500/maxStamina);
        }

        public override bool TryClose() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
