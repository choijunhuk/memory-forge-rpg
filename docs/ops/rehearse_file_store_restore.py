"""Backup/restore rehearsal for the Development file store (§18.3 "복구 리허설", local only).

Usage: python3 docs/ops/rehearse_file_store_restore.py --server-dll <built MemoryForge.Server.dll> --work <new dir outside any Git checkout> --out <result.json>

It runs the real host on a temporary fixture/store, makes a backup, keeps changing the store, restores the backup into a
fresh directory and checks what survives. No production data or network beyond 127.0.0.1 is involved: requests bypass
any configured proxy, and the temporary secret is never printed or written outside --work. Exit 0 only when every check
in GATE holds; the result file lists the failed ones.
"""
import argparse
import hashlib
import json
import os
import queue
import re
import secrets
import shutil
import subprocess
import sys
import tarfile
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DIRECT = urllib.request.build_opener(urllib.request.ProxyHandler({}))  # never hand the Bearer credential to a proxy
GATE = {
    'create_status': 200, 'enter_status': 200, 'restored_bytes_equal_backup': True, 'restored_store_mode': '0o700',
    'restored_envelope_modes': ['0o600'], 'account_status_after_restore': 200, 'run_status_after_restore': 200,
    'account_view_equals_backup_time': True, 'run_view_equals_backup_time': True,
    'pre_backup_receipt_replays_same_bytes': True, 'post_backup_command_status': 200,
    'post_backup_command_same_bytes_as_original': True, 'damaged_restore_refused': [500, 'STORE_CORRUPT'],
    'damaged_restore_preserved_bytes': True,
}


class Host:
    """One real host process. Use it with `with`: the process is always terminated and reaped, and a reader thread keeps
    draining its output so the host can never block on a full pipe."""

    def __init__(self, dll, fixture, store):
        env = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development'}
        args = ['dotnet', str(dll), f'--MemoryForge:ContentRoot={ROOT}', f'--MemoryForge:PracticeFixture={fixture}',
                f'--MemoryForge:PracticeStore={store}', '--MemoryForge:PracticePort=0']
        self.process = subprocess.Popen(args, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        self.lines = queue.Queue()
        threading.Thread(target=self._drain, daemon=True).start()

    def _drain(self):
        for line in self.process.stdout:
            self.lines.put(line)
        self.lines.put(None)

    def __enter__(self):
        try:
            deadline = time.monotonic() + 60
            while (left := deadline - time.monotonic()) > 0:
                line = self.lines.get(timeout=left)
                if line is None:
                    break
                match = re.search(r'listening on http://127\.0\.0\.1:(\d+) ', line)
                if match:
                    self.port = int(match.group(1))
                    return self
        except queue.Empty:
            pass
        self.stop()
        raise RuntimeError('host did not start listening within 60 s')

    def __exit__(self, *_):
        self.stop()

    def call(self, secret, method, path, body=None):
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(f'http://127.0.0.1:{self.port}{path}', data=data, method=method,
                                         headers={'Authorization': f'Bearer {secret}', 'Content-Type': 'application/json'})
        try:
            with DIRECT.open(request, timeout=60) as response:
                return response.status, response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.read()

    def stop(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()


def inside_git(path):
    """True when path (or its nearest existing parent) is inside any Git work tree or Git directory."""
    probe = path
    while not probe.exists():
        probe = probe.parent
    out = subprocess.run(['git', '-C', str(probe), 'rev-parse', '--is-inside-work-tree', '--is-inside-git-dir'],
                         capture_output=True, text=True)
    return out.returncode == 0 and 'true' in out.stdout.split()


def digests(store):
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(store.iterdir()) if p.is_file()}


def mode(path):
    return oct(path.stat().st_mode & 0o777)


def code(body):
    try:
        value = json.loads(body)
        return value.get('code') or value.get('error')
    except (ValueError, AttributeError):
        return None


def rehearse(dll, work, secret):
    fixture = work / 'fixture.json'
    fixture.write_text(json.dumps({
        'schema': 'mf-dev-practice-fixture-1', 'developmentOnly': True, 'label': 'restore rehearsal fixture',
        'accounts': [{'accountId': 'rehearsal', 'credentialSha256': hashlib.sha256(secret.encode()).hexdigest(),
                      'length': 'short', 'party': [{'charId': 'sera', 'res': 0}, {'charId': 'harin', 'res': 0},
                                                   {'charId': 'nia', 'res': 0}]}]}))
    fixture.chmod(0o600)
    live, restored, backup = work / 'store-live', work / 'store-restored', work / 'backup.tar'
    checks = {}

    # 1. Commit a run creation, stop the host (quiesced store) and back up the whole store directory.
    with Host(dll, fixture, live) as host:
        checks['create_status'], create_body = host.call(secret, 'POST', '/dev/practice/runs', {'requestId': 'rehearsal-create-1'})
        run_id = json.loads(create_body)['runId']
        _, account_at_backup = host.call(secret, 'GET', '/dev/practice/account')
        _, view_at_backup = host.call(secret, 'GET', f'/dev/practice/runs/{run_id}')
    with tarfile.open(backup, 'w') as tar:
        tar.add(live, arcname='.')
    backup_digests = digests(live)

    # 2. Keep working after the backup: one deterministic command the backup does not contain.
    view = json.loads(view_at_backup)
    enter = {'requestId': 'rehearsal-enter-1', 'expectedVersion': view['version'], 'kind': 'enter',
             'args': {'nodeId': view['state']['available'][0]}}
    with Host(dll, fixture, live) as host:
        checks['enter_status'], enter_body = host.call(secret, 'POST', f'/dev/practice/runs/{run_id}/commands', enter)

    # 3. Restore into a fresh owner-only directory and verify bytes and modes before starting the host.
    restored.mkdir(mode=0o700)
    with tarfile.open(backup) as tar:
        tar.extractall(restored, filter='data')
    checks['restored_bytes_equal_backup'] = digests(restored) == backup_digests
    checks['restored_store_mode'] = mode(restored)
    checks['restored_envelope_modes'] = sorted({mode(p) for p in restored.iterdir() if p.suffix == '.json'})
    with Host(dll, fixture, restored) as host:
        checks['account_status_after_restore'], account_after = host.call(secret, 'GET', '/dev/practice/account')
        checks['run_status_after_restore'], view_after = host.call(secret, 'GET', f'/dev/practice/runs/{run_id}')
        retry = host.call(secret, 'POST', '/dev/practice/runs', {'requestId': 'rehearsal-create-1'})
        # The post-backup command is not in the backup: the same requestId runs again on the restored state.
        lost = host.call(secret, 'POST', f'/dev/practice/runs/{run_id}/commands', enter)
    checks['account_view_equals_backup_time'] = account_after == account_at_backup
    checks['run_view_equals_backup_time'] = view_after == view_at_backup
    checks['pre_backup_receipt_replays_same_bytes'] = retry == (checks['create_status'], create_body)
    checks['post_backup_command_status'] = lost[0]
    checks['post_backup_command_same_bytes_as_original'] = lost == (checks['enter_status'], enter_body)

    # 4. A damaged restore must be refused and left byte-for-byte, never reset.
    damaged = work / 'store-damaged'
    shutil.copytree(restored, damaged)
    damaged.chmod(0o700)
    target = sorted(p for p in damaged.iterdir() if p.suffix == '.json')[0]
    raw = bytearray(target.read_bytes())
    raw[len(raw) // 2] ^= 0x01
    target.write_bytes(bytes(raw))
    before = digests(damaged)
    with Host(dll, fixture, damaged) as host:
        status, body = host.call(secret, 'GET', '/dev/practice/account')
    checks['damaged_restore_refused'] = [status, code(body)]
    checks['damaged_restore_preserved_bytes'] = digests(damaged) == before
    return checks, sorted(re.sub(r'account-.*?\.', 'account-<alias>.', n) for n in backup_digests)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server-dll', required=True, type=Path)
    parser.add_argument('--work', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    a = parser.parse_args()
    work = a.work.resolve()
    if work == ROOT or ROOT in work.parents or inside_git(work):
        sys.exit('--work must be outside every Git checkout and Git directory')
    work.mkdir(mode=0o700, parents=True, exist_ok=False)

    checks, files = rehearse(a.server_dll.resolve(), work, secrets.token_urlsafe(32))
    failed = sorted(k for k, expected in GATE.items() if checks.get(k) != expected)
    result = {
        'schema': 'mf-ops-file-restore-rehearsal-2', 'scope': 'Development file store only; not PostgreSQL, PITR or production',
        'server_dll_sha256': hashlib.sha256(a.server_dll.read_bytes()).hexdigest(), 'store_files_in_backup': files,
        'gate': GATE, 'checks': checks, 'failed': failed, 'pass': not failed,
    }
    a.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'pass': not failed, 'failed': failed}))
    sys.exit(0 if not failed else 1)


if __name__ == '__main__':
    main()
