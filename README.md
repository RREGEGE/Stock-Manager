# Stock-Manager — 포트폴리오 대시보드

삼성증권 보유종목의 수량·평균매입단가를 화면에서 직접 입력하고, 현재가는 한국투자증권 KIS Developers 시세 API로 갱신해
총 평가금액·종목별 비중·그룹(주식/채권/배당) 비중과 추가매수 배분을 보여주는 개인용 웹 대시보드입니다.

- 설계서: [`docs/design.md`](docs/design.md) (개발 기준 문서)
- 화면 목업: [`docs/mockup/`](docs/mockup/)

## 구성

| 프로젝트 | 역할 |
| --- | --- |
| `Portfolio.Core` | 도메인 모델, 비중 계산, 추가매수 배분(Rebalancer), `IPriceProvider` |
| `Portfolio.Kis` | KIS 시세 클라이언트, 토큰 관리, 종목 마스터 갱신 |
| `Portfolio.Data` | EF Core + SQLite, 리포지토리, 시드 데이터 |
| `Portfolio.Web` | Blazor Web App (Interactive Server) |
| `Portfolio.Tests` | xUnit 테스트 |

## 빌드·테스트

.NET 10 SDK가 필요합니다.

```bash
dotnet build Portfolio.sln
dotnet test Portfolio.sln
```

## 비밀값 규칙 (공개 저장소)

- KIS APP KEY·APP SECRET·계좌번호는 절대 커밋하지 않습니다. 개발 중에는 `dotnet user-secrets`, 운영에서는 암호화 저장(설계서 7.2)을 씁니다.
- `appsettings.json`에는 키 이름만 두고 값은 비웁니다.
- SQLite DB 파일(`*.db`, `*.db-shm`, `*.db-wal`)은 실제 보유 내역이 담기므로 `.gitignore`로 제외합니다.
