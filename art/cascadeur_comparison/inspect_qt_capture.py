"""Inspect only Cascadeur's own Qt windows for a native viewport still."""
from PySide6.QtGui import QGuiApplication
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
for i,w in enumerate(QGuiApplication.allWindows()):
    print(type(w).__name__,w.title(),w.width(),w.height(),'visible',w.isVisible(),'exposed',w.isExposed())
    if 'Cascadeur' in w.title() and hasattr(w,'grabWindow'):
        print('grabWindow docs',w.grabWindow.__doc__)
        image=w.grabWindow()
        print('Native Qt image',image.width(),image.height(),image.isNull())
        if not image.isNull():
            print('Saved',image.save(str(out/('cascadeur-window-'+str(i)+'.png'))))
