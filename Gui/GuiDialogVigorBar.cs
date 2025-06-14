using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Gui
{
    public class GuiDialogVigorBar : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        private GuiElementStatbar _staminaStatbar;

        public GuiDialogVigorBar(ICoreClientAPI capi) : base(capi)
        {
            ComposeDialog();
        }

        private void ComposeDialog()
        {
            // Define the dialog's overall position and size
            ElementBounds dialogBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = 210,
                fixedHeight = 35
            }.WithFixedAlignmentOffset(0, -60); // Position it above the hotbar

            // Define the statbar's bounds within the dialog
            ElementBounds statbarBounds = ElementBounds.Fixed(0, 0, 210, 35);

            // Define the color for the stamina bar
            double[] staminaBarColor = { 0.85, 0.65, 0, 0.9 }; // A gold-like color

            var composer = capi.Gui.CreateCompo("vigorhud", dialogBounds);

            _staminaStatbar = new GuiElementStatbar(composer.Api, statbarBounds, staminaBarColor, true, false);
            _staminaStatbar.SetMinMax(0, 100);

            composer.BeginChildElements(dialogBounds)
                .AddInteractiveElement(_staminaStatbar)
                .EndChildElements()
                .Compose();

            SingleComposer = composer;
        }

        public void UpdateVigor(float current, float max, bool isExhausted)
        {
            if (_staminaStatbar == null || !IsOpened()) return;

            _staminaStatbar.SetMinMax(0, max);
            _staminaStatbar.SetValue(current);

            // Use the flashing mechanic to indicate exhaustion, as seen in the 'jaunt' example
            _staminaStatbar.ShouldFlash = isExhausted;
        }
    }
}
