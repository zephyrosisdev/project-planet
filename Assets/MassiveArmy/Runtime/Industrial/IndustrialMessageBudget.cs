namespace MassiveArmy.Industrial {
    // Charge authenticated connections before JSON parsing, including malformed
    // and replayed payloads. Packet arrival never advances the simulation clock.
    public sealed class SurvivalMessageBudget {
        long window=-1,inputBytes,actionBytes;int inputs,actions;
        void Refresh(long tick,int rate){long next=tick/System.Math.Max(1,rate);if(next==window)return;window=next;inputs=actions=0;inputBytes=actionBytes=0;}
        public bool Input(long tick,int rate,int bytes){Refresh(tick,rate);inputs++;inputBytes+=System.Math.Max(0,bytes);return bytes>0&&bytes<=1024&&inputs<=rate*3&&inputBytes<=rate*3L*1024;}
        public bool Action(long tick,int rate,int bytes){Refresh(tick,rate);actions++;actionBytes+=System.Math.Max(0,bytes);return bytes>0&&bytes<=16384&&actions<=30&&actionBytes<=65536;}
    }
}
