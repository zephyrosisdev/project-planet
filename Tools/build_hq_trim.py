"""Deterministic, reusable 2048px HQ trim sheet. No photographic lighting baked in."""
from pathlib import Path
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[1]
out=root/'Assets/MassiveArmy/Art/Textures';out.mkdir(parents=True,exist_ok=True)
n=2048;rng=np.random.default_rng(7731)
colors=[(103,117,132),(166,178,188),(42,51,64),(13,20,29),(10,57,75),(5,163,209),(224,174,47),(212,221,223)]
metal=[.58,.78,.60,.35,.3,.1,.45,.65];smooth=[.48,.62,.43,.28,.88,.7,.55,.65]
albedo=np.zeros((n,n,3),np.uint8);mask=np.zeros((n,n,4),np.uint8);normal=np.zeros((n,n,3),np.uint8)
normal[:]=(128,128,255)
for i,col in enumerate(colors):
    # UV origin is bottom-left; each strip leaves a 12px sampling gutter.
    y0=(7-i)*256;y1=y0+256;y,x=np.mgrid[0:256,0:n]
    noise=rng.normal(0,.9,(256,n))+rng.normal(0,.5,(256,1))
    edge=np.minimum(y,255-y);line=np.exp(-((edge-27)/1.5)**2)
    val=np.asarray(col)[None,None,:]+noise[:,:,None]-line[:,:,None]*9
    if i in (0,1,2):
        scratches=(rng.random((256,n))>.9987)*rng.uniform(3,12,(256,n))
        wear=np.exp(-((edge-31)/2.1)**2)*14
        val+=scratches[:,:,None]+wear[:,:,None]+rng.normal(0,1.1,(256,1))[:,:,None]
    if i in (0,1,2):
        # Broad rolled-metal variation and brighter rubbed edges remain reusable
        # across panels without baking a particular camera or light direction.
        val+=(np.sin(x*.011+y*.024)*2.8+np.sin(x*.041)*1.3)[:,:,None]
        val-=np.exp(-((edge-25)/2.2)**2)[:,:,None]*18
        val+=np.exp(-((edge-32)/2.5)**2)[:,:,None]*24
    if i==5:
        glass=.24+.52*np.sin(np.pi*y/255)**3+.24*np.exp(-((y-166)/22)**2)
        glass*=.9+.1*np.sin(x*.008)**2
        val=np.asarray((5,89,133))[None,None,:]*(.45+.55*glass)[:,:,None]+noise[:,:,None]*.3
    albedo[y0:y1]=np.clip(val,0,255)
    mask[y0:y1,:,0]=int(metal[i]*255);mask[y0:y1,:,1]=255
    mask[y0:y1,:,2]=np.clip(glass*255,0,255) if i==5 else 0;mask[y0:y1,:,3]=int(smooth[i]*255)
    h=noise*.004+line*.05;dy,dx=np.gradient(h)
    norm=np.stack((-dx,-dy,np.ones_like(dx)),axis=-1);norm/=np.linalg.norm(norm,axis=-1,keepdims=True)
    normal[y0:y1]=np.clip((norm*.5+.5)*255,0,255)
for suffix,data in [('albedo',albedo),('metallic',mask),('normal',normal)]:Image.fromarray(data).save(out/f'building-command-{suffix}.png')
print('HQ trim sheet: eight strips, albedo / metal-AO-emission-smoothness / tangent normal, 2048px')
