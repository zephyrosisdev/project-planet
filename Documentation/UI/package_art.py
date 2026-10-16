"""Package approved portrait sources at the requested 512px and produce review sheets."""
from pathlib import Path
from PIL import Image, ImageDraw
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'TestResults/UiRedesign/PythonDeps'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
UI = ROOT / 'Assets/MassiveArmy/Resources/MassiveArmy/UI'
SOURCE = ROOT / 'Documentation/UI/PortraitSources'

def package():
    for source in SOURCE.glob('*.png'):
        target = UI / 'Portraits' / source.name
        if target.exists():
            continue
        image = Image.open(source).convert('RGBA')
        image.thumbnail((512, 512), Image.Resampling.LANCZOS)
        out = Image.new('RGBA', (512, 512))
        out.alpha_composite(image, ((512-image.width)//2, (512-image.height)//2))
        out.save(target)
    variable = UI / 'Fonts/Inter.ttf'
    if variable.exists():
        for name, weight in [('Regular',400),('Medium',500)]:
            target = UI / f'Fonts/Inter-{name}.ttf'
            if not target.exists():
                instantiateVariableFont(TTFont(variable), {'wght':weight,'opsz':14}, inplace=True).save(target)
    files = sorted((UI/'Portraits').glob('*.png'))
    review = ROOT / 'TestResults/UiRedesign/ArtReview'
    review.mkdir(parents=True, exist_ok=True)
    for start in range(0,len(files),12):
        sheet=Image.new('RGB',(1200,960),(3,16,27)); draw=ImageDraw.Draw(sheet)
        for i,file in enumerate(files[start:start+12]):
            image=Image.open(file).convert('RGBA');image.thumbnail((290,270))
            x,y=(i%4)*300,(i//4)*320
            sheet.paste(image,(x+(300-image.width)//2,y),image)
            draw.text((x+8,y+285),file.stem,fill=(120,220,255))
        sheet.save(review/f'portraits-{start//12+1}.jpg')
    print(f'{len(files)} portraits packaged')

if __name__=='__main__': package()
