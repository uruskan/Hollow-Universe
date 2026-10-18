"""Voice scheduling settings survive full and bounded catalogue writes; no audio encoding or production writes."""
import inspect  # Load stdlib before the adjacent legacy inspect.py can shadow it.
import json
import os
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build


class VoiceBudgetCatalogueTests(unittest.TestCase):
    def test_rebuild_preserves_runtime_voice_settings_and_item_roles(self):
        budget = {"spatialVoices": 18, "flatVoices": 4, "updateHz": 12,
                  "virtualizeBelowDb": -58, "hysteresisDb": 3, "priorityWeightDb": 0.4}
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "sounds.json")
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"voiceBudget": budget, "events": [{"key": "place", "files": ["old.ogg"]}]}, stream)
            roles = {"fixture": {"place": "place"}}
            with patch.object(build, "CATALOGUE", path):
                build.write_catalogue([{"key": "place", "files": ["new.ogg"]}], roles)
            with open(path, encoding="utf-8") as stream:
                result = json.load(stream)
            self.assertEqual(budget, result["voiceBudget"])
            self.assertEqual(roles, result["items"])
            self.assertEqual(["new.ogg"], result["events"][0]["files"])


if __name__ == "__main__":
    unittest.main()
