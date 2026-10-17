#!/usr/bin/env python3
"""Package tests; no model calls, credentials, cloud access, or payments."""
import unittest,json,re,hashlib,contextlib,io,os,subprocess,tempfile
from datetime import datetime,timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from delivery_files import font_errors, handoff_files, private_path
import run_claude as RUNNER
ROOT=Path(__file__).resolve().parents[2]
SESSION='721938b1-0e03-4a94-a4a2-8750b8d1154a'
class HandoffTests(unittest.TestCase):
 def test_root_files(self):
  for r in ['START_HERE.txt','Memory_Forge_Complete_Design.pdf','README.md','AGENTS.md','CLAUDE.md','package.json','prototype/index.html','docs/AI_WORKFLOW.md','docs/AI_Start_Guide.md']:
   self.assertTrue((ROOT/r).is_file(),r)
 def test_starter_is_same_as_codex_instructions(self):
  self.assertEqual((ROOT/'START_HERE.txt').read_text(),(ROOT/'docs/START_CODEX.md').read_text().split('\n\n',1)[1])
 def test_shared_policy(self):
  d=json.loads((ROOT/'ops/ai/policy.json').read_text())
  self.assertEqual(d['image_generation_owner'],'Codex');self.assertEqual(d['fallbacks'],[])
  self.assertFalse(d['api_auth_allowed']);self.assertFalse(d['extra_usage_allowed']);self.assertFalse(d['purchased_credit_spend_allowed'])
  self.assertFalse(d['actual_cli_call_verified'])
 def test_active_entrypoints_contain_billing_and_owner(self):
  for r in ['AGENTS.md','CLAUDE.md','START_HERE.txt','docs/START_CLAUDE.md','docs/AI_WORKFLOW.md','docs/AI_Start_Guide.md']:
   s=(ROOT/r).read_text();self.assertIn('구독',s,r);self.assertIn('Codex',s,r);self.assertIn('API',s,r)
 def test_auth_precedes_model_call(self):
  s=(ROOT/'docs/AI_WORKFLOW.md').read_text()
  self.assertIn('모든 조건이 확인된 뒤에만',s);self.assertIn('--bare는 사용하지 않는다',s)
  self.assertIn('자동 충전 OFF만으로는',s);self.assertIn('비밀값은 추출·복사·출력하지 않는다',s)
 def test_game_document_contract(self):
  s=(ROOT/'docs/design_v0.8.1.md').read_text()
  self.assertEqual(re.findall(r'^# <a id="ch(\d+)"></a>',s,re.M),list(map(str,range(24))))
  self.assertEqual(len(list((ROOT/'docs/source_v0.8.1').glob('[0-9][0-9]_chapter.md'))),24)
  self.assertEqual(s.count('**[SHEET]**'),10);self.assertEqual(s.count('**[KEYART]**'),10)
 def test_game_inventory(self):
  d=json.loads((ROOT/'data/game_data.json').read_text())
  for k,n in [('cards',173),('characters',10),('weapons',22),('parts',16),('relics',32),('enemies',20),('events',8)]:self.assertEqual(len(d[k]),n,k)
 def test_reference_and_targets_remain_distinct(self):
  s=(ROOT/'docs/CURRENT.md').read_text();self.assertIn('legacy',s);self.assertIn('독립', (ROOT/'README.md').read_text());self.assertIn('구현된 것이 아니다',s)
 def test_pdf_contains_full_base_text(self):
  import fitz
  with fitz.open(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf') as base,fitz.open(ROOT/'Memory_Forge_Complete_Design.pdf') as combined:
   self.assertEqual(base.page_count,154);self.assertEqual(combined.page_count,159)
   for i in range(base.page_count):self.assertEqual(base[i].get_text(),combined[i+5].get_text(),f'page {i+1}')
 def test_pdf_internal_links_shift(self):
  import fitz
  with fitz.open(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf') as base,fitz.open(ROOT/'Memory_Forge_Complete_Design.pdf') as combined:
   checked=0
   for i in range(base.page_count):
    a=[x['page'] for x in base[i].get_links() if x['kind'] in (fitz.LINK_GOTO,fitz.LINK_NAMED) and x.get('page',-1)>=0]
    b=[x['page'] for x in combined[i+5].get_links() if x['kind'] in (fitz.LINK_GOTO,fitz.LINK_NAMED) and x.get('page',-1)>=0]
    self.assertEqual([x+5 for x in a],b,f'links page {i+1}');checked+=len(a)
   self.assertGreater(checked,0,'Must check actual links, not an empty set')
 def test_pdf_named_destinations(self):
  from pypdf import PdfReader
  a=PdfReader(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf');b=PdfReader(ROOT/'Memory_Forge_Complete_Design.pdf')
  self.assertEqual(set(a.named_destinations),set(b.named_destinations))
  for k,d in a.named_destinations.items():self.assertEqual(a.get_destination_page_number(d)+5,b.get_destination_page_number(b.named_destinations[k]),k)
 def test_full_pdf_build_entrypoint(self):
  d=json.loads((ROOT/'package.json').read_text())
  self.assertIn('build_delivery_pdf.py',d['scripts']['docs']);self.assertIn('build_integrated_design.py',d['scripts']['docs:game'])
 def test_only_approved_licensed_font_and_no_credentials_shipped(self):
  payload=list(handoff_files(ROOT))
  self.assertEqual(font_errors(ROOT,payload),[])
  for p in payload:
   self.assertFalse(private_path(p.relative_to(ROOT)),str(p.relative_to(ROOT)))
class ClaudeMcpProfileTests(unittest.TestCase):
 """Mocked runner jobs only: no Claude CLI, MCP server, socket, screen, account or model is touched."""
 def setUp(self):
  temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
  base=Path(temporary.name).resolve();self.root,self.worktree,self.home=base/'root',base/'claude',base/'home'
  for folder in (self.root/'docs',self.root/'.claude',self.root/'ops/ai/local',self.root/'ops/ai/tasks',self.worktree,self.home,base/'bin'):folder.mkdir(parents=True)
  npx=base/'bin/npx';npx.write_text('#!/bin/sh\nexit 1\n');npx.chmod(0o755)
  (self.root/'docs/START_CLAUDE.md').write_text('Start instructions.',encoding='utf-8')
  (self.root/'.claude/settings.json').write_text(json.dumps({'model':RUNNER.MODEL,'effortLevel':'high','forceLoginMethod':'claudeai','availableModels':[RUNNER.MODEL],'fallbackModel':[],'switchModelsOnFlag':False}))
  self.task=self.root/'ops/ai/tasks/MCP-SMOKE.md';self.task.write_text('Smoke only.',encoding='utf-8')
  self.email='private-account@example.invalid'
  self.evidence={'verified_at':datetime.now(timezone.utc).isoformat(),'claude_auth':'claude.ai','verified_by':'Codex account UI','usage_credits_enabled':False,'auto_reload_enabled':False,'claude_account_fingerprint':hashlib.sha256(('claude.ai\0'+self.email).encode()).hexdigest()}
  self.write_evidence(self.evidence)
  self.auth={'loggedIn':True,'authMethod':'claude.ai','apiProvider':'firstParty','subscriptionType':'max','email':self.email}
  self.usage={RUNNER.MODEL:{'inputTokens':1}};self.refuse=self.timeout=self.drop_init=False
  self.servers=self.exposed=None;self.api_key_source='none'
  self.tool_calls=[('mcp__unityMCP__read_console',{'action':'get','types':['error'],'count':10})]
  self.calls,self.inferences,self.sockets,self.locked,self.worktree_locked=[],[],[],[],[]
  for active in (patch.object(RUNNER,'ROOT',self.root),patch.object(RUNNER,'sys',SimpleNamespace(platform='darwin')),
                 patch.object(RUNNER.socket,'create_connection',side_effect=self.connect),
                 patch.dict(os.environ,{'HOME':str(self.home),'PATH':str(base/'bin')},clear=True)):
   active.start();self.addCleanup(active.stop)
 def write_evidence(self,evidence):(self.root/'ops/ai/local/runtime-billing-evidence.json').write_text(json.dumps(evidence))
 def connect(self,address,timeout=None):
  self.sockets.append(address)
  if self.refuse:raise ConnectionRefusedError('closed')
  return contextlib.nullcontext()
 def process(self,command,**kwargs):
  self.calls.append((command,kwargs))
  if command[0]=='git':
   folder=Path(command[2]);reply={('rev-parse','--show-toplevel'):str(folder),('symbolic-ref','--short','HEAD'):'main' if folder==self.root else 'claude/mcp',('rev-parse','HEAD'):'a'*40}
   return subprocess.CompletedProcess(command,0,reply.get(tuple(command[3:]),str(self.root/'.git'))+'\n','')
  self.assertEqual(command[0],str(RUNNER.CLI))
  if '--version' in command:return subprocess.CompletedProcess(command,0,'2.1.285 (Claude Code)\n','')
  if 'auth' in command:return subprocess.CompletedProcess(command,0,json.dumps(self.auth),'')
  self.inferences.append((command,kwargs));self.locked.append((self.root/RUNNER.CONTROL_LOCK).exists())
  self.worktree_locked.append((self.root/RUNNER.worktree_lock(self.worktree)).exists())
  if self.timeout:raise subprocess.TimeoutExpired(command,1800,output=b'partial',stderr=b'')
  return subprocess.CompletedProcess(command,0,self.output(command),'')
 def output(self,command):
  # Shape of the CLI's json result and stream-json init/tool/result records.
  result={'type':'result','subtype':'success','is_error':False,'result':'Done.','session_id':SESSION,'modelUsage':self.usage,'permission_denials':[]}
  if command[command.index('--output-format')+1]=='json':return json.dumps(result)
  servers=json.loads(command[command.index('--mcp-config')+1])['mcpServers']
  allowed=[name for name in command[command.index('--allowedTools')+1].split(',') if name.startswith('mcp__')]
  init={'type':'system','subtype':'init','session_id':SESSION,'tools':['Read','Glob','Grep',*(allowed if self.exposed is None else self.exposed)],
        'mcp_servers':[{'name':name,'status':'connected'} for name in servers] if self.servers is None else self.servers,'apiKeySource':self.api_key_source}
  events=[] if self.drop_init else [init]
  for number,(name,arguments,*error) in enumerate(self.tool_calls):
   events+=[{'type':'assistant','message':{'content':[{'type':'tool_use','id':f'toolu_{number}','name':name,'input':arguments}]}},
            {'type':'user','message':{'content':[{'type':'tool_result','tool_use_id':f'toolu_{number}','content':'ok','is_error':bool(error and error[0])}]}}]
  return '\n'.join(json.dumps(event) for event in [*events,result])+'\n'
 def run_job(self,*options):
  self.calls,self.inferences,self.sockets,self.locked,self.worktree_locked=[],[],[],[],[]
  with patch.object(RUNNER.subprocess,'run',side_effect=self.process),contextlib.redirect_stdout(io.StringIO()) as console:
   code=RUNNER.main([str(self.task),'--worktree',str(self.worktree),*options])
  self.console=console.getvalue();self.result=json.loads((self.root/'ops/ai/tasks/MCP-SMOKE.result.json').read_text())
  return code
 def option(self,name):
  command=self.inferences[0][0];return command[command.index(name)+1]
 def cli_calls(self):return [command for command,_ in self.calls if command[0]==str(RUNNER.CLI)]
 def test_default_profile_keeps_the_existing_empty_strict_file_job(self):
  (self.worktree/'view.py').write_text('original')
  self.assertEqual(self.run_job('--allow-edit','view.py'),0);first=self.inferences[0];command,kwargs=first
  expected={'--mcp-config':'{"mcpServers":{}}','--tools':'Read,Glob,Grep,Edit','--allowedTools':'Read(./**),Glob,Grep,Edit(./view.py)',
            '--disallowedTools':','.join(RUNNER.PRIVATE_READS),'--output-format':'json','--model':RUNNER.MODEL,'--effort':'high',
            '--permission-mode':'dontAsk','--setting-sources':''}
  for name,value in expected.items():self.assertEqual(self.option(name),value,name)
  for flag in ('--restricted','--strict-mcp-config','--disable-slash-commands','-p'):self.assertIn(flag,command)
  for flag in ('--verbose','--bare','--dangerously-skip-permissions','--fallback-model'):self.assertNotIn(flag,command)
  self.assertNotIn('mcp__',' '.join(command));self.assertNotIn('MCP profile',kwargs['input'])
  self.assertEqual(kwargs['env']['ENABLE_CLAUDEAI_MCP_SERVERS'],'false');self.assertNotIn('MCP_TIMEOUT',kwargs['env'])
  self.assertEqual((self.sockets,self.locked,self.result['status'],self.result['mcp_profile']),([],[False],'RECEIVED_UNVERIFIED','none'))
  self.assertNotIn('mcp',self.result);self.assertEqual(self.result['access_controls'],'Restricted CLI built-in file tools; not an OS sandbox; managed policy still applies')
  self.assertEqual(self.run_job('--allow-edit','view.py','--mcp-profile','none'),0);self.assertEqual(self.inferences[0],first)
 def test_unknown_profiles_urls_and_commands_are_rejected_before_any_process(self):
  for options in (('--mcp-profile','unity'),('--mcp-profile','all'),('--mcp-profile',RUNNER.UNITY_URL),('--mcp-profile','npx'),
                  ('--mcp-config','{"mcpServers":{}}'),('--mcp-url','http://127.0.0.1:9/mcp'),('--mcp-command','sh')):
   with self.subTest(options=options),patch.object(RUNNER.subprocess,'run') as process:
    with contextlib.redirect_stderr(io.StringIO()),self.assertRaises(SystemExit):
     RUNNER.main([str(self.task),'--worktree',str(self.worktree),*options])
    process.assert_not_called()
  self.assertEqual(self.sockets,[]);self.assertFalse((self.root/RUNNER.CONTROL_LOCK).exists())
 def test_profiles_are_fixed_local_scopes_without_paid_generation_or_ai_analysis(self):
  profiles=RUNNER.MCP_PROFILES;unity={'unityMCP':{'type':'http','url':'http://127.0.0.1:8080/mcp'}}
  desktop={'peekaboo':{'type':'stdio','command':'npx','args':['-y','@steipete/peekaboo@4.9.5','mcp']}}
  self.assertEqual(list(profiles),['none','unity-read','unity-write','desktop'])
  self.assertEqual([p['servers'] for p in profiles.values()],[{},unity,unity,desktop])
  self.assertEqual([p['exclusive'] for p in profiles.values()],[False,False,True,True])
  self.assertEqual(profiles['none']['tools']+profiles['none']['allow']+profiles['none']['deny'],())
  short=lambda tools:{tool.split('__')[2] for tool in tools}
  never={'asset_gen','batch_execute','execute_custom_tool','execute_menu_item','manage_packages','analyze','agent','run'}
  for name,profile in profiles.items():
   with self.subTest(profile=name):
    for tool in profile['allow']+profile['deny']:
     self.assertRegex(tool,r'^mcp__(unityMCP|peekaboo)__[a-z_]+$');self.assertIn(tool.split('__')[1],profile['servers'])
    self.assertFalse(short(profile['allow'])&short(profile['deny']));self.assertFalse(short(profile['allow'])&never)
    self.assertLessEqual(set(profile['tools']),{'ListMcpResourcesTool','ReadMcpResourceTool'})
    for server in profile['servers'].values():
     self.assertLessEqual(set(server),{'type','url','command','args'});self.assertNotIn('/',server.get('command',''))
     self.assertNotRegex(json.dumps(server),re.compile(r'imagine|comfy|asset_gen|api.?key|token|secret',re.I))
  self.assertLessEqual(never-{'analyze','agent','run'},short(profiles['unity-write']['deny']))
  self.assertLessEqual({'manage_script','create_script','apply_text_edits','manage_shader'},short(profiles['unity-write']['deny']))
  self.assertLessEqual(set(RUNNER.UNITY_WRITE),short(profiles['unity-read']['deny']));self.assertFalse(short(profiles['unity-read']['allow'])&set(RUNNER.UNITY_WRITE))
  self.assertIn('execute_code',short(profiles['unity-write']['allow']));self.assertIn('execute_code',short(profiles['unity-read']['deny']))
  # The first live unity-read init exposed these unassigned names; each must now be an exact deny in both Unity profiles.
  unity_live=json.loads((ROOT/'ops/ai/runtime-policy.json').read_text())['claude_mcp_profiles']['profiles']['unity-read']['observed_exposed_not_allowed_20261017']
  self.assertEqual(len(unity_live),17);self.assertLessEqual({'generate_image','generate_model','generate_audio','import_model','import_model_file'},set(unity_live))
  for name in ('unity-read','unity-write'):
   self.assertLessEqual(set(unity_live),short(profiles[name]['deny']),name);self.assertFalse(set(unity_live)&short(profiles[name]['allow']),name)
   self.assertFalse([tool for tool in profiles[name]['allow']+profiles[name]['deny'] if '*' in tool or tool=='mcp__unityMCP'],name)
  # Codex's live Peekaboo 4.9.5 tools/list: every real tool is classified, none is invented.
  live=set(json.loads((ROOT/'ops/ai/runtime-policy.json').read_text())['claude_mcp_profiles']['profiles']['desktop']['observed_tools'])
  self.assertEqual(len(live),26);self.assertEqual(short(profiles['desktop']['allow'])|short(profiles['desktop']['deny']),live)
  self.assertFalse({'list','hotkey'}&live)
  self.assertEqual(short(profiles['desktop']['deny']),{'analyze','agent','clipboard','paste','browser'})
  self.assertLessEqual({'app','window','menu','see','image','click','type','inspect_ui','action'},short(profiles['desktop']['allow']))
  self.assertEqual((profiles['desktop']['ai_options'],profiles['unity-write']['ai_options']),(RUNNER.AI_OPTIONS,()))
  self.assertLessEqual({'question','analyze','provider'},set(RUNNER.AI_OPTIONS))
 def test_unity_read_passes_only_the_fixed_local_server_and_named_read_tools(self):
  (self.worktree/'view.py').write_text('original')
  self.assertEqual(self.run_job('--mcp-profile','unity-read','--allow-edit','view.py'),0);command,kwargs=self.inferences[0]
  self.assertEqual(json.loads(self.option('--mcp-config')),{'mcpServers':{'unityMCP':{'type':'http','url':'http://127.0.0.1:8080/mcp'}}})
  for flag in ('--restricted','--strict-mcp-config','--disable-slash-commands','--verbose','-p'):self.assertIn(flag,command)
  self.assertEqual((self.option('--output-format'),self.option('--permission-mode'),self.option('--model')),('stream-json','dontAsk',RUNNER.MODEL))
  self.assertEqual(self.option('--tools'),'Read,Glob,Grep,Edit,ListMcpResourcesTool,ReadMcpResourceTool')
  self.assertEqual(self.option('--allowedTools').split(','),['Read(./**)','Glob','Grep','Edit(./view.py)','ListMcpResourcesTool','ReadMcpResourceTool',*('mcp__unityMCP__'+tool for tool in RUNNER.UNITY_READ)])
  denied=self.option('--disallowedTools').split(',')
  for rule in (*RUNNER.PRIVATE_READS,'mcp__unityMCP__asset_gen','mcp__unityMCP__batch_execute','mcp__unityMCP__manage_scene','mcp__unityMCP__create_script',
               'mcp__unityMCP__generate_image','mcp__unityMCP__import_model_file','mcp__unityMCP__unity_reflect'):self.assertIn(rule,denied)
  self.assertEqual([check[check.index('--mcp-config')+1] for check in self.cli_calls()[:2]],[RUNNER.EMPTY_MCP]*2)
  self.assertEqual((self.sockets,self.locked),([('127.0.0.1',8080)],[False]))
  self.assertIn('MCP profile: unity-read',kwargs['input']);self.assertIn('Smoke only.',kwargs['input'])
  mcp=self.result['mcp']
  self.assertEqual((self.result['status'],mcp['connection_observed'],mcp['observed']['servers'],mcp['exclusive_lock']),('RECEIVED_UNVERIFIED',True,{'unityMCP':'connected'},None))
  self.assertEqual((mcp['observed']['calls'],mcp['observed']['api_key_source'],self.result['allowed_files']),([{'tool':'mcp__unityMCP__read_console','is_error':False}],'none',['view.py']))
  self.assertIn('not an OS sandbox',self.result['access_controls']);self.assertFalse(self.result['changes_verified'])
 def test_disconnected_or_unallowed_mcp_activity_is_not_counted_as_a_connection(self):
  self.servers=[{'name':'unityMCP','status':'failed'}];self.tool_calls=[]
  self.assertEqual(self.run_job('--mcp-profile','unity-read'),0)
  self.assertEqual((self.result['mcp']['connection_observed'],self.result['mcp']['observed']['servers'],self.result['mcp']['observed']['transport_connected']),(False,{'unityMCP':'failed'},False))
  self.servers=None;self.tool_calls=[('mcp__unityMCP__manage_scene',{'action':'save'})]
  self.assertEqual(self.run_job('--mcp-profile','unity-read'),0)
  self.assertFalse(self.result['mcp']['connection_observed']);self.assertEqual(self.result['status'],'RECEIVED_UNVERIFIED')
  self.assertTrue(self.result['mcp']['observed']['transport_connected'])
  self.tool_calls=[('mcp__unityMCP__read_console',{'action':'get'},True)]
  self.assertEqual(self.run_job('--mcp-profile','unity-read'),0)
  self.assertEqual((self.result['mcp']['observed']['transport_connected'],self.result['mcp']['connection_observed']),(True,False))
 def test_unity_write_and_desktop_share_one_exclusive_control_lock(self):
  lock=self.root/RUNNER.CONTROL_LOCK;lock.write_text('{"run_id":"another-writer"}')
  for profile in ('unity-write','desktop'):
   with self.subTest(profile=profile):
    self.assertEqual(self.run_job('--mcp-profile',profile),1)
    self.assertEqual(self.result['status'],'BLOCKED');self.assertIn('control lock',self.result['blocking_reason'])
    self.assertEqual((self.cli_calls(),self.sockets,lock.read_text()),([],[],'{"run_id":"another-writer"}'))
  lock.unlink();self.tool_calls=[('mcp__unityMCP__manage_scene',{'action':'get_hierarchy'})]
  self.assertEqual(self.run_job('--mcp-profile','unity-write'),0)
  self.assertEqual((self.locked,lock.exists(),self.result['mcp']['exclusive_lock']),([True],False,RUNNER.CONTROL_LOCK))
  self.assertTrue(self.result['mcp']['connection_observed'])
  allowed=self.option('--allowedTools').split(',');denied=self.option('--disallowedTools').split(',')
  self.assertIn('mcp__unityMCP__manage_scene',allowed);self.assertIn('mcp__unityMCP__execute_code',allowed);self.assertNotIn('mcp__unityMCP__manage_script',allowed)
  self.assertIn('execute_code only to install, inspect or verify',self.inferences[0][1]['input'])
  for tool in ('asset_gen','batch_execute','execute_menu_item','manage_packages','apply_text_edits'):self.assertIn('mcp__unityMCP__'+tool,denied)
  self.assertIn('single Unity Editor write lock',self.inferences[0][1]['input'])
  self.timeout=True
  self.assertEqual(self.run_job('--mcp-profile','unity-write'),1)
  self.assertEqual((self.result['status'],self.locked,lock.exists()),('TIMED_OUT',[True],False))
 def test_unity_preflight_blocks_before_the_cli_when_the_endpoint_is_closed(self):
  self.refuse=True
  for profile in ('unity-read','unity-write'):
   with self.subTest(profile=profile):
    self.assertEqual(self.run_job('--mcp-profile',profile),1)
    self.assertEqual((self.result['status'],self.cli_calls(),self.sockets),('BLOCKED',[],[('127.0.0.1',8080)]))
    self.assertIn(RUNNER.UNITY_URL,self.result['blocking_reason']);self.assertFalse((self.root/RUNNER.CONTROL_LOCK).exists())
 def test_desktop_uses_pinned_peekaboo_with_provider_and_secret_environment_removed(self):
  source={'XAI_API_KEY':'never-xai','GEMINI_API_KEY':'never-gemini','OPENROUTER_API_KEY':'never-router',
          'PEEKABOO_AI_PROVIDERS':'openai/never-model','PEEKABOO_CLI_PATH':'/never/peekaboo','OLLAMA_HOST':'never-ollama:11434',
          'IMAGINEART_API_KEY':'never-imagine','GITHUB_TOKEN':'never-github','npm_config__authToken':'never-npm',
          'ANTHROPIC_API_KEY':'never-anthropic','OPENAI_API_KEY':'never-openai'}
  self.tool_calls=[('mcp__peekaboo__see',{'app_target':'Unity'}),('mcp__peekaboo__click',{'on':'B1'})]
  with patch.dict(os.environ,source):self.assertEqual(self.run_job('--mcp-profile','desktop','--effort','xhigh'),0)
  self.assertEqual(json.loads(self.option('--mcp-config')),{'mcpServers':{'peekaboo':{'type':'stdio','command':'npx','args':['-y','@steipete/peekaboo@4.9.5','mcp']}}})
  self.assertEqual((self.option('--tools'),self.option('--model'),self.option('--effort')),('Read,Glob,Grep',RUNNER.MODEL,'xhigh'))
  self.assertEqual(self.option('--allowedTools').split(','),['Read(./**)','Glob','Grep',*('mcp__peekaboo__'+tool for tool in RUNNER.DESKTOP_ALLOW)])
  denied=self.option('--disallowedTools').split(',')
  self.assertEqual([tool for tool in denied if tool.startswith('mcp__')],['mcp__peekaboo__'+tool for tool in ('analyze','agent','clipboard','paste','browser')])
  self.assertEqual([check[check.index('--mcp-config')+1] for check in self.cli_calls()[:2]],[RUNNER.EMPTY_MCP]*2)
  for _,kwargs in [(c,k) for c,k in self.calls if c[0]==str(RUNNER.CLI)]:
   self.assertFalse(set(source)&set(kwargs['env']));self.assertEqual(kwargs['env']['CLAUDE_CODE_EFFORT_LEVEL'],'xhigh')
  env=self.inferences[0][1]['env']
  self.assertEqual((env['ENABLE_CLAUDEAI_MCP_SERVERS'],env['MCP_TIMEOUT'],env['HOME']),('false','120000',str(self.home)))
  self.assertIn('Never pass AI analysis options',self.inferences[0][1]['input']);self.assertIn('keychain',self.inferences[0][1]['input'])
  self.assertEqual((self.sockets,self.locked,(self.root/RUNNER.CONTROL_LOCK).exists()),([],[True],False))
  self.assertTrue(self.result['mcp']['connection_observed']);self.assertEqual(self.result['mcp']['observed']['servers'],{'peekaboo':'connected'})
  public=json.dumps(self.result)+self.console
  for value in source.values():self.assertNotIn(value,public)
 def test_desktop_preflight_blocks_provider_files_missing_npx_or_another_platform(self):
  stored=self.home/'.peekaboo';stored.mkdir()
  for name in ('credentials','config.json'):
   with self.subTest(file=name):
    secret=stored/name;secret.write_text('OPENAI_API_KEY=never-print-provider-key');secret.chmod(0)
    code=self.run_job('--mcp-profile','desktop');secret.chmod(0o600);secret.unlink();self.assertEqual(code,1)
    self.assertEqual((self.result['status'],self.cli_calls()),('BLOCKED',[]));self.assertIn('Peekaboo credential or config',self.result['blocking_reason'])
    self.assertNotIn('never-print-provider-key',json.dumps(self.result)+self.console)
  with patch.dict(os.environ,{'PATH':str(self.home)}):self.assertEqual(self.run_job('--mcp-profile','desktop'),1)
  self.assertIn('npx',self.result['blocking_reason']);self.assertEqual(self.cli_calls(),[])
  with patch.object(RUNNER,'sys',SimpleNamespace(platform='linux')):self.assertEqual(self.run_job('--mcp-profile','desktop'),1)
  self.assertIn('macOS',self.result['blocking_reason']);self.assertEqual(self.cli_calls(),[])
  self.assertFalse((self.root/RUNNER.CONTROL_LOCK).exists());self.assertEqual(self.run_job('--mcp-profile','desktop'),0)
 def test_stream_audit_fails_closed_on_scope_billing_or_ai_analysis_evidence(self):
  cases=(('unity-read',{'servers':[{'name':'unityMCP','status':'connected'},{'name':'claude.ai Gmail','status':'connected'}]},'outside the fixed profile'),
         ('unity-read',{'exposed':['mcp__unityMCP__read_console','mcp__claude_ai_Gmail__send_email']},'outside the fixed profile'),
         ('unity-write',{'exposed':['mcp__unityMCP__manage_scene','mcp__unityMCP__asset_gen']},'forbidden MCP tool'),
         ('desktop',{'exposed':['mcp__peekaboo__see','mcp__peekaboo__analyze']},'forbidden MCP tool'),
         ('unity-read',{'exposed':['mcp__unityMCP__read_console','mcp__unityMCP__generate_image']},'forbidden MCP tool'),
         ('unity-read',{'exposed':['mcp__unityMCP__read_console','mcp__unityMCP__unity_new_tool']},'outside the fixed allowlist'),
         ('unity-write',{'exposed':['mcp__unityMCP__manage_scene','mcp__unityMCP__unity_new_tool']},'unity_new_tool'),
         ('desktop',{'exposed':['mcp__peekaboo__see','mcp__peekaboo__future_tool']},'outside the fixed allowlist'),
         ('desktop',{'tool_calls':[('mcp__peekaboo__image',{'question':'What is on screen?'})]},'AI analysis'),
         ('desktop',{'tool_calls':[('mcp__peekaboo__see',{'options':[{'analyze':'describe'}]})]},'AI analysis'),
         ('unity-read',{'api_key_source':'ANTHROPIC_API_KEY'},'API key source'),
         ('desktop',{'drop_init':True},'initialization'))
  for profile,changes,reason in cases:
   with self.subTest(profile=profile,changes=changes):
    self.servers=self.exposed=None;self.api_key_source='none';self.drop_init=False;self.tool_calls=[]
    for key,value in changes.items():setattr(self,key,value)
    self.assertEqual(self.run_job('--mcp-profile',profile),1)
    self.assertEqual(self.result['status'],'FAILED');self.assertIn(reason,self.result['blocking_reason'])
    self.assertFalse((self.root/RUNNER.CONTROL_LOCK).exists())
 def test_mcp_profiles_keep_billing_account_model_effort_and_recursion_guards(self):
  self.write_evidence({**self.evidence,'usage_credits_enabled':True})
  self.assertEqual(self.run_job('--mcp-profile','unity-read'),1);self.assertEqual((self.calls,self.sockets,self.result['status']),([],[],'BLOCKED'))
  self.write_evidence(self.evidence);self.auth['authMethod']='api_key'
  self.assertEqual(self.run_job('--mcp-profile','desktop'),1);self.assertEqual(self.inferences,[])
  self.auth.update(authMethod='claude.ai',email='other-account@example.invalid')
  self.assertEqual(self.run_job('--mcp-profile','unity-write'),1);self.assertEqual(self.inferences,[]);self.assertFalse((self.root/RUNNER.CONTROL_LOCK).exists())
  self.assertNotIn('other-account@example.invalid',json.dumps(self.result)+self.console)
  self.auth['email']=self.email;self.usage={'claude-other':{}}
  self.assertEqual(self.run_job('--mcp-profile','unity-read'),1);self.assertEqual(self.result['status'],'FAILED')
  self.usage={RUNNER.MODEL:{'inputTokens':1}}
  with patch.dict(os.environ,{'MEMORY_FORGE_CLAUDE_JOB':'1'}):self.assertEqual(self.run_job('--mcp-profile','desktop'),1)
  self.assertEqual((self.calls,self.sockets,(self.root/RUNNER.CONTROL_LOCK).exists()),([],[],False))
  self.assertEqual(self.run_job('--mcp-profile','unity-read','--effort','max'),0)
  self.assertEqual((self.option('--model'),self.option('--effort'),self.inferences[0][1]['env']['CLAUDE_CODE_EFFORT_LEVEL']),(RUNNER.MODEL,'max','max'))
  self.assertEqual((self.result['requestedEffort'],self.result['model_usage_keys']),('max',[RUNNER.MODEL]))
 def test_named_test_commands_add_only_exact_bash_rules_and_record_real_runs(self):
  (self.worktree/'view.py').write_text('original')
  self.tool_calls=[('Bash',{'command':'npm run test:handoff','description':'handoff'}),('Bash',{'command':'git status'},True)]
  self.assertEqual(self.run_job('--allow-edit','view.py','--allow-test','handoff','--allow-test','python','--allow-test','handoff','--effort','xhigh'),0)
  command,kwargs=self.inferences[0]
  self.assertEqual(self.option('--tools'),'Read,Glob,Grep,Edit,Bash')
  self.assertEqual(self.option('--allowedTools').split(','),['Read(./**)','Glob','Grep','Edit(./view.py)','Bash(npm run test:handoff)','Bash(python3 -m unittest discover -s tests -v)'])
  denied=self.option('--disallowedTools').split(',');self.assertEqual(denied,[*RUNNER.PRIVATE_READS,*RUNNER.BASH_DENY])
  for rule in ('Bash(claude:*)','Bash(codex:*)','Bash(curl:*)','Bash(git push:*)','Bash(git reset:*)','Bash(security:*)','Bash(npm publish:*)'):self.assertIn(rule,denied)
  self.assertEqual((self.option('--output-format'),self.option('--mcp-config'),self.option('--model'),self.option('--effort')),('stream-json',RUNNER.EMPTY_MCP,RUNNER.MODEL,'xhigh'))
  self.assertIn('--verbose',command);self.assertNotIn('mcp__',' '.join(command));self.assertIn('Allowed test commands',kwargs['input'])
  self.assertEqual(self.result['tests'],{'allowed':['npm run test:handoff','python3 -m unittest discover -s tests -v'],
                                         'runs':[{'command':'npm run test:handoff','is_error':False},{'command':'git status','is_error':True}],
                                         'not_run':['python3 -m unittest discover -s tests -v']})
  self.assertEqual((self.result['status'],self.result['mcp_profile'],self.sockets,self.locked),('RECEIVED_UNVERIFIED','none',[],[False]))
  self.assertNotIn('mcp',self.result);self.assertIn('exact named test commands',self.result['access_controls']);self.assertIn('not an OS sandbox',self.result['access_controls'])
 def test_shell_activity_outside_named_tests_fails_closed_and_unknown_tests_are_rejected(self):
  self.tool_calls=[('Bash',{'command':'npm test'}),('Bash',{'command':'curl https://example.invalid'})]
  self.assertEqual(self.run_job('--allow-test','node'),1)
  self.assertEqual(self.result['status'],'FAILED');self.assertIn('outside the named tests',self.result['blocking_reason'])
  self.assertEqual(self.result['tests']['runs'][1],{'command':'curl https://example.invalid','is_error':False})
  for options in (('--allow-test','deploy'),('--allow-test','npm test'),('--allow-test','sh'),('--allow-command','npm test')):
   with self.subTest(options=options),patch.object(RUNNER.subprocess,'run') as process:
    with contextlib.redirect_stderr(io.StringIO()),self.assertRaises(SystemExit):
     RUNNER.main([str(self.task),'--worktree',str(self.worktree),*options])
    process.assert_not_called()
 def test_test_command_registry_is_local_checks_only(self):
  self.assertEqual(set(RUNNER.TEST_COMMANDS),{'node','proposals','memory-ui','late-ui','python','docs','handoff','manifest'})
  scripts=json.loads((ROOT/'package.json').read_text())['scripts']
  for name,command in RUNNER.TEST_COMMANDS.items():
   with self.subTest(name=name):
    self.assertRegex(command,r'^(npm test|npm run (test:[a-z-]+|verify:manifest)|python3 -m unittest discover -s tests -v)$')
    self.assertNotRegex(command,r'[;&|`$<>()*,]')
    script=scripts['test' if command=='npm test' else command[8:]] if command.startswith('npm') else command
    self.assertNotRegex(script,r'\b(claude|codex|curl|wget|ssh|gh|publish|deploy|push)\b')
 def test_parallel_jobs_cannot_share_a_worktree(self):
  lock=self.root/RUNNER.worktree_lock(self.worktree);lock.write_text('{"run_id":"parallel-job"}')
  self.assertEqual(self.run_job(),1)
  self.assertEqual((self.result['status'],self.cli_calls(),lock.read_text()),('BLOCKED',[],'{"run_id":"parallel-job"}'))
  self.assertIn('using this worktree',self.result['blocking_reason'])
  lock.unlink();self.assertEqual(self.run_job(),0);self.assertEqual(self.worktree_locked,[True]);self.assertFalse(lock.exists())
  self.assertNotEqual(RUNNER.worktree_lock(self.worktree),RUNNER.worktree_lock(self.home));self.assertNotIn(str(self.worktree),RUNNER.worktree_lock(self.worktree))
 def test_runtime_policy_records_claude_autonomy_and_test_command_boundaries(self):
  policy=json.loads((ROOT/'ops/ai/runtime-policy.json').read_text());autonomy=policy['claude_autonomy'];tests=policy['claude_test_commands']
  self.assertEqual((tests['registry'],tests['denied_shell_rules'],tests['default'],tests['os_sandbox']),(RUNNER.TEST_COMMANDS,list(RUNNER.BASH_DENY),[],False))
  self.assertEqual((autonomy['character_final_visual_design_owner'],policy['image_generation_owner']),('Codex','Codex'))
  self.assertIs(autonomy['claude_may_comment_on_all_fields_including_characters'],True);self.assertIs(autonomy['parallel_independent_jobs'],True)
  self.assertLessEqual({'price','probability','currency_totals','permanent_assets','age_policy','production_deploy'},set(autonomy['approval_still_required']))
  self.assertIs(autonomy['safety_permission_billing_boundaries_unchanged'],True)
  self.assertEqual(autonomy['feedback_classes'],['implementation_defect','design_mismatch','real_constraint','optional_opinion'])
 def test_claude_root_and_codex_image_design_review_routing(self):
  policy=json.loads((ROOT/'ops/ai/runtime-policy.json').read_text());route=policy['work_routing']
  duties=['creative originals under art/: character, boss and enemy design, outfits and skins','image generation and content edits','final character visual design',
   'story, world, dialogue, relationships and voice scripts with acting direction','expressions, poses, gestures and lobby interactions',
   'character motion (idle, move, attack, hit, victory, ultimate), rigs, keyframes and animation clips',
   'per-character staging: storyboards, camera, Timeline, VFX and character visual prefabs',
   'read-only independent code review and visual-fidelity review of implemented screens']
  self.assertEqual((route['codex_responsibilities'],policy['claude_autonomy']['codex_keeps']),(duties,duties))
  self.assertEqual(route['codex_must_not'],['task dispatch, direction or planning','system implementation: runtime code, UI, players, data plumbing','integration, commits or pushes','Unity Editor writes','calling Claude'])
  creative=route['creative_ownership'];self.assertIn('art/<folder>',creative['codex_writes_only']);self.assertIs(creative['minors_no_sexualised_staging'],True)
  self.assertEqual(set(creative['issue_classes']),{'technical_defect','creative_taste','real_constraint'});self.assertTrue((ROOT/creative['imported_copies_checked_by']).exists())
  self.assertIn('## 18. 2026-10-18 최신 우선',(ROOT/'docs/AI_WORKFLOW.md').read_text())
  for r in ['AGENTS.md','CLAUDE.md','docs/START_CLAUDE.md','README.md']: self.assertIn('OWNERSHIP.md',(ROOT/r).read_text(),r)
  self.assertTrue(route['root'].startswith('Claude Opus root session'));self.assertIn('Unity Editor single writer',route['root'])
  self.assertEqual(policy['unity']['writer'],'Claude root session');self.assertIs(policy['unity']['single_writer'],True)
  self.assertIn('Claude calls the official Codex CLI',route['invocation_direction']);self.assertIn('Codex never calls Claude',route['invocation_direction'])
  self.assertLessEqual({'server','save_and_memory','economy','auth','migration','netcode','core_rules'},set(route['codex_review_required_before_integration']))
  self.assertIn('read-only',route['codex_review']);self.assertIn('sensitive paths hold',route['codex_review_unavailable'])
  self.assertLessEqual({'mcp__unityMCP__'+t for t in ('generate_image','generate_model','generate_audio','import_model','import_model_file','asset_gen')},set(route['root_denied_tools']))
  self.assertEqual(set(route['root_session_controls']),{'session','start_checks','tool_restriction','permission_mode_tradeoff','unity_lock','codex_calls','audit'})
  self.assertLessEqual({'Read(~/.codex/auth.json)','Read(~/.claude/.credentials.json)','Bash(printenv:*)','Bash(security find-generic-password:*)'},set(route['root_denied_permissions']))
  self.assertIn("r.acquire_lock(r.CONTROL_LOCK",(ROOT/'docs/AI_WORKFLOW.md').read_text());self.assertEqual(RUNNER.CONTROL_LOCK,'ops/ai/local/exclusive-control.lock')
  self.assertIn('## 17. 2026-10-17 최신 우선 — Claude 루트',(ROOT/'docs/AI_WORKFLOW.md').read_text())
  for r in ['AGENTS.md','CLAUDE.md','docs/START_CLAUDE.md','README.md']:
   s=(ROOT/r).read_text();self.assertIn('§17',s,r);self.assertIn('읽기 전용',s,r)
 def test_root_session_audit_blocks_policy_breaks_and_accepts_a_clean_session(self):
  import audit_root_session as A
  policy=json.loads((ROOT/'ops/ai/runtime-policy.json').read_text());good={k:True for k in A.REQUIRED_CHECKS}
  def session(*steps,model=RUNNER.MODEL,sub=None,raw=()):
   d=Path(tempfile.mkdtemp());rows=[]
   for step in steps:
    if step[0]=='use':rows.append({'type':'assistant','message':{'model':model,'content':[{'type':'tool_use','id':i,'name':n,'input':a} for i,n,a in step[1]]}})
    elif step[0]=='result':rows.append({'type':'user','message':{'content':[{'type':'tool_result','tool_use_id':step[1],'content':step[2] if not isinstance(step[2],str) else [{'type':'text','text':step[2]}],'is_error':step[3:]==(True,)}]}})
    else:rows.append({'type':'attachment','attachment':{'type':'deferred_tools_delta','addedNames':step[1],'removedNames':step[2]}})
   (d/'s.jsonl').write_text('\n'.join([*map(json.dumps,rows),*raw]))
   if sub:(d/'s'/'subagents').mkdir(parents=True);(d/'s'/'subagents'/'agent-a.jsonl').write_text('\n'.join(map(json.dumps,sub)))
   return d/'s.jsonl'
  use=lambda i,n,a:('use',[(i,n,a)]);bash=lambda i,c:use(i,'Bash',{'command':c})
  check='python3 tools/ai/audit_root_session.py --start-check';passed=A.MARK+json.dumps({'pass':True,'checks':good})
  start=[bash('s',check),('result','s',passed)]
  acquire="python3 -c \"import sys;sys.path.insert(0,'tools/ai');import run_claude as r;r.acquire_lock(r.CONTROL_LOCK,{'run_id':'claude-root-s'},'Editor busy')\""
  release="python3 -c \"import run_claude as r;r.release_lock(r.ROOT/r.CONTROL_LOCK,'claude-root-s')\""
  codex="command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort='\"high\"' --json \"$(cat p.txt)\" < d.diff"
  save=use('w','mcp__unityMCP__manage_scene',{'action':'save'})
  report=lambda *steps,**k:A.audit(session(*steps,**k),pol=policy)
  kinds=lambda *steps,**k:{v['kind'] for v in report(*steps,**k)['violations']}
  clean=report(*start,bash('l',acquire),('result','l',''),save,bash('f',release),bash('c',codex),
   use('r','mcp__unityMCP__read_console',{'action':'get'}),bash('e','env | cut -d= -f1 | sort'),bash('p',"printf '%s' 'claude --bare'"),
   bash('h',"cat > f <<'EOF' && echo done\nreviews via codex exec without flags; Claude's note $(printenv OPENAI_API_KEY)\nEOF"),
   bash('g','git -C . status'),bash('i',"command codex exec -s workspace-write -m gpt-6.1-sol -c model_reasoning_effort='\"high\"' -C art/candidates/20261018/codex 'draw'"),use('o','Read',{'file_path':'/Users/u/.codex/config.toml'}),bash('q',"printf '%s' '$(printenv OPENAI_API_KEY)'"),
   use('k','Grep',{'pattern':'.codex/auth.json','path':'tools/ai'}))
  self.assertTrue(clean['pass'],clean);self.assertEqual(clean['codex_calls'],2);self.assertTrue(clean['start_check_current'])
  push=' '.join(['git','-C','.','push','origin','main','--'+'force'])  # assembled so shell hooks scanning commands do not trip on test data
  cases=[('unity_write_without_lock',[use('x','mcp__unityMCP__read_console',{'action':'clear'})]),
   ('unity_write_without_lock',[use('x','mcp__unityMCP__execute_code',{'action':'read'})]),
   ('unity_write_without_lock',[bash('l',"echo \"acquire_lock(r.CONTROL_LOCK,{'run_id':'claude-root-s'}\""),('result','l',''),save]),
   ('unity_write_without_lock',[('use',[('l','Bash',{'command':acquire}),('w','mcp__unityMCP__manage_scene',{'action':'save'})]),('result','l','')]),
   ('ambiguous_lock_command',[bash('l',acquire.replace('import run_claude','if False: import run_claude')),('result','l',''),save]),
   ('ambiguous_lock_command',[bash('l',acquire+' && '+release),('result','l',''),save]),
   ('codex_call',[bash('x','FOO=1 command codex exec -s workspace-write -m other -c model_reasoning_effort=high p')]),
   ('codex_call',[bash('x','env -i PATH="$PATH" HOME="$HOME" codex exec -s workspace-write -m other review')]),
   ('codex_call',[bash('x',"command codex exec 'review -s read-only -m gpt-6.1-sol -c model_reasoning_effort=high'")]),
   ('codex_call',[bash('x','command codex exec -s read-only -m gpt-6.1-sol p')]),
   ('codex_call',[bash('x',"command codex exec 'see --help'")]),('codex_call',[bash('x','command codex exec -- --help')]),
   ('codex_call',[bash('x',"command codex exec -s workspace-write -m gpt-6.1-sol -c model_reasoning_effort=high -C tools 'x'")]),
   ('codex_call',[bash('x',"command codex exec -s workspace-write -m gpt-6.1-sol -c model_reasoning_effort=high -C art/../tools 'x'")]),
   ('codex_call',[bash('x',"command codex exec -s workspace-write -m gpt-6.1-sol -c model_reasoning_effort=high -C /tmp/art/x 'x'")]),
   ('codex_call',[bash('x',"command codex exec -s workspace-write -m gpt-6.1-sol -c model_reasoning_effort=high -C '~/art/x' 'x'")]),
   ('force_push_main',[bash('x',push)]),('env_value_print',[bash('x','printenv OPENAI_API_KEY')]),
   ('env_value_print',[bash('x',"bash -c 'printenv OPENAI_API_KEY'")]),('env_value_print',[bash('x','printf "%s" "$(printenv OPENAI_API_KEY)"')]),
   ('env_value_print',[bash('x','cat > f <<EOF\n$(printenv OPENAI_API_KEY)\nEOF')]),
   ('secret_read',[use('x','Read',{'file_path':'/Users/u/.codex/auth.json'})]),('secret_read',[use('x','Read',{'file_path':'/Users/u/.codex/./auth.json'})]),
   ('untargeted_gui_input',[use('x','mcp__computer-use__computer_batch',{'actions':[{'action':'left_click'}]})]),
   ('denied_tool_exposed',[('expose',['mcp__unityMCP__generate_image'],[]),('expose',[],['mcp__unityMCP__generate_image'])]),
   ('opaque_execution',[use('x','mcp__plugin_desktop-commander_desktop-commander__interact_with_process',{'input':'x'})]),
   ('protected_before_start_check',[bash('x','echo {} > .claude/settings.local.json && '+codex)]),
   ('malformed_record',[bash('x','ls'),('result','x',7)]),('malformed_record',[use('x','Bash',['ls'])]),
   ('ambiguous_lock_command',[('use',[('l','Bash',{'command':acquire}),('f','Bash',{'command':release})]),('result','l',''),('result','f',''),save]),
   ('unity_write_without_lock',[('use',[('l','Bash',{'command':acquire}),('f','Bash',{'command':release})]),('result','l',''),('result','f',''),save]),
   ('codex_call',[bash('x','env codex exec -s workspace-write -m other review')]),
   ('codex_call',[bash('x','sudo -u user codex exec -s workspace-write -m other review')]),
   ('env_value_print',[bash('x',"bash -lc 'printenv OPENAI_API_KEY'")]),
   ('env_value_print',[bash('x',"bash > /dev/null <<'EOF'\nprintenv OPENAI_API_KEY\nEOF")]),
   ('malformed_record',[('expose',5,[])])]
  for kind,steps in cases:
   with self.subTest(kind=kind,steps=steps):self.assertIn(kind,kinds(*start,*steps))
  child={'type':'assistant','message':{'model':RUNNER.MODEL,'content':[{'type':'tool_use','id':'y','name':'Bash','input':{'command':codex}}]}}
  self.assertIn('protected_in_subagent',kinds(*start,sub=[child]))
  self.assertIn('protected_before_start_check',kinds(bash('c',codex),*start))
  self.assertIn('unreadable_transcript',kinds(*start,raw=['{not json','[]']))
  odd=[json.dumps({'type':'user','message':{'content':[{'type':'tool_result','tool_use_id':['x'],'content':''}]}}),
   json.dumps({'type':'assistant','message':{'model':RUNNER.MODEL,'content':{'type':'tool_use','id':'z','name':'mcp__unityMCP__generate_image','input':{}}}})]
  self.assertIn('malformed_record',kinds(*start,raw=odd[:1]));self.assertIn('malformed_record',kinds(*start,raw=odd[1:]))
  for steps in [[bash('s',check),('result','s',passed,True)],[*start,bash('t',check),('result','t','ROOT_START_CHECK {"pass": false}')],
   [*start,use('x','Write',{'file_path':'.claude/settings.local.json','content':'{}'})],
   [*start,use('x','Write',{'file_path':'/Users/u/.codex/auth.json','content':'{}'})],[*start,bash('x','export CLAUDE_CODE_USE_BEDROCK=1')],
   [bash('s',check),use('x','Write',{'file_path':'.claude/settings.local.json','content':'{}'}),('result','s',passed)],
   [bash('s',"python3 -c 'print(1)' tools/ai/audit_root_session.py --start-check"),('result','s',passed)]]:
   with self.subTest(start_steps=steps):
    result=report(*steps);self.assertFalse(result['pass']);self.assertFalse(result['start_check_current']);self.assertTrue(result['missing_evidence'])
  dirty=session(use('t0','mcp__unityMCP__generate_image',{}),use('t1','mcp__235d7bf9__notion-search',{}),use('t2','Agent',{'model':'haiku','prompt':'x'}),
   use('t3','mcp__computer-use__app_click',{'app':A.UNITY_APP}),('expose',['mcp__unityMCP__generate_audio'],[]),model='claude-sonnet-5-5')
  found=A.audit(dirty,pol=policy);self.assertFalse(found['pass'])
  self.assertLessEqual({'model','denied_tool','mcp_server_not_allowed','agent_model','unity_write_without_lock','denied_tool_exposed'},{v['kind'] for v in found['violations']})
  waived=A.audit(dirty,{'t3':'explained','denied_tool_exposed:mcp__unityMCP__generate_audio':'before deny'},policy)
  self.assertIn('explained',[w['reason'] for w in waived['waived']]);self.assertIn('before deny',[w['reason'] for w in waived['waived']])
  auth=lambda cmd,**_:SimpleNamespace(returncode=0,stdout='{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty"}' if cmd[0]=='claude' else 'Logged in using ChatGPT',stderr='')
  self.assertTrue(A.start_check({},auth,policy)['checks']['provider_env_clean'])
  for name,value in [('AWS_SESSION_TOKEN','x'),('GOOGLE_APPLICATION_CREDENTIALS','x'),('CODEX_BASE_URL','x'),('OPENAI_API_KEY','x'),('ANTHROPIC_BASE_URL','https://proxy.example'),('ANTHROPIC_BASE_URL','http://[bad')]:
   with self.subTest(env=name,value=value):
    checks=A.start_check({name:value},auth,policy)['checks'];self.assertTrue(checks['auth']);self.assertEqual(checks['provider_env_names'],[name])
  failing=lambda cmd,**_:SimpleNamespace(returncode=1,stdout='',stderr='ChatGPT authentication failed') if cmd[0]=='codex' else auth(cmd)
  self.assertFalse(A.start_check({},failing,policy)['checks']['codex_chatgpt_login'])
  local=Path(tempfile.mkdtemp());(local/'.claude').mkdir();(local/'.claude/settings.local.json').write_text('{"permissions":{"deny":null}}')
  with patch.object(A,'ROOT',local):self.assertFalse(A.start_check({},auth,policy)['checks']['root_denied_tools_enforced'])
  with self.assertRaises(KeyError):A.start_check({},auth,dict(policy,work_routing={k:v for k,v in policy['work_routing'].items() if k!='root_denied_permissions'}))
  self.assertLessEqual({'Bash(env:*)','Bash(/usr/bin/env:*)','Bash(/usr/bin/printenv:*)'},set(policy['work_routing']['root_denied_permissions']))
 def test_shared_mcp_files_and_runtime_policy_match_the_runner(self):
  self.assertEqual(json.loads((ROOT/'.mcp.json').read_text()),{'mcpServers':RUNNER.UNITY_SERVER})
  self.assertIn('url = "'+RUNNER.UNITY_URL+'"',(ROOT/'.codex/config.toml').read_text())
  policy=json.loads((ROOT/'ops/ai/runtime-policy.json').read_text());documented=policy['claude_mcp_profiles']
  self.assertEqual((policy['unity']['endpoint'],policy['unity']['paid_asset_generation'],policy['unity']['single_writer']),(RUNNER.UNITY_URL,False,True))
  self.assertEqual((documented['default'],documented['exclusive_control_lock'],list(documented['profiles'])),('none',RUNNER.CONTROL_LOCK,list(RUNNER.MCP_PROFILES)))
  for name,profile in RUNNER.MCP_PROFILES.items():
   with self.subTest(profile=name):
    entry=documented['profiles'][name];prefix=''.join('mcp__'+server+'__' for server in profile['servers'])
    self.assertEqual((entry['servers'],entry['builtin_mcp_tools'],entry['exclusive_control_lock']),(profile['servers'],list(profile['tools']),profile['exclusive']))
    self.assertEqual([prefix+tool for tool in entry['allowed_tools']],list(profile['allow']))
    self.assertEqual([prefix+tool for tool in entry['denied_tools']],list(profile['deny']))
  self.assertEqual(documented['profiles']['desktop']['ai_option_audit'],list(RUNNER.AI_OPTIONS))
  image=policy['image_generation_routes']['imagineart']
  self.assertEqual((policy['image_generation_owner'],image['operator'],image['allowed_balance']),('Codex','Codex','free_credits_only'))
  for key in ('purchased_or_paid_balance_allowed','auto_recharge_allowed','additional_payment_allowed','claude_tool_exposure'):self.assertIs(image[key],False,key)
  self.assertIs(image['hold_when_free_balance_or_deduction_unverified'],True)
  for key in ('api_auth_allowed','extra_usage_allowed','purchased_credit_spend_allowed','auto_recharge_allowed'):self.assertIs(policy[key],False,key)
  self.assertEqual((policy['fallbacks'],policy['models'],policy['private_repository']),([],{'codex':'gpt-6.1-sol','claude':RUNNER.MODEL},True))
 def test_active_entrypoints_record_mcp_profiles_and_free_imagineart_exception(self):
  for r in ['AGENTS.md','CLAUDE.md','docs/AI_WORKFLOW.md','docs/AI_ENVIRONMENT.md']:
   s=(ROOT/r).read_text()
   for phrase in ('--mcp-profile','unity-read','unity-write','desktop','asset_gen','analyze','execute_code','--allow-test','ImagineArt','무료 크레딧','NOT_RUN'):self.assertIn(phrase,s,r)
if __name__=='__main__':unittest.main(verbosity=2)
