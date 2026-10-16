using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>Integer reference for validation/debugging. Runtime traversal is in compute shaders.</summary>
    public static class PlanetCbtTopology
    {
        public static int Depth(uint node){int depth=-1;while(node!=0){depth++;node>>=1;}return depth;}
        public static void Decode(int root,uint node,int grid,out Vector2Int a,out Vector2Int b,out Vector2Int c)
        {
            if((root&1)==0){a=new Vector2Int(0,0);b=new Vector2Int(grid,0);c=new Vector2Int(grid,grid);}
            else{a=new Vector2Int(grid,grid);b=new Vector2Int(0,grid);c=new Vector2Int(0,0);}
            for(int bit=Depth(node)-1;bit>=0;bit--){var midpoint=(a+c)/2;var oldB=b;if(((node>>bit)&1)==0){c=a;a=oldB;}else{a=c;c=oldB;}b=midpoint;}
        }
        public static Vector3Int CubeVertex(int root,Vector2Int p,int grid)
        {
            int face=root/2;return Vector3Int.RoundToInt(CubeSphere.Normal[face]*grid+CubeSphere.Right[face]*(p.x*2-grid)+CubeSphere.Up[face]*(p.y*2-grid));
        }
        public static int VertexIndex(int root,Vector2Int p,int grid)
        {
            var v=CubeVertex(root,p,grid);int face=Mathf.Abs(v.x)==grid?(v.x>=0?0:1):Mathf.Abs(v.y)==grid?(v.y>=0?2:3):(v.z>=0?4:5);
            int x=((int)Vector3.Dot(v,CubeSphere.Right[face])+grid)/2,y=((int)Vector3.Dot(v,CubeSphere.Up[face])+grid)/2;
            return face*(grid+1)*(grid+1)+y*(grid+1)+x;
        }
    }
}
