using UnityEngine;

namespace MassiveArmy
{
    // Drag defines the front edge; rows extend behind its perpendicular.
    public readonly struct FormationLayout
    {
        public readonly Vector2 Front,Right;
        public readonly float Width,Spacing;
        public FormationLayout(Vector2 front,Vector2 right,float width,float spacing=BattleSettings.FormationSpacing)
        { Front=front;Right=right.sqrMagnitude>.001f?right.normalized:Vector2.right;Width=Mathf.Max(0,width);Spacing=Mathf.Max(BattleSettings.FormationSpacing,spacing); }
        public int Columns(int count) => Mathf.Clamp(Mathf.FloorToInt(Width/Spacing)+1,1,Mathf.Max(1,count));
        public int Rows(int count) => (count+Columns(count)-1)/Columns(count);
        public bool Fits(float worldSize,int count)
        {
            if(count<1)return true;
            float width=(Columns(count)-1)*Spacing,depth=(Rows(count)-1)*Spacing;
            // A footprint must not overlap its own repeated image.
            return Mathf.Sqrt(width*width+depth*depth)<worldSize-Spacing;
        }
        public Vector2 Slot(int rank,int count)
        {
            int columns=Columns(count),row=rank/columns,rowCount=Mathf.Min(columns,count-row*columns);
            return Front+Right*(rank%columns-(rowCount-1)*.5f)*Spacing-new Vector2(-Right.y,Right.x)*row*Spacing;
        }
        public static FormationLayout Compact(Vector2 centre,Vector2 right,int count,float spacing=BattleSettings.FormationSpacing)
        {
            int columns=Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1,count))),rows=(Mathf.Max(1,count)+columns-1)/columns;
            right=right.normalized;Vector2 forward=new Vector2(-right.y,right.x);
            return new FormationLayout(centre+forward*((rows-1)*spacing*.5f),right,(columns-1)*spacing,spacing);
        }
    }
}
