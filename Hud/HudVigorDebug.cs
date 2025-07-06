using System.Text;
using Vigor.Behaviors;
using Vigor.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vigor.Hud
{
    public class HudVigorDebug : HudElement
    {
        private GuiElementDynamicText _debugText;
        private long _listenerId;
        private bool _haveDumpedTrees;

        public HudVigorDebug(ICoreClientAPI capi) : base(capi)
        {
            _listenerId = capi.Event.RegisterGameTickListener(OnGameTick, 250);
            ComposeDialog();
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();
            UpdateDebugText();
        }

        private void OnGameTick(float dt)
        {
            if (_debugText == null) return;
            UpdateDebugText();
        }

        private void ComposeDialog()
        {
            ElementBounds textBounds = ElementBounds.Fixed(0, 20, 300, 200);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(textBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            var composer = capi.Gui.CreateCompo("vigorinfodialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Vigor Debug Info", () => TryClose());

            composer.AddDynamicText("", CairoFont.WhiteDetailText(), textBounds, "debugtext");

            Composers["vigorinfodialog"] = composer.Compose();
            _debugText = Composers["vigorinfodialog"].GetDynamicText("debugtext");

            TryOpen();
        }

        private void UpdateDebugText()
        {
            var sb = new StringBuilder();

            if (VigorModSystem.Instance?.CurrentConfig == null)
            {
                sb.AppendLine("Waiting for config...");
                _debugText?.SetNewText(sb.ToString());
                return;
            }

            var player = capi.World?.Player;
            if (player == null)
            {
                sb.AppendLine("Waiting for player data...");
                _debugText?.SetNewText(sb.ToString());
                return;
            }

            var config = VigorModSystem.Instance.CurrentConfig;

            // Nutrition Section
            sb.AppendLine("--- Vigor Nutrition ---");
            var hungerTree = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hungerTree == null)
            {
                sb.AppendLine("Waiting for nutrition data (hunger)...");
            }
            else
            {
                float protein = hungerTree.GetFloat("proteinLevel", 0);
                float fruit = hungerTree.GetFloat("fruitLevel", 0);
                float vegetable = hungerTree.GetFloat("vegetableLevel", 0);
                float dairy = hungerTree.GetFloat("dairyLevel", 0);
                float grain = hungerTree.GetFloat("grainLevel", 0);

                float grainMaxStamMod = grain * config.GrainMaxStaminaModifierPerPoint;
                float grainJumpCostMod = grain * config.GrainJumpCostModifierPerPoint;
                float proteinRecoveryMod = protein * config.ProteinRecoveryRateModifierPerPoint;
                float proteinMaxStamMod = protein * config.ProteinMaxStaminaModifierPerPoint;
                float vegDrainMod = vegetable * config.VegetableDrainRateModifierPerPoint;
                float vegRecoveryThreshMod = vegetable * config.VegetableRecoveryThresholdModifierPerPoint;
                float dairyRecoveryThreshMod = dairy * config.DairyRecoveryThresholdModifierPerPoint;
                float dairyRecoveryRateMod = dairy * config.DairyRecoveryRateModifierPerPoint;
                float fruitJumpCostMod = fruit * config.FruitJumpCostModifierPerPoint;
                float fruitDrainMod = fruit * config.FruitDrainRateModifierPerPoint;

                sb.AppendLine($"Grain: {grain:F1} (+{grainMaxStamMod * 100:F0}% max, -{grainJumpCostMod * 100:F0}% jump)");
                sb.AppendLine($"Protein: {protein:F1} (+{(proteinRecoveryMod * 100):F0}% rec, +{(proteinMaxStamMod * 100):F0}% max)");
                sb.AppendLine($"Vegetable: {vegetable:F1} (-{vegDrainMod * 100:F0}% drain, -{vegRecoveryThreshMod * 100:F0}% thresh)");
                sb.AppendLine($"Dairy: {dairy:F1} (-{dairyRecoveryThreshMod * 100:F0}% thresh, +{(dairyRecoveryRateMod * 100):F0}% rec)");
                sb.AppendLine($"Fruit: {fruit:F1} (-{fruitJumpCostMod * 100:F0}% jump, -{fruitDrainMod * 100:F0}% drain)");
            }

            // Stamina Section
            sb.AppendLine("\n--- Stamina Stats ---");
            var staminaTree = player.Entity.WatchedAttributes.GetTreeAttribute(EntityBehaviorVigorStamina.Name);
            if (staminaTree == null)
            {
                sb.AppendLine("Waiting for stamina data...");
            }
            else
            {
                sb.AppendLine($"Current: {staminaTree.GetFloat("currentStamina"):F1} / {staminaTree.GetFloat("maxStamina"):F1}");
                sb.AppendLine($"Exhausted: {staminaTree.GetBool("isExhausted")}");
            }

            _debugText.SetNewText(sb.ToString());
        }

        public override void Dispose()
        {
            base.Dispose();
            capi.Event.UnregisterGameTickListener(_listenerId);
        }

        public override string ToggleKeyCombinationCode => null;
        public override bool TryClose() => false;
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
