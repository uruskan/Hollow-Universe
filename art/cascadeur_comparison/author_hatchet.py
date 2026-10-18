from pathlib import Path
source=Path('D:/GameDev/Projects/HollowUniverseABTestB/art/cascadeur_comparison/author_native.py')
exec(compile(source.read_text(),str(source),'exec'))
author('hatchet')
