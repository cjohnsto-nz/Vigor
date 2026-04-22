using System;
using System.Text;
using Vigor.Behaviors;
using Vigor.Config;
using Vigor.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vigor.Hud
{
    public class HudVigorDebug : HudElement
    {
        private GuiElementDynamicText _leftDebugText;
        private GuiElementDynamicText _rightDebugText;
        private long _listenerId;

        public HudVigorDebug(ICoreClientAPI capi) : base(capi)
        {
            if (VigorModSystem.Instance.CurrentConfig.DebugMode)
            {
                _listenerId = capi.Event.RegisterGameTickListener(OnGameTick, 250);
                ComposeDialog();
            }
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();
            UpdateDebugText();
        }

        private void OnGameTick(float dt)
        {
            if (_leftDebugText == null || _rightDebugText == null) return;
            UpdateDebugText();
        }

        private void ComposeDialog()
        {
            ElementBounds leftTextBounds = ElementBounds.Fixed(0, 20, 360, 620);
            ElementBounds rightTextBounds = ElementBounds.Fixed(380, 20, 360, 620);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftTextBounds, rightTextBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            var composer = capi.Gui.CreateCompo("vigorinfodialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Vigor Debug Info", () => TryClose());

            composer.AddDynamicText("", CairoFont.WhiteDetailText(), leftTextBounds, "debugtext-left");
            composer.AddDynamicText("", CairoFont.WhiteDetailText(), rightTextBounds, "debugtext-right");

            Composers["vigorinfodialog"] = composer.Compose();
            _leftDebugText = Composers["vigorinfodialog"].GetDynamicText("debugtext-left");
            _rightDebugText = Composers["vigorinfodialog"].GetDynamicText("debugtext-right");

            if (VigorModSystem.Instance.CurrentConfig.DebugMode) TryOpen();
        }

        private void UpdateDebugText()
        {
            var left = new StringBuilder();
            var right = new StringBuilder();

            var config = VigorModSystem.Instance?.CurrentConfig;
            if (config == null)
            {
                left.AppendLine("Waiting for config...");
                _leftDebugText?.SetNewText(left.ToString());
                _rightDebugText?.SetNewText(string.Empty);
                return;
            }

            var player = capi.World?.Player;
            if (player == null)
            {
                left.AppendLine("Waiting for player data...");
                _leftDebugText?.SetNewText(left.ToString());
                _rightDebugText?.SetNewText(string.Empty);
                return;
            }

            // Nutrition Section
            left.AppendLine("--- Vigor Nutrition ---");
            var hungerTree = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hungerTree == null)
            {
                left.AppendLine("Waiting for nutrition data (hunger)...");
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

                left.AppendLine($"Grain: {grain:F1} (+{grainMaxStamMod * 100:F0}% max, -{grainJumpCostMod * 100:F0}% jump)");
                left.AppendLine($"Protein: {protein:F1} (+{proteinRecoveryMod * 100:F0}% rec, +{proteinMaxStamMod * 100:F0}% max)");
                left.AppendLine($"Vegetable: {vegetable:F1} (-{vegDrainMod * 100:F0}% drain, -{vegRecoveryThreshMod * 100:F0}% thresh)");
                left.AppendLine($"Dairy: {dairy:F1} (-{dairyRecoveryThreshMod * 100:F0}% thresh, +{dairyRecoveryRateMod * 100:F0}% rec)");
                left.AppendLine($"Fruit: {fruit:F1} (-{fruitJumpCostMod * 100:F0}% jump, -{fruitDrainMod * 100:F0}% drain)");
            }

            // Stamina Section
            left.AppendLine("\n--- Stamina Stats ---");
            var staminaTree = player.Entity.WatchedAttributes.GetTreeAttribute(EntityBehaviorVigorStamina.Name);
            if (staminaTree == null)
            {
                left.AppendLine("Waiting for stamina data...");
            }
            else
            {
                var syncedState = VigorModSystem.Instance.GetClientStaminaState(player.PlayerUID);
                float maxStamina = syncedState?.MaxStamina ?? staminaTree.GetFloat("calculatedMaxStamina", staminaTree.GetFloat("maxStamina"));
                float currentStamina = syncedState?.CurrentStamina ?? staminaTree.GetFloat("currentStamina");
                bool isExhausted = syncedState?.IsExhausted ?? staminaTree.GetBool("isExhausted");
                float recoveryThreshold = staminaTree.GetFloat("debug_recoveryThreshold");

                left.AppendLine($"Stamina: {currentStamina:F1} / {maxStamina:F1}");
                left.AppendLine($"Exhausted: {isExhausted} (Recovery at > {recoveryThreshold:F1})");
                left.AppendLine($"Sinking: {staminaTree.GetBool(EntityBehaviorVigorStamina.ATTR_EXHAUSTED_SINKING)}");

                left.AppendLine("\n--- Player State (Debug) ---");
                left.AppendLine($"Idle: {staminaTree.GetBool("debug_isIdle")}");
                left.AppendLine($"Sitting: {staminaTree.GetBool("debug_isSitting")}");
                left.AppendLine($"Sprinting: {staminaTree.GetBool("debug_isSprinting")}");
                left.AppendLine($"Swimming: {staminaTree.GetBool("debug_isSwimming")}");
                left.AppendLine($"Jumping: {staminaTree.GetBool("debug_isJumping")}");
                left.AppendLine($"Fatiguing Action: {staminaTree.GetBool("debug_fatiguingActionThisTick")}");
                left.AppendLine($"Regen Blocked: {staminaTree.GetBool("debug_regenPrevented")}");

                left.AppendLine("\n--- Rates & Timers (Debug) ---");
                left.AppendLine($"Cost/sec: {staminaTree.GetFloat("debug_costPerSecond"):F2}");
                left.AppendLine($"Gain/sec: {staminaTree.GetFloat("debug_staminaGainPerSecond"):F2}");
                left.AppendLine($"Time Since Fatigue: {staminaTree.GetFloat("debug_timeSinceFatigue"):F1}s");

                left.AppendLine("\n--- Final Modifiers (Debug) ---");
                left.AppendLine($"Max Stamina: {staminaTree.GetFloat("debug_mod_maxStamina"):P0}");
                left.AppendLine($"Recovery Rate: {staminaTree.GetFloat("debug_mod_recoveryRate"):P0}");
                left.AppendLine($"Drain Rate: {staminaTree.GetFloat("debug_mod_drainRate"):P0}");
                left.AppendLine($"Jump Cost: {staminaTree.GetFloat("debug_mod_jumpCost"):P0}");
                left.AppendLine($"Recovery Threshold: {staminaTree.GetFloat("debug_mod_recoveryThreshold"):P0}");
                left.AppendLine($"Recovery Delay: {staminaTree.GetFloat("debug_mod_recoveryDelay"):P0}");
            }

            right.AppendLine("\n--- Batching Diagnostics ---");
            long setAttempts = VigorDiagnostics.GetCounter("batchedTree.setCalls");
            long stagedChanges = VigorDiagnostics.GetCounter("batchedTree.setStaged");
            long noOpSkips = VigorDiagnostics.GetCounter("batchedTree.setNoOpSkipped");
            long debugFiltered = VigorDiagnostics.GetCounter("batchedTree.setDebugFiltered");
            long markDirtyCalls = VigorDiagnostics.GetCounter("batchedTree.markPathDirty");
            long forcedFlushes = VigorDiagnostics.GetCounter("batchedTree.forceSync");
            long deferredFlushes = VigorDiagnostics.GetCounter("batchedTree.flushInterval");
            long skippedNoChanges = VigorDiagnostics.GetCounter("batchedTree.flushSkippedNoChanges");
            long skippedNotDue = VigorDiagnostics.GetCounter("batchedTree.flushSkippedNotDue");
            double activeBatchers = VigorDiagnostics.GetGauge("batchedTree.activeApprox");
            double pendingFloats = VigorDiagnostics.GetGauge("batchedTree.pendingFloatCount");
            double pendingBools = VigorDiagnostics.GetGauge("batchedTree.pendingBoolCount");
            double pendingInts = VigorDiagnostics.GetGauge("batchedTree.pendingIntCount");
            double pendingStrings = VigorDiagnostics.GetGauge("batchedTree.pendingStringCount");
            double stagingRate = setAttempts > 0 ? Math.Max(0, stagedChanges * 100d / setAttempts) : 0d;
            double batchingEffectiveness = stagedChanges > 0 ? Math.Max(0, (double)(stagedChanges - markDirtyCalls) * 100d / stagedChanges) : 0d;

            right.AppendLine($"Set attempts: {setAttempts}");
            right.AppendLine($"Staged changes: {stagedChanges} ({stagingRate:F1}% of attempts)");
            right.AppendLine($"Skipped sets: no-op {noOpSkips}, debug-filter {debugFiltered}");
            right.AppendLine($"MarkDirty calls: {markDirtyCalls} ({batchingEffectiveness:F1}% coalesced)");
            right.AppendLine($"Flushes: forced {forcedFlushes}, deferred {deferredFlushes}");
            right.AppendLine($"Skipped flushes: empty {skippedNoChanges}, not due {skippedNotDue}");
            right.AppendLine($"Active batchers: {activeBatchers:F0}");
            right.AppendLine($"Pending staged values: F {pendingFloats:F0}, B {pendingBools:F0}, I {pendingInts:F0}, S {pendingStrings:F0}");
            right.AppendLine($"Packet-created batchers: {VigorDiagnostics.GetCounter("network.packetLocalPlayerBatchedTreeCreated")}");
            right.AppendLine($"Local packets cached: {VigorDiagnostics.GetCounter("network.packetLocalPlayerCached")}");

            right.AppendLine("\n--- Prediction Diagnostics ---");
            right.AppendLine($"Prediction mode: {(VigorModSystem.Instance.CurrentConfig.UseNewClientPredictionModel ? "New" : "Legacy")}");
            right.AppendLine($"Sync interval: {VigorModSystem.Instance.CurrentConfig.StaminaSyncIntervalSeconds * 1000f:F0} ms");
            right.AppendLine($"Prediction error: {VigorDiagnostics.GetGauge("prediction.error"):F2}");
            right.AppendLine($"Pending correction: {VigorDiagnostics.GetGauge("prediction.pendingCorrection"):F2}");
            right.AppendLine($"Recovering: {VigorDiagnostics.GetGauge("prediction.isRecovering"):F0}");
            right.AppendLine($"Local delta: {VigorDiagnostics.GetGauge("prediction.localDelta"):F2}");
            right.AppendLine($"Local recovery gain: {VigorDiagnostics.GetGauge("prediction.localRecoveryGain"):F2}");
            right.AppendLine($"Last queued correction: {VigorDiagnostics.GetGauge("prediction.lastQueuedCorrection"):F2}");
            right.AppendLine($"Last applied correction: {VigorDiagnostics.GetGauge("prediction.lastAppliedCorrection"):F2}");
            right.AppendLine($"Last server packet delta: {VigorDiagnostics.GetGauge("prediction.lastServerPacketDelta"):F2}");
            right.AppendLine($"Cooldown remaining: {VigorDiagnostics.GetGauge("prediction.cooldownRemaining"):F2}s");
            right.AppendLine($"Packet age: {VigorDiagnostics.GetGauge("prediction.lastPacketAgeMs"):F0} ms");
            right.AppendLine($"Packet interval: {VigorDiagnostics.GetGauge("prediction.lastServerPacketIntervalMs"):F0} ms");
            right.AppendLine($"Server packets: {VigorDiagnostics.GetCounter("prediction.serverPackets")}");
            right.AppendLine($"Soft corrections: {VigorDiagnostics.GetCounter("prediction.softCorrections")}");
            right.AppendLine($"Jump grace suppressions: {VigorDiagnostics.GetCounter("prediction.jumpGraceSuppressions")}");
            right.AppendLine($"Recovery suppressions: {VigorDiagnostics.GetCounter("prediction.recoverySuppressions")}");
            right.AppendLine($"Recovery corrections: + {VigorDiagnostics.GetCounter("prediction.recoveryPositiveCorrections")}, - {VigorDiagnostics.GetCounter("prediction.recoveryNegativeCorrections")}");
            right.AppendLine($"Snaps: hard {VigorDiagnostics.GetCounter("prediction.hardSnaps")}, boundary {VigorDiagnostics.GetCounter("prediction.boundarySnaps")}");

            _leftDebugText?.SetNewText(left.ToString());
            _rightDebugText?.SetNewText(right.ToString());
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
