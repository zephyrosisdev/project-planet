namespace MassiveArmy.Planetary {
    /// <summary>A focused, stable target must be held continuously; fires once per key press.</summary>
    public struct InteractionHold {
        long target;double started;bool tracking,fired;
        public bool Update(double now,bool pressed,bool allowed,long currentTarget,double seconds=.45){
            if(!pressed){this=default;return false;}
            if(fired)return false;
            if(!allowed||currentTarget==0){tracking=false;target=0;return false;}
            if(!tracking||target!=currentTarget||now<started){tracking=true;target=currentTarget;started=now;return false;}
            if(now-started<seconds)return false;fired=true;return true;
        }
    }
}
