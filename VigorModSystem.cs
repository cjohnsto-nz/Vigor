using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vigor.API;
using Vigor.Behaviors;
using Vigor.Config;
using Vigor.Hud;

namespace Vigor
{
    public class VigorModSystem : ModSystem
    {
        private ICoreClientAPI _capi;
        private HudVigorBar _vigorHud;
        private HudVigorDebug _debugHud;
        public static VigorModSystem Instance { get; private set; }
        public VigorConfig CurrentConfig { get; private set; }
        public string ModId => Mod.Info.ModID;
        public ILogger Logger => Mod.Logger;
        
        // Public API instances that other mods can access
        public IVigorAPI API { get; private set; } // General API - maintains backward compatibility
        public IVigorAPI ClientAPI { get; private set; } // Client-specific API
        public IVigorAPI ServerAPI { get; private set; } // Server-specific API

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            Instance = this;
            LoadConfig(api);
            
            // Create general API instance for backward compatibility
            // Note: This instance should be avoided in client/server contexts
            API = new VigorAPI(api);
            
            // Register behavior
            api.RegisterEntityBehaviorClass("vigor:vigorstamina", typeof(Behaviors.EntityBehaviorVigorStamina));
            
            // Always log API availability for debugging integration
            Logger.Event($"[{ModId}] {api.Side} API initialized and available via ModLoader.GetModSystem<VigorModSystem>().API");
            
            // Log config/debug mode too
            if (CurrentConfig.DebugMode) Logger.Notification($"[{ModId}] API available for other mods via ModLoader.GetModSystem<VigorModSystem>().API");
        }

        // Added property to check for HydrateOrDiedrate mod
        public bool IsHydrateOrDiedrateLoaded { get; private set; }
        
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            _capi = api;
            
            // Create client-specific API instance
            ClientAPI = new VigorAPI(api);
            Logger.Event($"[{ModId}] Client API initialized and available via ModLoader.GetModSystem<VigorModSystem>().ClientAPI");
            
            // Check if HydrateOrDiedrate mod is installed
            IsHydrateOrDiedrateLoaded = api.ModLoader.IsModEnabled("hydrateordiedrate");
            
            if (IsHydrateOrDiedrateLoaded && CurrentConfig.DebugMode)
            {
                Logger.Notification($"[{ModId}] HydrateOrDiedrate mod detected, will adjust HUD position.");
            }

            _vigorHud = new HudVigorBar(api);
            _debugHud = new HudVigorDebug(api);
            api.Gui.RegisterDialog(_vigorHud);
            api.Gui.RegisterDialog(_debugHud);

            api.Input.RegisterHotKey("vigordebug", "Vigor: Toggle Debug Info", GlKeys.F8, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("vigordebug", OnToggleDebugHud);

            if (CurrentConfig.DebugMode) Logger.Notification($"[{ModId}] Client-side systems started.");
        }

        private bool OnToggleDebugHud(KeyCombination comb)
        {
            _debugHud?.Toggle();
            return true;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            
            // Create server-specific API instance
            ServerAPI = new VigorAPI(api);
            Logger.Event($"[{ModId}] Server API initialized and available via ModLoader.GetModSystem<VigorModSystem>().ServerAPI");
            
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
