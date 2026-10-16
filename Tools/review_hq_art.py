"""Reference comparison contact sheet, preserving the image aspect ratios."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import numpy as np
root=Path(__file__).resolve().parents[1];source=root/'ArtSource/HQ';out=root/'TestResults/HQ';out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',22)
def framed(path):
    img=Image.open(path).convert('RGBA');data=np.array(img);visible=(data[:,:,:3].max(axis=2)>12)&(data[:,:,3]>12);y,x=np.where(visible)
    crop=img.crop((int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)));scale=min(570/crop.width,560/crop.height);crop=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.LANCZOS)
    canvas=Image.new('RGB',(640,640),(4,8,13));canvas.paste(crop,((640-crop.width)//2,620-crop.height),crop)
    return canvas
ref=framed(source/'reference.png');model=framed(source/'concept-view.png')
ref.save(out/'reference-aligned.png');model.save(out/'model-aligned.png')
sheet=Image.new('RGB',(1280,690),(4,8,13));sheet.paste(ref,(0,50));sheet.paste(model,(640,50));draw=ImageDraw.Draw(sheet)
draw.text((24,18),'SUPPLIED CONCEPT',font=font,fill=(100,215,245));draw.text((664,18),'HQ MODEL · STUDIO RENDER',font=font,fill=(100,215,245));sheet.save(out/'comparison.png')
html='''<!doctype html><meta charset="utf-8"><title>Command Centre art review</title>
<style>body{margin:24px;background:#04080d;color:#d5e7f0;font:16px system-ui}h1{font-size:24px}section{display:flex;flex-wrap:wrap;gap:20px}figure{margin:0;flex:1;min-width:320px}img{width:100%;background:#04080d;object-fit:contain;max-height:760px}figcaption{padding:12px;color:#65d5ed}a{color:#65d5ed}button{background:#122938;color:#d5e7f0;border:1px solid #387e9a;padding:10px;margin:4px;cursor:pointer}.overlay{position:relative;max-width:640px}.overlay img{display:block}.overlay .model{position:absolute;inset:0}input{width:min(600px,90vw)}</style>
<h1>Command Centre / HQ</h1><p>Reference and authored geometry. Aspect ratios are preserved. Studio and runtime lighting differ.</p>
<section><figure><img src="reference-aligned.png"><figcaption>Supplied concept</figcaption></figure><figure><img id="view" src="model-aligned.png"><figcaption id="caption">Current model — Cycles studio view</figcaption></figure></section>
<p><button onclick="setView('model-aligned.png','Concept angle')">Concept angle</button><button onclick="setView('../../ArtSource/HQ/front.png','Front')">Front</button><button onclick="setView('../../ArtSource/HQ/rear.png','Rear')">Rear</button><button onclick="setView('Validation/hq-runtime-concept-view.png','Actual Unity HDRP material')">Unity runtime</button><button onclick="setView('Validation/hq-placement.png','Construction preview on its placement grid')">Placement preview</button></p>
<p>Overlay opacity: <output id="value">50%</output></p><input type="range" min="0" max="100" value="50" oninput="document.querySelector('.model').style.opacity=this.value/100;document.getElementById('value').value=this.value+'%'">
<div class="overlay"><img src="reference-aligned.png"><img class="model" style="opacity:.5" src="model-aligned.png"></div>
<p><a href="../../ArtSource/HQ/CommandCentre.blend">Editable Blender source</a> · <a href="../../Documentation/HQ_ART.md">Authoring notes</a> · <a href="RESULTS.md">Measured results and limits</a></p>
<script>function setView(src,text){document.getElementById('view').src=src;document.getElementById('caption').textContent=text}</script>'''
(out/'gallery.html').write_text(html,encoding='utf-8')
print('HQ reference comparison and review gallery saved')
