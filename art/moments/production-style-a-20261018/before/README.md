# 성인 캐릭터 모먼트 원본 — 2026-10-18 round 3

**Codex 창작 draft.** 세라24·라비엔28·하린26·에일라23·시온27·루미22의 기존 디자인·나이·대사·입력·의도를 유지한다.

18장면·215박자 모두 필요한 그림/배경이 있다. **116개 매핑 / 고유 PNG 114개 / 이미지 todo 0.** 독립 표정은 성인마다 6개, 총 **36개 512×512 RGBA**다. 이번 선택 신규 PNG는 72개(장면/배경36 + 표정36).

라비엔 A의 손 내림·빈 의자·놓인 서류철과 에일라 A의 의자 등받이에 손을 얹는 끝 자세를 그림 및 박자/idle 배정으로 고쳤다. 에일라 초대/끝 카메라는 full/face로 넓혔다. 나머지 다섯 인물의 B/C를 제작했고, 에일라 C의 최종 기다림은 새 돌아봄 포즈의 실제 PNG를 재사용한다.

- [HANDOFF.md](HANDOFF.md): 현재 선택·검증·재사용·미완료·구현 인계.
- [VALIDATION.json](VALIDATION.json): 경로/PNG/ready·보존 데이터·표정 규격 검사 PASS.
- [선택 자산과 해시](production-round3-20261018/selected-assets.json), [프롬프트/참조/검수 기록](production-round3-20261018/records/).
- [이전 인계·JSON·README·검증](production-round3-20261018/before/): 작업 전 기록.

선택 전신은 1024×1536, 배경은 1672×941 PNG다. 표정은 별도 라이브러리이며 기존 장면의 portrait 슬롯에 새로 연결하지 않았다. 신규 리그·중간 프레임·녹음·PSD·AnimationClip·Timeline·VFX는 만들지 않았다.

**신규 Unity 임포트/재생/시각 수용은 NOT_RUN.** 정적 검증과 자체 원본 이미지 검수만 PASS다. 프로젝트 수정은 art/moments/ 안에서 수행했고 게임 코드·Unity·커밋·push·Claude 호출·API/추가 크레딧 경로는 사용하지 않았다. 내장 구독 image_gen만 사용했다.
