import contextlib
from datetime import datetime, timedelta, timezone
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('ai_runner', Path(__file__).resolve().parents[1] / 'tools/ai/run_claude.py')
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class ClaudeRunner(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve() / 'root'
        self.worktree = Path(temporary.name).resolve() / 'claude'
        self.worktree.mkdir()
        for folder in ('docs', '.claude', 'ops/ai/local', 'ops/ai/tasks'):
            (self.root / folder).mkdir(parents=True, exist_ok=True)
        (self.root / 'docs/START_CLAUDE.md').write_text('Start instructions.', encoding='utf-8')
        self.settings = {'model': RUNNER.MODEL, 'effortLevel': 'high',
                         'modelSettings': {RUNNER.MODEL: {'effortLevel': 'high'}},
                         'forceLoginMethod': 'claudeai', 'availableModels': [RUNNER.MODEL],
                         'fallbackModel': [], 'switchModelsOnFlag': False}
        (self.root / '.claude/settings.json').write_text(json.dumps(self.settings), encoding='utf-8')
        self.task = self.root / 'ops/ai/tasks/REVIEW.md'
        self.task.write_text('Review only.', encoding='utf-8')
        self.evidence = {'verified_at': datetime.now(timezone.utc).isoformat(),
                         'claude_auth': 'claude.ai', 'verified_by': 'Codex account UI',
                         'usage_credits_enabled': False, 'auto_reload_enabled': False,
                         'claude_account_fingerprint': hashlib.sha256(b'claude.ai\x00private-account@example.invalid').hexdigest()}
        self.evidence_path = self.root / 'ops/ai/local/runtime-billing-evidence.json'
        self.write_evidence()
        self.auth = {'loggedIn': True, 'authMethod': 'claude.ai', 'apiProvider': 'firstParty',
                     'subscriptionType': 'max', 'email': 'private-account@example.invalid'}
        self.payload = {'session_id': '721938b1-0e03-4a94-a4a2-8750b8d1154a',
                        'subtype': 'success', 'is_error': False, 'result': 'Review received.',
                        'modelUsage': {RUNNER.MODEL: {'inputTokens': 12}}}
        self.calls = []
        self.inferences = []
        self.cli_exit = 0
        self.timeout = False
        self.stderr = ''
        self.raw_result = None
        self.root_patch = patch.object(RUNNER, 'ROOT', self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)
        self.environment_patch = patch.dict(os.environ, {'HOME': '/subscription-home', 'PATH': '/usr/bin'}, clear=True)
        self.environment_patch.start()
        self.addCleanup(self.environment_patch.stop)

    def write_evidence(self):
        self.evidence_path.write_text(json.dumps(self.evidence), encoding='utf-8')

    def process(self, command, **kwargs):
        self.calls.append((command, kwargs))
        if command[0] == 'git':
            directory = Path(command[2])
            args = command[3:]
            if args == ['rev-parse', '--show-toplevel']:
                value = str(directory)
            elif '--git-common-dir' in args:
                value = str(self.root / '.git')
            elif args == ['symbolic-ref', '--short', 'HEAD']:
                value = 'main' if directory == self.root else 'claude/review'
            elif args == ['rev-parse', 'HEAD']:
                value = 'a' * 40
            else:
                self.fail('Unexpected git command: ' + repr(args))
            return subprocess.CompletedProcess(command, 0, value + '\n', '')
        self.assertEqual(command[0], str(RUNNER.CLI))
        if '--version' in command:
            return subprocess.CompletedProcess(command, 0, '2.1.285 (Claude Code)\n', '')
        if 'auth' in command:
            return subprocess.CompletedProcess(command, 0, json.dumps(self.auth), '')
        self.assertIn('-p', command)
        self.inferences.append((command, kwargs))
        if self.timeout:
            raise subprocess.TimeoutExpired(command, 1800, output=b'partial checkpoint', stderr=b'timed out')
        output = self.raw_result if self.raw_result is not None else json.dumps(self.payload)
        return subprocess.CompletedProcess(command, self.cli_exit, output, self.stderr)

    def run_job(self, *options):
        with patch.object(RUNNER.subprocess, 'run', side_effect=self.process), contextlib.redirect_stdout(io.StringIO()) as output:
            code = RUNNER.main([str(self.task), '--worktree', str(self.worktree), *options])
        self.console = output.getvalue()
        self.result = json.loads((self.root / 'ops/ai/tasks/REVIEW.result.json').read_text())
        return code

    def test_unsupported_effort_model_and_resume_are_rejected_without_processes(self):
        for options in (('--effort', 'medium'), ('--model', 'other'), ('--resume', 'latest')):
            with self.subTest(options=options), patch.object(RUNNER.subprocess, 'run') as process:
                with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                    RUNNER.main([str(self.task), '--worktree', str(self.worktree), *options])
                process.assert_not_called()

    def test_missing_billing_attestation_blocks_before_any_process(self):
        self.evidence_path.unlink()
        self.assertEqual(self.run_job(), 1)
        self.assertEqual(self.calls, [])
        self.assertEqual(self.result['status'], 'BLOCKED')

    def test_unsafe_or_stale_attestation_blocks_before_any_process(self):
        now = datetime.now(timezone.utc)
        cases = ({'verified_at': (now - timedelta(hours=25)).isoformat()},
                 {'verified_at': (now + timedelta(hours=1)).isoformat()},
                 {'verified_at': now.replace(tzinfo=None).isoformat()},
                 {'claude_auth': 'api'}, {'verified_by': 'model self-report'},
                 {'usage_credits_enabled': True}, {'usage_credits_enabled': 'false'},
                 {'auto_reload_enabled': True}, {'auto_reload_enabled': None},
                 {'claude_account_fingerprint': None}, {'claude_account_fingerprint': 'invalid'})
        original = dict(self.evidence)
        for changes in cases:
            with self.subTest(changes=changes):
                self.evidence = {**original, **changes}
                self.write_evidence()
                self.assertEqual(self.run_job(), 1)
                self.assertEqual(self.calls, [])

    def test_api_cloud_proxy_and_model_environment_are_removed_without_output(self):
        source = {'ANTHROPIC_API_KEY': 'never-print-key', 'ANTHROPIC_AUTH_TOKEN': 'never-print-token',
                  'CLAUDE_CODE_USE_BEDROCK': '1', 'AWS_PROFILE': 'private-profile',
                  'GOOGLE_APPLICATION_CREDENTIALS': '/private/credentials', 'AZURE_OPENAI_ENDPOINT': 'never-print-azure',
                  'https_proxy': 'never-print-proxy', 'PROXY_URL': 'never-print-other-proxy',
                  'CUSTOM_MODEL_OVERRIDE': 'wrong-model',
                  'MAX_THINKING_TOKENS': '1137', 'DISABLE_INTERLEAVED_THINKING': '1',
                  'NODE_OPTIONS': 'unsafe-module', 'CLAUDE_CONFIG_DIR': '/alternate-auth'}
        with patch.dict(os.environ, source):
            self.assertEqual(self.run_job('--effort', 'max'), 0)
        cli_calls = [(cmd, kw) for cmd, kw in self.calls if cmd[0] == str(RUNNER.CLI)]
        self.assertEqual(len(cli_calls), 3)
        for _, kwargs in cli_calls:
            env = kwargs['env']
            self.assertFalse(set(source) & set(env))
            self.assertEqual(env['HOME'], '/subscription-home')
            self.assertEqual(env['CLAUDE_CODE_EFFORT_LEVEL'], 'max')
            self.assertEqual(env['CLAUDE_CODE_DISABLE_TERMINAL_TITLE'], '1')
        self.assertEqual(cli_calls[0][1]['env'], cli_calls[-1][1]['env'])
        public_result = json.dumps(self.result) + self.console
        for value in (*source.values(), self.auth['email']):
            if len(value) > 1:
                self.assertNotIn(value, public_result)

    def test_non_subscription_or_mixed_authentication_blocks_inference(self):
        original = dict(self.auth)
        for changes in ({'loggedIn': False}, {'authMethod': 'api_key'},
                        {'apiProvider': 'bedrock'}, {'subscriptionType': 'unknown'}):
            with self.subTest(changes=changes):
                self.auth = {**original, **changes}
                self.assertEqual(self.run_job(), 1)
                self.assertEqual(self.inferences, [])

    def test_exact_model_effort_permissions_context_and_resume_are_passed(self):
        session = self.payload['session_id']
        self.assertEqual(self.run_job('--effort', 'max', '--resume', session), 0)
        command, kwargs = self.inferences[0]
        expected = {'--model': RUNNER.MODEL, '--effort': 'max', '--permission-mode': 'dontAsk',
                    '--tools': 'Read,Glob,Grep', '--allowedTools': 'Read(./**),Glob,Grep',
                    '--mcp-config': '{"mcpServers":{}}', '--settings': str(self.root / '.claude/settings.json'),
                    '--output-format': 'json', '--resume': session, '--setting-sources': ''}
        for option, value in expected.items():
            self.assertEqual(command[command.index(option) + 1], value)
        for option in ('--restricted', '--strict-mcp-config', '--disable-slash-commands', '-p'):
            self.assertIn(option, command)
        for forbidden in ('--bare', '--fallback-model', '--dangerously-skip-permissions'):
            self.assertNotIn(forbidden, command)
        self.assertEqual(kwargs['cwd'], self.worktree)
        self.assertIn('Start instructions.', kwargs['input'])
        self.assertIn('Review only.', kwargs['input'])
        self.assertEqual(self.result['requestedEffort'], 'max')
        self.assertTrue(self.result['billing_account_matched'])
        self.assertNotIn('actualEffort', self.result)
        self.assertFalse(self.result['changes_verified'])
        self.assertEqual(self.result['status'], 'RECEIVED_UNVERIFIED')

    def test_only_expected_model_usage_is_accepted(self):
        for usage in ({}, {'claude-other': {}}, {RUNNER.MODEL: {}, 'claude-other': {}}, None):
            with self.subTest(usage=usage):
                self.payload['modelUsage'] = usage
                self.assertEqual(self.run_job(), 1)
                self.assertEqual(self.result['status'], 'FAILED')

    def test_edit_scope_uses_exact_permissions(self):
        (self.worktree / 'view.py').write_text('original', encoding='utf-8')
        self.assertEqual(self.run_job('--allow-edit', 'view.py'), 0)
        command, _ = self.inferences[0]
        self.assertEqual(command[command.index('--tools') + 1], 'Read,Glob,Grep,Edit')
        self.assertEqual(command[command.index('--effort') + 1], 'high')
        self.assertEqual(command[command.index('--allowedTools') + 1], 'Read(./**),Glob,Grep,Edit(./view.py)')
        self.assertEqual(self.result['allowed_files'], ['view.py'])

    def test_edit_scope_rejects_traversal_globs_nonfiles_and_symlink_escape(self):
        (self.worktree / 'escape.py').symlink_to(self.task)
        (self.worktree / 'view.py').write_text('original', encoding='utf-8')
        (self.worktree / 'alias.py').symlink_to(self.worktree / 'view.py')
        for value in ('../root/ops/ai/tasks/REVIEW.md', str(self.task), '*.py', 'view.py)', '.', 'escape.py', 'alias.py'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                RUNNER.edit_scope(self.worktree, [value])

    def test_main_checkout_nested_folder_other_repo_and_same_branch_are_rejected(self):
        with self.assertRaises(ValueError):
            RUNNER.worktree_metadata(self.root)
        cases = ([str(self.worktree.parent)], [str(self.worktree), '/other/.git', '/root/.git'],
                 [str(self.worktree), '/same/.git', '/same/.git', 'main', 'main'])
        for replies in cases:
            with self.subTest(replies=replies), patch.object(RUNNER, 'git', side_effect=replies):
                with self.assertRaises(ValueError):
                    RUNNER.worktree_metadata(self.worktree)

    def test_settings_cannot_enable_api_auth_fallback_hooks_or_broad_permissions(self):
        for changes in ({'apiKeyHelper': 'secret-helper'}, {'env': {}}, {'hooks': {}},
                        {'permissions': {'allow': ['Edit']}}, {'fallbackModel': ['other']},
                        {'model': 'other'}, {'forceLoginMethod': 'console'}):
            with self.subTest(changes=changes):
                (self.root / '.claude/settings.json').write_text(json.dumps({**self.settings, **changes}))
                self.assertEqual(self.run_job(), 1)
                self.assertEqual(self.inferences, [])

    def test_adverse_user_project_and_local_sources_are_excluded(self):
        home = self.root.parent / 'adverse-home'
        paths = {'user': home / '.claude/settings.json',
                 'project': self.worktree / '.claude/settings.json',
                 'local': self.worktree / '.claude/settings.local.json'}
        adverse = {'env': {'ANTHROPIC_API_KEY': 'never-load-adverse-key'},
                   'apiKeyHelper': 'never-run-adverse-helper',
                   'hooks': {'SessionStart': [{'hooks': [{'type': 'command', 'command': 'never-run-adverse-hook'}]}]},
                   'permissions': {'allow': ['Edit'], 'additionalDirectories': [str(home)]}}
        for path in paths.values():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(adverse))

        # Model the documented CLI source selection without executing hooks or helpers.
        def inherited(command):
            sources = ['user', 'project', 'local']
            if '--setting-sources' in command:
                sources = list(filter(None, command[command.index('--setting-sources') + 1].split(',')))
            if '--restricted' in command:
                sources = []
            return {key: value for source in sources for key, value in json.loads(paths[source].read_text()).items()}

        self.assertEqual(inherited([]), adverse)
        original = self.process
        source_checks = []

        def process(command, **kwargs):
            if command[0] == str(RUNNER.CLI):
                self.assertEqual(command[command.index('--setting-sources') + 1], '')
                source_checks.append(inherited(command))
                self.assertEqual(inherited(command), {})
            return original(command, **kwargs)

        with patch.dict(os.environ, {'HOME': str(home)}), patch.object(self, 'process', side_effect=process):
            self.assertEqual(self.run_job(), 0)
        self.assertEqual(source_checks, [{}, {}, {}])
        self.assertNotIn('never-load-adverse-key', json.dumps(self.result) + self.console)

    def test_account_mismatch_or_missing_identity_blocks_inference_without_disclosure(self):
        for email in ('other-account@example.invalid', None, ''):
            with self.subTest(email=email):
                self.auth['email'] = email
                self.assertEqual(self.run_job(), 1)
                self.assertEqual(self.inferences, [])
                public = json.dumps(self.result) + self.console
                self.assertNotIn(self.evidence['claude_account_fingerprint'], public)
                self.assertNotIn('private-account@example.invalid', public)
                self.assertNotIn('other-account@example.invalid', public)
                self.assertNotIn('billing_account_matched', self.result)

    def test_matching_account_is_case_normalized_and_not_disclosed(self):
        self.auth['email'] = ' Private-Account@Example.Invalid '
        self.assertEqual(self.run_job(), 0)
        self.assertTrue(self.result['billing_account_matched'])
        self.assertNotIn(self.evidence['claude_account_fingerprint'], json.dumps(self.result))

    def test_credentials_and_local_checkpoints_receive_read_denials(self):
        self.assertEqual(self.run_job(), 0)
        command, _ = self.inferences[0]
        denials = command[command.index('--disallowedTools') + 1].split(',')
        for rule in ('Read(./.env)', 'Read(./.env.*)', 'Read(./**/.env)', 'Read(./**/.env.*)',
                     'Read(./.codex/auth.json)', 'Read(./.claude/settings.local.json)', 'Read(./ops/ai/local/**)'):
            self.assertIn(rule, denials)
        self.assertNotIn('Read', command[command.index('--allowedTools') + 1].split(','))
        self.assertIn('not an OS sandbox', self.result['access_controls'])

    def test_cli_error_and_malformed_json_fail_with_local_output(self):
        self.cli_exit = 7
        self.stderr = 'CLI failure details'
        self.assertEqual(self.run_job(), 1)
        self.assertEqual(self.result['process_exit'], 7)
        self.assertEqual(self.result['status'], 'FAILED')
        self.assertTrue(any('CLI failure details' in path.read_text() for path in (self.root / 'ops/ai/local').glob('*.stderr.txt')))
        self.cli_exit = 0
        self.raw_result = 'malformed'
        self.assertEqual(self.run_job(), 1)
        self.assertEqual(self.result['status'], 'FAILED')

    def test_quota_keeps_checkpoint_without_retry(self):
        self.payload.update(is_error=True, subtype='error_during_execution', result="You've hit your limit")
        self.payload.pop('modelUsage')
        self.assertEqual(self.run_job(), 1)
        self.assertEqual(len(self.inferences), 1)
        self.assertEqual(self.result['status'], 'BLOCKED_SUBSCRIPTION_LIMIT')

    def test_timeout_keeps_partial_output_without_retry(self):
        self.timeout = True
        self.assertEqual(self.run_job(), 1)
        self.assertEqual(len(self.inferences), 1)
        self.assertEqual(self.result['status'], 'TIMED_OUT')
        self.assertTrue(any(path.read_text() == 'partial checkpoint' for path in (self.root / 'ops/ai/local').glob('*.stdout.json')))

    def test_recursive_call_is_rejected_before_any_process(self):
        with patch.dict(os.environ, {'MEMORY_FORGE_CLAUDE_JOB': '1'}):
            self.assertEqual(self.run_job(), 1)
        self.assertEqual(self.calls, [])

    def test_unavailable_cli_saves_a_blocked_checkpoint(self):
        original = self.process

        def unavailable(command, **kwargs):
            if command[0] == str(RUNNER.CLI):
                raise FileNotFoundError('Unavailable CLI')
            return original(command, **kwargs)

        with patch.object(self, 'process', side_effect=unavailable):
            self.assertEqual(self.run_job(), 1)
        self.assertEqual(self.inferences, [])
        self.assertEqual(self.result['status'], 'BLOCKED')
        self.assertEqual(self.result['blocking_reason'], 'FileNotFoundError')


if __name__ == '__main__':
    unittest.main()
