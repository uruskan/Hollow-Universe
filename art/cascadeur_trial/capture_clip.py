"""Capture 43 individual native stills serially, avoiding native sequence render.

Run only after an individual still succeeded in the intended saved scene.
Writes a receipt for every frame and stops on unknown response or missing PNG.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

from mcp_client import OUT, call


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scene",choices=("baseline","candidate"),required=True)
    args=parser.parse_args()
    filenames={"baseline":"Survivor-TPS-03-baseline-chop.casc",
               "candidate":"Survivor-TPS-04-candidate-chop.casc"}
    folder=OUT / (args.scene+"-individual-frames")
    folder.mkdir(exist_ok=True)
    call("initialize",{"protocolVersion":"2024-11-05","capabilities":{},
                       "clientInfo":{"name":"cascadeur-still-capture","version":"1.0"}},1)
    for frame in range(43):
        path=folder / f"frame_{frame:04}.png"
        receipt=folder / f"frame_{frame:04}.json"
        if path.exists() and receipt.exists():
            continue
        assert not path.exists() and not receipt.exists(), "Inspect incomplete frame before retrying"
        code=f'''from pathlib import Path
import numpy as np
assert Path(app.current_scene().get_path_name()).name == {filenames[args.scene]!r}
scene.set_current_frame({frame})
viewport=app.current_scene().active_viewport().domain_viewport()
camera=viewport.camera_struct()
camera.position=np.array([-250.,160.,360.],dtype=np.float32)
camera.target=np.array([0.,93.,20.],dtype=np.float32)
viewport.set_camera_struct(camera)
viewport.set_mode_visualizers(csc.view.ViewportMode.View)
params=csc.tools.RenderParameters();params.width=960;params.height=720;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.take_image(app.current_scene(),params,{path.as_posix()!r})
print("Individual frame requested",{frame})
'''
        try:
            response=call("tools/call",{"name":"run_script","arguments":{"code":code}},frame+2)
        except Exception as error:
            response={"error":{"type":type(error).__name__,"message":str(error)}}
        receipt.write_text(json.dumps({"code":code,"response":response},indent=2)+"\n",encoding="utf-8")
        if "error" in response or response.get("result",{}).get("isError",False):
            raise RuntimeError(f"Capture stopped at {frame}; inspect {receipt}")
        for content in response.get("result",{}).get("content",[]):
            if content.get("type")=="text" and not json.loads(content["text"]).get("ok",False):
                raise RuntimeError(f"Capture script failed: {receipt}")
        deadline=time.monotonic()+10
        while not path.exists() and time.monotonic()<deadline:
            time.sleep(.2)
        if not path.exists():
            raise RuntimeError(f"PNG missing after frame {frame}; stop before changing frame")
        if frame % 7 == 0 or frame==42:
            print(f"{args.scene}: captured {frame+1}/43",flush=True)
    return 0


if __name__=="__main__":
    raise SystemExit(main())
