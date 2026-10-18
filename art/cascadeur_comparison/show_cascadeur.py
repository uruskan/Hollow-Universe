"""Request visibility of Cascadeur's own window for its native render capture."""
from PySide6.QtGui import QGuiApplication,QWindow
for w in QGuiApplication.allWindows():
    if w.title().endswith(' - Cascadeur'):
        if w.visibility()==QWindow.Visibility.Minimized:
            w.showMaximized()
        w.raise_();w.requestActivate();w.requestUpdate()
        print('Requested Cascadeur window activation; exposed=',w.isExposed())
