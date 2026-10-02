# 화면 목업

claude.ai Design 캔버스로 만든 화면 목업의 원본입니다. 색상·간격·문구·구조를 구현할 때 기준으로 씁니다.
토큰 값과 화면별 명세는 `../design.md` 9장에 정리되어 있으며, 값이 다르면 이 폴더의 원본이 우선입니다.

| 파일 | 화면 | 라우트 | 크기 |
| --- | --- | --- | --- |
| `Main.dc.html` | 대시보드 (데스크톱) | `/` | 폭 1440, 반응형 |
| `Mobile.dc.html` | 대시보드 (모바일, 폭 640px 미만) | `/` | 390 × 844 |
| `Holdings.dc.html` | 보유 종목 입력 | `/holdings` | 폭 1440, 반응형 |
| `Rebalance.dc.html` | 추가매수 계산 | `/rebalance` | 폭 1440, 반응형 |
| `Groups.dc.html` | 그룹 관리 (목표 비중) | `/groups` | 폭 1440, 반응형 |

## 읽는 법

- 각 파일은 일반 HTML + 인라인 CSS입니다. `<x-dc>` 안이 화면 마크업이고, 맨 아래 `<script type="text/x-dc">`의
  `renderVals()`가 예시 데이터와 계산 로직입니다.
- `{{이름}}`은 `renderVals()`가 돌려주는 값이 들어갈 자리, `<sc-for>`는 반복, `<sc-if>`는 조건 표시입니다.
- `Rebalance.dc.html`의 `renderVals()`에는 설계서 5.5절 추가매수 배분 알고리즘의 JavaScript 버전이 들어 있습니다.
  C# 구현 결과와 대조할 때 참고합니다.
- 이 파일은 캔버스 전용 런타임(`support.js`)으로 그려지므로 **브라우저에서 단독으로 열면 화면이 제대로 나오지 않습니다.**
  실제 모양은 아래 원본 캔버스 링크나 `images/`의 이미지로 확인합니다.
- 화면의 종목명·금액은 가상 예시 데이터입니다 (설계서 10.4 시드 데이터와 동일).

## 원본과 이미지

- 원본 캔버스 (claude.ai 로그인 필요): https://claude.ai/artifact/SUeezRWufXu8mf7MwAy8tn
- `images/`: 캔버스에서 각 화면을 이미지로 저장해 `main.png`, `mobile.png`, `holdings.png`, `rebalance.png`,
  `groups.png` 이름으로 넣습니다.
