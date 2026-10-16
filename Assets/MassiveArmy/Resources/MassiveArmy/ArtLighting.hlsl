#ifndef SF_ART_LIGHTING
#define SF_ART_LIGHTING
float3 _ArmyLightDirection,_ArmyDirectLight;
float _ArmyAmbientLight;
float3 ArmyDiffuse(float3 normal){return _ArmyAmbientLight*.30+_ArmyDirectLight*saturate(dot(normal,_ArmyLightDirection))*.70;}
float3 ArmyImpostorLight(){return _ArmyAmbientLight*.30+_ArmyDirectLight*.70;}
float3 ShadeArmour(float3 position,float3 normal,float3 albedo,float4 mask,float3 team,float3 camera){
    float3 light=_ArmyLightDirection;
    float3 view=normalize(camera-position),halfway=normalize(light+view);
    float diffuse=saturate(dot(normal,light));
    float spec=pow(saturate(dot(normal,halfway)),8+mask.a*mask.a*88);
    float3 f0=lerp(.04,albedo,mask.r);
    float3 sky=lerp(float3(.10,.14,.19),float3(.40,.49,.60),saturate(normal.y*.5+.5));
    float3 body=albedo*ArmyDiffuse(normal)*(1-mask.r*.48)+f0*(sky*.48*_ArmyAmbientLight+spec*(.35+mask.a)*_ArmyDirectLight);
    return body*lerp(.55,1,mask.g)+team*mask.b*1.75*ArmyImpostorLight();
}
#endif
