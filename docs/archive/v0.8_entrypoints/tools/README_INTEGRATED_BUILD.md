# 전체 설계 PDF 재생성

현재 설계 원본은 docs/design_v0.8.md다. Python 환경에 reportlab, pymupdf, Pillow를 설치한 뒤 다음을 실행한다.

```sh
python tools/check_integrated_release.py
python tools/render_integrated_design.py
```

출력: docs/Memory_Forge_v0.8_Integrated_Design.pdf.
한국어 TTF가 설치되어 있으면 사용하고 없으면 PDF CJK 기본 글꼴을 사용한다. 글꼴 원본 파일은 ZIP에 넣지 않는다. 렌더된 모든 페이지의 줄바꿈·표·잘림을 실제 배포 전에 확인한다.

기존 tools/gen_docs.mjs는 구판의 데이터 표 생성기다. 결과를 곧바로 현재 설계라고 배포하지 않는다. 새 통합본에 반영한 후 상태표·생성 데이터·원장 변경을 대조한다.
