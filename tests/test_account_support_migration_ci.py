"""Trust-boundary/orchestration tests only; no .NET execution, database or AI calls."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("account_support_ci", Path(__file__).resolve().parents[1] / "tools/ai/test_account_support_migration_ci.py")
GATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GATE)


def encoded(value):
    return json.dumps(value, separators=(",", ":")).encode()


def il_image():
    # Minimal PE/CLR header witness for parser tests, deliberately never executed as an assembly.
    image = bytearray(1024)
    image[:2] = b"MZ"
    struct.pack_into("<I", image, 0x3c, 0x80)
    image[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<H", image, 0x86, 1)
    struct.pack_into("<H", image, 0x94, 224)
    struct.pack_into("<H", image, 0x98, 0x10b)
    struct.pack_into("<II", image, 0x98 + 96 + 14 * 8, 0x2000, 72)
    struct.pack_into("<IIII", image, 0x178 + 8, 512, 0x2000, 512, 512)
    struct.pack_into("<I", image, 0x200, 72)
    struct.pack_into("<II", image, 0x208, 0x2080, 16)
    struct.pack_into("<I", image, 0x210, 1)
    image[0x280:0x284] = b"BSJB"
    return bytes(image)


class AccountSupportCiTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.base = Path(temporary.name)
        self.payload = {}
        for name in GATE.PAYLOAD:
            self.payload[name] = il_image() if name.endswith(".dll") else encoded({})
        self.payload["content/data/game_data.json"] = encoded({"meta": {"version": "unit-only"}})
        for prefix in ("old-runtime", "target-runtime"):
            self.payload[prefix + "/MemoryForge.Server.runtimeconfig.json"] = encoded({"runtimeOptions": {"tfm": "net10.0"}})
            self.payload[prefix + "/MemoryForge.Server.deps.json"] = encoded({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0"}})
        self.fixture = {"schema": "mf-dev-practice-fixture-1", "developmentOnly": True, "label": "unit-only synthetic parser fixture",
                        "accounts": [{"accountId": "growth-check", "credentialSha256": "a" * 64, "length": "short", "party": [{"charId": "sera"}]}]}
        self.body = {"accountId": "growth-check", "binding": {"coreModule": "1" * 32, "replay": "mf-dev-practice-replay-1",
                    "contentSha256": hashlib.sha256(self.payload["content/data/game_data.json"]).hexdigest(),
                    "fixtureSha256": hashlib.sha256(json.dumps({"length": "short", "party": [{"charId": "sera"}]}, sort_keys=True, separators=(",", ":")).encode()).hexdigest()},
                     "growth": {"ledger": [{"kind": "unit-only"}]}, "recruitment": {"ownedChars": {"aila": 1}}}
        self.provenance = {"synthetic": True, "personalAccount": False, "sourceKind": "legitimate-earned-old-s3", "accountId": "growth-check",
                           "oldCoreModule": "1" * 32, "targetCoreModule": "2" * 32}

    def pack(self, change=None, extras=()):
        self.payload["source/fixture.json"] = encoded(self.fixture)
        body = encoded(self.body).decode()
        self.payload["source/account-growth-check.json"] = encoded({"schema": "mf-dev-practice-account-1", "body": body, "sha256": hashlib.sha256(body.encode()).hexdigest()})
        manifest = {"schema": GATE.SCHEMA, "provenance": self.provenance,
                    "files": {name: hashlib.sha256(data).hexdigest() for name, data in self.payload.items()}}
        if change:
            change(manifest)
        archive = self.base / "bundle.tar.gz"
        with tarfile.open(archive, "w:gz") as bundle:
            for name, data in [*self.payload.items(), ("manifest.json", encoded(manifest))]:
                member = tarfile.TarInfo(name)
                member.size = len(data)
                bundle.addfile(member, io.BytesIO(data))
            for member in extras:
                bundle.addfile(member, io.BytesIO(b"x") if member.isfile() else None)
        return archive, GATE.digest(archive)

    def validate(self, *args, **kwargs):
        archive, sha = self.pack(*args, **kwargs)
        return GATE.validate_bundle(archive, sha, self.base / "unpacked")

    def test_all_hashes_and_synthetic_binding_are_required_before_execution(self):
        with patch.object(GATE.subprocess, "run") as run:
            self.assertEqual(self.validate(), self.provenance)
        run.assert_not_called()
        self.assertEqual((self.base / "unpacked/source/fixture.json").stat().st_mode & 0o777, 0o600)
        self.assertEqual((self.base / "unpacked").stat().st_mode & 0o777, 0o700)

    def test_wrong_outer_pin_or_unreviewed_inner_bytes_are_refused(self):
        archive, _ = self.pack()
        with self.assertRaises(GATE.GateError):
            GATE.validate_bundle(archive, "0" * 64, self.base / "wrong-pin")
        with self.assertRaises(GATE.GateError):
            self.validate(lambda m: m["files"].update({"old-runtime/MemoryForge.Core.dll": "0" * 64}))

    def test_missing_hash_and_missing_provenance_are_refused(self):
        for key in ("files", "provenance", "schema"):
            with self.subTest(key=key), self.assertRaises(GATE.GateError):
                self.validate(lambda m: m.pop(key))

    def test_extra_payload_and_file_missing_from_manifest_are_refused(self):
        self.payload["target-runtime/apphost"] = b"native"
        with self.assertRaises(GATE.GateError):
            self.validate()
        del self.payload["target-runtime/apphost"]
        with self.assertRaises(GATE.GateError):
            self.validate(lambda m: m["files"].pop("source/fixture.json"))

    def test_traversal_absolute_duplicate_and_symlink_members_never_escape(self):
        for name, kind in (("../escaped", tarfile.REGTYPE), ("/tmp/escaped", tarfile.REGTYPE),
                           ("old-runtime/MemoryForge.Core.dll", tarfile.REGTYPE), ("source/link", tarfile.SYMTYPE)):
            member = tarfile.TarInfo(name)
            member.type, member.size, member.linkname = kind, 1 if kind == tarfile.REGTYPE else 0, "../../escaped"
            with self.subTest(name=name), self.assertRaises(GATE.GateError):
                self.validate(extras=[member])
        self.assertFalse((self.base / "escaped").exists())

    def test_macho_native_pe_and_truncated_clr_are_refused(self):
        for raw in (b"\xcf\xfa\xed\xfe" + bytes(1024), b"MZ" + bytes(64), il_image()[:512]):
            with self.subTest(size=len(raw)), self.assertRaises(GATE.GateError):
                GATE.managed_il(raw)
        native = bytearray(il_image())
        struct.pack_into("<I", native, 0x210, 0)
        with self.assertRaises(GATE.GateError):
            GATE.managed_il(native)

    def test_personal_alias_missing_aila_and_wrong_source_module_are_refused(self):
        for mutate in (lambda: self.body.update(accountId="personal"), lambda: self.body["recruitment"]["ownedChars"].clear(),
                       lambda: self.body["binding"].update(coreModule="3" * 32)):
            original = json.loads(json.dumps(self.body))
            mutate()
            with self.assertRaises(GATE.GateError):
                self.validate()
            self.body = original
            shutil.rmtree(self.base / "unpacked", ignore_errors=True)

    def test_changed_fixture_party_is_refused_before_runtime_execution(self):
        self.fixture["accounts"][0]["party"][0]["charId"] = "nia"
        with self.assertRaises(GATE.GateError), patch.object(GATE.subprocess, "run") as run:
            self.validate()
        run.assert_not_called()

    def test_failed_child_output_is_kept_in_a_private_log(self):
        log = self.base / "diagnostics.log"
        with contextlib.redirect_stdout(io.StringIO()) as stdout, contextlib.redirect_stderr(io.StringIO()) as stderr:
            with self.assertRaises(GATE.GateError):
                GATE.run_private([sys.executable, "-c", "print('unit-only-secret');raise SystemExit(3)"], log, dict(os.environ))
        self.assertEqual(stdout.getvalue() + stderr.getvalue(), "")
        self.assertIn("unit-only-secret", log.read_text())
        self.assertEqual(log.stat().st_mode & 0o777, 0o600)

    def test_duplicate_json_members_are_not_silently_overwritten(self):
        with self.assertRaises(GATE.GateError):
            GATE.read_json('{"synthetic":true,"synthetic":false}')

    def test_platform_specific_deps_are_refused_before_managed_execution(self):
        self.payload["old-runtime/MemoryForge.Server.deps.json"] = encoded({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/osx-arm64"}})
        with self.assertRaises(GATE.GateError), patch.object(GATE.subprocess, "run") as run:
            self.validate()
        run.assert_not_called()

    def test_validate_only_makes_no_subprocess_or_postgres_pass_claim(self):
        archive, sha = self.pack()
        with patch.object(GATE.subprocess, "run") as run, contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(GATE.main(["--bundle", str(archive), "--sha256", sha, "--validate-only"]), 0)
        run.assert_not_called()
        self.assertIn("PostgreSQL NOT_RUN", output.getvalue())
        self.assertNotIn("CI PASS", output.getvalue())

    def test_unset_bundle_reports_not_run_after_the_existing_legacy_command(self):
        # Process stubs exercise the shell branch only; no PostgreSQL or .NET checks are represented as run.
        binaries = self.base / "stub-bin"
        binaries.mkdir()
        scripts = {"id": "printf '1000\\n'", "postgres": "printf 'postgres (PostgreSQL) 16.9\\n'",
                   "initdb": ":", "pg_ctl": ":", "psql": ":", "dotnet": "printf 'LEGACY_COMMAND_INVOKED\\n'"}
        for name, body in scripts.items():
            path = binaries / name
            path.write_text("#!/bin/sh\n" + body + "\n")
            path.chmod(0o700)
        env = {key: value for key, value in os.environ.items() if key not in
               {"MF_ACCOUNT_SUPPORT_BUNDLE", "MF_ACCOUNT_SUPPORT_BUNDLE_SHA256"}}
        env.update(PGBIN=str(binaries), PATH=str(binaries) + os.pathsep + env.get("PATH", ""), TMPDIR=str(self.base))
        result = subprocess.run(["bash", str(GATE.ROOT / "tools/check_practice_postgres.sh")], env=env,
                                capture_output=True, text=True, timeout=30, check=False)
        self.assertEqual(result.returncode, 0, "the optional unset branch must preserve legacy execution")
        self.assertIn("LEGACY_COMMAND_INVOKED", result.stdout)
        self.assertIn("Account migration/support checks: NOT_RUN (MF_ACCOUNT_SUPPORT_BUNDLE is unset).", result.stdout)
        self.assertNotIn("Account migration/support CI PASS", result.stdout)

    def test_two_disposable_dbs_and_exact_runtime_overlay_orchestration(self):
        self.validate()
        bundle = self.base / "unpacked"
        context = self.base / "cluster"
        context.mkdir(mode=0o700)
        (context / "data").mkdir(mode=0o700)
        (context / "data/PG_VERSION").write_text("16\n")
        password = "b" * 64
        for name, data in {"port": "25432", "pgpass": "127.0.0.1:25432:*:mf_admin:" + "a" * 64,
                           "runtime.txt": "Host=127.0.0.1;Port=25432;Database=mf_practice_checks;Username=mf_runtime;Password=" + password + ";SSL Mode=Disable"}.items():
            (context / name).write_text(data)
            (context / name).chmod(0o600)
        pgbin = self.base / "pgbin"
        pgbin.mkdir()
        (pgbin / "psql").write_text("unit-only")
        calls = []
        stages = []

        def child(command, log, env, data=None, timeout=7200):
            calls.append((command, env.copy(), data))
            self.assertNotIn(password, " ".join(command))
            self.assertNotIn("PGPASSWORD", env)
            self.assertIn("TMPDIR", env)
            self.assertTrue(Path(env["TMPDIR"]).is_relative_to(context))
            self.assertEqual(Path(env["TMPDIR"]).stat().st_mode & 0o777, 0o700)
            if "build" in command:
                output = Path(command[command.index("-o") + 1])
                output.mkdir()
                for name in GATE.RUNTIME_FILES:
                    (output / name).write_bytes(b"fresh HEAD " + name.encode())
            elif "MemoryForge.MigrationChecks.dll" in command[1]:
                pinned = "--postgres-file" in command
                stages.append("pinned-migration" if pinned else "head-migration")
                self.assertIn("--cli", command)
                if pinned:
                    self.assertIn("--require-registered-target", command)
                else:
                    self.assertNotIn("--require-registered-target", command)
                    for name in GATE.RUNTIME_FILES:
                        self.assertTrue((Path(command[1]).parent / name).read_bytes().startswith(b"fresh HEAD "))
                proof = Path(command[command.index("--out") + 1])
                proof.mkdir()
                known = pinned and not getattr(self, "unknown_target", False)
                candidate = b"unit-only pinned candidate" if pinned else b"unit-only fresh HEAD candidate"
                (proof / "candidate.envelope.json").write_bytes(candidate)
                (proof / "result.json").write_bytes(encoded({"postgres": pinned, "postgresCli": pinned, "ownerFileCli": known,
                    "ownerPgCli": known, "cli": True, "cliTarget": "KNOWN" if known else "REFUSED_UNKNOWN",
                    "oldCoreModule": "1" * 32, "newCoreModule": "2" * 32 if pinned else "3" * 32,
                    "sourceSha256": GATE.digest(bundle / "source/account-growth-check.json"), "candidateSha256": hashlib.sha256(candidate).hexdigest()}))
                log.write_text("MigrationChecks: 10 passed;scope=full-old-new-proof,file" + (",postgres" if pinned else "")
                               + ",cli;cliTarget=" + ("KNOWN;ownerFileCli=MIGRATED;ownerPgCli=MIGRATED" if known else "REFUSED_UNKNOWN;ownerFileCli=NOT_RUN;ownerPgCli=NOT_RUN") + "\n")
            elif "MemoryForge.SupportChecks.dll" in command[1]:
                pinned = "--postgres-file" in command
                stages.append("pinned-support" if pinned else "head-support")
                self.assertIn("--http", command)
                if not pinned:
                    for name in GATE.RUNTIME_FILES:
                        self.assertTrue((Path(command[1]).parent / name).read_bytes().startswith(b"fresh HEAD "))
                    self.assertEqual(Path(command[3]).read_bytes(), b"unit-only fresh HEAD candidate")
                with log.open("a") as stream:
                    stream.write("SupportChecks: 10 passed\nSupportChecks isolated proof backend=" + ("postgres" if pinned else "file") + "\n")

        with patch.object(GATE, "run_private", side_effect=child), patch.dict(os.environ, {"DOTNET_HOST_PATH": sys.executable, "PGPASSWORD": "must-not-propagate"}):
            GATE.run_gate(bundle, self.provenance, context, pgbin)
        created = [data for command, _, data in calls if data and data.startswith(b"CREATE DATABASE")]
        self.assertEqual(len(created), 2)
        self.assertNotEqual(created[0], created[1])
        self.assertEqual(stages, ["head-migration", "head-support", "pinned-migration", "pinned-support"])
        migration = next(c for c, _, _ in calls if "MemoryForge.MigrationChecks.dll" in c[1] and "--postgres-file" in c)
        support = next(c for c, _, _ in calls if "MemoryForge.SupportChecks.dll" in c[1] and "--postgres-file" in c)
        internal = migration[migration.index("--postgres-file") + 1]
        owner_cli = migration[migration.index("--postgres-cli-file") + 1]
        self.assertNotEqual(internal, owner_cli)
        self.assertEqual(support[support.index("--postgres-file") + 1], internal)
        for command in (migration, support):
            for name in GATE.RUNTIME_FILES:
                self.assertEqual((Path(command[1]).parent / name).read_bytes(), (bundle / "target-runtime" / name).read_bytes())
        self.unknown_target = True
        count = stages.count("pinned-support")
        with patch.object(GATE, "run_private", side_effect=child), patch.dict(os.environ, {"DOTNET_HOST_PATH": sys.executable}):
            with self.assertRaises(GATE.GateError):
                GATE.run_gate(bundle, self.provenance, context, pgbin)
        self.assertEqual(stages.count("pinned-support"), count,
                         "a deliberately refused unknown target can never masquerade as positive migration/support acceptance")


if __name__ == "__main__":
    unittest.main(verbosity=2)
