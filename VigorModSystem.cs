using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vigor.Behaviors;
using Vigor.Config;
using Vigor.Hud;

namespace Vigor
{
    public class VigorModSystem : ModSystem
    {
        private ICoreClientAPI _capi;
        private HudVigorBar _vigorHud;
        public static VigorModSystem Instance { get; private set; }
        public VigorConfig CurrentConfig { get; private set; }
        public string ModId => Mod.Info.ModID;
        public ILogger Logger => Mod.Logger;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            Instance = this;
            LoadConfig(api);
            api.RegisterEntityBehaviorClass("vigor:vigorstamina", typeof(Behaviors.EntityBehaviorVigorStamina));

        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            _capi = api;

            _vigorHud = new HudVigorBar(api);
            api.Gui.RegisterDialog(_vigorHud);

            if (CurrentConfig.DebugMode) Logger.Notification($"[{ModId}] Client-side systems started.");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            if (CurrentConfig.DebugMode) Logger.Notification($"[{ModId}] Server-side systems started.");
        }

        public void LoadConfig(ICoreAPI api)
        {
            try
            {
                CurrentConfig = api.LoadModConfig<VigorConfig>($"{ModId}.json");

                if (CurrentConfig == null)
                {
                    Logger.Notification($"[{ModId}] No config found, creating default.");
                    CurrentConfig = new VigorConfig();
                    api.StoreModConfig(CurrentConfig, $"{ModId}.json");
                }
                else
                {
                    // Use a loud, unconditional log level to ensure this message always appears.
                Logger.Warning($"[{ModId}] Config loaded. DebugMode is set to: {CurrentConfig.DebugMode}");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[{ModId}] Failed to load or create config: {e}");
                CurrentConfig = new VigorConfig();
            }
        }
    }
}
