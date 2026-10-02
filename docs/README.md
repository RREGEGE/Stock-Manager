# docs

포트폴리오 대시보드(Stock-Manager)의 설계 자료입니다.

| 파일 | 내용 |
| --- | --- |
| `design.md` | 설계서. 기능, 데이터 모델, KIS 연동, 화면, 보안, 배포, 개발 인계. **개발 기준 문서** |
| `mockup/` | 화면 목업 원본 5개와 이미지 |

## design.md 갱신 방법

설계서 원본은 claude.ai 문서입니다. 원본을 수정했다면 문서 이름 클릭 → Export → Markdown으로 내려받아
이 폴더의 `design.md`를 덮어씁니다. 개발 착수 후에는 `design.md`를 직접 수정하는 것을 기준으로 합니다.

내보낸 마크다운에서는 3장 시스템 구성도(그림)가 빠지고 자리표시 문구만 남습니다. 구성은 아래와 같습니다.

```
[브라우저 PC·모바일] --Tailscale VPN·HTTPS--> [호스트 PC · Windows 서비스]
                                              ├ Blazor Web App (화면·로그인/TOTP·보유종목 입력·그룹 관리)
                                              │   ↑ SignalR 푸시
                                              ├ 시세 폴링 서비스 (BackgroundService·비중 계산, 장중 60초 주기)
                                              │   ↓
                                              ├ KIS 클라이언트 (토큰 관리·멀티시세) --REST--> KIS Developers
                                              │   ↓ 토큰 저장
                                              └ SQLite (보유종목·그룹 매핑·스냅샷·토큰)
```
