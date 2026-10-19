using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementHouseholdValidation {
        void ValidatePlacementCaches(){
            var local=world.Base;var b=local.Buildings[0];var position=b.position;int rotation=b.rotation,width=b.definition.footprintWidth;
            void Verify(string label){var expected=local.Grid.Area(b.definition,CubeSphere.Decode(b.position),b.rotation);var actual=local.Area(b);Check(actual.face==expected.face&&actual.x==expected.x&&actual.y==expected.y&&actual.width==expected.width&&actual.depth==expected.depth&&actual.rotation==expected.rotation,label+": cached footprint matches direct grid calculation");var port=local.Grid.Direction(local.Grid.Port(expected));Check((local.Roads.Port(b)-port).sqrMagnitude<1e-12f,label+": road entrance matches direct grid calculation");int node=local.Roads.Node(port);Check(local.Roads.Connected(b,b)==(node>=0),label+": cached road node matches current graph");}
            try{Verify("Initial placement");Verify("Repeated query");b.rotation=(rotation+1)&3;Verify("Rotated placement");b.position=CubeSphere.Encode(local.Grid.Point(local.Area(b).face,local.Area(b).x+30,local.Area(b).y+30));Verify("Moved placement");b.definition.footprintWidth=width+1;Verify("Changed footprint dimensions");}
            finally{b.position=position;b.rotation=rotation;b.definition.footprintWidth=width;}
            Verify("Restored placement");
        }
    }
}
