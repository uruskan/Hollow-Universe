"""Compose diagnostic sheets from current catalogue PNGs and literal placeholder SVGs.

Requires Pillow and a Node.js Sharp installation. Does not run Godot or emulate the
complete UI: complex SVG expressions/aliases are left undrawn. Never edits game assets.
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=Path('.artifacts/item-art-review'))
parser.add_argument('--node', default='node', help='Node executable')
parser.add_argument('--sharp', default='sharp', help='Sharp module name or absolute package path')
parser.add_argument('--font', default='DejaVuSans.ttf', help='Pillow TrueType font name or path')
parser.add_argument('--date', required=True, help='Date to label the diagnostic snapshot, YYYY-MM-DD')
args = parser.parse_args()
if not re.fullmatch(r'\d{4}-\d{2}-\d{2}', args.date):
    parser.error('--date must use YYYY-MM-DD')
ROOT = Path(__file__).resolve().parents[1]
OUT = args.output.resolve()
if OUT == ROOT or OUT.is_relative_to(ROOT/'game'):
    parser.error('Choose a diagnostic directory outside game assets and the repository root')
OUT.mkdir(parents=True, exist_ok=False)
NODE, SHARP = args.node, args.sharp
models = json.loads((ROOT/'game/data/ground/models.json').read_text())['models']
items = json.loads((ROOT/'game/data/ground/items.json').read_text())['items']
by_key = {m['key']:m for m in models}
source = (ROOT/'game/scripts/UI/ItemIcons.cs').read_text()
source = source.split('private static string Shape(string key) => key switch',1)[1].split('    };',1)[0]
shapes = {}
generic_shape = ''.join(json.loads(s) for s in re.findall(r'"(?:\\.|[^"\\])*"', source.rsplit('_ =>',1)[1]))
for match in re.finditer(r'^\s*"([^"\n]+)" => (.*?)(?=^\s*(?:"[^"\n]+"|_) =>)', source, re.M|re.S):
    if match.group(2).lstrip().startswith('"'):
        shapes[match.group(1)] = ''.join(json.loads(s) for s in re.findall(r'"(?:\\.|[^"\\])*"', match.group(2)))
focus = [i for i in items if i['category']=='components'] + [i for i in items if i['key']=='salvage']
rows = []
svg_files = []
for item in items:
    m = by_key[item['key']]
    icon = m.get('icon')
    icon_key = icon.get('key', item['key']) if icon is not None else None
    icon_path = ROOT/f'game/assets/items/icons/256/{icon_key}.png' if icon_key else None
    row = dict(key=item['key'], name=item['name'], category=item['category'], model=m.get('file'),
               stand_in=m.get('standIn'), icon_key=icon_key, icon_path=str(icon_path) if icon_path else None,
               configured_png_exists=icon_path.exists() if icon_path else None,
               kind='revised' if 'Hollow_Salvage_R01' in m.get('file','') else 'fallback' if icon_key is None else 'rendered')
    if icon_path and icon_path.exists():
        row['icon_sha256']=hashlib.sha256(icon_path.read_bytes()).hexdigest()
    elif item['key'] in shapes or icon_key:
        svg_path = OUT/(item['key']+'-fallback.svg')
        # Only the audited Wire Tool is known to reach the generic drawing. Do not
        # invent a result for future keys that may have an alias or computed SVG.
        if item['key'] not in shapes and item['key'] != 'wire-tool':
            row['kind']='missing'
            rows.append(row)
            continue
        svg_path.write_text('<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 128 128">'+shapes.get(item['key'],generic_shape)+'</svg>',encoding='utf-8')
        svg_files.append(str(svg_path))
        row['diagnostic_svg']=str(svg_path)
        if icon_key:
            row['kind']='missing'
    rows.append(row)
snapshot=[]
for row in rows:
    portable=dict(row)
    if portable.get('icon_path'):
        portable['icon_path']=Path(portable['icon_path']).relative_to(ROOT).as_posix()
    if portable.get('diagnostic_svg'):
        portable['diagnostic_svg']=Path(portable['diagnostic_svg']).name
    snapshot.append(portable)
inputs=['game/data/ground/items.json','game/data/ground/models.json','game/scripts/UI/ItemIcons.cs',
        'game/scripts/Ground/ItemIconAliases.cs','game/scripts/Items/CatalogItemIcons.cs']
(OUT/'inventory.json').write_text(json.dumps({'audit_date':args.date,'basis':'working tree',
    'head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
    'source_sha256':{p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in inputs},
    'items':snapshot},indent=2)+'\n',encoding='utf-8')
converter = OUT/'render_svgs.cjs'
converter.write_text('const sharp=require('+json.dumps(SHARP)+');\n(async()=>{for(const f of '+json.dumps(svg_files)+'){await sharp(f).png().toFile(f.replace(/\\.svg$/, ".png"));}})().catch(e=>{console.error(e);process.exit(1)});')
subprocess.run([str(NODE),str(converter)],check=True)
font = lambda n: ImageFont.truetype(args.font,n)
colors = dict(revised='#8bcfa9',fallback='#e4ae71',missing='#e4ae71',rendered='#99b9dd')
def sheet(selected, title, subtitle, dest, cols=5, cell=224, art=154, extra=True):
    top, gap, cell_h = 96, 12, (236 if extra else 200)
    width = cols*cell + (cols+1)*gap
    height = top + ((len(selected)+cols-1)//cols)*(cell_h+gap)+34
    image = Image.new('RGB',(width,height),'#14191e'); draw=ImageDraw.Draw(image)
    draw.text((18,14),title,fill='#f4f3ef',font=font(25))
    draw.text((18,52),subtitle,fill='#bac1c7',font=font(15))
    for n,row in enumerate(selected):
        x=gap+(n%cols)*(cell+gap); y=top+(n//cols)*(cell_h+gap)
        draw.rounded_rectangle((x,y,x+cell,y+cell_h),radius=7,fill='#262d33')
        path = row.get('icon_path')
        if not path or not Path(path).exists():
            path = row.get('diagnostic_svg','').replace('.svg','.png')
        if path and Path(path).exists():
            icon=Image.open(path).convert('RGBA'); icon.thumbnail((art,art),Image.Resampling.LANCZOS)
            image.paste(icon,(x+(cell-icon.width)//2,y+8),icon)
        else:
            draw.text((x+18,y+50),'SVG fallback',font=font(18),fill='#e4ae71')
        name_size=15
        while font(name_size).getlength(row['name']) > cell-20 and name_size > 9:
            name_size-=1
        draw.text((x+10,y+art+12),row['name'],fill='#f0f1ee',font=font(name_size))
        status = {'revised':'REVISED MODEL + PNG','fallback':'SVG + BORROWED MODEL' if row['stand_in'] else 'SVG FALLBACK','rendered':'RENDERED PNG','missing':'PNG MISSING: FALLBACK'}[row['kind']]
        status_size=12
        while font(status_size).getlength(status)>cell-20:
            status_size-=1
        draw.text((x+10,y+art+37),status,fill=colors[row['kind']],font=font(status_size))
        if extra:
            model = Path(row['model']).stem
            draw.text((x+10,y+art+58),'Model: '+model,fill='#b9bec4',font=font(12))
    draw.text((15,height-25),f'Source-file diagnostic, {args.date}. PNGs are current game files; fallback SVGs rasterized outside Godot.',fill='#929ba4',font=font(12))
    image.save(OUT/dest)
focus_keys={i['key'] for i in focus}
focus_rows=sorted([r for r in rows if r['key'] in focus_keys],key=lambda r: (r['kind']!='revised', r['key']=='salvage',r['key']))
sheet(focus_rows,'Current components and salvage','Green: revised assets. Orange: SVG fallbacks. Each card identifies its selected model.','components-current.png')
sheet([r for r in rows if r['category']=='resources'],'Current resource icons','Current selected icon artwork and model names. See inventory.json for aliases and stand-in annotations.','resources-current.png',cols=6)
for page,start in enumerate(range(0,len(rows),70),1):
    sheet(rows[start:start+70],f'Current item catalogue — page {page}','PNG assets selected by the catalogue. Missing dedicated PNGs are shown from SVG where available.',f'all-items-{page}.png',cols=7,cell=176,art=128,extra=False)
print(json.dumps({'items':len(rows),'focus':len(focus_rows),'configured_png_missing':[r['key'] for r in rows if r['configured_png_exists'] is False],'no_icon_field':[r['key'] for r in rows if r['icon_key'] is None],'outputs':[p.name for p in OUT.glob('*current.png')]},indent=2))
