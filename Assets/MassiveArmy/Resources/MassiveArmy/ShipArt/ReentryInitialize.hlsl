void InitializeReentry(inout VFXAttributes attributes, in float3 FlowDirection, in float3 HalfExtents, in float Intensity, in float Heat)
{
    uint a=attributes.particleId*747796405u+2891336453u;
    a=((a>>((a>>28u)+4u))^a)*277803737u;a=(a>>22u)^a;
    float u=(a&65535u)/65535.0,v=(a>>16u)/65535.0;
    float3 flow=normalize(FlowDirection+float3(0,0,.00001));
    float3 side=normalize(cross(flow,abs(flow.y)>.9?float3(1,0,0):float3(0,1,0)));
    float3 other=cross(flow,side);float angle=v*6.2831853;
    float3 n=flow*u+(side*cos(angle)+other*sin(angle))*sqrt(1-u*u);
    float3 normal=normalize(n/max(HalfExtents,1));
    attributes.position=float3(0,0,-3)+n*HalfExtents+normal*2+flow;
    attributes.velocity=-flow*lerp(45,110,Heat)+normal*8;
    attributes.lifetime=.35+.4*v;attributes.size=2+4*u;
    attributes.scaleX=v<.2?.16:1;attributes.scaleY=v<.2?3:1;
    attributes.color=lerp(float3(.5,.15,4),float3(5,1.1,.06),saturate(Heat*1.4))*(.5+.5*u);
    attributes.alpha=Intensity*.16;
}
