"""Synthetic authentication/transaction tests only. Root owns execution; no Godot.

python -m unittest discover -s tools/blender/tests -p test_ground_cover_import_settings.py
"""
import copy
import importlib.util
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest import mock
import zlib

TOOL = Path(__file__).resolve().parents[1] / "ground_cover_import_settings.py"
SPEC = importlib.util.spec_from_file_location("cover_import_policy", TOOL)
policy = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = policy
SPEC.loader.exec_module(policy)


def png_header(colour, identifier):
    # Header/parser fixture, not a renderer texture. Its complete bytes are hash-bound
    # exactly like the real PNG; production files are authenticated independently.
    ihdr = b"IHDR" + struct.pack(">2I5B", 2048, 2048, 8, colour, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + ihdr + struct.pack(">I", zlib.crc32(ihdr)) + bytes([identifier])


def texture_import(relative, ctex, uid):
    settings = dict(policy.PRESERVE, **{"compress/mode": "0", "compress/high_quality": "false",
        "compress/normal_map": "0", "detect_3d/compress_to": "1", "unrelated/number": "0.765"})
    return (f'[remap]\r\nimporter="texture"\r\ntype="CompressedTexture2D"\r\nuid="uid://{uid}"\r\n'
            f'path="res://{ctex}"\r\ngenerator_parameters={{\r\n"md5": "' + "a"*32 + '"\r\n}\r\n'
            f'\r\n[deps]\r\nsource_file="res://{relative}"\r\n\r\n[params]\r\n'
            + "\r\n".join(f"{key}={value}" for key, value in settings.items()) + "\r\n; keep comment\r\n").encode()


class Fixture:
    def __init__(self, parent):
        self.game = parent / "game"
        folder = self.game / policy.PREFIX
        folder.mkdir(parents=True)
        (self.game / ".godot/imported").mkdir(parents=True)
        self.manifest_path = parent / "manifest.json"
        self.catalog_path = folder / "catalog.json"
        self.model = folder / "fern.glb"
        self.model_import = folder / "fern.glb.import"
        self.model_import.write_bytes(b"[params]\ngltf/embedded_image_handling=1\ngltf/texture_map_mode=1\n")
        self.imports, self.pngs, self.ctexs = [], [], []
        roles = ("normal_xy", "albedo_cutout", "packed_ort", "billboard_normal_alpha", "albedo_cutout")
        uses = (["0/normal"], ["0/baseColor"], ["0/metallicRoughness"], ["1/normal"], ["1/baseColor"])
        self.document = {"asset": {"version": "2.0"}, "buffers": [{"byteLength": 0}], "bufferViews": [],
            "images": [], "textures": [{"source": n} for n in range(5)], "materials": [
                {"normalTexture": {"index": 0}, "pbrMetallicRoughness": {
                    "baseColorTexture": {"index": 1}, "metallicRoughnessTexture": {"index": 2}}},
                {"normalTexture": {"index": 3}, "pbrMetallicRoughness": {"baseColorTexture": {"index": 4}}}]}
        self.binary = b""
        image_receipts = []
        for index, role in enumerate(roles):
            name = f"Image{index}"
            relative = policy.PREFIX + f"fern_{name}.png"
            data = png_header(2 if role in ("normal_xy", "packed_ort") else 6, index)
            path = self.game / relative
            path.write_bytes(data)
            self.pngs.append(path)
            self.document["bufferViews"].append({"buffer": 0, "byteOffset": len(self.binary), "byteLength": len(data)})
            self.binary += data
            self.document["images"].append({"name": name, "mimeType": "image/png", "bufferView": index})
            ctex_relative = f".godot/imported/fixture{index}.ctex"
            ctex = self.game / ctex_relative
            ctex.write_bytes(b"GST2" + struct.pack("<3I", 1, 2048, 2048) + bytes(36) + bytes([index]))
            self.ctexs.append(ctex)
            imp = Path(str(path) + ".import")
            imp.write_bytes(texture_import(relative, ctex_relative, f"fixture{index}"))
            self.imports.append(imp)
            image_receipts.append({"imageIndex": index, "imageName": name, "path": relative,
                "sha256": policy.digest(data), "bytes": len(data), "uses": uses[index], "role": role})
        self.document["buffers"][0]["byteLength"] = len(self.binary)
        self.recipe = {"key": "fern", "model": "res://" + policy.PREFIX + "fern.glb", "images": image_receipts}
        self.manifest = {"schema": "hollow.cover-texture-import-policy.v1", "runtimeAppearanceApproved": False,
            "families": [{"key": key} for key in ("wild", "cotton", "tundra")] + [self.recipe]}
        self.catalog = {"families": [{"key": key} for key in ("wild", "cotton", "tundra")] + [{}]}
        self.seal_model()

    def seal_model(self):
        text = json.dumps(self.document, separators=(",", ":")).encode()
        text += b" " * (-len(text) % 4)
        binary = self.binary + bytes(-len(self.binary) % 4)
        data = b"glTF" + struct.pack("<4I", 2, 28 + len(text) + len(binary), len(text), 0x4E4F534A)
        data += text + struct.pack("<2I", len(binary), 0x004E4942) + binary
        self.model.write_bytes(data)
        self.recipe.update(modelSha256=policy.digest(data), modelBytes=len(data))
        self.catalog["families"][-1] = {"key": "fern", "model": self.recipe["model"],
            "outputSha256": self.recipe["modelSha256"], "outputBytes": len(data)}
        raw = policy.canonical(self.catalog)
        self.catalog_path.write_bytes(raw)
        self.manifest["catalogSha256"] = policy.digest(raw)
        self.save_manifest()

    def save_manifest(self):
        self.manifest_path.write_bytes(policy.canonical(self.manifest))

    def plan(self):
        return policy.build_plan(self.game, "fern", self.manifest_path)

    def prepare_migration(self):
        old_raw = self.manifest_path.read_bytes()
        self.parent_receipt = policy.apply_plan(self.plan(),self.game.parent/".artifacts"/"v1")
        self.parent_pin = policy.digest(self.parent_receipt.read_bytes())
        # Simulate only Godot's generated desktop CTEX remap; settings retain exact v1 bytes.
        for index,(path,ctex) in enumerate(zip(self.imports,self.ctexs)):
            suffix = ".s3tc.ctex" if index == 0 else ".bptc.ctex"
            candidate = ctex.with_name(ctex.name[:-5]+suffix)
            candidate.write_bytes(ctex.read_bytes())
            path.write_bytes(path.read_bytes().replace(ctex.name.encode(),candidate.name.encode()))
        self.manifest.update(schema=policy.SCHEMA_V2,billboardSettings=policy.policy_settings("billboard_normal_alpha",policy.SCHEMA_V2),
            previousPolicy=dict(schema=policy.SCHEMA_V1,sha256=policy.digest(old_raw),utf8=old_raw.decode()))
        self.save_manifest()

    def migration(self):
        return policy.build_migration_plan(self.game,"fern",self.parent_receipt,self.parent_pin,self.manifest_path)


class FakeGuard:
    """Tests inject IO failures; the separate native Windows test verifies actual exclusion."""
    def __init__(self,path,writable=False): self.path,self.writable = path,writable
    def __enter__(self): return self
    def __exit__(self,*_): pass
    def read(self): return self.path.read_bytes()
    def write_byte(self,offset,value):
        if not self.writable: raise AssertionError("Read-only map guard")
        raw = self.read()
        self.path.write_bytes(raw[:offset]+value+raw[offset+1:])


class GroundCoverImportSettingsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.parent = Path(self.temp.name)
        self.fixture = Fixture(self.parent)

    def test_check_is_read_only_and_preserves_every_unrelated_byte(self):
        before = {p: p.read_bytes() for p in self.fixture.game.rglob("*") if p.is_file()}
        plan = self.fixture.plan()
        self.assertEqual(5, len(plan.edits))
        self.assertEqual(before, {p: p.read_bytes() for p in self.fixture.game.rglob("*") if p.is_file()})
        for edit in plan.edits:
            old, new = policy.params(edit.before), policy.params(edit.after)
            for key in old:
                if key not in edit.settings:
                    self.assertEqual(old[key][0], new[key][0])
            self.assertEqual(edit.before.split(b"[params]")[0], edit.after.split(b"[params]")[0])
            self.assertTrue(edit.after.endswith(b"; keep comment\r\n"))
        self.assertFalse((self.parent / ".artifacts").exists())

    def test_roles_preserve_packed_billboard_alpha_and_use_bc5_only_for_pure_normal(self):
        edits = self.fixture.plan().edits
        self.assertEqual("1", edits[0].settings["compress/normal_map"])
        self.assertEqual("false", edits[0].settings["compress/high_quality"])
        self.assertEqual("2", edits[3].settings["compress/normal_map"])
        self.assertEqual("true", edits[3].settings["compress/high_quality"])
        self.assertEqual("true", edits[4].settings["compress/high_quality"])

    def test_apply_saves_exact_import_and_ctex_before_changes_and_is_idempotent(self):
        plan = self.fixture.plan()
        directory = self.parent / ".artifacts" / "first"
        receipt_path = policy.apply_plan(plan, directory)
        receipt = json.loads(receipt_path.read_text())
        self.assertEqual("applied-awaiting-native-import-and-visual-review", receipt["status"])
        for entry, edit in zip(receipt["files"], plan.edits):
            self.assertEqual(edit.before, (directory / entry["backupImport"]).read_bytes())
            self.assertEqual(edit.after, edit.path.read_bytes())
            self.assertEqual(edit.ctex.read_bytes(), (directory / entry["backupCtex"]).read_bytes())
        self.assertEqual(0, policy.summary(self.fixture.plan())["changedImports"])
        with self.assertRaises(policy.ImportPolicyError):
            policy.apply_plan(self.fixture.plan(), directory)

    def test_hash_failure_prevents_all_mutation_and_backup_creation(self):
        plan = self.fixture.plan()
        self.fixture.pngs[4].write_bytes(self.fixture.pngs[4].read_bytes() + b"changed")
        destination = self.parent / ".artifacts" / "refused"
        with self.assertRaises(policy.ImportPolicyError):
            policy.apply_plan(plan, destination)
        self.assertFalse(destination.exists())
        for edit in plan.edits:
            self.assertEqual(edit.before, edit.path.read_bytes())
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()

    def test_untrusted_catalog_and_duplicate_json_fields_are_rejected(self):
        self.fixture.catalog_path.write_bytes(b"{}")
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()
        with self.assertRaises(policy.ImportPolicyError):
            policy.strict_json(b'{"x":1,"\\u0078":2}')
        with self.assertRaises(policy.ImportPolicyError):
            policy.strict_json(b'{"x":NaN}')

    def test_explicit_roles_are_checked_against_actual_material_indices(self):
        self.fixture.document["materials"][0]["normalTexture"]["index"] = 3
        self.fixture.seal_model()  # Even a reauthenticated changed model cannot bypass role compatibility.
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()

    def test_embedded_png_and_extracted_png_must_match_independently(self):
        self.fixture.binary = self.fixture.binary[:-1] + b"X"
        self.fixture.seal_model()
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()

    def test_glb_image_range_and_dimensions_are_bounded(self):
        self.fixture.document["bufferViews"][0]["byteLength"] = policy.MAX_PNG + 1
        self.fixture.seal_model()
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()
        with self.assertRaises(policy.ImportPolicyError):
            policy.png_header(self.fixture.pngs[0], 6)

    def test_modified_preserved_params_and_duplicate_parameters_are_refused(self):
        first = self.fixture.imports[0]
        original = first.read_bytes()
        first.write_bytes(original.replace(b"process/size_limit=0", b"process/size_limit=1024"))
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()
        first.write_bytes(original + b"compress/mode=2\r\n")
        with self.assertRaises(policy.ImportPolicyError):
            self.fixture.plan()

    def test_path_escape_and_backup_outside_artifacts_are_refused(self):
        plan = self.fixture.plan()
        with self.assertRaises(policy.ImportPolicyError):
            policy.apply_plan(plan, self.parent / "wrong")
        self.assertFalse((self.parent / "wrong").exists())
        for relative in ("../x", "C:/x", "/x", "a\\b"):
            with self.assertRaises(policy.ImportPolicyError):
                policy.inside(self.fixture.game, relative)

    def test_failure_after_first_import_rolls_back_without_touching_sources(self):
        plan = self.fixture.plan()
        original_write = policy.atomic_write
        writes = []
        def fail_second(path, data):
            if path == plan.edits[1].path and data == plan.edits[1].after:
                raise OSError("injected second import failure")
            writes.append(path)
            original_write(path, data)
        destination = self.parent / ".artifacts" / "failure"
        with mock.patch.object(policy, "atomic_write", side_effect=fail_second):
            with self.assertRaises(OSError):
                policy.apply_plan(plan, destination)
        self.assertIn(plan.edits[0].path, writes)
        self.assertEqual("rolled-back", json.loads((destination / "receipt.json").read_text())["status"])
        for edit in plan.edits:
            self.assertEqual(edit.before, edit.path.read_bytes())

    def test_concurrent_edit_is_not_overwritten_by_rollback(self):
        plan = self.fixture.plan()
        original_write = policy.atomic_write
        concurrent = plan.edits[0].after + b"; somebody else's edit\n"
        def changed_second(path, data):
            if path == plan.edits[1].path and data == plan.edits[1].after:
                plan.edits[0].path.write_bytes(concurrent)
                raise OSError("injected concurrent writer")
            original_write(path, data)
        destination = self.parent / ".artifacts" / "concurrent"
        with mock.patch.object(policy, "atomic_write", side_effect=changed_second):
            with self.assertRaises(OSError):
                policy.apply_plan(plan, destination)
        receipt = json.loads((destination / "receipt.json").read_text())
        self.assertEqual("rollback-incomplete", receipt["status"])
        self.assertEqual(concurrent, plan.edits[0].path.read_bytes())
        self.assertEqual(1, len(receipt["rollbackFailures"]))

    def test_backup_change_is_detected_before_first_import_mutation(self):
        plan = self.fixture.plan()
        self.fixture.ctexs[4].write_bytes(self.fixture.ctexs[4].read_bytes() + b"changed")
        with self.assertRaises(policy.ImportPolicyError):
            policy.apply_plan(plan, self.parent / ".artifacts" / "bad-baseline")
        for edit in plan.edits:
            self.assertEqual(edit.before, edit.path.read_bytes())


class PackedBillboardMigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.parent = Path(self.temp.name)
        self.fixture = Fixture(self.parent)
        self.fixture.prepare_migration()

    def test_real_v2_policy_retains_exact_known_v1_bytes_and_source_catalog(self):
        value = policy.strict_json(policy.bounded_read(policy.MANIFEST,policy.MAX_JSON))
        self.assertEqual(policy.SCHEMA_V2,value["schema"])
        history = policy.previous_policy(value)
        self.assertEqual("3407f8a44be725d868dec88ceca02307bd2741c03865e37d67528dd2a16714bf",history["sha256"])
        self.assertEqual(history["sha256"],policy.digest(history["utf8"].encode()))

    def test_readonly_migration_reconstructs_v1_and_changes_only_one_mode_byte(self):
        before = {path:path.read_bytes() for path in self.fixture.imports}
        plan = self.fixture.migration()
        self.assertEqual(before,{path:path.read_bytes() for path in self.fixture.imports})
        changed = [edit for edit in plan.edits if edit.before != edit.after]
        self.assertEqual(1,len(changed))
        self.assertEqual("billboard_normal_alpha",changed[0].role)
        offset = plan.migration["changedByteOffset"]
        self.assertEqual(ord("2"),changed[0].before[offset]); self.assertEqual(ord("0"),changed[0].after[offset])
        self.assertEqual(changed[0].before[:offset]+b"0"+changed[0].before[offset+1:],changed[0].after)
        self.assertEqual(policy.SCHEMA_V2,plan.policy_schema)
        self.assertEqual("2",policy.policy_settings("billboard_normal_alpha",policy.SCHEMA_V1)["compress/mode"])
        self.assertFalse(policy.summary(plan)["runtimeAppearanceApproved"])

    def test_wrong_parent_pin_family_status_and_backup_membership_refuse(self):
        with self.assertRaises(policy.ImportPolicyError):
            policy.build_migration_plan(self.fixture.game,"fern",self.fixture.parent_receipt,"0"*64,self.fixture.manifest_path)
        raw = self.fixture.parent_receipt.read_bytes()
        for change in (lambda p:p.update(family="wild"),lambda p:p.update(status="rolled-back"),
                       lambda p:p["files"][0].update(backupImport="../escape"),
                       lambda p:p["files"][0].update(afterSha256="b"*64),
                       lambda p:p["sources"][0].update(sha256="b"*64)):
            value = json.loads(raw); change(value)
            changed = policy.canonical(value); self.fixture.parent_receipt.write_bytes(changed)
            with self.assertRaises(policy.ImportPolicyError):
                policy.build_migration_plan(self.fixture.game,"fern",self.fixture.parent_receipt,policy.digest(changed),self.fixture.manifest_path)
        self.fixture.parent_receipt.write_bytes(raw)

    def test_policy_history_and_current_independent_edit_cannot_be_silently_reinterpreted(self):
        original = self.fixture.manifest_path.read_bytes()
        for mutate in (lambda p:p["previousPolicy"].update(utf8=p["previousPolicy"]["utf8"]+" "),
                       lambda p:p["billboardSettings"].update(**{"compress/mode":"2"}),
                       lambda p:p["families"][-1]["images"][3].update(role="normal_xy")):
            value = json.loads(original); mutate(value); self.fixture.manifest_path.write_bytes(policy.canonical(value))
            with self.assertRaises(policy.ImportPolicyError): self.fixture.migration()
        self.fixture.manifest_path.write_bytes(original)
        path = self.fixture.imports[1]
        path.write_bytes(path.read_bytes().replace(b"unrelated/number=0.765",b"unrelated/number=0.766"))
        with self.assertRaisesRegex(policy.ImportPolicyError,"exact v1 candidate"): self.fixture.migration()

    def test_original_snapshot_changed_or_unexpected_ctex_variant_refuses(self):
        parent = json.loads(self.fixture.parent_receipt.read_bytes())
        baseline = self.fixture.parent_receipt.parent/parent["files"][3]["backupCtex"]
        original = baseline.read_bytes(); baseline.write_bytes(original+b"changed")
        with self.assertRaises(policy.ImportPolicyError): self.fixture.migration()
        baseline.write_bytes(original)
        path = self.fixture.imports[3]
        candidate = self.fixture.ctexs[3].with_name("different.bptc.ctex"); candidate.write_bytes(original)
        path.write_bytes(path.read_bytes().replace(b"fixture3.bptc.ctex",candidate.name.encode()))
        with self.assertRaisesRegex(policy.ImportPolicyError,"CTEX variant"): self.fixture.migration()

    def test_migration_keeps_every_v1_snapshot_and_other_four_imports_unchanged(self):
        plan = self.fixture.migration()
        legacy = {path:path.read_bytes() for path in self.fixture.parent_receipt.parent.rglob("*") if path.is_file()}
        target = self.parent/".artifacts"/"v2"
        path = policy.apply_migration(plan,target,guard_factory=FakeGuard)
        receipt = json.loads(path.read_bytes())
        self.assertEqual("hollow.cover-texture-import-receipt.v2",receipt["schema"])
        self.assertEqual("applied-awaiting-native-lossless-equality",receipt["status"])
        self.assertFalse(receipt["allMipEqualityVerified"])
        self.assertEqual(4,len(receipt["unchangedImports"]))
        self.assertEqual(legacy,{path:path.read_bytes() for path in legacy})
        self.assertEqual(self.fixture.parent_receipt.read_bytes(),(target/"parent-receipt.json").read_bytes())
        self.assertEqual(plan.manifest_sha256,policy.digest((target/"policy.json").read_bytes()))
        self.assertEqual(1,len(list((target/"ctex").iterdir())))
        for edit in plan.edits:
            self.assertEqual(edit.before,(target/"imports"/edit.path.name).read_bytes())
            self.assertEqual(edit.after,edit.path.read_bytes())
        with self.assertRaisesRegex(policy.ImportPolicyError,"exact v1 candidate"): self.fixture.migration()
        with self.assertRaises(policy.ImportPolicyError): policy.apply_plan(plan,self.parent/".artifacts"/"bypass")

    def test_mutated_source_or_import_before_lock_creates_no_journal(self):
        plan = self.fixture.migration()
        current = self.fixture.imports[0]
        foreign = current.read_bytes()+b"; independent edit\n"; current.write_bytes(foreign)
        target = self.parent/".artifacts"/"changed"
        with self.assertRaisesRegex(policy.ImportPolicyError,"exclusive acquisition"):
            policy.apply_migration(plan,target,guard_factory=FakeGuard)
        self.assertEqual(foreign,current.read_bytes()); self.assertFalse(target.exists())

    def test_guard_failure_releases_prior_guards_and_never_creates_output(self):
        plan = self.fixture.migration(); events = []
        class Guard(FakeGuard):
            def __enter__(self):
                if len(events) == 2: raise OSError("concurrent native writer")
                events.append(("opened",self.path)); return self
            def __exit__(self,*_): events.append(("closed",self.path))
        target = self.parent/".artifacts"/"locked"
        with self.assertRaises(OSError): policy.apply_migration(plan,target,guard_factory=Guard)
        self.assertEqual(2,sum(kind == "opened" for kind,_ in events))
        self.assertEqual(2,sum(kind == "closed" for kind,_ in events))
        self.assertFalse(target.exists())

    def test_write_failure_after_byte_mutation_restores_preimage_from_locked_handle(self):
        plan = self.fixture.migration()
        class Guard(FakeGuard):
            def write_byte(self,offset,value):
                super().write_byte(offset,value)
                if value == b"0": raise OSError("injected flush failure")
        target = self.parent/".artifacts"/"rollback"
        with self.assertRaises(OSError): policy.apply_migration(plan,target,guard_factory=Guard)
        self.assertEqual("rolled-back",json.loads((target/"receipt.json").read_bytes())["status"])
        for edit in plan.edits: self.assertEqual(edit.before,edit.path.read_bytes())

    def test_unknown_bytes_prevent_rollback_and_remain_recoverable_without_overwrite(self):
        plan = self.fixture.migration()
        class Guard(FakeGuard):
            def write_byte(self,offset,value):
                super().write_byte(offset,value)
                if value == b"0":
                    self.path.write_bytes(self.read()+b"; unexpected changed bytes\n")
                    raise OSError("injected foreign bytes")
        target = self.parent/".artifacts"/"quarantine"
        with self.assertRaises(OSError): policy.apply_migration(plan,target,guard_factory=Guard)
        receipt = json.loads((target/"receipt.json").read_bytes())
        self.assertEqual("rollback-incomplete",receipt["status"])
        self.assertEqual(1,len(receipt["rollbackFailures"]))
        self.assertTrue(self.fixture.imports[3].read_bytes().endswith(b"; unexpected changed bytes\n"))

    def test_final_receipt_failure_rolls_back_the_byte_and_updates_prepared_journal(self):
        plan = self.fixture.migration(); atomic = policy.atomic_write
        def refuse_applied(path,data):
            if policy.strict_json(data).get("status") == "applied-awaiting-native-lossless-equality":
                raise OSError("injected final journal failure")
            return atomic(path,data)
        target = self.parent/".artifacts"/"receipt-failure"
        with mock.patch.object(policy,"atomic_write",side_effect=refuse_applied):
            with self.assertRaises(OSError): policy.apply_migration(plan,target,guard_factory=FakeGuard)
        self.assertEqual("rolled-back",json.loads((target/"receipt.json").read_bytes())["status"])
        for edit in plan.edits: self.assertEqual(edit.before,edit.path.read_bytes())

    @unittest.skipUnless(os.name == "nt","The migration intentionally has no non-Windows write fallback")
    def test_native_guard_denies_other_writers_and_deletion_until_closed(self):
        path = self.parent/"native.import"
        path.write_bytes(b"compress/mode=2\n")
        with policy.WindowsImportGuard(path,writable=True) as guard:
            with self.assertRaises(OSError): path.write_bytes(b"concurrent")
            with self.assertRaises(OSError): path.unlink()
            guard.write_byte(14,b"0")
            self.assertEqual(b"compress/mode=0\n",guard.read())
        path.write_bytes(b"released")
        self.assertEqual(b"released",path.read_bytes())


if __name__ == "__main__":
    unittest.main()
