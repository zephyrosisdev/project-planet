using UnityEngine;
namespace MassiveArmy.Planetary
{
    public struct ScaledBodyPose {public Vector3 position;public float radius,factor;}
    public static class ScaledSpace
    {
        /// <summary>Rendering only: same factor for centre and radius preserves angular size.</summary>
        public static ScaledBodyPose Project(UniversePosition body,UniversePosition observer,double radius,float shellDistance=10000)
        {
            var d=body.RelativeTo(observer);double distance=d.Magnitude;
            double factor=System.Math.Min(1,shellDistance/System.Math.Max(1,distance));
            return new ScaledBodyPose{position=(d*factor).Float,radius=(float)(radius*factor),factor=(float)factor};
        }
    }
}
