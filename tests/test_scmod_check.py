import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from scmod_check import validate_archive, inspect_log


class ScmodCheckTests(unittest.TestCase):
    def make_package(self, root: Path, metadata=None):
        package = root / "sample.scmod"
        info = metadata or {
            "Name": "Test Mod",
            "Version": "1.0.0",
            "ApiVersion": "1.9.5.1",
            "ScVersion": "2.4.0.0",
            "PackageName": "test.mod",
            "Dependencies": {},
        }
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr("TestMod.dll", b"assembly")
            archive.writestr("modinfo.json", json.dumps(info))
            archive.writestr("Assets/Test.xdb", "<Database />")
        return package

    def test_valid_package_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            package = self.make_package(Path(tmp))
            self.assertEqual(validate_archive(str(package)), [])

    def test_wrong_api_is_reported(self):
        with tempfile.TemporaryDirectory() as tmp:
            package = self.make_package(Path(tmp))
            errors = validate_archive(str(package), expected_api="0.0")
            self.assertTrue(any("ApiVersion mismatch" in error for error in errors))

    def test_missing_assembly_is_reported(self):
        with tempfile.TemporaryDirectory() as tmp:
            package = Path(tmp) / "bad.scmod"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("modinfo.json", "{}")
            errors = validate_archive(str(package))
            self.assertTrue(any("No .dll" in error for error in errors))

    def test_invalid_xml_is_reported(self):
        with tempfile.TemporaryDirectory() as tmp:
            package = Path(tmp) / "bad.scmod"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("Test.dll", b"x")
                archive.writestr("modinfo.json", json.dumps({
                    "Name": "x", "Version": "1", "ApiVersion": "1.9.5.1",
                    "ScVersion": "2.4.0.0", "PackageName": "x"
                }))
                archive.writestr("broken.xdb", "<Database>")
            errors = validate_archive(str(package))
            self.assertTrue(any("Invalid XML" in error for error in errors))

    def test_log_fatal_patterns_are_found(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / "game.log"
            log.write_text("INFO started\\nERROR NullReferenceException in mod\\n", encoding="utf-8")
            findings = inspect_log(str(log))
            self.assertEqual(len(findings), 1)
            self.assertIn(":2:", findings[0])


if __name__ == "__main__":
    unittest.main()
