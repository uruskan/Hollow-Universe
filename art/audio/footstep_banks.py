"""Recorded surface/gait banks and data-driven footwear treatments; no synthesized impacts."""
import json
import os
import re

import sources
from build import Out, Seg


def generate(event):
    path = os.path.join(sources.REPO, "game", "data", "ground", "footsteps.json")
    with open(path, encoding="utf-8") as stream:
        data = json.load(stream)
    profiles = data["profiles"]
    keys = [p["key"] for p in profiles]
    if any(not isinstance(key, str) or re.fullmatch(r"[a-z][a-z0-9_-]{0,63}", key) is None for key in keys):
        raise ValueError("footwear keys must be lowercase identifiers, at most 64 characters")
    if len(keys) != len(set(keys)) or data["defaultProfile"] not in keys:
        raise ValueError("duplicate footwear profile or missing default")
    processing = data["contactProcessing"]
    for surface, banks in sources.NOX_FOOTSTEPS["banks"].items():
        for profile in profiles:
            outputs = {}
            for bank_name, recordings in banks.items():
                outputs[bank_name] = [
                    Out("footstep/%s/%s/%s_%02d.ogg" % (surface, profile["key"], bank_name, i + 1),
                        "footstep-contact",
                        [Seg(sources.pack(sources.NOX_FOOTSTEPS["downloadFolder"], recording["member"]))],
                        fx=processing["filters"] + "," + profile["filters"],
                        fade_in=0.0, max_fade=processing["maxTailFade"], normalize=False,
                        level_range=tuple(processing["levelRange"]), peak_ceiling=processing["peakCeiling"])
                    for i, recording in enumerate(recordings)]
            registered = set()
            for gait, settings in data["gaits"].items():
                bank_name = settings["sourceBank"]
                bank = outputs[bank_name]
                files = ["res://assets/audio/" + output.rel for output in bank]
                entry = event("footstep.%s.%s.%s" % (gait, surface, profile["key"]),
                              [] if bank_name in registered else bank, "Footsteps",
                              settings["volumeDb"] + profile["volumeDb"],
                              category="footstep-contact", maxDistance=settings["maxDistance"],
                              pitch=[0.98, 1.02], jitter=0.8)
                entry["files"] = files
                entry["_generatedFields"] = ("volume", "maxDistance")
                registered.add(bank_name)
                # Legacy callers/viewers keep working; real characters resolve a declared footwear profile.
                if profile["key"] == data["defaultProfile"]:
                    alias = event("footstep.%s.%s" % (gait, surface), [], "Footsteps",
                                  settings["volumeDb"] + profile["volumeDb"],
                                  category="footstep-contact", maxDistance=settings["maxDistance"],
                                  pitch=[0.98, 1.02], jitter=0.8)
                    alias["files"] = files
                    alias["_generatedFields"] = ("volume", "maxDistance")
