void UpdateReentry(inout VFXAttributes attributes, in float3 FlowDirection, in float3 HalfExtents, in float Intensity, in float Heat)
{
    float3 q=(attributes.position-float3(0,0,-3))/max(HalfExtents,1);
    float lengthQ=max(.001,length(q));float3 normal=normalize(q/max(HalfExtents,1));
    if(lengthQ<1.03)attributes.position=float3(0,0,-3)+q/lengthQ*HalfExtents*1.03;
    float3 flow=normalize(FlowDirection+float3(0,0,.00001));
    float nose=dot(normal,flow);
    attributes.velocity=(-flow+normal*max(0,nose)*.85)*lerp(45,110,Heat)+normal*4;
    float life=saturate(1-attributes.age/max(.01,attributes.lifetime));
    float flicker=.8+.2*sin(attributes.age*19+attributes.particleId*.71);
    attributes.alpha=Intensity*life*life*.16*flicker;
    attributes.color=lerp(float3(.5,.15,4),float3(5,1.1,.06),saturate(Heat*1.4));
}
