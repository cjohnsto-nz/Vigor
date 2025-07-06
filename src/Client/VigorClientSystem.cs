using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vigor.Client
{
    public class VigorClientSystem : ModSystem
    {
        private ICoreClientAPI capi;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.capi = api;

            api.Input.InWorldAction += OnInWorldAction;
            
            capi.Logger.Notification("[Vigor] Client system loaded and listening for input actions.");
        }

        private void OnInWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            EntityPlayer player = capi.World.Player?.Entity;
            if (player == null) 
            {
                // If we can't get the player, don't interfere with actions
                // This could happen during loading or if the player entity is somehow unavailable
                return; 
            }

            bool isExhaustedAndSinking = player.WatchedAttributes.GetBool("vigor:exhaustedSinking", false);
            capi.Logger.Debug($"[Vigor Client] OnInWorldAction: Action={action}, On={on}, PlayerUID={player.PlayerUID}, IsExhaustedAndSinking={isExhaustedAndSinking}");

            if (isExhaustedAndSinking)
            {
                // Log the action being attempted
                capi.Logger.Notification($"[Vigor Client] Attempting to handle action: {action} because player is exhausted and sinking.");

                switch (action)
                {
                    case EnumEntityAction.Jump:
                    case EnumEntityAction.Up: // Prevent swimming up
                    case EnumEntityAction.Forward:
                    case EnumEntityAction.Backward:
                    case EnumEntityAction.Left:
                    case EnumEntityAction.Right:
                    case EnumEntityAction.Sprint:
                        capi.Logger.Notification($"[Vigor Client] Preventing action {action} due to exhaustion.");
                        handled = EnumHandling.PreventSubsequent;
                        break;
                    
                    // Optionally, explicitly allow 'Down' or do nothing
                    case EnumEntityAction.Down:
                        // Let it pass or handle specifically if needed
                        break;

                    default:
                        // Allow other actions
                        break;
                }
            }
        }

        public override void Dispose()
        {
            if (capi != null)
            {
                capi.Input.InWorldAction -= OnInWorldAction;
            }
            base.Dispose();
        }
    }
}
