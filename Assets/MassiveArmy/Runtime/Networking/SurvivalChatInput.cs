namespace MassiveArmy.Networking {
    // Shared input policy; it owns no simulation, rendering or cursor resources.
    internal sealed class SurvivalChatInput {
        internal bool Typing {get;private set;}
        internal string Text="";int capturedFrame=-1;
        internal bool Blocks(int frame)=>Typing||capturedFrame==frame;
        internal string Enter(int frame){capturedFrame=frame;if(!Typing){Typing=true;Text="";return null;}string message=(Text??"").Trim();Typing=false;Text="";return message.Length==0?null:message;}
        internal bool Escape(int frame){if(!Typing)return false;capturedFrame=frame;Typing=false;Text="";return true;}
        internal bool CanTransmit(int frame,bool focused,bool held,bool voiceReady)=>focused&&held&&voiceReady&&!Blocks(frame);
        internal void Clear(){Typing=false;Text="";}
    }
}
