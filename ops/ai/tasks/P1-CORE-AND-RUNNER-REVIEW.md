# P1-CORE-AND-RUNNER-REVIEW

Independent Opus-only MAX review. No edits or commands/otheragents/modelcalls. User wants continuouscurrent-sessiondevelopment, originalcompletev0.8.1/numberspreserved, no paidmodelAPI/credits/fallback. Worktreebase9be537f with exactcurrentfiles copiedbyCodex; rootownsallUnityEditorwrites, do notconnecttoEditor fromthisdefaultemptyMCPjob.
Review importantcompletedcodeagainstdesign5.3,5.4step6,5.7/5.8card-onlyreflect,5.10ultnotcard,5.14atomicend. Existing3enginefixes (!ctx.isUlt reflect;enemy atk/atkdebuff checkEnd perhit) weresource-reviewedlastsession. Newsame-rootcausefix onplayer dmg loop: innercheckEnd plusouterisOver stops subsequenthits/targets/trailingopsafterlethalreflect. Samefatalhit'sexistingkill/counter/rewardorder preserved.7newrealcardtests red4fail/3pass->7pass,includingimmediate/reserved/AoE/envelope/ownership/idempotency; total133enginetestsfreshPASS beforethisreview.
Also review tools/review/balance_causes.mjs canonicaltargetguard. BothindependentSolMaxreviewersfoundreportmin/maxtrusted; nowusesexistingassessTargets(report),validatesbounds/overall, keepsalloutcomeindependentaggregation/replay/metriccoverage.6realreporttamperregressionspass(oldexploit5fail->fake0nowreject). Before1320exact andfixedsame440starts; reflectchanges8trials81lessnetHP,totaloutcomes/turnsunchanged,32failuresstill. Sampleexpansion40repeats5280gives21failuresbefore/afterwithoutchangingtargets; groupsdifferinparty so arithmeticcontributionsarenotisolatedcausalproof. Newplayerfixmeasurementrootwillrerunagainstnewhash. No64dataenemies/contextsretuning:23candidateonly and20liveunchanged.
Review newofficialCLI runner tools/ai/run_claude.py and16mockedtests. RootprojectsettingsdefaultOpus5.5High,explicit--effortMaxpercorejob,availableModels exactOpusID,nofallback,switchModelsOnFlagfalse,forceclaudeai, strictreadtools,emptyMCP. RunnerrequiresfreshlocalaccountUIattestation andseparatebranch/worktree,queriesactualauthbeforeinferenceunderfilteredchildenv,recordsrawlocalonly andsanitizedRECEIVED_UNVERIFIED/chang verifiedfalse results,validatesreturnedmodelUsage. Attestationisnotserverbillinglock andrequestedEffortmustnotfalselyclaimedobservedthinking. Checkforrealbypasses/misleadingclaims/callerregressions. Currentjobisfirstactualrunnerexec; CLIstatusproofeffortwillbeseparatelycollectedbyroot.
Returnatmost5concreteissues(severity/fileline/repro),distinguishblockingfromnonblocking andNOT_RUNexecution. Ifnone sayno concreteblocker intargetedsource. No formalapprovalwithheldjustnoLSP. Don'tdesignnewpoliciesorclaimgame/C#/Unityfinished. Donotreadcredentials/authfiles. Exactsourcehashes:
{
  "AGENTS.md": "418a809c88a6acf3971dadc985ad35d6e2fde4eb3f384270287f427557aa5a6b",
  "CLAUDE.md": "d00d30f6d2df2aee61a8a14f6b238ca1f95342b95e79ca9fc4899e6d3c6b985c",
  "docs/AI_WORKFLOW.md": "1c95f6e043a091ede45bbcc949329be61413853da0a9a7d0e546721a68c41e6a",
  "docs/START_CLAUDE.md": "ed1d216792c8fd8b097522e876ca7345c715a3bc0afe6d2b6dfce6803813c84c",
  "engine/rules-core.js": "e9fb8765caee6faea4fb06e29a24fc86e181a619bf2766c179c1b983e4c56b9c",
  "engine/tests/player_lethal_reflect.test.mjs": "ca5ec6214b205f32064d8123714d60ad6ad097f5cf167f32fc481bcf34c3465f",
  "engine/tests/reflect_ultimate.test.mjs": "648faebdd901b99713dd91c749bc4c322d38c16136e51bfc4aaf4e00381d7eeb",
  "engine/tests/enemy_lethal_multihit.test.mjs": "7becd72845b61961de378fe83e2fb93d8a72b03e59c36dffb76614822dc796b4",
  "engine/tests/balance_causes.test.mjs": "5ab636882e6d4debb0b05e87a492a14b44eace8a1c02b0c99fbc3c99e1bbdca9",
  "tools/review/balance_causes.mjs": "3db989f8ae7ef64e95ebbc5bb686d79f6761083a095bd1a78fdd35de0d3cbf21",
  "tools/ai/run_claude.py": "bdb33d336c7ba3f36412f98de92a9712cc0f35c878cc773ad7611a97e296dd9f",
  "tests/test_ai_runner.py": "f1b722c56e8c69f70e789044adc26175e0dcd838aa863e80afec1f4981f91588",
  ".claude/settings.json": "441832633e67d7013a9579491b48cfd4b56b20180801774762aa9a3303b72ca0",
  "ops/ai/runtime-policy.json": "eb7079000724456d0c52c945bf945d6bbb1b0aade4391d4efa0724f7e7af89e8"
}
## 검토 이후 확인된 사항과 수정 범위

- 검토 worktree의 prototype/index.html은 기준 커밋 버전이었다. root의 실제 HTML은 3건 수정 후 빌드된 sha256 75984a5fd2d02f91f08f232a1b16274ed5e2de5211db1b18d3d5c546522be98b였고 동일 바이트를 브라우저에서 로드해 검증했다. 최종 수정 후 다시 빌드·복사·검증한다.
- 추가 결함 1: 원문 §5.3·§5.14. 만기 예약의 소멸 후 유물 처치도 즉시 종료하고, 대기 예약은 버린 더미로 이동하며 end 이벤트는 1개여야 한다. 새 post_resolution_end 회귀 시험으로 검증한다.
- 추가 결함 2: 원문 §5.9의 체력 0 패배와 memory_envelope만의 생존 예외를 적용한다. 같은 타격의 처치·보상 우선순위는 유지하고 일반 healParty가 체력 0을 되살리지 않게 한다. 은빛 바늘·봉투·생존 회복을 함께 시험한다.
- 검사기 4: 공식 CLI 입력은 정식 조우×반복·기본 시드/제한에서 재생성한 method/scenarios와 일치해야 한다. 작은 조우를 사용하는 공개 analyze 시험 API와 구분한다.
- 러너 5: 설치 2.1.285의 --restricted는 설정 계층과 파일 도구 범위를 제한한다. 해당 가설은 공식 도움말과 대조했고, 계정 지문 대조와 노력 환경변수 정리는 추가한다.
