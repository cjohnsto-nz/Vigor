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
            ElementBounds textBounds = ElementBounds.Fixed(0, 20, 300, 600);
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

            if (VigorModSystem.Instance.CurrentConfig.DebugMode) TryOpen();
        }

        private void UpdateDebugText()
        {
            var sb = new StringBuilder();

            var config = VigorModSystem.Instance?.CurrentConfig;
            if (config == null)
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

            // Nutrition Section
            sb.AppendLine("--- Vigor Nutrition ---");
            var hungerTree = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hungerTree == null)
            {
                sb.AppendLine("Waiting for nutrition data (hunger)...");
            }
            else
            {
                const float NUTRITION_MAX = 1500f;
                float protein = hungerTree.GetFloat("proteinLevel", 0);
                float fruit = hungerTree.GetFloat("fruitLevel", 0);
                float vegetable = hungerTree.GetFloat("vegetableLevel", 0);
                float dairy = hungerTree.GetFloat("dairyLevel", 0);
                float grain = hungerTree.GetFloat("grainLevel", 0);

                float grainMaxStamMod = (grain / NUTRITION_MAX) * config.GrainMaxStaminaBonusAtMax;
                float grainJumpCostMod = (grain / NUTRITION_MAX) * config.GrainJumpCostBonusAtMax;
                float proteinRecoveryMod = (protein / NUTRITION_MAX) * config.ProteinRecoveryRateBonusAtMax;
                float proteinMaxStamMod = (protein / NUTRITION_MAX) * config.ProteinMaxStaminaBonusAtMax;
                float vegDrainMod = (vegetable / NUTRITION_MAX) * config.VegetableDrainRateBonusAtMax;
                float vegRecoveryThreshMod = (vegetable / NUTRITION_MAX) * config.VegetableRecoveryThresholdBonusAtMax;
                float dairyRecoveryThreshMod = (dairy / NUTRITION_MAX) * config.DairyRecoveryThresholdBonusAtMax;
                float dairyRecoveryRateMod = (dairy / NUTRITION_MAX) * config.DairyRecoveryRateBonusAtMax;
                float fruitJumpCostMod = (fruit / NUTRITION_MAX) * config.FruitJumpCostBonusAtMax;
                float fruitDrainMod = (fruit / NUTRITION_MAX) * config.FruitDrainRateBonusAtMax;

                sb.AppendLine($"Grain: {grain:F1} (+{grainMaxStamMod * 100:F0}% max, -{grainJumpCostMod * 100:F0}% jump)");
                sb.AppendLine($"Protein: {protein:F1} (+{proteinRecoveryMod * 100:F0}% rec, +{proteinMaxStamMod * 100:F0}% max)");
                sb.AppendLine($"Vegetable: {vegetable:F1} (-{vegDrainMod * 100:F0}% drain, -{vegRecoveryThreshMod * 100:F0}% thresh)");
                sb.AppendLine($"Dairy: {dairy:F1} (-{dairyRecoveryThreshMod * 100:F0}% thresh, +{dairyRecoveryRateMod * 100:F0}% rec)");
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
                float maxStamina = staminaTree.GetFloat("calculatedMaxStamina", staminaTree.GetFloat("maxStamina"));
                float currentStamina = staminaTree.GetFloat("currentStamina");
                bool isExhausted = staminaTree.GetBool("isExhausted");
                float recoveryThreshold = staminaTree.GetFloat("debug_recoveryThreshold");

                sb.AppendLine($"Stamina: {currentStamina:F1} / {maxStamina:F1}");
                sb.AppendLine($"Exhausted: {isExhausted} (Recovery at > {recoveryThreshold:F1})");
                sb.AppendLine($"Sinking: {staminaTree.GetBool(EntityBehaviorVigorStamina.ATTR_EXHAUSTED_SINKING)}");

                sb.AppendLine("\n--- Player State (Debug) ---");
                sb.AppendLine($"Idle: {staminaTree.GetBool("debug_isIdle")}");
                sb.AppendLine($"Sprinting: {staminaTree.GetBool("debug_isSprinting")}");
                sb.AppendLine($"Swimming: {staminaTree.GetBool("debug_isSwimming")}");
                sb.AppendLine($"Jumping: {staminaTree.GetBool("debug_isJumping")}");
                sb.AppendLine($"Fatiguing Action: {staminaTree.GetBool("debug_fatiguingActionThisTick")}");
                sb.AppendLine($"Regen Blocked: {staminaTree.GetBool("debug_regenPrevented")}");

                sb.AppendLine("\n--- Rates & Timers (Debug) ---");
                sb.AppendLine($"Cost/sec: {staminaTree.GetFloat("debug_costPerSecond"):F2}");
                sb.AppendLine($"Gain/sec: {staminaTree.GetFloat("debug_staminaGainPerSecond"):F2}");
                sb.AppendLine($"Time Since Fatigue: {staminaTree.GetFloat("debug_timeSinceFatigue"):F1}s");

                sb.AppendLine("\n--- Final Modifiers (Debug) ---");
                sb.AppendLine($"Max Stamina: {staminaTree.GetFloat("debug_mod_maxStamina"):P0}");
                sb.AppendLine($"Recovery Rate: {staminaTree.GetFloat("debug_mod_recoveryRate"):P0}");
                sb.AppendLine($"Drain Rate: {staminaTree.GetFloat("debug_mod_drainRate"):P0}");
                sb.AppendLine($"Jump Cost: {staminaTree.GetFloat("debug_mod_jumpCost"):P0}");
                sb.AppendLine($"Recovery Threshold: {staminaTree.GetFloat("debug_mod_recoveryThreshold"):P0}");
            }

            _debugText?.SetNewText(sb.ToString());
        }

        public override void Dispose()
        {
            base.Dispose();
            capi.Event.UnregisterGameTickListener(_listenerId);
        }

        public override void Toggle()
        {
            if (IsOpened()) TryClose();
            else TryOpen();
        }

        public override string ToggleKeyCombinationCode => null;
        public override bool TryClose() => base.TryClose();
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override bool Focusable => false;
    }
}
