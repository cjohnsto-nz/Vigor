using ProtoBuf;
using Vintagestory.API.Common;

namespace Vigor.Utils
{
    /// <summary>
    /// Network packet containing player stamina state information
    /// </summary>
    [ProtoContract]
    public class StaminaStatePacket
    {
        [ProtoMember(1)]
        public string PlayerUID { get; set; }
        
        [ProtoMember(2)]
        public float CurrentStamina { get; set; }
        
        [ProtoMember(3)]
        public float MaxStamina { get; set; }
        
        [ProtoMember(4)]
        public bool IsExhausted { get; set; }
    }
}
