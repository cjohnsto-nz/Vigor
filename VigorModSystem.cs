using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vigor.API;
using Vigor.Behaviors;
using Vigor.Config;
using Vigor.Hud;
using Vigor.Utils;

namespace Vigor
{
    public class VigorModSystem : ModSystem
    {
        private const string NETWORK_CHANNEL = "vigor:statesync";
        private const float SYNC_INTERVAL_SECONDS = 1.0f; // How often to sync state (in seconds)
        
        private ICoreClientAPI _capi;
        private ICoreServerAPI _sapi;
        private HudVigorBar _vigorHud;
        private HudVigorDebug _debugHud;
        private long _syncTimerId;
        private IClientNetworkChannel _clientNetworkChannel;
        private IServerNetworkChannel _serverNetworkChannel;
        
        // Dictionary to store synchronized client-side stamina state by player UID
        private Dictionary<string, StaminaStatePacket> _clientStaminaState = new Dictionary<string, StaminaStatePacket>();
        
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
            
            // Register network channel for stamina state synchronization
            api.Network.RegisterChannel(NETWORK_CHANNEL)
                .RegisterMessageType<StaminaStatePacket>();
                
            Logger.Event($"[{ModId}] Network channel '{NETWORK_CHANNEL}' registered for stamina state synchronization");
            
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
            
            // Set up client network channel and register packet handler
            _clientNetworkChannel = api.Network.GetChannel(NETWORK_CHANNEL);
            _clientNetworkChannel.SetMessageHandler<StaminaStatePacket>(OnStaminaStatePacket);
            Logger.Event($"[{ModId}] Client network handler registered for stamina state synchronization");
            
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
        
        /// <summary>
        /// Client-side handler for stamina state packet
        /// </summary>
        private void OnStaminaStatePacket(StaminaStatePacket packet)
        {
            if (_capi == null) return;
            
            // Store the updated state in our client-side dictionary
            _clientStaminaState[packet.PlayerUID] = packet;
            
            if (CurrentConfig.DebugMode && _capi.World.Player.PlayerUID == packet.PlayerUID)
            {
                _capi.Logger.Debug($"[{ModId}] Received stamina state: {packet.CurrentStamina}/{packet.MaxStamina}, Exhausted: {packet.IsExhausted}");
            }
        }
        
        /// <summary>
        /// Gets the synchronized client-side stamina state for a player
        /// </summary>
        /// <param name="playerUID">The player UID</param>
        /// <returns>The stamina state packet, or null if not found</returns>
        public StaminaStatePacket GetClientStaminaState(string playerUID)
        {
            if (string.IsNullOrEmpty(playerUID)) return null;
            
            if (_clientStaminaState.TryGetValue(playerUID, out var state))
            {
                return state;
            }
            
            return null;
        }
        
        /// <summary>
        /// Server-side method to sync stamina state to clients
        /// </summary>
        private void SyncStaminaState(float dt)
        {
            if (_sapi == null) return;
            
            try
            {
                // Sync stamina state for all online players
                foreach (var player in _sapi.World.AllOnlinePlayers)
                {
                    // Get player entity
                    var playerEntity = player.Entity;
                    if (playerEntity == null) continue;
                    
                    // Get stamina behavior
                    var staminaBehavior = playerEntity.GetBehavior<EntityBehaviorVigorStamina>();
                    if (staminaBehavior == null) continue;
                    
                    // Create packet with current state
                    var packet = new StaminaStatePacket
                    {
                        PlayerUID = player.PlayerUID,
                        CurrentStamina = staminaBehavior.CurrentStamina,
                        MaxStamina = staminaBehavior.MaxStamina,
                        IsExhausted = staminaBehavior.IsExhausted
                    };
                    
                    // Send to player (cast to IServerPlayer is safe since we're in server-side code)
                    _serverNetworkChannel.SendPacket(packet, player as IServerPlayer);
                    
                    if (CurrentConfig.DebugMode && _sapi.World.Rand.NextDouble() < 0.05) // Only log occasionally
                    {
                        _sapi.Logger.Debug($"[{ModId}] Synced stamina state for {player.PlayerName}: {packet.CurrentStamina}/{packet.MaxStamina}, Exhausted: {packet.IsExhausted}");
                    }
                }
            }
            catch (Exception ex)
            {
                _sapi.Logger.Error($"[{ModId}] Error syncing stamina state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cleanup when player disconnects
        /// </summary>
        private void OnPlayerDisconnect(IServerPlayer player)
        {
            if (player != null)
            {
                // No need to sync data for disconnected players
                _sapi?.Logger.Debug($"[{ModId}] Player {player.PlayerName} disconnected, cleaning up sync state");
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            _sapi = api;
            
            // Create server-specific API instance
            ServerAPI = new VigorAPI(api);
            Logger.Event($"[{ModId}] Server API initialized and available via ModLoader.GetModSystem<VigorModSystem>().ServerAPI");
            
            // Set up server network channel
            _serverNetworkChannel = api.Network.GetChannel(NETWORK_CHANNEL);
            if (_serverNetworkChannel == null)
            {
                Logger.Error($"[{ModId}] Failed to get network channel for stamina state sync");
            }
            else
            {
                Logger.Event($"[{ModId}] Server network channel ready for stamina state synchronization");
            }
            
            // Register periodic sync timer
            _syncTimerId = api.Event.RegisterGameTickListener(SyncStaminaState, (int)(SYNC_INTERVAL_SECONDS * 1000));
            Logger.Event($"[{ModId}] Registered stamina state sync timer with interval {SYNC_INTERVAL_SECONDS}s");
            
            // Listen for player disconnect to clean up sync state
            api.Event.PlayerDisconnect += OnPlayerDisconnect;
            
            if (CurrentConfig.DebugMode) Logger.Notification($"[{ModId}] Server-side systems started with state synchronization.");
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
