#!/usr/bin/env python3
"""Run one scoped official Claude CLI job after local subscription checks."""
import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
from urllib.parse import urlsplit
from uuid import UUID

ROOT = Path(__file__).resolve().parents[2]
CLI = Path('/opt/homebrew/bin/claude')
MODEL = 'claude-opus-5-5'
TIMEOUT = 1800
PRIVATE_READS = ('Read(./.env)', 'Read(./.env.*)', 'Read(./**/.env)',
                 'Read(./**/.env.*)', 'Read(./.codex/auth.json)',
                 'Read(./.claude/settings.local.json)', 'Read(./ops/ai/local/**)')
# Child processes, including stdio MCP servers, never inherit provider routes or stored secrets.
AI_PREFIXES = ('ANTHROPIC', 'CLAUDE', 'AWS_', 'AMAZON_', 'GOOGLE_', 'GCLOUD_', 'AZURE_', 'VERTEX_',
               'BEDROCK_', 'FOUNDRY_', 'OPENAI_', 'CODEX_', 'LITELLM_', 'PEEKABOO_', 'OLLAMA',
               'GEMINI_', 'XAI_', 'X_AI_', 'GROK_', 'MISTRAL_', 'GROQ_', 'OPENROUTER_', 'DEEPSEEK_',
               'PERPLEXITY_', 'TOGETHER_', 'FIREWORKS_', 'COHERE_', 'REPLICATE_', 'HUGGINGFACE',
               'HF_', 'STABILITY_', 'FAL_', 'IMAGINEART', 'MESHY_', 'TRIPO_', 'COPLAY')
SECRET_PARTS = ('API_KEY', 'APIKEY', 'TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL')
EMPTY_MCP = '{"mcpServers":{}}'
UNITY_URL = 'http://127.0.0.1:8080/mcp'
PEEKABOO = '@steipete/peekaboo@4.9.5'
CONTROL_LOCK = 'ops/ai/local/exclusive-control.lock'
RESOURCE_TOOLS = ('ListMcpResourcesTool', 'ReadMcpResourceTool')
# Fixed MCP tool names. Unity follows the recorded Unity MCP 10.3.0 calls and its upstream tool
# list; Peekaboo follows Codex's live 4.9.5 tools/list. CLI permissions match whole tool names,
# never arguments; compare the live tools/list before changing these sets.
UNITY_READ = ('set_active_instance', 'read_console', 'find_gameobjects', 'get_test_job',
              'get_sha', 'validate_script')
# execute_code runs arbitrary Editor C# for the write owner; it is not confined to any file scope.
UNITY_WRITE = ('manage_editor', 'manage_scene', 'manage_gameobject', 'manage_components',
               'manage_prefabs', 'manage_asset', 'manage_material', 'manage_texture',
               'manage_scriptable_object', 'manage_animation', 'manage_ui', 'manage_vfx',
               'refresh_unity', 'run_tests', 'manage_build', 'execute_code')
# Paid generation, server-side dispatch that bypasses per-tool permissions, arbitrary menu or
# project tools, dependency changes, and source edits that would skip the worktree edit scope.
# Generation/import names the installed server exposed beside asset_gen (image generation is Codex's).
UNITY_GENERATION = ('generate_image', 'generate_model', 'generate_audio', 'import_model',
                    'import_model_file', 'blender_bridge')
# Other installed tools not yet assigned to a Claude profile; assign them separately when needed.
UNITY_UNASSIGNED = ('debug_request_context', 'find_in_file', 'manage_camera', 'manage_graphics',
                    'manage_physics', 'manage_probuilder', 'manage_profiler',
                    'manage_script_capabilities', 'manage_tools', 'unity_docs', 'unity_reflect')
UNITY_DENY = ('asset_gen', 'batch_execute', 'execute_custom_tool', 'execute_menu_item',
              'manage_packages', 'manage_script', 'create_script', 'delete_script',
              'apply_text_edits', 'script_apply_edits', 'manage_shader',
              *UNITY_GENERATION, *UNITY_UNASSIGNED)
# Free native capture/OCR, accessibility and input for the task's target app or window.
DESKTOP_ALLOW = ('permissions', 'app', 'window', 'menu', 'dock', 'dialog', 'space', 'image', 'see',
                 'capture', 'inspect_ui', 'verify_state', 'click', 'type', 'press', 'scroll', 'drag',
                 'select_text', 'set_value', 'action', 'sleep')
# AI provider calls, clipboard contents that may hold secrets, and web navigation outside the task.
DESKTOP_DENY = ('analyze', 'agent', 'clipboard', 'paste', 'browser')
# Input keys that would turn an allowed screen call into AI analysis; audited after the run.
AI_OPTIONS = ('question', 'analyze', 'prompt', 'provider', 'provider_config', 'model')
# Opt-in local checks Claude may run itself, matched exactly from the worktree root. Tests run
# repository code as the user, so this is not an OS sandbox.
TEST_COMMANDS = {'node': 'npm test', 'proposals': 'npm run test:proposals',
                 'memory-ui': 'npm run test:memory-ui', 'late-ui': 'npm run test:late-ui',
                 'python': 'python3 -m unittest discover -s tests -v', 'docs': 'npm run test:docs',
                 'handoff': 'npm run test:handoff', 'manifest': 'npm run verify:manifest'}
BASH_DENY = ('Bash(claude:*)', 'Bash(codex:*)', 'Bash(npx:*)', 'Bash(curl:*)', 'Bash(wget:*)',
             'Bash(ssh:*)', 'Bash(scp:*)', 'Bash(security:*)', 'Bash(gh:*)', 'Bash(rm:*)',
             'Bash(git push:*)', 'Bash(git reset:*)', 'Bash(git clean:*)', 'Bash(git checkout:*)',
             'Bash(git rebase:*)', 'Bash(git stash:*)', 'Bash(git branch:*)', 'Bash(git remote:*)',
             'Bash(npm publish:*)', 'Bash(npm install:*)')


def mcp_tools(server, names):
    return tuple('mcp__' + server + '__' + name for name in names)


UNITY_SERVER = {'unityMCP': {'type': 'http', 'url': UNITY_URL}}
DESKTOP_SERVER = {'peekaboo': {'type': 'stdio', 'command': 'npx', 'args': ['-y', PEEKABOO, 'mcp']}}
MCP_PROFILES = {
    'none': {'servers': {}, 'tools': (), 'allow': (), 'deny': (), 'exclusive': False,
             'ai_options': (), 'rules': ''},
    'unity-read': {
        'servers': UNITY_SERVER, 'tools': RESOURCE_TOOLS, 'allow': mcp_tools('unityMCP', UNITY_READ),
        'deny': mcp_tools('unityMCP', UNITY_WRITE + UNITY_DENY), 'exclusive': False, 'ai_options': (),
        'rules': 'Read Unity state only: do not clear the Console or change Editor, scene, prefab, '
                 'asset or Play state. Codex keeps Editor write ownership.'},
    'unity-write': {
        'servers': UNITY_SERVER, 'tools': RESOURCE_TOOLS,
        'allow': mcp_tools('unityMCP', UNITY_READ + UNITY_WRITE), 'deny': mcp_tools('unityMCP', UNITY_DENY),
        'exclusive': True, 'ai_options': (),
        'rules': 'This job holds the single Unity Editor write lock. Editor writes land in the open '
                 'Editor project outside this worktree, so list every Editor change in the result. '
                 'Keep C# source edits in the allowed worktree files. Use execute_code only to install, '
                 'inspect or verify this job, never for file deletion, network, credentials, billing, '
                 'other models or paid or AI asset generation.'},
    'desktop': {
        'servers': DESKTOP_SERVER, 'tools': (), 'allow': mcp_tools('peekaboo', DESKTOP_ALLOW),
        'deny': mcp_tools('peekaboo', DESKTOP_DENY), 'exclusive': True, 'ai_options': AI_OPTIONS,
        'rules': 'Operate only the app or window named in the task; app and window list actions are '
                 'for discovery. Never pass AI analysis options (question, analyze, prompt, provider, '
                 'model). Never use the GUI to reach terminals or shells, account, billing, credential '
                 'or keychain views, privacy or security settings, or AI and image services.'},
}


def child_environment(source, effort, profile='none'):
    env = {key: value for key, value in source.items()
           if not key.upper().startswith(AI_PREFIXES)
           and not any(part in key.upper() for part in SECRET_PARTS)
           and 'PROXY' not in key.upper() and 'MODEL' not in key.upper()
           and key.upper() not in {'NODE_OPTIONS', 'CLOUD_ML_REGION', 'MAX_THINKING_TOKENS',
                                   'DISABLE_INTERLEAVED_THINKING'}}
    env['CLAUDE_CODE_EFFORT_LEVEL'] = effort
    env['CLAUDE_CODE_DISABLE_TERMINAL_TITLE'] = '1'
    env['ENABLE_CLAUDEAI_MCP_SERVERS'] = 'false'
    env['MEMORY_FORGE_CLAUDE_JOB'] = '1'
    if 'peekaboo' in MCP_PROFILES[profile]['servers']:
        env['MCP_TIMEOUT'] = '120000'  # the first pinned npx download can exceed the default startup wait
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


def mcp_preflight(servers, env):
    """Non-inference readiness checks; none of them proves an MCP tool call."""
    checks = []
    if 'unityMCP' in servers:
        endpoint = urlsplit(UNITY_URL)
        try:
            with socket.create_connection((endpoint.hostname, endpoint.port), timeout=2):
                pass
        except OSError:
            raise ValueError('Unity MCP is not listening at ' + UNITY_URL + '; start the Editor MCP server first')
        checks.append('local Unity MCP port listening')
    if 'peekaboo' in servers:
        if sys.platform != 'darwin':
            raise ValueError('The desktop MCP profile needs the local macOS session')
        if not shutil.which('npx', path=env.get('PATH', '')):
            raise ValueError('npx is not on PATH; install free Node.js through an approved path first')
        # Stored provider keys could serve hidden AI options of allowed capture tools; existence only.
        stored = Path.home() / '.peekaboo'
        if any(os.path.lexists(stored / name) for name in ('credentials', 'config.json')):
            raise ValueError('A Peekaboo credential or config file exists; remove its AI provider settings '
                             'before desktop control (contents were not read)')
        checks.append('macOS, npx on PATH, no Peekaboo credential or config file')
    return '; '.join(checks)


def worktree_lock(worktree):
    return 'ops/ai/local/worktree-' + hashlib.sha256(str(worktree).encode('utf-8')).hexdigest()[:16] + '.lock'


def acquire_lock(relative, owner, busy):
    """Exclusive among runner jobs only; Codex honors the same locks by policy."""
    path = ROOT / relative
    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        raise ValueError(busy + '; inspect ' + relative + ' before continuing')
    with os.fdopen(descriptor, 'w', encoding='utf-8') as handle:
        json.dump(owner, handle)
    return path


def release_lock(path, run_id):
    try:
        if json.loads(path.read_text(encoding='utf-8')).get('run_id') == run_id:
            path.unlink()
    except (OSError, ValueError, AttributeError):
        pass  # A lock that cannot be confirmed as ours stays and blocks the next job for inspection.


def stream_events(stdout):
    events = [json.loads(line) for line in stdout.splitlines() if line.strip()]
    results = [event for event in events if isinstance(event, dict) and event.get('type') == 'result']
    if len(results) != 1 or results[0] is not events[-1]:
        raise ValueError('Claude stream did not end with exactly one result event')
    return results[0], events


def option_keys(value):
    if isinstance(value, dict):
        return set(value).union(*map(option_keys, value.values()))
    if isinstance(value, list):
        return set().union(*map(option_keys, value))
    return set()


def tool_records(events):
    """Tool calls and their error flags exactly as the CLI streamed them."""
    calls, failed = [], {}
    for event in events:
        message = event.get('message') if isinstance(event, dict) else None
        content = message.get('content') if isinstance(message, dict) else None
        for block in content if isinstance(content, list) else ():
            if not isinstance(block, dict):
                continue
            if block.get('type') == 'tool_use':
                calls.append(block)
            elif block.get('type') == 'tool_result':
                failed[block.get('tool_use_id')] = block.get('is_error') is True
    return calls, failed


def shell_evidence(commands, calls, failed):
    """Record which named tests actually ran; a non-test shell command that succeeded is a violation."""
    runs = [{'command': str(call['input'].get('command')).strip(), 'is_error': failed.get(call.get('id'))}
            for call in calls if call.get('name') == 'Bash' and isinstance(call.get('input'), dict)]
    escaped = [run for run in runs if run['command'] not in commands and run['is_error'] is False]
    return ({'runs': runs, 'not_run': [command for command in commands
                                       if command not in {run['command'] for run in runs}]},
            'A shell command outside the named tests ran; review the local checkpoint' if escaped else None)


def mcp_evidence(profile, events, payload, calls, failed):
    """Summarize the CLI's own MCP stream records; return (observed, connected, violation)."""
    init = next((event for event in events if isinstance(event, dict)
                 and event.get('type') == 'system' and event.get('subtype') == 'init'), None)
    if init is None:
        return {}, False, 'Claude stream did not report MCP initialization'
    servers = {item.get('name'): item.get('status') for item in init.get('mcp_servers') or []
               if isinstance(item, dict)}
    exposed = sorted(name for name in init.get('tools') or []
                     if isinstance(name, str) and name.startswith('mcp__'))
    calls = [call for call in calls if str(call.get('name')).startswith('mcp__')]
    source = init.get('apiKeySource')
    observed = {'servers': servers, 'exposed_tools': exposed,
                'exposed_not_allowed': sorted(set(exposed) - set(profile['allow'])),
                'calls': [{'tool': call.get('name'), 'is_error': failed.get(call.get('id'))} for call in calls],
                'permission_denials': sorted({str(item.get('tool_name')) for item in
                                              payload.get('permission_denials') or [] if isinstance(item, dict)}),
                'api_key_source': source if isinstance(source, str) else None}
    # Transport status and a non-error allowed call are CLI observations, not Editor/screen effects.
    observed['transport_connected'] = (bool(servers) and set(servers) == set(profile['servers'])
                                       and all(status == 'connected' for status in servers.values()))
    connected = observed['transport_connected'] and any(
        call.get('name') in profile['allow'] and failed.get(call.get('id')) is False for call in calls)
    prefixes = tuple('mcp__' + name + '__' for name in profile['servers'])
    violation = None
    if source not in (None, 'none'):
        violation = 'Claude reported an API key source; review billing before accepting'
    elif set(servers) - set(profile['servers']) or not all(name.startswith(prefixes) for name in exposed):
        violation = 'Claude loaded an MCP server or tool outside the fixed profile'
    elif set(exposed) & set(profile['deny']):
        violation = 'A forbidden MCP tool reached the model context'
    elif observed['exposed_not_allowed']:
        violation = ('MCP tools outside the fixed allowlist reached the model context; deny or assign them: '
                     + ', '.join(observed['exposed_not_allowed']))
    elif any(option_keys(call.get('input')) & set(profile['ai_options']) for call in calls):
        violation = 'A desktop call requested AI analysis; review provider billing before accepting'
    return observed, connected, violation


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('task', type=Path)
    parser.add_argument('--worktree', required=True, type=Path)
    parser.add_argument('--effort', choices=('high', 'xhigh', 'max'), default='high')
    parser.add_argument('--billing-evidence', type=Path,
                        default=ROOT / 'ops/ai/local/runtime-billing-evidence.json')
    parser.add_argument('--resume', type=UUID)
    parser.add_argument('--allow-edit', action='append', default=[], metavar='RELATIVE_FILE')
    parser.add_argument('--mcp-profile', choices=tuple(MCP_PROFILES), default='none',
                        help='fixed MCP profile; URLs, commands and user or cloud servers are not accepted')
    parser.add_argument('--allow-test', action='append', default=[], choices=tuple(TEST_COMMANDS),
                        help='named local check Claude may run exactly; repeat per check')
    args = parser.parse_args(argv)
    task_id = args.task.stem
    if args.task.suffix != '.md' or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]*', task_id):
        parser.error('Task must be a Markdown file with a simple task ID filename')
    profile = MCP_PROFILES[args.mcp_profile]
    tests = list(dict.fromkeys(TEST_COMMANDS[name] for name in args.allow_test))
    streamed = bool(profile['servers'] or tests)
    now = datetime.now(timezone.utc)
    run_id = task_id + '-' + now.strftime('%Y%m%dT%H%M%S%fZ')
    local = ROOT / 'ops/ai/local'
    local.mkdir(parents=True, exist_ok=True)
    result_path = ROOT / 'ops/ai/tasks' / (task_id + '.result.json')
    result = {'task_id': task_id, 'run_id': run_id, 'status': 'BLOCKED',
              'requestedEffort': args.effort, 'requestedModel': MODEL, 'mcp_profile': args.mcp_profile,
              'process_exit': None, 'changes_verified': False, 'raw_output': 'ops/ai/local/' + run_id + '*'}
    locks = []
    try:
        if os.environ.get('CLAUDECODE') or os.environ.get('MEMORY_FORGE_CLAUDE_JOB'):
            raise ValueError('Recursive Claude jobs are forbidden')
        result['billing_verified_at'], expected_account = billing_evidence(args.billing_evidence, now)
        result['billing_evidence_kind'] = 'account UI attestation; not server enforcement'
        result['access_controls'] = ('Restricted CLI built-in file tools'
                                     + (' plus fixed MCP tool names; MCP actions reach the Unity Editor '
                                        'project or macOS GUI outside the worktree' if profile['servers'] else '')
                                     + (' plus exact named test commands that run repository code as the user'
                                        if tests else '')
                                     + '; not an OS sandbox; managed policy still applies')
        worktree = args.worktree.resolve()
        result.update(worktree_metadata(worktree), worktree=str(worktree))
        scope = edit_scope(worktree, args.allow_edit)
        result['allowed_files'] = scope
        owner = {'run_id': run_id, 'task_id': task_id, 'profile': args.mcp_profile, 'pid': os.getpid()}
        # Independent jobs may run in parallel, but never two in the same worktree.
        locks.append(acquire_lock(worktree_lock(worktree), owner, 'Another job is using this worktree'))
        env = child_environment(os.environ, args.effort, args.mcp_profile)
        settings = ['--restricted', '--setting-sources', '', '--settings', str(settings_file())]
        flags = [*settings, '--mcp-config', EMPTY_MCP, '--strict-mcp-config']
        if tests:
            result['tests'] = {'allowed': tests, 'runs': [], 'not_run': tests}
        if profile['servers']:
            result['mcp'] = {'servers': sorted(profile['servers']),
                             'allowed_tools': [*profile['tools'], *profile['allow']],
                             'denied_tools': list(profile['deny']),
                             'exclusive_lock': CONTROL_LOCK if profile['exclusive'] else None,
                             'connection_observed': False}
            if profile['exclusive']:
                locks.append(acquire_lock(CONTROL_LOCK, owner,
                                          'Another job holds the single Unity Editor/desktop control lock'))
            result['mcp']['preflight'] = mcp_preflight(profile['servers'], env)
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
        rules = ('\nMCP profile: ' + args.mcp_profile + '. Allowed MCP tools: ' + json.dumps(list(profile['allow']))
                 + '. ' + profile['rules']) if profile['servers'] else ''
        if tests:
            rules += ('\nAllowed test commands (run each exactly as written from the worktree root, without '
                      'pipes or extra arguments; report each as PASS, FAIL or NOT_RUN): ' + json.dumps(tests))
        prompt = ((ROOT / 'docs/START_CLAUDE.md').read_text(encoding='utf-8')
                  + '\n\nTask ID: ' + task_id + '\nRequested effort: ' + args.effort
                  + '\nAllowed edit files: ' + json.dumps(scope) + rules
                  + '\nStay within this task. Do not call agents or read credentials.\n\n'
                  + args.task.read_text(encoding='utf-8'))
        (local / (run_id + '.request.txt')).write_text(prompt, encoding='utf-8')
        tools = ','.join(['Read', 'Glob', 'Grep', *(['Edit'] if scope else []), *(['Bash'] if tests else []),
                          *profile['tools']])
        allowed = ','.join(['Read(./**)', 'Glob', 'Grep', *('Edit(./' + path + ')' for path in scope),
                            *('Bash(' + test + ')' for test in tests), *profile['tools'], *profile['allow']])
        denied = PRIVATE_READS + (BASH_DENY if tests else ()) + profile['deny']
        servers = (json.dumps({'mcpServers': profile['servers']}, separators=(',', ':'))
                   if profile['servers'] else EMPTY_MCP)
        # MCP and test jobs stream the CLI's own tool records so evidence is not a model claim.
        output = ['stream-json', '--verbose'] if streamed else ['json']
        command = [*settings, '--mcp-config', servers, '--strict-mcp-config', '--model', MODEL,
                   '--effort', args.effort, '--permission-mode', 'dontAsk', '--tools', tools,
                   '--allowedTools', allowed, '--disallowedTools', ','.join(denied),
                   '--disable-slash-commands', '--output-format', *output]
        if args.resume:
            command.extend(['--resume', str(args.resume)])
        result['status'] = 'FAILED'
        proc = invoke([*command, '-p'], worktree, env, local / run_id, TIMEOUT, prompt)
        result['process_exit'] = proc.returncode
        payload, events = stream_events(proc.stdout) if streamed else (json.loads(proc.stdout), None)
        if not isinstance(payload, dict):
            raise ValueError('Claude returned a non-object result')
        result.update({key: payload.get(key) for key in ('session_id', 'subtype', 'is_error', 'result')})
        usage = payload.get('modelUsage')
        result['model_usage_keys'] = list(usage) if isinstance(usage, dict) else []
        if events is not None:
            calls, failed = tool_records(events)
            violation = None
            if tests:
                runs, violation = shell_evidence(tests, calls, failed)
                result['tests'].update(runs)
            if profile['servers']:
                observed, connected, mcp_violation = mcp_evidence(profile, events, payload, calls, failed)
                result['mcp'].update(observed=observed, connection_observed=connected)
                violation = mcp_violation or violation
            if violation:
                raise ValueError(violation)
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
    finally:
        for lock in reversed(locks):
            release_lock(lock, run_id)
    result_path.parent.mkdir(parents=True, exist_ok=True)
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(result['status'] + ': ' + str(result_path))
    return 0 if result['status'] == 'RECEIVED_UNVERIFIED' else 1


if __name__ == '__main__':
    sys.exit(main())
