# 성인 캐릭터 모먼트 원본 — 2026-10-18

## 현재 보정 — Unity 시각 리뷰 finding 3·4 (2026-10-18)

18장면·215박자의 표시 내레이션 102곳을 점검했다. 18곳의 제작 지시를 캐릭터 README의 비표시 박자표로 옮기고 산문으로 고쳤으며 발화 대사 108개는 그대로다. 라비엔 로비의 의자 권유 4·5박자는 모두 `full/face`로 넓혔다. 새 필드는 추가하지 않았다. 현재 JSON 해시·분량·정적 검증은 [VALIDATION.json](VALIDATION.json)의 `review_fixes_20261018`, 상세 인계는 [HANDOFF.md](HANDOFF.md)를 따른다. **이번 보정의 Unity 임포트·재생·시각 수용은 NOT_RUN**이다. 아래는 이미지 2차 패스 기록이다.

**Codex 창작 초안(draft), 사용자 승인 전.** 세라24·라비엔28·하린26·에일라23·시온27·루미22의 나이·기본 디자인·기존 대사를 유지한다.

현재 산출물은 18장면·215박자와 **실제 PNG 42개**다. 6명 로비 A와 세라 B/C는 선언 이미지가 모두 존재한다. 나머지 5명 B/C는 이미지가 일부 부족하다. 대기 그림은 6명 모두 지정했다.

모든 PNG의 현재 경로는 각 캐릭터 moments.json, ready/todo와 검사 결과는 [VALIDATION.json](VALIDATION.json), 제작 범위·검증 한계·다음 이미지 목록은 [HANDOFF.md](HANDOFF.md)를 따른다.

이번 2차 패스는 선택 파일39개를 추가했다. 내장 구독 image_gen 선택본37개와 기존 공방 배경 바이트 복사본2개다. 42회 생성/수정의 정확한 프롬프트·참조·원본 경로·해시는 [generation-records.json](production-20261018/generation-records.json)에 있다. 에일라 등불/종은 은색 수정본 v02를 사용한다.

인물은 1024×1536 RGBA 투명 PNG, 배경은 1672×941 PNG다. 실제 포즈 컷과 기존 슬롯 이동/프레임 데이터를 구별한다. 신규 리그·연속 관절 동작·녹음·Unity 재생을 완성한 것으로 세지 않는다.

기준은 art/requests/character-moments-20261018.md, docs/MOMENTS_FORMAT.md, docs/AI_WORKFLOW.md §18, OWNERSHIP.md와 기존 캐릭터 브리프다. 입력 이력은 [SOURCE-RECORD.json](SOURCE-RECORD.json), 이전 패스 문서는 production-20261018/ 안에 보관했다.

외부 API·별도 유료 생성·Claude 호출·게임 코드/Unity 편집·커밋·push 없이 art/moments/ 안에서 제작했다. **새 이미지의 Unity 임포트/재생과 사용자 승인은 별도 미완료**다.
