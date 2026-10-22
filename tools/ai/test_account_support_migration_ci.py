#!/usr/bin/env python3
"""Linux-only PG16 migration/support gate over a reviewed synthetic managed-runtime bundle.

The existing check_practice_postgres.sh owns the disposable cluster and cleanup. Raw child
output stays in that cluster's private directory; nothing here reads a personal account.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import shutil
import stat
import struct
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
RUNTIME_FILES = ("MemoryForge.Server.dll", "MemoryForge.Core.dll", "Npgsql.dll",
                 "MemoryForge.Server.deps.json", "MemoryForge.Server.runtimeconfig.json")
PAYLOAD = {f"{kind}/{name}" for kind in ("old-runtime", "target-runtime") for name in RUNTIME_FILES}
PAYLOAD |= {"content/data/game_data.json", "source/fixture.json", "source/account-growth-check.json"}
SCHEMA = "memoryforge-account-support-ci-v1"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
HEX32 = re.compile(r"[0-9a-f]{32}\Z")


class GateError(Exception):
    """Only fixed, non-secret diagnostics may reach CI stdout/stderr."""


def require(ok, message):
    if not ok:
        raise GateError(message)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def unique_members(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON members are refused.")
        result[key] = value
    return result


def read_json(raw):
    try:
        return json.loads(raw, object_pairs_hook=unique_members)
    except (ValueError, UnicodeError):
        raise GateError("A bundle JSON document is malformed.") from None


def managed_il(raw):
    """Require a PE CLR metadata image with IL-only code, not a platform apphost/native DLL."""
    try:
        require(raw[:2] == b"MZ", "Runtime DLL is not a managed PE image.")
        pe = struct.unpack_from("<I", raw, 0x3c)[0]
        require(raw[pe:pe + 4] == b"PE\0\0", "Runtime DLL has no PE header.")
        sections, size = struct.unpack_from("<H", raw, pe + 6)[0], struct.unpack_from("<H", raw, pe + 20)[0]
        opt = pe + 24
        magic = struct.unpack_from("<H", raw, opt)[0]
        require(magic in (0x10b, 0x20b), "Runtime PE optional header is unsupported.")
        directories = opt + (96 if magic == 0x10b else 112)
        require(size >= directories - opt + 15 * 8, "Runtime DLL has no CLR directory.")
        rva, length = struct.unpack_from("<II", raw, directories + 14 * 8)
        require(rva > 0 and length >= 72 and sections > 0, "Runtime DLL has no managed CLR header.")

        def offset(value, count):
            for index in range(sections):
                section = opt + size + index * 40
                virtual, address, count_raw, start = struct.unpack_from("<IIII", raw, section + 8)
                delta = value - address
                if 0 <= delta and delta + count <= count_raw and delta < max(virtual, count_raw):
                    require(start + delta + count <= len(raw), "Runtime PE section is truncated.")
                    return start + delta
            raise GateError("Runtime CLR metadata lies outside a file section.")

        cli = offset(rva, 72)
        flags = struct.unpack_from("<I", raw, cli + 16)[0]
        require(flags & 1 and not flags & 0x10, "Runtime DLL is not portable managed IL.")
        metadata, metadata_size = struct.unpack_from("<II", raw, cli + 8)
        require(metadata_size >= 16 and raw[offset(metadata, 16):offset(metadata, 16) + 4] == b"BSJB",
                "Runtime DLL has no CLR metadata signature.")
    except (struct.error, IndexError):
        raise GateError("Runtime PE/CLR metadata is truncated.") from None


def validate_bundle(archive, expected_sha, destination):
    require(bool(HEX64.fullmatch(expected_sha)), "An exact reviewed bundle SHA256 is required.")
    require(archive.is_file() and not archive.is_symlink(), "The reviewed bundle must be a regular file.")
    require(digest(archive) == expected_sha, "Bundle SHA256 does not match its reviewed pin.")
    require(not destination.exists(), "Bundle destination must be a fresh private directory.")
    try:
        with tarfile.open(archive, "r:gz") as bundle:
            members = bundle.getmembers()
            paths = {}
            for member in members:
                name = member.name.rstrip("/")
                path = PurePosixPath(name)
                require(name and "\\" not in name and not path.is_absolute()
                        and all(part not in (".", "..", "") for part in name.split("/")), "Unsafe bundle member path.")
                require(name not in paths, "Duplicate bundle member path.")
                require(member.isfile() or member.isdir(), "Bundle links and special files are refused.")
                require(name in PAYLOAD | {"manifest.json"} or member.isdir() and name in
                        {"old-runtime", "target-runtime", "content", "content/data", "source"}, "Unexpected bundle member path.")
                require(0 <= member.size <= 536870912, "Bundle member exceeds the development bound.")
                paths[name] = member
            require(sum(m.size for m in members) <= 1073741824, "Bundle exceeds the development size bound.")
            require({n for n, m in paths.items() if m.isfile()} == PAYLOAD | {"manifest.json"}, "Bundle payload is incomplete.")
            require(paths["manifest.json"].size <= 65536, "Bundle manifest is oversized.")
            manifest = read_json(bundle.extractfile(paths["manifest.json"]).read())
            require(isinstance(manifest, dict) and set(manifest) == {"schema", "provenance", "files"}
                    and manifest["schema"] == SCHEMA, "Bundle manifest schema is unsupported.")
            provenance = manifest["provenance"]
            require(isinstance(provenance, dict) and set(provenance) == {"synthetic", "personalAccount", "sourceKind", "accountId", "oldCoreModule", "targetCoreModule"}
                    and provenance["synthetic"] is True and provenance["personalAccount"] is False
                    and provenance["sourceKind"] == "legitimate-earned-old-s3" and provenance["accountId"] == "growth-check"
                    and isinstance(provenance["oldCoreModule"], str) and bool(HEX32.fullmatch(provenance["oldCoreModule"]))
                    and isinstance(provenance["targetCoreModule"], str) and bool(HEX32.fullmatch(provenance["targetCoreModule"]))
                    and provenance["oldCoreModule"] != provenance["targetCoreModule"], "Bundle is not the reviewed synthetic old/new proof.")
            hashes = manifest["files"]
            require(isinstance(hashes, dict) and set(hashes) == PAYLOAD
                    and all(isinstance(v, str) and HEX64.fullmatch(v) for v in hashes.values()), "Every payload needs its exact SHA256.")
            destination.mkdir(mode=0o700)
            for name in sorted(PAYLOAD):
                target = destination / name
                target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                with bundle.extractfile(paths[name]) as source, target.open("xb") as output:
                    os.chmod(target, 0o600)
                    shutil.copyfileobj(source, output)
                require(digest(target) == hashes[name], "An inner bundle payload hash differs.")
                if name.endswith(".dll"):
                    managed_il(target.read_bytes())
                elif name.endswith(".json"):
                    read_json(target.read_bytes())
    except (tarfile.TarError, OSError):
        raise GateError("The bundle could not be safely read into its private directory.") from None

    content = destination / "content/data/game_data.json"
    outer = read_json((destination / "source/account-growth-check.json").read_bytes())
    require(isinstance(outer, dict) and set(outer) == {"schema", "sha256", "body"} and outer["schema"] == "mf-dev-practice-account-1"
            and isinstance(outer["body"], str) and hashlib.sha256(outer["body"].encode()).hexdigest() == outer["sha256"], "Synthetic account envelope checksum is invalid.")
    body = read_json(outer["body"])
    require(isinstance(body, dict) and body.get("accountId") == "growth-check" and isinstance(body.get("binding"), dict), "A personal or foreign account is refused.")
    binding = body["binding"]
    require(binding.get("coreModule") == provenance["oldCoreModule"] and binding.get("contentSha256") == digest(content)
            and binding.get("replay") == "mf-dev-practice-replay-1", "Source/runtime/content provenance does not match.")
    recruitment = body.get("recruitment")
    require(isinstance(body.get("growth"), dict) and isinstance(body["growth"].get("ledger"), list)
            and body["growth"]["ledger"] and isinstance(recruitment, dict) and isinstance(recruitment.get("ownedChars"), dict)
            and type(recruitment["ownedChars"].get("aila")) is int and recruitment["ownedChars"]["aila"] >= 1,
            "The synthetic source must contain the earned S3 history and owned Aila.")
    fixture = read_json((destination / "source/fixture.json").read_bytes())
    require(isinstance(fixture, dict) and fixture.get("schema") == "mf-dev-practice-fixture-1" and fixture.get("developmentOnly") is True
            and isinstance(fixture.get("accounts"), list) and len(fixture["accounts"]) == 1
            and isinstance(fixture["accounts"][0], dict)
            and set(fixture["accounts"][0]) == {"accountId", "credentialSha256", "length", "party"}
            and fixture["accounts"][0]["accountId"] == "growth-check"
            and isinstance(fixture["accounts"][0]["credentialSha256"], str) and HEX64.fullmatch(fixture["accounts"][0]["credentialSha256"]),
            "The fixture must name only the synthetic development account.")
    account = fixture["accounts"][0]
    fixture_identity = json.dumps({"length": account["length"], "party": account["party"]}, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    require(account["length"] in ("short", "full") and isinstance(account["party"], list) and 1 <= len(account["party"]) <= 3
            and binding.get("fixtureSha256") == hashlib.sha256(fixture_identity).hexdigest(), "The source is bound to a different fixture party.")
    for folder in ("old-runtime", "target-runtime"):
        config = read_json((destination / folder / "MemoryForge.Server.runtimeconfig.json").read_bytes())
        require(isinstance(config, dict) and isinstance(config.get("runtimeOptions"), dict)
                and config["runtimeOptions"].get("tfm") == "net10.0", "The reviewed runtime must target .NET10.")
        deps = read_json((destination / folder / "MemoryForge.Server.deps.json").read_bytes())
        require(isinstance(deps, dict) and isinstance(deps.get("runtimeTarget"), dict)
                and deps["runtimeTarget"].get("name") == ".NETCoreApp,Version=v10.0", "Platform-specific runtime dependency manifests are refused.")
    return provenance


def private(path, directory=False):
    info = path.lstat()
    require(not path.is_symlink() and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == (0o700 if directory else 0o600)
            and (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)), "PostgreSQL context permissions are not owner-only.")


def run_private(command, log, env, data=None, timeout=7200):
    with log.open("ab") as output:
        os.chmod(log, 0o600)
        try:
            result = subprocess.run(command, input=data, stdout=output, stderr=subprocess.STDOUT, env=env, cwd=ROOT, timeout=timeout, check=False)
        except (OSError, subprocess.TimeoutExpired):
            raise GateError("A CI child failed or timed out; see private diagnostics.") from None
    require(result.returncode == 0, "A CI child returned failure; see private diagnostics.")


def run_gate(bundle, provenance, context, pgbin):
    private(context, True)
    private(context / "data", True)
    require((context / "data/PG_VERSION").read_text().strip() == "16", "Only the disposable PostgreSQL16 cluster is accepted.")
    for filename in ("port", "pgpass", "runtime.txt"):
        private(context / filename)
    raw = (context / "runtime.txt").read_text()
    port = (context / "port").read_text().strip()
    require(port.isdigit() and 1024 <= int(port) <= 65535, "Disposable PostgreSQL port is invalid.")
    require(re.fullmatch(r"Host=127\.0\.0\.1;Port=" + port + r";Database=mf_practice_checks;Username=mf_runtime;Password=[0-9a-f]{64};SSL Mode=Disable", raw) is not None,
            "Only the existing disposable-cluster runtime connection is accepted.")
    require((pgbin / "psql").is_file(), "PostgreSQL16 psql is required.")
    work = context / ("account-support-" + secrets.token_hex(8))
    work.mkdir(mode=0o700)
    log = work / "diagnostics.log"
    env = {k: v for k, v in os.environ.items() if not k.startswith("PG")}
    dotnet = os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet")
    require(dotnet is not None and Path(dotnet).is_absolute() and Path(dotnet).is_file(), "An absolute .NET10 host is required.")
    env["DOTNET_HOST_PATH"] = dotnet
    env["TMPDIR"] = str(work)
    admin = dict(env, PGPASSFILE=str(context / "pgpass"))
    files = []
    for role in ("internal", "ownercli"):
        database = "mf_support_" + role + "_" + secrets.token_hex(8)
        psql = [str(pgbin / "psql"), "-h", "127.0.0.1", "-p", port, "-U", "mf_admin", "-Xq", "-v", "ON_ERROR_STOP=1", "-d"]
        run_private(psql + ["postgres"], log, admin, f"CREATE DATABASE {database};\n".encode())
        for schema in ("schema.sql", "dev_practice_schema.sql"):
            run_private(psql + [database], log, admin, (ROOT / "server" / schema).read_bytes())
        grants = f"GRANT CONNECT ON DATABASE {database} TO mf_runtime;\nGRANT USAGE ON SCHEMA dev_practice TO mf_runtime;\nGRANT SELECT ON dev_practice.schema_marker TO mf_runtime;\nGRANT SELECT,INSERT,UPDATE ON dev_practice.account_state TO mf_runtime;\n"
        run_private(psql + [database], log, admin, grants.encode())
        config = work / (role + ".txt")
        config.write_text(raw.replace("Database=mf_practice_checks;", "Database=" + database + ";"))
        config.chmod(0o600)
        files.append(config)
    outputs = []
    for project in ("MemoryForge.MigrationChecks", "MemoryForge.SupportChecks"):
        output = work / project
        run_private([dotnet, "build", str(ROOT / "server" / project / (project + ".csproj")), "-c", "Debug", "-o", str(output), "--nologo"], log, env)
        outputs.append(output)
    # Test the actual freshly compiled HEAD before replacing its runtime with the reviewed positive-migration target.
    head_proof = work / "head-file-result"
    head_migration_log = work / "head-migration.log"
    run_private([dotnet, str(outputs[0] / "MemoryForge.MigrationChecks.dll"), str(bundle / "content"), str(bundle / "old-runtime"),
                 str(bundle / "source/fixture.json"), str(bundle / "source/account-growth-check.json"), "--cli", "--out", str(head_proof)], head_migration_log, env)
    head = read_json((head_proof / "result.json").read_bytes())
    require(head.get("postgres") is False and head.get("postgresCli") is False and head.get("ownerPgCli") is False
            and head.get("cli") is True and head.get("cliTarget") in ("KNOWN", "REFUSED_UNKNOWN")
            and head.get("ownerFileCli") is (head["cliTarget"] == "KNOWN")
            and isinstance(head.get("newCoreModule"), str) and HEX32.fullmatch(head["newCoreModule"])
            and head.get("oldCoreModule") == provenance["oldCoreModule"]
            and head.get("sourceSha256") == digest(bundle / "source/account-growth-check.json")
            and head.get("candidateSha256") == digest(head_proof / "candidate.envelope.json"), "Fresh HEAD full file migration proof is incomplete.")
    file_cli = "MIGRATED" if head["ownerFileCli"] else "NOT_RUN"
    require(re.search(r"MigrationChecks: [1-9][0-9]* passed;scope=full-old-new-proof,file,cli;cliTarget="
                      + head["cliTarget"] + ";ownerFileCli=" + file_cli + ";ownerPgCli=NOT_RUN", head_migration_log.read_text()) is not None,
            "Fresh HEAD file proof or honest unknown-target refusal marker is absent.")
    head_support_log = work / "head-support.log"
    run_private([dotnet, str(outputs[1] / "MemoryForge.SupportChecks.dll"), str(bundle / "content"), str(head_proof / "candidate.envelope.json"), "--http"], head_support_log, env)
    require(re.search(r"SupportChecks: [1-9][0-9]* passed", head_support_log.read_text()) is not None and "backend=file" in head_support_log.read_text(),
            "Fresh HEAD actual file support/HTTP acceptance marker is absent.")
    for output in outputs:
        for filename in RUNTIME_FILES:
            shutil.copyfile(bundle / "target-runtime" / filename, output / filename)
    proof = work / "migration-result"
    migration_log = work / "pinned-migration.log"
    run_private([dotnet, str(outputs[0] / "MemoryForge.MigrationChecks.dll"), str(bundle / "content"), str(bundle / "old-runtime"),
                 str(bundle / "source/fixture.json"), str(bundle / "source/account-growth-check.json"), "--postgres-file", str(files[0]),
                 "--postgres-cli-file", str(files[1]), "--cli", "--require-registered-target", "--out", str(proof)], migration_log, env)
    result = read_json((proof / "result.json").read_bytes())
    require(result.get("postgres") is True and result.get("postgresCli") is True and result.get("ownerFileCli") is True
            and result.get("ownerPgCli") is True and result.get("cliTarget") == "KNOWN" and result.get("newCoreModule") == provenance["targetCoreModule"],
            "Positive registered-target PostgreSQL migration proof is incomplete.")
    text = migration_log.read_text()
    require(re.search(r"MigrationChecks: [1-9][0-9]* passed;.*cliTarget=KNOWN;ownerFileCli=MIGRATED;ownerPgCli=MIGRATED", text) is not None,
            "The registered owner CLI PostgreSQL acceptance marker is absent.")
    support_log = work / "pinned-support.log"
    run_private([dotnet, str(outputs[1] / "MemoryForge.SupportChecks.dll"), str(bundle / "content"), str(proof / "candidate.envelope.json"),
                 "--postgres-file", str(files[0]), "--http"], support_log, env)
    text = support_log.read_text()
    require(re.search(r"SupportChecks: [1-9][0-9]* passed", text) is not None and "backend=postgres" in text,
            "The actual PostgreSQL support/HTTP acceptance marker is absent.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--cluster-context", type=Path)
    parser.add_argument("--pgbin", type=Path, default=Path("/usr/lib/postgresql/16/bin"))
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(argv)
    try:
        if not args.validate_only:
            require(sys.platform == "linux" and args.cluster_context is not None, "The real gate requires Linux and the disposable PostgreSQL context.")
            private(args.cluster_context, True)
        import tempfile
        with tempfile.TemporaryDirectory(prefix="mf-support-bundle-", dir=args.cluster_context) as temporary:
            os.chmod(temporary, 0o700)
            bundle = Path(temporary) / "payload"
            provenance = validate_bundle(args.bundle, args.sha256, bundle)
            if args.validate_only:
                print("Synthetic managed bundle validation PASS; PostgreSQL NOT_RUN.")
                return 0
            run_gate(bundle, provenance, args.cluster_context, args.pgbin)
        print("Account migration/support CI PASS [fresh HEAD file/HTTP, pinned PostgreSQL16, registered owner CLI/HTTP].")
        return 0
    except GateError as error:
        print("Account migration/support CI FAIL: " + str(error) + " No unverified PostgreSQL PASS.", file=sys.stderr)
        return 1
    except (OSError, KeyError, TypeError):
        print("Account migration/support CI FAIL; no unverified PostgreSQL PASS. See private cluster diagnostics.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
