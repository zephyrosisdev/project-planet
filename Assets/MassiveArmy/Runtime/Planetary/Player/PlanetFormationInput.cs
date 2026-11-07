using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        bool formationDragging;Vector3 formationStart,formationFinish;Vector2 formationPointer;
        void FormationInput(Mouse mouse,bool placing){
            if(BiologicalTargetArmed||NuclearTargetArmed||placing||modes.Mode!=PlayerControlMode.RTS||!rig.InputEnabled||!Application.isFocused||mouse==null||RemoteInputBlocked||sessionMode==PlanetGameMode.AiTest){formationDragging=false;return;}
            var pointer=mouse.position.ReadValue();bool blocked=hud?.Blocks(pointer)==true;
            if(mouse.rightButton.wasPressedThisFrame&&!blocked&&rig.GroundPoint(pointer,out formationStart)){formationDragging=true;formationFinish=formationStart;formationPointer=pointer;}
            if(!formationDragging)return;
            if(!blocked&&rig.GroundPoint(pointer,out var finish))formationFinish=finish;
            if(mouse.rightButton.wasReleasedThisFrame){formationDragging=false;if(blocked)return;bool dragged=(pointer-formationPointer).sqrMagnitude>64;
                if(commands!=null)OrderSelected(formationStart,formationEnd:dragged?formationFinish:(Vector3?)null);else {goal=formationFinish;navigation.SetGoal(0,goal);}}
        }
        void DrawFormationPreview(){
            if(!formationDragging||commands==null||commands.SelectedCount==0)return;
            var colour=GUI.color;GUI.color=Color.cyan;
            var centre=(formationStart+formationFinish).normalized;if(!CubeSphere.TryFormationRight(formationStart,formationFinish,out var right)){GUI.color=colour;return;}var forward=Vector3.Cross(right,centre);
            float width=CubeSphere.ArcDistance(formationStart,formationFinish,definition.radius),depth=0;
            void Mark(Vector3 up,float size){var p=view.WorldToScreenPoint(PlanetRenderCentre+surface.GetSurfacePosition(up,1));if(p.z>0)GUI.DrawTexture(new Rect(p.x-size*.5f,Screen.height-p.y-size*.5f,size,size),Texture2D.whiteTexture);}
            // Sample a bounded set of slot markers; no unit positions are read back.
            for(int m=0;m<5;m++){int count=commands.FormationCount(m);if(count==0)continue;float spacing=commands.FormationSpacing(m);int columns=Mathf.Clamp(Mathf.FloorToInt(width/spacing)+1,1,count),stride=Mathf.Max(1,Mathf.CeilToInt(commands.SelectedCount/128f));
                for(int rank=0;rank<count;rank+=stride){int row=rank/columns,rowCount=Mathf.Min(columns,count-row*columns);float across=(rank%columns-(rowCount-1)*.5f)*spacing;Mark((centre+(right*across-forward*(row*spacing+depth))/definition.radius).normalized,5);}
                depth+=((count+columns-1)/columns)*spacing+spacing;
            }
            // An arrow perpendicular to the front edge shows the final facing.
            float arrow=Mathf.Max(15,Mathf.Min(80,width*.35f));
            for(int i=0;i<=16;i++)Mark((centre+forward*(arrow*i/16)/definition.radius).normalized,3);
            for(int i=0;i<=8;i++){float t=i/8f;Mark((centre+(forward*(arrow-arrow*.3f*t)+right*arrow*.2f*t)/definition.radius).normalized,3);Mark((centre+(forward*(arrow-arrow*.3f*t)-right*arrow*.2f*t)/definition.radius).normalized,3);}
            GUI.color=colour;
        }
    }
}
