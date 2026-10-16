"""Copy only the validated HQ mesh/material; reject unrelated catalogue drift."""
from pathlib import Path
import hashlib,json,re,shutil
root=Path(__file__).resolve().parents[1]
isolated=root/'TestResults/ValidationProject'
catalogue=Path('Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Catalogue.asset')
main=(root/catalogue).read_bytes();checked=(isolated/catalogue).read_bytes()
baseline=(root/'TestResults/HQ/baseline-catalogue.asset').read_bytes()
def other_entries(data):
    return re.sub(rb'  - id: command\r?\n.*?(?=  - id: )',b'',data,count=1,flags=re.S)
assert other_entries(main)==other_entries(baseline), 'Unrelated catalogue content changed'
assert main==checked, 'Validated catalogue differs from the main catalogue'
assert (root/Path(str(catalogue)+'.meta')).read_bytes()==(isolated/Path(str(catalogue)+'.meta')).read_bytes()
copied=[]
for relative in ['Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Meshes/building-command.asset',
                 'Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Materials/building-command.mat']:
    relative=Path(relative)
    assert (root/Path(str(relative)+'.meta')).read_bytes()==(isolated/Path(str(relative)+'.meta')).read_bytes(), 'HQ asset GUID drift'
    shutil.copyfile(isolated/relative,root/relative)
    copied.append(dict(path=str(relative),sha256=hashlib.sha256((root/relative).read_bytes()).hexdigest()))
proof=dict(allNonHQCatalogueContentUnchanged=True,changedEntry='command',mainCatalogueMatchesValidatedCatalogue=True,meshMatchesValidatedAsset=True,copied=copied)
(root/'TestResults/HQ/catalogue-proof.json').write_text(json.dumps(proof,indent=2))
print('Integrated only HQ mesh and material; catalogue/GUID scope checks passed')
