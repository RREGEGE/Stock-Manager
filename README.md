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

## 실행

### 실제 사용 (본인 기기에서만)

1. `scripts\set-password.cmd` — 로그인 비밀번호 설정 (처음 한 번)
2. `scripts\run.cmd` — 앱 실행 후 http://127.0.0.1:5137 접속
3. KIS 키는 `%LOCALAPPDATA%\Portfolio\settings.json`에 넣습니다 (첫 실행 때 값이 빈 채로 생성)

휴대폰·다른 PC 접속(Tailscale), 홈 화면에 추가, 백업·복원은 [`docs/operations.md`](docs/operations.md)에 있습니다.
실제 DB·백업·KIS 키는 저장소 밖 데이터 폴더(`%LOCALAPPDATA%\Portfolio`)에 저장됩니다.

### 개발용 (가짜 시세)

```bash
dotnet run --project Portfolio.Web --launch-profile http
```

- `http://localhost:5137`로 접속합니다. Development 환경에서는 `PriceSource=Fake`, 로그인 없음으로 동작합니다.
- 별도 DB 파일 `Portfolio.Web/portfolio.dev.db`에 설계서 10.4의 가상 시드 데이터를 넣고, 고정된 가짜 시세를 씁니다. **실제 시세가 아닙니다.**
- 로그인을 끄는 설정은 가짜 시세일 때만 허용됩니다. 실제 시세 구성에서 끄면 앱이 기동하지 않습니다.
- 자동 갱신을 눈으로 확인하려면 가격을 흔드는 옵션을 붙입니다 (5초마다 ±0.5%).

  ```bash
  dotnet run --project Portfolio.Web --launch-profile http -- --FakePrices:FluctuationPercent=0.5 --PricePolling:IntervalSeconds=5
  ```

## 비밀값 규칙 (공개 저장소)

- KIS APP KEY·APP SECRET·계좌번호는 절대 커밋하지 않습니다. 키는 저장소 밖 데이터 폴더의 `settings.json`에만 둡니다 (개인 사용 단계의 간소화, 설계서 7.3).
- `appsettings.json`에는 키 이름만 두고 값은 비웁니다.
- SQLite DB 파일(`*.db`, `*.db-shm`, `*.db-wal`)은 실제 보유 내역이 담기므로 `.gitignore`로 제외합니다.
