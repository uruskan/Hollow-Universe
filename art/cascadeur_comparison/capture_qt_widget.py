"""Capture only Cascadeur's own main widget without desktop capture."""
from PySide6.QtWidgets import QApplication
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
for i,w in enumerate(QApplication.topLevelWidgets()):
    if w.windowTitle().endswith(' - Cascadeur'):
        print(type(w).__name__,w.windowTitle(),'size',w.width(),w.height())
        pix=w.grab()
        path=out/'Cascadeur-native-window.png'
        print('Widget capture',pix.width(),pix.height(),'saved',pix.save(str(path)))
