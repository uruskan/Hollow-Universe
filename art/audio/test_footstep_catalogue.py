"""Regression checks for data-owned footwear tuning and removal; no encoding or live catalogue writes."""
import inspect  # Load stdlib before the adjacent legacy inspect.py can shadow it.
import json
import os
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build


class FootstepCatalogueTests(unittest.TestCase):
    def test_rebuild_applies_profile_tuning_and_preserves_other_mix_settings(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "sounds.json")
            before = {"events": [
                {"key": "footstep.walk.grass.boots", "volume": 1, "maxDistance": 30,
                 "pitch": [0.99, 1.01], "files": ["old.ogg"]},
                {"key": "tool.hit", "volume": -6, "files": ["tool.ogg"]},
            ]}
            with open(path, "w", encoding="utf-8") as stream:
                json.dump(before, stream)
            with patch.object(build, "CATALOGUE", path):
                build.write_catalogue([
                    {"key": "footstep.walk.grass.boots", "volume": -1, "maxDistance": 24,
                     "pitch": [0.98, 1.02], "files": ["new.ogg"],
                     "_generatedFields": ("volume", "maxDistance")},
                ], {}, replace_prefixes=("footstep.",))
            with open(path, encoding="utf-8") as stream:
                result = {event["key"]: event for event in json.load(stream)["events"]}
            self.assertEqual(-1, result["footstep.walk.grass.boots"]["volume"])
            self.assertEqual(24, result["footstep.walk.grass.boots"]["maxDistance"])
            self.assertEqual([0.99, 1.01], result["footstep.walk.grass.boots"]["pitch"])
            self.assertNotIn("_generatedFields", result["footstep.walk.grass.boots"])
            self.assertEqual(before["events"][1], result["tool.hit"])

    def test_removed_profile_cannot_leave_events_referencing_deleted_banks(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "sounds.json")
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"events": [
                    {"key": "footstep.walk.grass.removed", "files": ["removed.ogg"]},
                    {"key": "hand.authored", "files": ["keep.ogg"]},
                ]}, stream)
            with patch.object(build, "CATALOGUE", path):
                build.write_catalogue([
                    {"key": "footstep.walk.grass.barefoot", "files": ["current.ogg"]}
                ], {}, replace_prefixes=("footstep.",))
            with open(path, encoding="utf-8") as stream:
                keys = {event["key"] for event in json.load(stream)["events"]}
            self.assertEqual({"footstep.walk.grass.barefoot", "hand.authored"}, keys)


if __name__ == "__main__":
    unittest.main()
