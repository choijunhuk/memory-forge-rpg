# 기억 공방 / Memory Forge v0.8 전체 통합 패키지

## 바로 시작
- 설계서: `docs/design_v0.8.md` 및 루트의 PDF
- Codex: `docs/START_CODEX.md`
- Claude: `docs/START_CLAUDE.md`
- 프로토타입: `prototype/index.html`
- 실제 상태: `reports/integration_v080/status.json`

이 버전은 v0.7 전체 내용과 v0.7.1 보완을 해당 장에 합친 **전체 설계본**이다. 원본 PDF와 검토서를 번갈아 읽을 필요가 없다. 기존 확인된 JS 수정과 전체 데이터·C# 일부 코어·DB 스키마·시험·프로토타입을 포함한다.

## 구현 상태
저장 v2는 독립 모듈이며 기존 Run/HTML/서버에는 아직 연결되지 않았다. HTML의 기본 저장은 v0.7 방식이다. §8의 안전한 보존 정책을 연결하는 것이 다음 첫 작업이다. 본편 인장·무료 용량·기록 균열 팀 완화도 통합 목표와 런타임 적용 여부를 구분한다. Unity·운영 서버·실결제는 완성되지 않았다.

## 재현
```sh
node --test engine/tests/*.test.mjs
python3 -m unittest discover -s tests
node tools/build_prototype.mjs --no-data
```
C#·DB·브라우저의 실제 실행 여부는 이번 로그를 확인한다. 과거 PASS를 이번 검사로 합산하지 않는다.

## 보안·운영
개발용 합성 데이터만 사용한다. 실결제·운영 배포·비밀키·실사용자 DB에 접근하지 않는다. 비공개 GitHub 저장소 생성 및 원격 업로드는 이 패키지 생성 과정에서 수행하지 않았다. 폰트 파일·node_modules·빌드 산출물은 배포 ZIP에 넣지 않는다.
