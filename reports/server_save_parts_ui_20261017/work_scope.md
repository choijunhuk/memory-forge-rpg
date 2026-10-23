# 저장 직전 보유 부품 장착 UI

요구: 원본 §8.12와 SAVE-081-12. 게임 내 역할: 탐사에서 획득한 부품을 종료 뒤에도 카드에 붙이고 서버가 다시 만든 변화를 선택해 영구 기억에 남긴다. Core/서버의 applyPart와 비용/교체/소모/스냅샷 재캡처 의미는 그대로다.

완료: 보유 수량/칸/모든 비무기 대상 접근, 오래된 선택/미리보기 폐기, 모달 상호 배제, 중복/미확정/재접속/동일 요청 복구, 서버 반영/영구 저장 구별. 별도 Opus High UI 작성자는 MemoryForgeServerPracticeClient.cs, 독립 시험 작성자는 새 PracticeServerSavePartsPlayModeTests.cs를 소유한다. Codex가 교차 검토·실제 Unity/실제 Core 검증·통합한다. Editor 쓰기는 Codex 한 명이다.

기준 커밋062caf8/Private PR#2. 실제 개발 fixture는 앞선 기억v1로 승리했고 e_pierce1개를 정상 보상으로 보유한다. fixture 시험과 실제Core·물리 입력/실기기/운영을 구별한다. 신규 의존성/수치/원본·장면 변경 없음.
