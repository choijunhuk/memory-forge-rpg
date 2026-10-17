#!/usr/bin/env python3
"""Claude root-session audit (docs/AI_WORKFLOW.md §17).

--start-check   checks the live root environment and prints one ROOT_START_CHECK line (names/types only, never values).
(default)       audits the session transcript JSONL (plus its subagents) against ops/ai/runtime-policy.json.

Bounded retrospective evidence against accidental policy breaks, not launch enforcement and not proof against a session
that sets out to evade it. Shell input is parsed with shlex: quoted-delimiter heredoc bodies are literal data; unquoted
heredoc bodies and quoted words are searched for $(...)/`...` substitutions; `sh|bash|zsh -c`, `eval` and shell heredocs are
scanned recursively. Interpreter code (python -c, python heredocs, node -e) is not analysed semantically. An input it cannot
parse that names a sensitive program blocks. Reads only. Exit 1 = a blocking violation or missing evidence; warnings never
block; --waive records an explained exception in the report.
"""
import argparse, json, os, re, shlex, subprocess, sys
from pathlib import Path
from urllib.parse import urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_claude as R  # noqa: E402  (shared pins, lock path, settings check and secret name parts)

ROOT = R.ROOT
MARK = 'ROOT_START_CHECK '
UNITY_APP = 'com.unity3d.UnityEditor5.x'
PROVIDERS = ('ANTHROPIC', 'OPENAI', 'CODEX', 'AWS', 'AMAZON', 'GOOGLE', 'GCLOUD', 'GEMINI', 'AZURE', 'VERTEX')
CREDENTIAL_PARTS = R.SECRET_PARTS + ('ACCESS_KEY',)
URL_OVERRIDES = ('OPENAI_BASE_URL', 'OPENAI_API_BASE', 'CODEX_BASE_URL')
REQUIRED_CHECKS = ('auth', 'provider_env_clean', 'project_settings_pinned', 'root_denied_tools_enforced', 'codex_chatgpt_login')
CREDENTIAL_PATH = re.compile(r'\.codex/auth\.json|\.claude/\.credentials|/keychains/', re.I)
SENSITIVE = re.compile(r'\bcodex\b|\bgit\b[^\n]*\bpush\b|\bprintenv\b|\bsecurity\s|--bare|auth\.json|\.credentials|acquire_lock|release_lock'
                       r'|\benv\s*($|[|;&])', re.M)
SETTINGS_WRITE = re.compile(r'(>>?|\btee\b|\bsed\s+-i\b|\bmv\b|\bcp\b|\brm\b|\bln\b)[^\n;|&]*(\.claude/settings|\.claude/\.credentials|\.codex/)'
                            r'|\bclaude\s+(auth\s+(login|logout)|login|logout|setup-token)\b'
                            r'|\bcodex\s+(login(?!\s+status)|logout)\b'
                            r'|\b(export|unset)\s+(\w*(KEY|TOKEN|SECRET|CREDENTIAL|BASE_URL)\w*|CLAUDE_CODE_USE_\w+|ANTHROPIC_\w+|OPENAI_\w+|CODEX_\w+)')
TARGET_KEYS = {'file_path', 'path', 'paths', 'notebook_path', 'uri', 'file', 'filename', 'source', 'destination'}
UNITY_WRITE_ALWAYS = {'execute_code', 'batch_execute', 'execute_custom_tool', 'execute_menu_item'}
READ_ACTION = re.compile(r'^(get|list|read|find|search|telemetry)')
GUI_INPUT = {'app_click', 'app_type', 'app_key', 'app_drag'}
GUI_READ = {'screenshot', 'zoom', 'scroll', 'wait', 'cursor_position', 'mouse_move'}
WRITE_TOOLS = {'Write', 'Edit', 'MultiEdit', 'NotebookEdit'}
SHELLS = {'sh', 'bash', 'zsh', 'dash'}
WRAPPERS = {'command': set(), 'exec': {'-a'}, 'nohup': set(), 'time': {'-f', '-o'}, 'nice': {'-n'}, 'timeout': {'-s', '-k'},
            'sudo': {'-u', '-g', '-h', '-p', '-C', '-D', '-R', '-T', '-U'}, 'xargs': {'-n', '-I', '-L', '-P', '-s', '-d', '-E', '-a'}}
# Tools whose shell input is parsed like Bash; tools that run code the audit cannot read are blocking evidence gaps.
SHELL_TOOLS = {'Bash': 'command', 'mcp__plugin_desktop-commander_desktop-commander__start_process': 'command',
               'mcp__plugin_serena_serena__execute_shell_command': 'command', 'mcp__terminal__run_in_terminal': 'command'}
OPAQUE_EXECUTION = {'mcp__plugin_desktop-commander_desktop-commander__interact_with_process',
                    'mcp__plugin_playwright_playwright__browser_run_code_unsafe', 'mcp__plugin_oh-my-claudecode_t__python_repl'}
CODEX_VALUE_OPTIONS = {'-c': 'config', '--config': 'config', '-m': 'model', '--model': 'model', '-s': 'sandbox', '--sandbox': 'sandbox',
                       '-a': 'approval', '--ask-for-approval': 'approval', '-p': 'profile', '--profile': 'profile', '-C': 'cd',
                       '--cd': 'cd', '-i': 'image', '--image': 'image', '-o': 'output', '--output-last-message': 'output'}
GIT_VALUE_OPTIONS = {'-C', '-c', '--git-dir', '--work-tree', '--namespace', '--exec-path', '--config-env'}
ASSIGNMENT = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*=')
SEPARATOR = re.compile(r'^[;&|()\n]+$')
LOCK_PREFIX = r"\s*(import sys;\s*sys\.path\.insert\(0,\s*'tools/ai'\);\s*)?import run_claude as r;\s*"


def policy():
    return json.loads((ROOT / 'ops/ai/runtime-policy.json').read_text(encoding='utf-8'))


def start_check(env=None, run=subprocess.run, pol=None):
    env = os.environ if env is None else env
    route, c = (pol or policy())['work_routing'], {}
    try:
        out = run(['claude', 'auth', 'status', '--json'], capture_output=True, text=True, timeout=30)
        a = json.loads(out.stdout)
        c['auth'] = (getattr(out, 'returncode', 0) == 0 and isinstance(a, dict) and a.get('loggedIn') is True
                     and a.get('authMethod') == 'claude.ai' and a.get('apiProvider') == 'firstParty')
    except (OSError, ValueError, AttributeError, TypeError, subprocess.TimeoutExpired):
        c['auth'] = False
    bad = [k for k in env if k.upper().startswith(PROVIDERS) and any(p in k.upper() for p in CREDENTIAL_PARTS)]
    bad += [k for k in ('CLAUDE_CODE_USE_BEDROCK', 'CLAUDE_CODE_USE_VERTEX', 'CLAUDE_CODE_USE_FOUNDRY', *URL_OVERRIDES) if env.get(k)]
    if env.get('ANTHROPIC_BASE_URL'):
        try:
            host = urlsplit(env['ANTHROPIC_BASE_URL']).hostname or ''
        except ValueError:
            host = ''
        if not (host == 'anthropic.com' or host.endswith('.anthropic.com')):
            bad.append('ANTHROPIC_BASE_URL')
    c['provider_env_clean'], c['provider_env_names'] = not bad, sorted(set(bad))
    try:
        R.settings_file(); c['project_settings_pinned'] = True
    except (OSError, ValueError):
        c['project_settings_pinned'] = False
    try:
        deny = json.loads((ROOT / '.claude/settings.local.json').read_text(encoding='utf-8'))['permissions']['deny']
    except (OSError, ValueError, KeyError, TypeError):
        deny = []
    required = set(route['root_denied_tools']) | set(route['root_denied_permissions'])  # a missing key raises: fail closed
    c['root_denied_tools_enforced'] = isinstance(deny, list) and required <= {d for d in deny if isinstance(d, str)}
    try:
        out = run(['codex', 'login', 'status'], capture_output=True, text=True, timeout=30)
        said = (out.stdout or '') + '\n' + (out.stderr or '')
        c['codex_chatgpt_login'] = getattr(out, 'returncode', 0) == 0 and bool(re.search(r'^Logged in using ChatGPT\b', said, re.M))
    except (OSError, AttributeError, TypeError, subprocess.TimeoutExpired):
        c['codex_chatgpt_login'] = False
    c['proxy_env_names'] = sorted(k for k in env if 'PROXY' in k.upper())  # recorded, not blocking
    return {'pass': all(c[k] is True for k in REQUIRED_CHECKS), 'checks': c}


def heredocs(cmd):
    """(command text without heredoc bodies, [(delimiter_quoted, marker_line, body)])."""
    out, bodies, pos = [], [], 0
    for m in re.finditer(r"(?<!<)<<(?!<)-?\s*(['\"]?)(\w+)\1", cmd):
        if m.start() < pos:
            continue
        eol = cmd.find('\n', m.end())
        end = re.compile(r'^[ \t]*' + re.escape(m.group(2)) + r'[ \t]*$', re.M).search(cmd, eol + 1) if eol >= 0 else None
        if not end:
            break
        line_start = cmd.rfind('\n', 0, m.start()) + 1
        bodies.append((bool(m.group(1)), cmd[line_start:eol], cmd[eol + 1:end.start()]))
        out.append(cmd[pos:eol]); pos = end.end()
    return ''.join(out) + cmd[pos:], bodies


def substitutions(text):
    """Inner commands of $(...) (balanced) and `...` that the shell would run: outside single quotes, honouring backslashes."""
    found, i, quote = [], 0, None
    while i < len(text):
        ch = text[i]
        if ch == '\\' and quote != "'":
            i += 2; continue
        if quote == "'":
            quote = None if ch == "'" else quote
        elif ch in '\'"' and quote is None:
            quote = ch
        elif ch == '"' and quote == '"':
            quote = None
        elif ch == '`':
            end = text.find('`', i + 1)
            end = len(text) if end < 0 else end
            found.append(text[i + 1:end]); i = end
        elif text.startswith('$(', i):
            depth, j = 1, i + 2
            while j < len(text) and depth:
                depth += {'(': 1, ')': -1}.get(text[j], 0); j += 1
            found.append(text[i + 2:j - 1]); i = j - 1
        i += 1
    return found


def simple_commands(text):
    """[(words, separator_after)] of shell text, or None when shlex cannot split it."""
    try:
        lex = shlex.shlex(text, posix=True, punctuation_chars=';&|()\n')
        lex.whitespace, lex.whitespace_split = ' \t\r', True
        tokens = list(lex)
    except ValueError:
        return None
    result, words = [], []
    for token in tokens + [';']:
        if SEPARATOR.match(token):
            if words:
                result.append((words, token))
            words = []
        else:
            words.append(token)
    return result


def program(words):
    """Strip VAR=value, env [options] VAR=value and wrapper prefixes: (program words, had_env_prefix, via_command)."""
    env = command = False
    i = 0
    while i < len(words):
        word = words[i]
        if ASSIGNMENT.match(word):
            env = True
        elif word == 'env' and i + 1 < len(words):  # env with anything after it runs a program in a changed environment
            env = True
            while i + 1 < len(words) and (words[i + 1].startswith('-') or ASSIGNMENT.match(words[i + 1])):
                i += 2 if words[i + 1] in ('-u', '--unset', '-C', '--chdir', '-S', '--split-string') else 1
        elif word in WRAPPERS:
            command = command or word == 'command'
            while i + 1 < len(words) and words[i + 1].startswith('-'):
                i += 2 if words[i + 1] in WRAPPERS[word] else 1
            if word == 'timeout' and i + 1 < len(words):
                i += 1  # its duration operand
        else:
            break
        i += 1
    return words[i:], env, command


def codex_args(words):
    values, flags, positional, i = {}, set(), [], 0
    while i < len(words):
        word = words[i]
        if word == '--':
            positional += words[i + 1:]; break
        key, value = word.split('=', 1) if word.startswith('--') and '=' in word else (word, None)
        if key in CODEX_VALUE_OPTIONS:
            if value is None:
                i += 1
                value = words[i] if i < len(words) else ''
            values.setdefault(CODEX_VALUE_OPTIONS[key], []).append(value)
        elif word.startswith('-'):
            flags.add(word)
        else:
            positional.append(word)
        i += 1
    return values, flags, positional


def git_command(args):
    i = 0
    while i < len(args) and args[i].startswith('-'):
        i += 2 if args[i] in GIT_VALUE_OPTIONS else 1
    return args[i:i + 1], args[i + 1:]


def lock_code(code, owner):
    o = re.escape(owner)
    if re.fullmatch(LOCK_PREFIX + r"r\.acquire_lock\(\s*r\.CONTROL_LOCK\s*,\s*\{\s*'run_id'\s*:\s*'" + o + r"'\s*\}\s*,\s*'Editor busy'\s*\)\s*;?\s*", code):
        return 'acquire'
    if re.fullmatch(LOCK_PREFIX + r"r\.release_lock\(\s*r\.ROOT\s*/\s*r\.CONTROL_LOCK\s*,\s*'" + o + r"'\s*\)\s*;?\s*", code):
        return 'release'
    return 'other' if re.search(r'acquire_lock|release_lock|CONTROL_LOCK', code) else None


def scan(cmd, codex_model, owner, depth=0):
    """Findings and events of shell input. Events (acquire, release, start) count only for a single plain command."""
    seen = {'findings': [], 'codex': 0, 'protected': False, 'acquire': False, 'release': False, 'start': False,
            'settings': bool(SETTINGS_WRITE.search(heredocs(cmd)[0]))}

    def merge(inner):
        for key in ('findings', 'codex'):
            seen[key] += inner[key]
        seen['protected'] = seen['protected'] or inner['protected']
        seen['settings'] = seen['settings'] or inner['settings']

    if depth > 4:
        seen['findings'].append(('unparsed_command', 'nesting too deep: ' + cmd[:100])); return seen
    text, bodies = heredocs(cmd)
    for word in substitutions(text):
        merge(scan(word, codex_model, owner, depth + 1))
    for quoted, marker, body in bodies:
        if re.search(r'(^|[;&|(]\s*)(\S*/)?(ba|z|da)?sh\b[^\n]*<<', marker):
            merge(scan(body, codex_model, owner, depth + 1))
        elif not quoted:
            for inner in substitutions(body):
                merge(scan(inner, codex_model, owner, depth + 1))
    parsed = simple_commands(text)
    if parsed is None:
        if SENSITIVE.search(text):
            seen['findings'].append(('unparsed_command', cmd[:120]))
        return seen
    for n, (words, sep) in enumerate(parsed):
        prog, env, via_command = program(words)
        if not prog:
            continue
        name, args = prog[0].rsplit('/', 1)[-1], prog[1:]
        after = parsed[n + 1][0] if sep == '|' and n + 1 < len(parsed) else []
        names_only = after[:1] == ['cut'] and ('-d=' in after or '-d' in after and '=' in after) and (
            '-f1' in after or '-f' in after and '1' in after)
        text_of_command = ' '.join(prog)[:160]
        found = lambda kind: seen['findings'].append((kind, text_of_command))
        flag = next((k for k, w in enumerate(args) if re.fullmatch(r'-[A-Za-z]*c[A-Za-z]*', w)), None) if name in SHELLS else None
        if flag is not None:  # sh -c, bash -lc, zsh -ic ...
            merge(scan(args[flag + 1] if flag + 1 < len(args) else '', codex_model, owner, depth + 1))
        if name == 'eval':
            merge(scan(' '.join(args), codex_model, owner, depth + 1))
        if name == 'codex':
            values, flags, positional = codex_args(args)
            if flags & {'--help', '-h', '--version', '-V'} or positional[:1] == ['login']:
                continue
            if positional[:1] != ['exec']:
                found('codex_call'); continue
            seen['codex'] += 1; seen['protected'] = True
            configs = values.get('config', [])
            # Reviews are read-only; image work may write only inside a folder under art/ given with -C (AI_WORKFLOW §17).
            cd = values.get('cd', [])
            art_write = values.get('sandbox') == ['workspace-write'] and len(cd) == 1 and bool(re.match(r'(.*/)?art/[^.][^\s]*$', cd[0])) \
                and '..' not in cd[0].split('/')
            if not (via_command and not env and (values.get('sandbox') == ['read-only'] or art_write) and values.get('model') == [codex_model]
                    and configs and all(c.startswith('model_reasoning_effort=') for c in configs) and 'profile' not in values
                    and not flags & {'--dangerously-bypass-approvals-and-sandbox', '--full-auto'}):
                found('codex_call')
        if any(w.endswith('codex-companion.mjs') for w in prog) and '--write' in prog:
            found('codex_write')
        if name == 'git':
            sub, rest = git_command(args)
            seen['protected'] = seen['protected'] or sub in (['commit'], ['push'])
            refs = [w for w in rest if not w.startswith('-')][1:]
            force = any(w in ('-f', '--force', '--force-with-lease') or w.startswith(('--force-with-lease=', '--force-if-includes'))
                        for w in rest) or any(r.startswith('+') for r in refs)
            if sub == ['push'] and force and (not refs or any(re.search(r'(^|[:/+])(main|master)$', r) for r in refs)):
                found('force_push_main')
        if name == 'claude' and '--bare' in args:
            found('claude_bare')
        if name == 'security' and args and re.match(r'find-(generic|internet)-password|dump-keychain', args[0]):
            found('secret_read')
        if name not in ('echo', 'printf') and any(CREDENTIAL_PATH.search(os.path.normpath(w)) for w in prog if w):
            found('secret_read')
        if name in ('env', 'printenv'):
            operands = [w for w in args if not w.startswith('-')]
            if name == 'printenv' and operands:
                found('env_value_print')
            elif not operands and not names_only:
                found('env_dump')
        if name in ('echo', 'printf') and any(re.search(r'\$\{?[A-Za-z0-9_]*(' + '|'.join(CREDENTIAL_PARTS) + ')', w) for w in args):
            found('secret_echo')
        if name.startswith('python'):
            # Lock and start-check events count only for one plain top-level command whose code is exactly the documented form.
            plain = len(parsed) == 1 and depth == 0 and not env
            kind = lock_code(args[1] if args[:1] == ['-c'] and len(args) == 2 else ' '.join(args), owner)
            if kind == 'other' or kind and not (plain and args[:1] == ['-c'] and len(args) == 2):
                found('ambiguous_lock_command')
            elif kind:
                seen[kind] = True
            seen['start'] = seen['start'] or plain and len(args) == 2 and args[1] == '--start-check' and \
                args[0].replace('\\', '/').endswith('tools/ai/audit_root_session.py')
    return seen


def text_of(content):
    """Text of a tool_result content, or None when its shape is not a string or a list of text blocks."""
    if content is None or isinstance(content, str):
        return content or ''
    if isinstance(content, list) and all(isinstance(c, dict) and isinstance(c.get('text', ''), str) for c in content):
        return '\n'.join(c.get('text', '') for c in content)
    return None


def unity_read(tool, args, read_tools):
    action = str(args.get('action') or '')
    if tool in UNITY_WRITE_ALWAYS:
        return False
    if tool == 'read_console':
        return action != 'clear'
    return tool in read_tools or bool(READ_ACTION.match(action))


def passing_start(text, failed):
    for line in text.splitlines():
        if line.startswith(MARK) and not failed:
            try:
                result = json.loads(line[len(MARK):])
            except ValueError:
                return None
            checks = result.get('checks') if isinstance(result, dict) else None
            ok = isinstance(checks, dict) and result.get('pass') is True and all(checks.get(k) is True for k in REQUIRED_CHECKS)
            return result if ok else None
    return None


def strings(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for v in value.values():
            yield from strings(v)
    elif isinstance(value, list):
        for v in value:
            yield from strings(v)


def audit(path, waivers=None, pol=None):
    pol, waivers = pol or policy(), waivers or {}
    route, model, codex_model = pol['work_routing'], pol['models']['claude'], pol['models']['codex']
    read_tools = set(pol['claude_mcp_profiles']['profiles']['unity-read']['allowed_tools'])
    denied, servers = set(route['root_denied_tools']), set(route['root_allowed_mcp_servers'])
    owner = 'claude-root-' + path.stem
    report = {'transcript': str(path), 'session_id': path.stem, 'models': {}, 'permission_modes': [], 'mcp_servers_used': [],
              'codex_calls': 0, 'lock_events': [], 'start_check': None, 'start_check_current': False, 'violations': [], 'waived': [],
              'warnings': [], 'missing_evidence': [],
              'unverified': ['initial tool list', 'MCP subprocess environments', 'live inference environment versus the checked shell environment',
                             'interpreter code semantics (python -c, python heredocs, node -e)', 'Usage credits/Extra usage OFF (account UI)']}

    def bad(kind, ident, detail):
        report['violations'].append({'kind': kind, 'id': ident, 'detail': detail})

    for f in [path, *sorted((path.parent / path.stem / 'subagents').glob('*.jsonl'))]:
        main, where = f == path, f.name
        held, start_ok, generation, exposed, acquiring, releasing, starting = False, False, 0, set(), set(), set(), {}
        try:
            lines = f.read_text(encoding='utf-8').splitlines()
        except (OSError, UnicodeDecodeError) as error:
            bad('unreadable_transcript', None, where + ': ' + error.__class__.__name__); continue
        for number, line in enumerate(lines, 1):
            try:
                o = json.loads(line)
            except ValueError:
                o = 'unreadable'
            if not isinstance(o, dict):
                bad('unreadable_transcript', None, '%s:%d' % (where, number)); continue
            kind, message, attachment = o.get('type'), o.get('message'), o.get('attachment')
            message = message if isinstance(message, dict) else {}
            mode = o.get('permissionMode')
            if isinstance(mode, str) and mode not in report['permission_modes']:
                report['permission_modes'].append(mode)
            a = attachment if isinstance(attachment, dict) else {}
            if kind == 'attachment' and a.get('type') == 'deferred_tools_delta':
                added, removed = a.get('addedNames', []), a.get('removedNames', [])
                if not isinstance(added, list) or not isinstance(removed, list):
                    bad('malformed_record', None, 'deferred_tools_delta in %s:%d' % (where, number)); continue
                added = {n for n in added if isinstance(n, str)} - exposed
                for name in sorted(added & denied):
                    bad('denied_tool_exposed', None, name)
                exposed = (exposed | added) - {n for n in removed if isinstance(n, str)}
                for server in sorted({n.split('__')[1] for n in added if n.startswith('mcp__') and n.count('__') >= 2} - servers):
                    report['warnings'].append({'kind': 'exposed_server_not_allowed', 'detail': server})
            content = message.get('content')
            if kind == 'assistant' and not isinstance(content, list) or kind == 'user' and not isinstance(content, (list, str, type(None))):
                bad('malformed_record', None, '%s message content in %s:%d' % (kind, where, number)); continue
            content = content if isinstance(content, list) else []
            if kind == 'user':
                for c in content:
                    if not isinstance(c, dict) or c.get('type') != 'tool_result':
                        continue
                    ident, text, failed = c.get('tool_use_id'), text_of(c.get('content')), bool(c.get('is_error'))
                    if not isinstance(ident, str):
                        bad('malformed_record', None, 'tool_result id in %s:%d' % (where, number)); continue
                    if text is None:
                        bad('malformed_record', ident, 'tool_result content in ' + where); text, failed = '', True
                    if ident in acquiring:
                        acquiring.discard(ident)
                        held = held or not failed and not re.search(r'busy|Error|Traceback', text)
                        report['lock_events'].append({'id': ident, 'event': 'acquired' if held else 'acquire_failed'})
                    if ident in releasing:
                        releasing.discard(ident)
                        held = False
                        report['lock_events'].append({'id': ident, 'event': 'released'})
                    if ident in starting:
                        result = passing_start(text, failed) if starting.pop(ident) == generation else None
                        start_ok = result is not None
                        report['start_check'] = result or report['start_check']
                continue
            if kind != 'assistant':
                continue
            m = message.get('model')
            report['models'][str(m)] = report['models'].get(str(m), 0) + 1
            if m not in (model, '<synthetic>'):
                bad('model', None, '%s in %s' % (m, where))
            for c in content:
                if not isinstance(c, dict) or c.get('type') != 'tool_use':
                    continue
                name, ident, args = str(c.get('name', '')), c.get('id'), c.get('input')
                if not isinstance(args, dict) or not isinstance(ident, str):
                    bad('malformed_record', ident if isinstance(ident, str) else None, name + ' tool_use in ' + where); continue
                server = name.split('__')[1] if name.startswith('mcp__') and name.count('__') >= 2 else (
                    args.get('server') if name.endswith('McpResourceTool') else None)
                if server:
                    if server not in report['mcp_servers_used']:
                        report['mcp_servers_used'].append(server)
                    if server not in servers:
                        bad('mcp_server_not_allowed', ident, str(server))
                if name in denied:
                    bad('denied_tool', ident, name)
                if name == 'Agent' and args.get('model') not in (None, 'opus'):
                    bad('agent_model', ident, str(args.get('model')))
                if name in OPAQUE_EXECUTION:
                    bad('opaque_execution', ident, name)
                targets = [os.path.normpath(s) for k, v in args.items() if k in TARGET_KEYS for s in strings(v) if s]
                if name not in WRITE_TOOLS and name not in SHELL_TOOLS and any(CREDENTIAL_PATH.search(t) for t in targets):
                    bad('secret_read', ident, name)
                if name in WRITE_TOOLS and any(re.search(r'\.claude/settings|\.codex/', t) or CREDENTIAL_PATH.search(t) for t in targets):
                    start_ok, generation = False, generation + 1
                tool = name[len('mcp__computer-use__'):] if name.startswith('mcp__computer-use__') else ''
                actions = [x for x in args.get('actions') or [] if isinstance(x, dict)] if isinstance(args.get('actions'), list) else []
                editor_write = name.startswith('mcp__unityMCP__') and not unity_read(name[len('mcp__unityMCP__'):], args, read_tools)
                editor_write = editor_write or 'unity' in str(args.get('app', '')).lower() and (
                    tool in GUI_INPUT or tool == 'app_menu' and 'path' in args
                    or tool == 'app_batch' and any(x.get('action') not in GUI_READ for x in actions))
                if tool == 'computer_batch' and any(x.get('action') not in GUI_READ for x in actions):
                    bad('untargeted_gui_input', ident, 'full-screen input has no recorded target app')
                protected = editor_write
                if name in SHELL_TOOLS:
                    command = args.get(SHELL_TOOLS[name])
                    if not isinstance(command, str):
                        bad('malformed_record', ident, name + ' command in ' + where); continue
                    seen = scan(command, codex_model, owner)
                    for finding, detail in seen['findings']:
                        bad(finding, ident, detail)
                    report['codex_calls'] += seen['codex']
                    if seen['settings']:  # applied before the same input's protected work: order inside one input is unknown
                        start_ok, generation = False, generation + 1
                    protected = protected or seen['protected']
                    if main and seen['acquire']:
                        acquiring.add(ident)
                    if main and seen['release']:
                        if acquiring:  # overlapping acquire/release: ownership cannot be established
                            bad('ambiguous_lock_command', ident, 'release submitted while an acquisition was pending')
                            acquiring.clear()
                        held = False
                        releasing.add(ident)
                    if main and seen['start']:
                        starting[ident] = generation
                if editor_write and main and not held:
                    bad('unity_write_without_lock', ident, name)
                if protected and not main:
                    bad('protected_in_subagent', ident, name)
                elif protected and not start_ok:
                    bad('protected_before_start_check', ident, name)
        if main:
            report['start_check_current'] = start_ok
            if held:
                report['warnings'].append({'kind': 'lock_still_held', 'detail': R.CONTROL_LOCK})
    if not report['start_check_current']:
        report['missing_evidence'].append('a current passing ROOT_START_CHECK (audit_root_session.py --start-check) in the root transcript, '
                                          'after its last settings or auth change')
    for v in list(report['violations']):
        reason = (waivers.get(v['id']) if v['id'] else None) or waivers.get('%s:%s' % (v['kind'], v['detail']))
        if reason:
            report['violations'].remove(v)
            report['waived'].append(dict(v, reason=reason))
    report['pass'] = not report['violations'] and not report['missing_evidence']
    return report


def main(argv=None):
    p = argparse.ArgumentParser(description='Claude root-session audit (docs/AI_WORKFLOW.md §17)')
    p.add_argument('transcript', nargs='?', type=Path, help='session JSONL (default: this session via CLAUDE_CODE_SESSION_ID)')
    p.add_argument('--start-check', action='store_true')
    p.add_argument('--waive', action='append', default=[], metavar='ID=REASON',
                   help='TOOL_USE_ID=REASON or KIND:DETAIL=REASON; listed in the report, never silent')
    p.add_argument('--out', type=Path)
    args = p.parse_args(argv)
    if args.start_check:
        result = start_check()
        print(MARK + json.dumps(result, sort_keys=True))
        return 0 if result['pass'] else 1
    session = os.environ.get('CLAUDE_CODE_SESSION_ID')
    slug = re.sub(r'[^A-Za-z0-9]', '-', str(ROOT))
    path = args.transcript or (Path.home() / '.claude/projects' / slug / (session + '.jsonl') if session else None)
    if not path or not path.is_file():
        p.error('no session transcript; pass its path')
    waivers = dict(w.split('=', 1) for w in args.waive if '=' in w)
    report = audit(path, waivers)
    text = json.dumps(report, ensure_ascii=False, indent=2)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text + '\n', encoding='utf-8')
    print(text)
    return 0 if report['pass'] else 1


if __name__ == '__main__':
    sys.exit(main())
