"""Capture only Cascadeur's own Qt Quick surface, never other desktop windows."""
from PySide6.QtGui import QGuiApplication
from PySide6.QtQuick import QQuickWindow
import shiboken6
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
stem=Path(app.current_scene().get_path_name()).stem
frame=scene.get_current_frame()
for w in QGuiApplication.allWindows():
    if w.title().endswith(' - Cascadeur'):
        print('Native window class',w.metaObject().className(),'QQuickWindow',w.inherits('QQuickWindow'))
        if w.inherits('QQuickWindow'):
            q=shiboken6.wrapInstance(shiboken6.getCppPointer(w)[0],QQuickWindow)
            img=q.grabWindow()
            print('Native grab',img.width(),img.height(),'null',img.isNull())
            if not img.isNull():
                path=out/(stem+'-frame-'+str(frame)+'-ui.png')
                print('Saved',img.save(str(path)),str(path))
