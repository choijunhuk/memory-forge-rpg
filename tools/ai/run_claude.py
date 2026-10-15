#!/usr/bin/env python3
"""Run one scoped official Claude CLI job after local subscription checks."""
import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from uuid import UUID

ROOT = Path(__file__).resolve().parents[2]
CLI = Path('/opt/homebrew/bin/claude')
MODEL = 'claude-opus-5-5'
TIMEOUT = 1800
PRIVATE_READS = ('Read(./.env)', 'Read(./.env.*)', 'Read(./**/.env)',
                 'Read(./**/.env.*)', 'Read(./.codex/auth.json)',
                 'Read(./.claude/settings.local.json)', 'Read(./ops/ai/local/**)')


def child_environment(source, effort):
    prefixes = ('ANTHROPIC', 'CLAUDE', 'AWS_', 'AMAZON_', 'GOOGLE_', 'GCLOUD_',
                'AZURE_', 'VERTEX_', 'BEDROCK_', 'FOUNDRY_', 'OPENAI_', 'CODEX_', 'LITELLM_')
    env = {key: value for key, value in source.items()
           if not key.upper().startswith(prefixes)
           and 'PROXY' not in key.upper() and 'MODEL' not in key.upper()
           and key.upper() not in {'NODE_OPTIONS', 'CLOUD_ML_REGION', 'MAX_THINKING_TOKENS',
                                   'DISABLE_INTERLEAVED_THINKING'}}
    env['CLAUDE_CODE_EFFORT_LEVEL'] = effort
    env['CLAUDE_CODE_DISABLE_TERMINAL_TITLE'] = '1'
    env['MEMORY_FORGE_CLAUDE_JOB'] = '1'
    return env


def billing_evidence(path, now):
    evidence = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(evidence, dict) or not isinstance(evidence.get('verified_at'), str):
        raise ValueError('Account UI attestation is missing its verification timestamp')
    verified = datetime.fromisoformat(evidence['verified_at'].replace('Z', '+00:00'))
    if (verified.tzinfo is None or not -timedelta(minutes=5) <= now - verified <= timedelta(days=1)
            or evidence.get('claude_auth') != 'claude.ai'
            or evidence.get('verified_by') != 'Codex account UI'
            or evidence.get('usage_credits_enabled') is not False
            or evidence.get('auto_reload_enabled') is not False
            or not isinstance(evidence.get('claude_account_fingerprint'), str)
            or not re.fullmatch(r'[0-9a-f]{64}', evidence['claude_account_fingerprint'])):
        raise ValueError('Fresh account UI attestation, account fingerprint, and credits/auto reload OFF are required')
    return verified.isoformat(), evidence['claude_account_fingerprint']


def account_fingerprint(email):
    if not isinstance(email, str) or not re.fullmatch(r'[^@\s]+@[^@\s]+', email.strip()):
        raise ValueError('CLI account email is required to match the account UI attestation')
    return hashlib.sha256(('claude.ai\0' + email.strip().casefold()).encode('utf-8')).hexdigest()


def git(worktree, *args):
    proc = subprocess.run(['git', '-C', str(worktree), *args], capture_output=True,
                          text=True, timeout=20)
    if proc.returncode:
        raise ValueError('Git worktree metadata could not be verified')
    return proc.stdout.strip()


def worktree_metadata(worktree):
    if (worktree == ROOT.resolve()
            or Path(git(worktree, 'rev-parse', '--show-toplevel')).resolve() != worktree
            or git(worktree, 'rev-parse', '--path-format=absolute', '--git-common-dir')
            != git(ROOT, 'rev-parse', '--path-format=absolute', '--git-common-dir')):
        raise ValueError('A separate worktree of this repository is required')
    branch = git(worktree, 'symbolic-ref', '--short', 'HEAD')
    if branch == git(ROOT, 'symbolic-ref', '--short', 'HEAD'):
        raise ValueError('Claude must use a separate branch')
    return {'branch': branch, 'base_commit': git(worktree, 'rev-parse', 'HEAD')}


def edit_scope(worktree, paths):
    for value in paths:
        path = Path(value)
        if (not re.fullmatch(r'[A-Za-z0-9_./-]+', value) or path.is_absolute()
                or '..' in path.parts or not (worktree / path).is_file()
                or not (worktree / path).resolve().is_relative_to(worktree)
                or (worktree / path).resolve() != worktree / path):
            raise ValueError('Edit scope must name existing relative files inside the worktree')
    return sorted({Path(value).as_posix() for value in paths})


def settings_file():
    path = ROOT / '.claude/settings.json'
    settings = json.loads(path.read_text(encoding='utf-8'))
    allowed = {'model', 'effortLevel', 'modelSettings', 'forceLoginMethod',
               'availableModels', 'fallbackModel', 'switchModelsOnFlag'}
    if (not isinstance(settings, dict) or set(settings) - allowed or settings.get('model') != MODEL
            or settings.get('forceLoginMethod') != 'claudeai'
            or settings.get('availableModels') != [MODEL]
            or settings.get('fallbackModel') != []
            or settings.get('switchModelsOnFlag') is not False
            or settings.get('effortLevel') != 'high'
            or settings.get('modelSettings', {}) not in ({}, {MODEL: {'effortLevel': 'high'}})):
        raise ValueError('Project CLI settings must pin subscription login, Opus and no fallback')
    return path


def invoke(arguments, worktree, env, log, timeout=20, prompt=None):
    stdout = stderr = ''
    try:
        proc = subprocess.run([str(CLI), *arguments], cwd=worktree, env=env,
                              input=prompt, capture_output=True, text=True, timeout=timeout)
        stdout, stderr = proc.stdout, proc.stderr
    except subprocess.TimeoutExpired as error:
        stdout, stderr = error.stdout or '', error.stderr or ''
        raise
    finally:
        for name, value in (('stdout.json', stdout), ('stderr.txt', stderr)):
            if isinstance(value, bytes):
                value = value.decode('utf-8', errors='replace')
            log.with_name(log.name + '.' + name).write_text(value, encoding='utf-8')
    return proc


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('task', type=Path)
    parser.add_argument('--worktree', required=True, type=Path)
    parser.add_argument('--effort', choices=('high', 'xhigh', 'max'), default='high')
    parser.add_argument('--billing-evidence', type=Path,
                        default=ROOT / 'ops/ai/local/runtime-billing-evidence.json')
    parser.add_argument('--resume', type=UUID)
    parser.add_argument('--allow-edit', action='append', default=[], metavar='RELATIVE_FILE')
    args = parser.parse_args(argv)
    task_id = args.task.stem
    if args.task.suffix != '.md' or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]*', task_id):
        parser.error('Task must be a Markdown file with a simple task ID filename')
    now = datetime.now(timezone.utc)
    run_id = task_id + '-' + now.strftime('%Y%m%dT%H%M%S%fZ')
    local = ROOT / 'ops/ai/local'
    local.mkdir(parents=True, exist_ok=True)
    result_path = ROOT / 'ops/ai/tasks' / (task_id + '.result.json')
    result = {'task_id': task_id, 'run_id': run_id, 'status': 'BLOCKED',
              'requestedEffort': args.effort, 'requestedModel': MODEL, 'process_exit': None,
              'changes_verified': False, 'raw_output': 'ops/ai/local/' + run_id + '*'}
    try:
        if os.environ.get('CLAUDECODE') or os.environ.get('MEMORY_FORGE_CLAUDE_JOB'):
            raise ValueError('Recursive Claude jobs are forbidden')
        result['billing_verified_at'], expected_account = billing_evidence(args.billing_evidence, now)
        result['billing_evidence_kind'] = 'account UI attestation; not server enforcement'
        result['access_controls'] = 'Restricted CLI built-in file tools; not an OS sandbox; managed policy still applies'
        worktree = args.worktree.resolve()
        result.update(worktree_metadata(worktree), worktree=str(worktree))
        scope = edit_scope(worktree, args.allow_edit)
        result['allowed_files'] = scope
        env = child_environment(os.environ, args.effort)
        flags = ['--restricted', '--setting-sources', '', '--settings', str(settings_file()),
                 '--mcp-config', '{"mcpServers":{}}', '--strict-mcp-config']
        version = invoke([*flags, '--version'], worktree, env, local / (run_id + '.version'))
        if version.returncode or not re.fullmatch(r'\d+\.\d+\.\d+ \(Claude Code\)', version.stdout.strip()):
            raise ValueError('Official Claude Code CLI version could not be verified')
        result['cli_version'] = version.stdout.strip()
        auth = invoke([*flags, 'auth', 'status'], worktree, env, local / (run_id + '.auth'))
        authentication = json.loads(auth.stdout)
        if (auth.returncode or not isinstance(authentication, dict) or authentication.get('loggedIn') is not True
                or authentication.get('authMethod') != 'claude.ai'
                or authentication.get('apiProvider') != 'firstParty'
                or authentication.get('subscriptionType') not in {'pro', 'max'}):
            raise ValueError('Included claude.ai Pro/Max subscription authentication is required')
        if account_fingerprint(authentication.get('email')) != expected_account:
            raise ValueError('CLI account does not match the account UI billing attestation')
        result['billing_account_matched'] = True
        prompt = ((ROOT / 'docs/START_CLAUDE.md').read_text(encoding='utf-8')
                  + '\n\nTask ID: ' + task_id + '\nRequested effort: ' + args.effort
                  + '\nAllowed edit files: ' + json.dumps(scope)
                  + '\nStay within this task. Do not call agents or read credentials.\n\n'
                  + args.task.read_text(encoding='utf-8'))
        (local / (run_id + '.request.txt')).write_text(prompt, encoding='utf-8')
        tools = 'Read,Glob,Grep' + (',Edit' if scope else '')
        allowed = 'Read(./**),Glob,Grep' + ''.join(',Edit(./' + path + ')' for path in scope)
        command = [*flags, '--model', MODEL, '--effort', args.effort,
                   '--permission-mode', 'dontAsk', '--tools', tools, '--allowedTools', allowed,
                   '--disallowedTools', ','.join(PRIVATE_READS),
                   '--disable-slash-commands', '--output-format', 'json']
        if args.resume:
            command.extend(['--resume', str(args.resume)])
        result['status'] = 'FAILED'
        proc = invoke([*command, '-p'], worktree, env, local / run_id, TIMEOUT, prompt)
        result['process_exit'] = proc.returncode
        payload = json.loads(proc.stdout)
        if not isinstance(payload, dict):
            raise ValueError('Claude returned a non-object result')
        result.update({key: payload.get(key) for key in ('session_id', 'subtype', 'is_error', 'result')})
        usage = payload.get('modelUsage')
        result['model_usage_keys'] = list(usage) if isinstance(usage, dict) else []
        if proc.returncode or payload.get('is_error') is not False or payload.get('subtype') != 'success':
            if re.search(r'hit your limit|usage limit|rate.limit|subscription limit', str(payload.get('result')), re.I):
                result['status'] = 'BLOCKED_SUBSCRIPTION_LIMIT'
            raise ValueError('Claude job failed; inspect the local checkpoint before continuing')
        if set(result['model_usage_keys']) != {MODEL}:
            raise ValueError('Returned model usage is missing or violates the Opus-only policy')
        if not isinstance(payload.get('session_id'), str) or not payload['session_id']:
            raise ValueError('Claude result did not include a session ID')
        result['status'] = 'RECEIVED_UNVERIFIED'
    except subprocess.TimeoutExpired:
        result.update(status='TIMED_OUT', blocking_reason='CLI timeout; local checkpoint saved; no retry')
    except (OSError, ValueError, KeyError, TypeError) as error:
        result['blocking_reason'] = str(error) if isinstance(error, ValueError) else type(error).__name__
    result_path.parent.mkdir(parents=True, exist_ok=True)
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(result['status'] + ': ' + str(result_path))
    return 0 if result['status'] == 'RECEIVED_UNVERIFIED' else 1


if __name__ == '__main__':
    sys.exit(main())
