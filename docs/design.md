# 포트폴리오 대시보드 설계서

Oct 2, 2026 · @Groot

## 1. 개요

삼성증권 계좌의 보유수량과 평균매입단가는 프로그램 화면에 직접 입력하고, 현재가만 한국투자증권 KIS Developers 시세 API로 자동 갱신해 총 평가금액·종목별 비중·그룹(주식/채권/배당) 비중을 웹 대시보드로 보여주는 개인용 프로그램이다.

| 항목 | 결정 내용 |
| --- | --- |
| 보유 계좌 | 삼성증권 (전 종목) |
| 보유수량·평균단가 | 화면 직접 입력 (종목 검색 후 수량·평균매입단가 입력) |
| 현재가 | KIS Developers 국내주식 현재가 시세 API, 장중 자동 갱신 |
| 예수금 | 화면 직접 입력 |
| 클라이언트 | 웹 (PC·모바일 브라우저) |
| 접속 범위 | 외부망 접속 허용 (모바일 포함) |
| 사용자 | 본인 1인 (다중 사용자 미지원) |
| 주 개발 언어 | C# (.NET 10 LTS) |

**전제 및 범위 제외**

- 삼성증권은 개인용 공개 API가 확인되지 않아 잔고 자동 연동은 하지 않는다 (비공식 출처 기준, 삼성증권 고객센터 확인 권장). 앱·웹 화면 자동 수집은 인증 방식상 불안정하고 약관 위반 소지가 있어 제외한다.
- KIS API 키 발급에는 한국투자증권 계좌가 필요하다. 시세 조회 전용으로 비대면 개설하며, 시세는 보유 계좌와 무관하게 조회된다.
- 보유수량은 매매 후 사용자가 갱신해야 정확하다. 자동 동기화가 없다는 점이 이 구조의 본질적 한계다.
- 주문(매수/매도) 기능은 범위에서 제외한다.

## 2. 기능 요구사항

핵심 기능은 8개이며, F-01\~F-07이 1차 구현 범위다.

| ID | 기능 | 상세 | 우선순위 |
| --- | --- | --- | --- |
| F-01 | 보유종목 입력 | 종목 검색으로 선택 후 수량·평균매입단가 입력, 수정·삭제, 예수금 입력(선택) | 필수 |
| F-02 | 총 보유금액 | Σ(수량 × 현재가) + 예수금, 매입금액·평가손익·수익률 표시 | 필수 |
| F-03 | 종목별 비중 | 종목 평가금액 ÷ 총 평가금액, 도넛 차트 + 정렬 가능한 표 | 필수 |
| F-04 | 그룹 지정·그룹별 비중 | 종목마다 주식/채권/배당 중 1개 지정, 미지정은 '미분류', 그룹 비중 도넛 차트 | 필수 |
| F-05 | 시세 자동 갱신 | 장중 현재가 주기 조회, 장외 시간은 마지막 종가 유지 | 필수 |
| F-06 | 그룹 목표 비중 | 그룹별 목표 비중(%) 입력, 합계 100% 검증, 현재 비중과 차이 표시 | 필수 |
| F-07 | 추가매수 배분 계산 | 투입금액 N원 입력 → 그룹별 매수금액 + 종목별 매수 주수·금액·잔액 표시 (5.5) | 필수 |
| F-08 | 비중 추이 | 일별 스냅샷 저장 후 그룹 비중 변화 라인 차트 | 2차 |

**계산 기준**

- 평가금액 = 수량 × 현재가, 매입금액 = 수량 × 평균매입단가. 평균매입단가는 삼성증권 잔고 화면에 표시된 값을 그대로 옮겨 적는다.
- 예수금 포함 여부는 설정으로 선택한다. 포함 시 '현금' 항목으로 별도 표시한다.
- 그룹은 사용자가 이름을 추가·변경할 수 있게 테이블로 관리한다. 초기값은 주식/채권/배당 3개이며, 1종목 1그룹 원칙으로 한다.
- 종목별 '최종 수정일'을 표시해, 매매 후 수량을 고치지 않은 종목을 알아볼 수 있게 한다.

## 3. 시스템 아키텍처

ASP.NET Core Blazor Web App(Interactive Server) 단일 프로젝트로 화면과 서버를 C#으로 통일하고, KIS 시세 호출은 서버 내부 폴링 서비스에서만 수행한다. 브라우저는 KIS에 직접 접근하지 않으므로 API 키가 클라이언트로 나가지 않는다.

&#91;embedded content: 시스템 구성도 · 서버 4개 구성요소\]

사용자가 화면에서 입력한 보유종목은 SQLite에 저장된다. 폴링 서비스가 보유 종목코드로 현재가를 받아 비중을 계산한 뒤 접속 중인 브라우저에 SignalR로 밀어주고, 장 마감 후 스냅샷을 저장한다.

### 3.1 웹 구현 방식 비교

| 항목 | A. Blazor Web App (Interactive Server) | B. ASP.NET Core Web API + React |
| --- | --- | --- |
| 언어 | C# 단일 | C# + TypeScript |
| 실시간 갱신 | SignalR 연결이 기본 내장 | SignalR JS 클라이언트 별도 구성 |
| 차트 | CSS conic-gradient·div 막대로 직접 구현 (9.1) | Recharts, Chart.js 등 선택지 많음 |
| 모바일 연결 | 서버 연결 유지 필요, 끊기면 재연결 처리 | 정적 파일 + API, 연결 끊김에 강함 |
| 빌드·배포 | 프로젝트 1개 | 프론트·백엔드 2개 |

**A 채택.** 사용자가 1명이라 서버 연결 유지 비용이 문제가 되지 않고, C# 단일 언어로 유지보수 부담이 가장 작다. 모바일 재연결 문제가 실사용에서 불편하면 화면만 B로 교체할 수 있도록 계산 로직은 Core 프로젝트에 둔다.

### 3.2 기술 스택

| 계층 | 선택 | 비고 |
| --- | --- | --- |
| 런타임 | .NET 10 (LTS) | .NET 8은 2026년 11월 지원 종료 예정이므로 신규 개발은 10 기준 |
| 웹 | Blazor Web App, Interactive Server | SignalR 포함 |
| UI 컴포넌트 | 없음 (자체 컴포넌트) | → 순수 Blazor + 컴포넌트별 CSS로 변경 (9.1) |
| DB | SQLite + EF Core | 단일 파일, 별도 DB 서버 불필요 |
| HTTP | HttpClient + IHttpClientFactory | Polly로 재시도 정책 |
| 인증 | ASP.NET Core Identity + TOTP | 7장 참조 |

## 4. 외부 연동 (KIS Developers)

KIS는 시세 조회에만 쓰며, 관심종목 멀티시세 API로 1회 최대 30종목을 받아 장중 60초 주기로 갱신한다. 아래 사양은 [KIS 공식 GitHub 저장소](https://github.com/koreainvestment/open-trading-api) 예제를 인용한 2차 자료 기준이므로, 구현 전 [KIS Developers 포털](https://apiportal.koreainvestment.com) 문서로 재확인한다.

### 4.1 사용 API

| 용도 | Method / Path | tr\_id | 비고 |
| --- | --- | --- | --- |
| 접근토큰 발급 | POST /oauth2/tokenP | - | 유효 24시간, 발급 1분 1회 제한 |
| 멀티종목 시세 | GET /uapi/domestic-stock/v1/quotations/intstock-multprice | FHKST11300006 | 1회 최대 30종목, 기본 사용 |
| 단일종목 현재가 | GET /uapi/domestic-stock/v1/quotations/inquire-price | FHKST01010100 | 종목 추가 시 코드 검증, 멀티시세 실패 시 대체. 응답에 종목명이 없으므로 종목명은 SymbolMaster에서 가져온다 (5.4) |
| 해외주식 현재가 (선택) | GET /uapi/overseas-price/v1/quotations/price | HHDFS00000300 | 해외 보유 시에만 |

출처: [멀티종목 시세](https://algolab.co.kr/blog/kis-api-intstock-multprice-30-2026), [현재가 조회](https://algolab.co.kr/blog/kis-api-inquire-price-fhkst01010100-2026), [토큰 재발급 규칙](https://algolab.co.kr/blog/kis-api-access-token-expiry-refresh-2026), [해외주식 현재가](https://algolab.co.kr/blog/kis-api-overseas-price-hhdfs00000300-2026)

### 4.2 시세 갱신 방식 비교

| 방식 | 장점 | 단점 | 판단 |
| --- | --- | --- | --- |
| A. 멀티시세 주기 폴링 | 30종목당 1회 호출, 구현 단순 | 주기만큼 지연 | **채택** |
| B. 단일 현재가 종목별 폴링 | 응답 필드 단순 | 종목 수만큼 호출 | 대체 경로로만 사용 |
| C. WebSocket 실시간 체결가 | 틱 단위 갱신 | 구독·재연결 관리 필요 | 2차 검토 |

비중 확인 용도에서는 60초 지연이 판단에 영향을 주지 않으므로 A를 채택한다.

### 4.3 호출 정책

1. 장중(평일 09:00\~15:30): 60초 주기로 보유 종목을 30개 단위로 나누어 멀티시세 조회. 주기는 설정값.
2. 장외: 15:40에 1회 조회해 종가 저장, F-08 스냅샷 생성 후 갱신 중지.
3. 종목 추가 직후: 신규 종목만 즉시 1회 조회.
4. 호출량은 종목 60개 기준 분당 2회 수준이라 초당 한도 대비 여유가 크다. 직렬 호출로 충분하다.

### 4.4 토큰 관리

- 발급 토큰과 만료시각을 DB에 저장하고 재시작 시 재사용한다. 매 호출마다 발급하면 1분 1회 제한에 걸린다.
- 만료 10분 전 선제 재발급, 인증 오류 시 1회만 재발급 후 재시도한다. 무한 재시도는 금지.

### 4.5 응답 처리 주의점

- 숫자 필드가 문자열로 수신되므로 `decimal.Parse(…, CultureInfo.InvariantCulture)`로 변환한다.
- `rt_cd != "0"`이면 실패로 처리하고 `msg_cd`, `msg1`을 로그로 남긴다. 시세 조회 실패 시 직전 가격을 유지하고 화면에 '시세 지연'을 표시한다.
- 거래정지·상장폐지 종목은 현재가가 0 또는 미수신일 수 있다. 0원 처리 대신 마지막 유효가를 쓰고 경고를 표시한다.

## 5. 데이터 모델

보유종목·예수금·그룹 매핑은 사용자 입력 원본이라 SQLite에 저장하고, 현재가만 메모리 캐시로 관리한다. DB는 SQLite + EF Core를 사용한다.

### 5.1 테이블

| 테이블 | 주요 컬럼 | 용도 |
| --- | --- | --- |
| Holding | SymbolCode (PK), SymbolName, Quantity, AvgPrice, GroupId (FK), UpdatedAt | 보유종목 (화면 입력) |
| SymbolMaster | SymbolCode (PK), SymbolName, Market | 종목 검색용 마스터 |
| CashBalance | Id, Amount, UpdatedAt | 예수금 |
| AssetGroup | Id, Name, Color, SortOrder, TargetWeight | 그룹 정의 (초기: 주식/채권/배당) |
| PriceCache | SymbolCode (PK), Price, PrevClose, FetchedAt | 마지막 유효가 (재시작·장외 표시용) |
| ApiToken | Id, AccessToken, ExpiresAt | KIS 토큰 캐시 (암호화 저장) |
| DailySnapshot | Date, SymbolCode, Quantity, ClosePrice, GroupId | 일별 종가 스냅샷 (F-08) |
| AppSetting | Key, Value | 폴링 주기, 예수금 포함 여부 등 |

### 5.2 메모리 모델 (C#)

```csharp
public sealed record HoldingView(
    string SymbolCode,
    string SymbolName,
    string GroupName,
    long Quantity,
    decimal AvgPrice,
    decimal CurrentPrice,   // 멀티시세 응답의 현재가
    bool IsStale)           // 시세 지연·실패 여부
{
    public decimal PurchaseAmount => Quantity * AvgPrice;
    public decimal EvalAmount     => Quantity * CurrentPrice;
    public decimal ProfitLoss     => EvalAmount - PurchaseAmount;
}

public sealed record PortfolioSnapshot(
    DateTimeOffset PricedAt,
    IReadOnlyList<HoldingView> Holdings,
    decimal Cash);
```

### 5.3 비중 계산

```latex
E_i = q_i \cdot p_i, \qquad W_i = \frac{E_i}{\sum_{k} E_k + C \cdot \delta}, \qquad W_g = \sum_{i \in g} W_i
```

q = 보유수량, p = 현재가, E = 종목 평가금액, C = 예수금, δ = 예수금 포함 설정(1 또는 0), g = 그룹. 그룹 미지정 종목은 '미분류'로 합산한다. 표시는 소수점 1자리 반올림이며, 반올림 합계가 100.0%가 아닐 수 있음을 각주로 표기한다.

### 5.4 종목 입력 규칙

1. 사용자는 종목명 또는 종목코드로 검색해 종목을 고른다. 검색은 SymbolMaster를 대상으로 하며, 사용자가 코드를 외울 필요가 없다. 종목코드는 6자리 숫자만이 아니라 영문이 섞인 6자리(예: `0001A0`)와 ETN의 `Q`로 시작하는 7자리(예: `Q500067`)도 있으므로 숫자만 받도록 제한하지 않는다.
2. 선택 후 수량(정수, 1 이상)과 평균매입단가(원, 0 초과)만 입력한다. 소수점 주식은 지원하지 않는다.
3. 이미 등록된 종목을 다시 고르면 신규 추가가 아니라 해당 행 수정으로 처리한다.
4. 저장 즉시 해당 종목 현재가를 1회 조회해 화면에 반영한다.
5. SymbolMaster는 KIS가 공개 주소로 제공하는 종목 마스터 파일(`kospi_code.mst.zip`, `kosdaq_code.mst.zip`, 인증 불필요)로 주 1회 갱신한다. 파일은 cp949 인코딩의 고정 바이트 폭 레코드이며(단축코드 0\~9, 표준코드 9\~21, 종목명 21\~61, 그룹코드 61\~63바이트), 2026-10-02 실제 파일로 확인했다. 주식(ST)·ETF(EF)·ETN(EN)·리츠(RT)·투자회사(IF/MF/PF)·해외 원주와 DR(FS/DR)만 넣고, 수익증권 펀드(BC)와 신주인수권(SW/SR)은 제외한다. 갱신에 실패하면 기존 마스터를 유지한다.

### 5.5 추가매수 배분 계산

투입금액 N원을 매도 없이 매수만으로 배분하며, 목표 비중보다 이미 많은 그룹에는 배정하지 않고, 부족한 그룹끼리는 매수 후 목표 비율이 같아지도록 나눈다.

**전제**

- 계산 기준 자산은 보유종목 평가금액 합이다. 예수금은 그룹이 아니므로 기준에서 제외하고, N원만 새로 투입하는 것으로 본다.
- 그룹 목표 비중 합계는 100%여야 계산을 실행한다. '미분류'는 목표 0%로 고정되며, 미분류 종목이 있으면 계산 전에 경고한다.
- 수수료·세금은 반영하지 않는다. 주수는 정수(소수점 주식 미지원)이므로 배분 금액과 실제 매수 금액 사이에 잔액이 생긴다.

**그룹 배분 방식 비교**

| 방식 | 계산 | 장점 | 단점 | 판단 |
| --- | --- | --- | --- | --- |
| A. 수위 맞추기 | 초과 그룹을 제외하고, 남은 그룹들의 '평가금액 ÷ 목표비중'이 같아지게 배분. 음수가 나오는 그룹은 제외 후 반복 | 부족 그룹 간 목표 비율이 정확히 유지됨 | 반복 계산 필요 (그룹 수만큼 이내) | **채택** |
| B. 부족액 비례 | 각 그룹 부족액(목표금액 − 현재금액)에 비례해 N원 분배 | 계산 1회 | 부족 그룹 간 비율이 목표와 어긋남 | 비채택 |

**계산 예시** (현재 1억 원, 목표 주식 50% / 채권 30% / 배당 20%, 투입 1,000만 원)

| 그룹 | 현재 금액 | 목표 금액 (1억 1,000만 기준) | A 매수 | A 매수 후 비중 | B 매수 | B 매수 후 비중 |
| --- | --- | --- | --- | --- | --- | --- |
| 주식 | 6,000만 | 5,500만 | 0 | 54.5% | 0 | 54.5% |
| 채권 | 2,000만 | 3,300만 | 1,000만 | 27.3% | 867만 | 26.1% |
| 배당 | 2,000만 | 2,200만 | 0 | 18.2% | 133만 | 19.4% |

A에서 주식은 초과라 제외되고, 채권·배당만으로 계산하면 매수 후 채권 3,000만 : 배당 2,000만 = 30 : 20으로 목표 비율과 같아진다. N이 충분히 크면 모든 그룹이 목표 비중에 정확히 도달한다.

**종목별 주수 환산**

1. 그룹 배정 금액을 그룹 내 종목의 현재 평가금액 비율로 나눈다. 평가금액 합이 0이면 균등 분할한다.
2. 종목별 금액 ÷ 현재가를 내림해 주수를 구한다.
3. 남은 잔액으로 살 수 있는 종목 중 '배정 금액 − 실제 매수 금액'이 가장 큰 종목을 1주씩 추가하며, 더 살 수 없을 때 멈춘다.
4. 시세 지연·0원 종목은 제외한다. 목표 비중은 있는데 보유 종목이 없는 그룹은 '종목 없음'으로 표시하고 배정 금액 전체를 잔액으로 남긴다.

**구현 코드 (Portfolio.Core)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record GroupState(int GroupId, string Name, decimal TargetWeight, decimal CurrentValue); // TargetWeight: 0~1
public sealed record GroupAllocation(int GroupId, string Name, decimal BuyAmount, decimal ValueAfter, decimal WeightAfter);
public sealed record StockOrder(string SymbolCode, string SymbolName, decimal Price, long Shares, decimal Amount);

public static class Rebalancer
{
    // 그룹별 매수금액 (매수만, 방식 A)
    public static IReadOnlyList<GroupAllocation> AllocateBuyOnly(IReadOnlyList<GroupState> groups, decimal newMoney)
    {
        if (newMoney <= 0) throw new ArgumentOutOfRangeException(nameof(newMoney));
        if (Math.Abs(groups.Sum(g => g.TargetWeight) - 1m) > 0.0001m)
            throw new ArgumentException("목표 비중 합계가 100%가 아닙니다.");

        var active = groups.Where(g => g.TargetWeight > 0).ToList();
        decimal level;
        while (true)
        {
            level = (active.Sum(g => g.CurrentValue) + newMoney) / active.Sum(g => g.TargetWeight);
            var over = active.Where(g => g.TargetWeight * level < g.CurrentValue).ToList();
            if (over.Count == 0) break;
            active = active.Except(over).ToList(); // 매수액 합이 N > 0 이므로 active는 비지 않음
        }

        decimal totalAfter = groups.Sum(g => g.CurrentValue) + newMoney;
        return groups.Select(g =>
        {
            decimal buy = active.Contains(g) ? g.TargetWeight * level - g.CurrentValue : 0m;
            decimal after = g.CurrentValue + buy;
            return new GroupAllocation(g.GroupId, g.Name, Math.Round(buy, 0), after, after / totalAfter);
        }).ToList();
    }

    // 그룹 배정 금액 → 종목별 주수
    public static (IReadOnlyList<StockOrder> Orders, decimal Leftover) ToShares(
        IReadOnlyList<HoldingView> groupHoldings, decimal budget)
    {
        var valid = groupHoldings.Where(h => h.CurrentPrice > 0 && !h.IsStale).ToList();
        if (valid.Count == 0 || budget <= 0) return (Array.Empty<StockOrder>(), budget);

        decimal baseSum = valid.Sum(h => h.EvalAmount);
        var target = valid.ToDictionary(h => h.SymbolCode,
            h => baseSum > 0 ? budget * h.EvalAmount / baseSum : budget / valid.Count);
        var shares = valid.ToDictionary(h => h.SymbolCode,
            h => (long)Math.Floor(target[h.SymbolCode] / h.CurrentPrice));

        decimal left = budget - valid.Sum(h => shares[h.SymbolCode] * h.CurrentPrice);
        while (true)
        {
            var pick = valid.Where(h => h.CurrentPrice <= left)
                .OrderByDescending(h => target[h.SymbolCode] - shares[h.SymbolCode] * h.CurrentPrice)
                .FirstOrDefault();
            if (pick is null) break;
            shares[pick.SymbolCode]++;
            left -= pick.CurrentPrice;
        }

        var orders = valid.Where(h => shares[h.SymbolCode] > 0)
            .Select(h => new StockOrder(h.SymbolCode, h.SymbolName, h.CurrentPrice,
                shares[h.SymbolCode], shares[h.SymbolCode] * h.CurrentPrice))
            .ToList();
        return (orders, left);
    }
}
```

단위 테스트는 위 계산 예시 3행을 기대값으로 고정하고, N = 0, 목표 합계 ≠ 100%, 보유 종목 없는 그룹, 1주 가격이 배정 금액보다 큰 경우를 포함한다.

**2차 검토 항목**

- 그룹 간 잔액 재배분: 그룹별로 남은 잔액을 합쳐 다른 그룹 종목을 1주 더 사는 처리.
- 그룹 내 종목별 목표 비중: 현재 평가금액 비례 대신 종목마다 그룹 내 목표 비중을 지정.

## 6. 화면 설계

화면은 로그인 포함 6개이며, 모바일 세로 화면(폭 360px)에서도 차트와 표가 한 열로 쌓이도록 반응형으로 만든다.

| 화면 | 구성 요소 | 동작 |
| --- | --- | --- |
| 로그인 | ID/비밀번호, TOTP 6자리 | 7장 인증 정책 적용 |
| 대시보드 (메인) | 요약 카드: 총 평가금액, 매입금액, 평가손익, 수익률, 시세 갱신 시각 / 그룹 비중 도넛 / 종목 비중 도넛 / 그룹별 현재 vs 목표 비중 막대 | 장중 60초 자동 갱신(SignalR 푸시), 차트 조각 클릭 시 해당 그룹·종목 필터 |
| 보유 종목 | 표: 종목명, 그룹, 수량, 평균매입단가, 현재가, 평가금액, 비중(%), 손익률, 최종 수정일 / 예수금 입력란 | \[추가\] 버튼 → 입력창(종목 검색, 수량, 평균매입단가, 그룹) / 행 클릭 시 같은 입력창으로 수정 / 삭제는 확인 후 처리 |
| 추가매수 계산 | 투입금액 N원 입력란 / 그룹별 표: 현재 비중, 목표 비중, 매수금액, 매수 후 비중 / 종목별 표: 종목명, 현재가, 매수 주수, 매수금액 / 잔액 합계 | \[계산\] 시 5.5 방식으로 산출. 계산 결과는 저장하지 않으며 주문 기능 없음. 계산 시점 시세 시각 표시 |
| 그룹 관리 | 그룹 목록(이름, 색상, 순서, 목표 비중 %), 목표 합계 표시, 미분류 종목 목록 | 그룹 추가·이름변경·삭제(삭제 시 소속 종목은 미분류로 이동), 목표 합계가 100%가 아니면 저장 불가 |
| 설정 | 폴링 주기, 예수금 포함 여부 | 변경 즉시 적용 |

**차트 표시 규칙**

- 종목 수가 10개를 넘으면 비중 상위 9개 + '기타'로 묶는다.
- 그룹 색상은 그룹 관리에서 지정한 색을 두 차트에서 동일하게 사용한다.
- 미분류 종목이 있으면 대시보드 상단에 그룹 지정 알림을 띄운다.
- 시세 지연 종목은 표에서 회색 표시하고, 대시보드에 지연 종목 수를 표시한다.

## 7. 보안 설계

보호 대상은 삼성증권 보유 내역(자산 규모·종목)과 KIS APP KEY다. KIS 계좌는 시세 조회용이라 잔고가 없어도, 키가 유출되면 해당 계좌로 주문·조회가 가능하므로 외부 접속은 포트 개방 없이 터널로만 열고 키는 서버 밖으로 나가지 않게 한다.

### 7.1 외부 접속 방식 비교

| 방식 | 장점 | 단점 | 판단 |
| --- | --- | --- | --- |
| A. 공유기 포트포워딩 + DDNS | 추가 서비스 불필요 | 공인 IP에 포트 노출, 인증서·방화벽 직접 관리, 공격 표면 큼 | 비권장 |
| B. Tailscale (WireGuard VPN) | 포트 개방 없음, 등록된 기기만 접속, 무료 개인 플랜 | 모바일에 앱 설치 필요, 등록 기기 외 접속 불가 | **권장** |
| C. Cloudflare Tunnel + Access | 포트 개방 없음, 브라우저만으로 접속, 도메인 HTTPS 자동 | 도메인 필요, 트래픽이 Cloudflare 경유 | 대안 |
| D. 클라우드 VM 배포 | PC 꺼져도 접속 가능 | 월 비용, 키가 외부 서버에 상주 | 비권장 |

개인 1인 사용이고 접속 기기가 정해져 있으므로 B를 기본으로 한다. 기기 미설치 환경(회사 PC 등)에서도 접속이 필요하면 C로 전환한다. 서비스 요금·정책은 각 사이트에서 확인 필요.

### 7.2 애플리케이션 보안

- 인증: ASP.NET Core Identity 단일 계정 + TOTP 2차 인증. 로그인 5회 실패 시 15분 잠금.
- API 키 보관: appsettings에 평문 저장 금지. Windows DPAPI(`ProtectedData`) 또는 ASP.NET Core Data Protection으로 암호화해 DB/파일에 저장.
- 권한 최소화: 서버 코드에 주문 API 호출 경로를 두지 않는다 (KIS 키 자체는 주문 권한을 포함하므로 코드 수준 차단).
- 전송: HTTPS 필수. Tailscale 사용 시에도 Kestrel HTTPS 적용.
- 로그: 토큰·APP SECRET·계좌번호 전체는 로그에 남기지 않는다 (계좌번호는 뒤 4자리 마스킹).

### 7.3 개인 사용 단계의 간소화 (2026-10-05 결정)

본인 1명이 본인 기기로만 쓰는 동안에는 7.1·7.2를 아래처럼 줄여 적용한다. **다른 사람에게 배포하거나 인터넷에 공개하게 되면 7.1·7.2 원안으로 돌아가 다시 검토한다.**

| 항목 | 원안 (7.1, 7.2) | 개인 사용 단계 적용 | 배포 시 재검토 |
| --- | --- | --- | --- |
| 접속 경로 | Tailscale | Tailscale만. 앱은 127.0.0.1에서만 듣고 `tailscale serve`로 본인 기기에 연다. 공유기 포트 개방·Funnel(인터넷 공개) 금지 | Tailscale을 설치할 수 없는 기기에서 접속해야 하면 로그인 강화가 먼저 |
| 전송 | Kestrel HTTPS | `tailscale serve`가 HTTPS(인증서 자동 발급·갱신)를 맡고, 앱은 PC 내부에서 HTTP | 직접 노출 시 Kestrel HTTPS |
| 인증 | ID/비밀번호 + TOTP, 5회 실패 시 15분 잠금 | 계정 1개(아이디 + 비밀번호, 비밀번호는 해시 저장), 기기마다 90일 유지, 연속 5회 실패 시 1분 차단. TOTP 없음. 여러 PC·휴대폰에서 같은 계정으로 동시에 로그인 | TOTP, 계정 잠금 정책 |
| 가입·비밀번호 변경 | - | 웹 화면에서 한다. 가입은 계정이 아직 없을 때만 열리며 어느 기기에서든 가능(먼저 접속한 기기가 계정을 만든다). 비밀번호 변경은 설정 화면에서 현재 비밀번호를 확인한 뒤. 변경하면 다른 기기는 모두 다시 로그인. 비밀번호를 잊으면 앱을 실행하는 PC의 `set-password` 명령으로 다시 정한다 | 가입을 호스트 PC로 제한하거나 초대 방식으로 변경 |
| KIS 키 보관 | 암호화해 DB/파일에 저장 | 저장소 밖 데이터 폴더의 `settings.json`에 평문. 접근토큰은 암호화 저장(원안대로) | 암호화 저장, 설정 화면 입력 |
| 주문 차단 | 주문 경로를 두지 않음 | 원안 + KIS 요청을 토큰 발급·시세 조회 경로로만 제한하는 필터 | 유지 |
| 실행 형태 | Windows 서비스, 부팅 시 자동 시작 | 실행 스크립트로 수동 실행. 자동 시작 없음 | Windows 서비스 |

**이 단계에서 감수하는 위험**

- 아이디·비밀번호를 아는 사람이 본인 기기(또는 Tailscale에 등록된 기기)를 쓰면 보유 내역을 보고 고칠 수 있다.
- 계정을 만들기 전에 앱에 접속할 수 있는 사람은 먼저 가입해 계정을 차지할 수 있다. 설치 직후 바로 가입해 두고, 가입 전에는 앱을 다른 기기에 열지 않는다.
- 이 PC의 파일을 읽을 수 있는 사람·프로그램은 `settings.json`의 KIS 키를 볼 수 있다. KIS 계좌는 시세 조회용으로만 쓰고 잔고를 두지 않는 전제(7장 머리말)를 지킨다.
- 회사 PC처럼 Tailscale을 설치할 수 없는 기기에서는 접속하지 않는다 (휴대폰으로 본다).

## 8. 배포·구조·일정

상시 켜둔 Windows PC에 Windows 서비스로 설치하고, 1차 범위(F-01\~F-07)는 4단계로 개발한다.

### 8.1 배포

- 실행 형태: ASP.NET Core 앱을 `UseWindowsService()`로 Windows 서비스 등록, 부팅 시 자동 시작.
- 호스트 PC가 꺼지면 외부 접속도 불가하다. 상시 가동 PC가 없으면 미니PC 또는 NAS(Docker) 배치를 검토한다.
- 백업: SQLite 파일 일 1회 복사. 보유종목 원본이 DB에만 있으므로 백업이 필수다.
- 개인 사용 단계(7.3)에서는 서비스로 등록하지 않고, `scripts/install.cmd`로 만든 실행 파일(바탕화면 바로가기)로 실행한다. 설치 폴더는 저장소 폴더 옆의 `Portfolio`이며(`app\` 실행 파일, `data\` 실제 DB·백업(최근 14일분)·KIS 키 설정 파일), 사용자 프로필(C 드라이브)에는 아무것도 두지 않는다. 실행·접속 절차는 `docs/operations.md`에 있다.

### 8.2 솔루션 구조

```
Portfolio.sln
├─ Portfolio.Core        // 도메인 모델, 비중 계산, IPriceProvider 인터페이스
├─ Portfolio.Kis         // KIS 시세 클라이언트, 토큰 관리, 종목 마스터 갱신
├─ Portfolio.Data        // EF Core + SQLite, 리포지토리
├─ Portfolio.Web         // Blazor Web App, SignalR, 인증, 시세 폴링 BackgroundService
└─ Portfolio.Tests       // xUnit: 비중 계산, 입력 검증, KIS 응답 파싱(샘플 JSON)
```

시세 출처를 `IPriceProvider`로 분리해 두면, KIS 외 다른 시세 소스로 바꿀 때 화면·계산 코드를 건드리지 않아도 된다.

### 8.3 개발 단계

1. 데이터·계산: SQLite 스키마, 보유종목 입력·수정·삭제, 비중 계산, 목표 비중·추가매수 배분 계산 + 단위 테스트
2. KIS 시세: 토큰 관리, 종목 마스터, 멀티시세·단일 현재가 조회, 장중 폴링
3. 화면: 대시보드·보유종목·추가매수 계산·그룹관리, 차트, SignalR 자동 갱신
4. 보안·배포: 로그인/TOTP, 키 암호화, Windows 서비스, Tailscale 접속 확인

### 8.4 미확정 사항

- [ ] 채권 보유 형태: 채권 ETF(현재가 조회 가능)인지, 채권 직접 보유(KIS 채권 시세 API 또는 수동 단가 입력 필요)인지
- [ ] 해외주식·펀드·RP 등 보유 여부: 있으면 해외 시세 + 환율, 또는 평가금액 수동 입력 항목 추가
- [ ] 한국투자증권 계좌 개설 가능 여부 (KIS API 키 발급 전제)
- [ ] 호스트 PC: 상시 가동 PC 유무

## 9. UI 디자인 명세

화면은 [디자인 목업](https://claude.ai/artifact/SUeezRWufXu8mf7MwAy8tn)을 기준으로 구현하며, 목업의 색상·간격·문구를 그대로 쓴다. 목업 값과 이 절의 값이 다르면 목업이 우선이다.

### 9.1 UI 구현 방식 비교

| 방식 | 장점 | 단점 | 판단 |
| --- | --- | --- | --- |
| A. 순수 Blazor + 컴포넌트별 CSS (`.razor.css`) | 목업과 동일한 모양 재현, 외부 의존성 없음, 도넛은 CSS `conic-gradient`로 구현 가능 | 정렬·대화상자 등 직접 구현 | **채택** |
| B. MudBlazor | 표·대화상자·입력 컴포넌트 기본 제공 | Material 스타일이라 목업과 맞추려면 테마·CSS 덮어쓰기 필요 | 비채택 |

목업의 차트는 도넛 2개와 막대뿐이라 차트 라이브러리 없이 구현한다. 3.2 기술 스택의 UI 컴포넌트 항목도 A로 변경한다.

### 9.2 디자인 토큰

| 구분 | 이름 | 값 | 사용처 |
| --- | --- | --- | --- |
| 배경 | bg-page | #F5F5F2 | 페이지 바탕 |
| 배경 | bg-card | #FFFFFF | 카드, 입력창 |
| 선 | line-card | #E3E3DE | 카드 테두리, 표 헤더 밑줄 |
| 선 | line-row | #EFEFEA | 표 행 구분선 |
| 선 | line-input | #C9C9C2 | 입력창·보조 버튼 테두리 |
| 배경 | bg-track | #ECECE7 | 막대 차트 바탕 |
| 글자 | text | #1B1F24 | 본문, 목표선 |
| 글자 | text-muted | #5F646B | 보조 설명, 표 헤더 |
| 헤더 | header-bg | #1B1F24 | 상단 바 |
| 헤더 | header-text | #C9CCD1 | 비활성 메뉴 |
| 헤더 | header-active | #343940 | 활성 메뉴 배경 |
| 강조 | primary | #23395B | 주 버튼, 링크, 포커스 테두리 |
| 강조 | primary-soft | #EEF1F6 | 선택된 행·검색 결과 |
| 그룹 | group-stock | #23395B | 주식 (종목 음영: #3F5B86, #7189AE) |
| 그룹 | group-bond | #E08A2E | 채권 (종목 음영: #F2B56E) |
| 그룹 | group-dividend | #6BB3A8 | 배당 (종목 음영: #4E9B90, #A3D3CB) |
| 손익 | up | #C0312A | 상승 (모바일 어두운 헤더 위: #FF8A80) |
| 손익 | down | #1F5FAF | 하락 |
| 상태 | over | 배경 #FBEBDD / 글자 #8A4A0F | 목표 초과 |
| 상태 | under | 배경 #E3ECF7 / 글자 #1F4C86 | 목표 부족 |
| 상태 | ok | 배경 #E4F3EC / 글자 #1E6B47 | 목표 도달, 합계 정상 |
| 상태 | error | #B42318 | 합계 오류 |
| 상태 | live | #4CC38A | 장중 표시 점 |
| 상태 | disabled | #9AA0A8 | 비활성 버튼 배경 |

그룹 색상은 그룹 관리에서 바꿀 수 있으므로 DB의 AssetGroup.Color가 우선이고, 위 값은 초기값이다. 주식 그룹 위 글자는 흰색, 채권·배당 그룹 위 글자는 #1B1F24로 한다 (대비 확보).

**타이포그래피**: IBM Plex Sans KR (400/500/600/700, Google Fonts), 대체 글꼴 Apple SD Gothic Neo, Malgun Gothic. 숫자는 `font-variant-numeric: tabular-nums`로 자릿수를 맞춘다.

| 용도 | 크기 / 굵기 |
| --- | --- |
| 페이지 제목 | 26px / 700 |
| 요약 카드 금액 | 30px / 700 (단위 '원'은 16px / 500) |
| 카드 제목 | 16px / 600 |
| 본문, 표 | 14px / 400 |
| 보조 설명, 표 헤더 | 13px / 400\~500 |
| 각주 | 12px / 400 |

**레이아웃**: 본문 최대 폭 1200px, 좌우 여백 24px, 상단 32px. 섹션 간격 24px, 카드 간격 16px. 카드는 반경 12px, 안쪽 여백 22px. 버튼·입력창은 최소 높이 44px, 반경 8px.

### 9.3 화면별 명세

| 화면 | 라우트 | 구성 (목업 순서) | 데이터 출처 |
| --- | --- | --- | --- |
| 대시보드 | `/` | 제목 + '추가매수 계산하기' 버튼 / 요약 카드 4개 (총 평가금액, 매입금액, 평가손익, 보유 종목 수) / 그룹 비중 도넛, 종목 비중 도넛, 목표 대비 막대 / 보유 종목 표 (종목명, 그룹, 평가금액, 비중, 손익률) | PortfolioSnapshot (SignalR 갱신) |
| 보유 종목 | `/holdings` | 종목 표 (종목명, 그룹, 수량, 평균매입단가, 현재가, 평가금액, 손익률, 수정일, 수정 버튼) + 예수금 입력·포함 체크 / 오른쪽 입력 패널 (종목 검색, 수량, 평균매입단가, 그룹, 예상 평가금액, 취소·저장) | Holding, CashBalance, SymbolMaster, PriceCache |
| 추가매수 계산 | `/rebalance` | 투입 금액 입력 + 빠른 추가 버튼 (+100만, +500만, +1,000만, 지우기) + 실제 매수 합계·남는 금액 / 현재·매수 후·목표 누적 막대 / 그룹별 매수금액 표 / 종목별 매수 수량 표 | Rebalancer (5.5), 입력 즉시 재계산 |
| 그룹 관리 | `/groups` | 그룹 표 (색상, 이름, 종목 수, 현재 비중, 목표 비중 입력, 차이) + 합계 행 + 상태 문구 + 되돌리기·저장 / 미분류 종목 카드 | AssetGroup, Holding |
| 설정 | `/settings` | 폴링 주기, 예수금 포함 여부 (목업 없음, 같은 카드 양식) | AppSetting |
| 로그인 | `/login` | ID, 비밀번호, TOTP 6자리 (목업 없음, 가운데 카드 1개) | Identity |

**표시 규칙**

- 금액은 천 단위 쉼표 + '원', 비중은 소수점 1자리 %, 손익률은 소수점 2자리 %와 부호, 차이는 소수점 1자리 %p.
- 목표 대비 막대: 막대 길이 = 현재 비중, 검은 세로선(2px) = 목표 비중. 차이 표시는 ±0.05%p 이내면 '목표 도달'.
- 추가매수 누적 막대: 구간 폭이 8% 미만이면 구간 안 숫자를 숨긴다.
- 상단 바 오른쪽에 장 상태와 마지막 시세 갱신 시각 (예: '장중 · 09:41 시세 갱신')을 항상 표시한다.

### 9.4 반응형 규칙

- 요약 카드: `grid-template-columns: repeat(auto-fit, minmax(220px, 1fr))`, 차트 카드: `minmax(340px, 1fr)`.
- 보유 종목·그룹 관리의 2단 배치는 `flex-wrap`으로, 폭이 부족하면 입력 패널이 표 아래로 내려간다.
- 표는 카드 안에서만 가로 스크롤 (`overflow-x: auto`), 페이지 전체는 가로 스크롤 없음.
- 폭 640px 미만: 상단 메뉴를 숨기고 하단 탭 바 (대시보드, 보유 종목, 추가매수, 그룹 관리)를 표시한다. 상단은 총 평가금액·손익을 담은 어두운 헤더로 바뀐다 (모바일 목업 기준).

### 9.5 Blazor 컴포넌트 매핑

| 컴포넌트 | 역할 | 사용 화면 |
| --- | --- | --- |
| `MainLayout` + `TopNav` / `BottomTabs` | 상단 바·하단 탭, 장 상태 표시 | 전체 |
| `SummaryCard` | 제목·금액·보조 문구 카드 | 대시보드 |
| `DonutChart` | `conic-gradient` 도넛 + 범례, 입력: (이름, 색, 비율) 목록 | 대시보드 |
| `TargetBar` | 현재 비중 막대 + 목표선 + 차이 배지 | 대시보드, 모바일 |
| `StackedBar` | 그룹별 누적 막대 1줄 | 추가매수 |
| `HoldingTable` | 보유 종목 표, 열 구성은 매개변수 | 대시보드, 보유 종목 |
| `HoldingEditor` | 종목 검색·수량·단가·그룹 입력 패널 | 보유 종목 |
| `SymbolSearch` | SymbolMaster 검색 + 결과 목록, 보유 중 표시 | 보유 종목 |
| `GroupTargetTable` | 목표 비중 입력 + 합계 검증 | 그룹 관리 |
| `MoneyInput` | 숫자만 입력, 쉼표 자동 표시 | 보유 종목, 추가매수 |

### 9.6 홈 화면 앱 (2026-10-05 추가)

휴대폰 브라우저의 '홈 화면에 추가'로 설치하면 주소창 없이 앱처럼 열리도록 웹 앱 정보 파일(`manifest.webmanifest`)과 아이콘(192·512px, apple-touch-icon)을 둔다. 앱 이름은 '포트폴리오', 표시 방식은 `standalone`, 테마 색은 header-bg(#1B1F24), 배경 색은 bg-page(#F5F5F2)다. 화면은 9.4의 모바일 레이아웃을 그대로 쓰며, 오프라인 동작(서비스 워커)은 넣지 않는다.

## 10. 개발 인계

새 대화에서는 아래 두 링크와 10.5 시작 프롬프트만 주면 이 설계서와 목업을 읽고 바로 개발을 시작할 수 있다. 단계는 8.3 순서를 따르며, 각 단계는 10.3 완료 기준을 모두 통과해야 다음으로 넘어간다.

### 10.1 인계 자료

| 자료 | 위치 | 비고 |
| --- | --- | --- |
| 소스 저장소 | [RREGEGE/Stock-Manager](https://github.com/RREGEGE/Stock-Manager) | 공개 저장소. 2026-10-02 확인 시 커밋 없는 빈 저장소, 기본 브랜치 main |
| 설계서 (저장소 사본) | `docs/design.md` | 이 문서를 마크다운으로 내보낸 파일. **개발 착수 후에는 이 파일이 기준** |
| 목업 원본 (저장소 사본) | `docs/mockup/*.dc.html` | 화면 5개의 원본 코드. 색상·간격·구조 확인용 (브라우저에서 단독 실행은 안 됨) |
| 목업 이미지 | `docs/mockup/images/*.png` | 캔버스에서 사용자가 직접 내보내 추가 |
| 설계서 (claude.ai 원본) | [포트폴리오 대시보드 설계서](https://claude.ai/code/artifact/3470b5a0-c279-417e-8421-dd2bf4fe995f) | claude.ai 채팅에서만 열람 가능 |
| 디자인 목업 (claude.ai 원본) | [포트폴리오 대시보드 목업](https://claude.ai/artifact/SUeezRWufXu8mf7MwAy8tn) | 추가매수 계산·그룹 관리는 Play로 동작 확인 가능 |

claude.ai 원본은 로그인이 필요해 로컬 개발 도구에서는 열리지 않을 수 있다. 그래서 저장소 `docs/`에 사본을 두고, 개발 중 설계 변경은 `docs/design.md`에 직접 반영한다.

### 10.1.1 저장소 사용 규칙

```
git clone https://github.com/RREGEGE/Stock-Manager.git
cd Stock-Manager
```

1. 저장소 루트에 `Portfolio.sln`과 8.2의 프로젝트 폴더를 둔다. 첫 커밋에 `docs/` 폴더, `dotnet new gitignore`로 만든 .gitignore, README.md를 포함한다.
2. **공개 저장소이므로 비밀값은 절대 커밋하지 않는다.** KIS APP KEY·APP SECRET·계좌번호는 개발 중 `dotnet user-secrets`, 운영에서는 7.2의 암호화 저장을 쓴다. `appsettings.json`에는 키 이름만 두고 값은 비운다.
3. SQLite DB 파일(`*.db`, `*.db-shm`, `*.db-wal`)은 .gitignore에 추가한다. 실제 보유 내역이 담기기 때문이다.
4. 단계별로 브랜치를 만들고(`stage1`, `stage2` 등, 번호는 8.3 단계), 해당 단계 AC를 모두 통과하면 main에 병합한다. 커밋 메시지에 AC 번호를 적는다 (예: `AC-03 추가매수 배분 계산`).
5. 커밋·푸시는 사용자의 PC에서 사용자 계정으로 한다. 채팅에서 개발하면 Claude가 만든 파일을 내려받아 커밋하고, 로컬에서 Claude Code를 쓰면 클론한 폴더에서 바로 작업·커밋할 수 있다. 어느 경우에도 GitHub 토큰을 채팅에 입력하지 않는다.

### 10.2 확정된 결정

| 항목 | 결정 | 근거 절 |
| --- | --- | --- |
| 보유 데이터 | 삼성증권 보유분을 화면에서 직접 입력 (수량, 평균매입단가) | 1, 5.4 |
| 시세 | KIS 멀티종목 시세 API, 장중 60초 폴링 | 4 |
| 웹 | Blazor Web App (Interactive Server), .NET 10, 순수 CSS 컴포넌트 | 3, 9.1 |
| DB | SQLite + EF Core | 5 |
| 추가매수 배분 | 매수 전용, 수위 맞추기 방식(A), 그룹 내 현재 평가금액 비례 | 5.5 |
| 외부 접속 | Tailscale + HTTPS, ID/비밀번호 + TOTP. 개인 사용 단계에서는 Tailscale(`tailscale serve`) + 계정 1개(아이디·비밀번호)로 간소화 | 7, 7.3 |
| 범위 제외 | 주문, 삼성증권 자동 연동, 엑셀 가져오기 | 1 |

### 10.3 단계별 완료 기준

| 단계 | ID | 완료 기준 |
| --- | --- | --- |
| 1. 데이터·계산 | AC-01 | 이미 등록된 종목을 다시 추가하면 새 행이 아니라 기존 행이 수정된다 |
| 1. 데이터·계산 | AC-02 | 10.4 시드 데이터로 총 평가금액 100,000,000원, 평가손익 +4,500,000원 (+4.71%), 그룹 비중 60.0 / 20.0 / 20.0%가 나온다 |
| 1. 데이터·계산 | AC-03 | 시드 데이터 + 투입 10,000,000원 → 채권 10,000,000원, 주식·배당 0원. 종목별 국고채 10년 ETF 50주, 미국채 10년 ETF 500주, 남는 금액 0원 |
| 1. 데이터·계산 | AC-04 | 투입 0원, 목표 합계 ≠ 100%, 보유 종목 없는 그룹, 1주 가격 > 배정 금액 각각에 대한 단위 테스트 통과 |
| 2. KIS 시세 | AC-05 | 모의/실전 앱키로 토큰 발급 후 재시작해도 토큰을 재사용한다 (발급 API 재호출 없음) |
| 2. KIS 시세 | AC-06 | 보유 31종목 이상일 때 멀티시세가 30종목 단위로 나뉘어 호출된다 |
| 2. KIS 시세 | AC-07 | 시세 조회 실패 시 직전 가격을 유지하고 해당 종목을 '시세 지연'으로 표시한다 |
| 3. 화면 | AC-08 | 화면 5개가 목업과 같은 배치·색상·문구로 표시된다 (9.2 토큰 기준) |
| 3. 화면 | AC-09 | 장중에 새로고침 없이 60초마다 대시보드 금액이 갱신된다 |
| 3. 화면 | AC-10 | 폭 360px에서 페이지 가로 스크롤이 없고 하단 탭 바가 표시된다 (표 내부 스크롤은 허용) |
| 3. 화면 | AC-11 | 그룹 관리에서 목표 합계가 100%가 아니면 저장 버튼이 비활성화된다 |
| 4. 보안·배포 | AC-12 | 로그인 없이 모든 라우트 접근 시 `/login`으로 이동하고, TOTP 없이는 로그인되지 않는다 |
| 4. 보안·배포 | AC-13 | DB·로그 어디에도 APP SECRET과 접근토큰이 평문으로 남지 않는다 |
| 4. 보안·배포 | AC-14 | PC 재부팅 후 서비스가 자동 시작되고, 휴대폰에서 Tailscale로 접속된다 |

**4단계의 개인 사용 단계 적용 (7.3)**

| ID | 개인 사용 단계의 완료 기준 | 보류한 부분 (배포 시 재검토) |
| --- | --- | --- |
| AC-12 | 로그인 없이 모든 라우트 접근 시 `/login`으로 이동하고(계정이 없으면 가입 화면으로 이어짐), 아이디·비밀번호 없이는 로그인되지 않는다 | TOTP |
| AC-13 | 원안 그대로 | 없음 (단, KIS 키는 DB가 아닌 데이터 폴더의 `settings.json`에 평문으로 둔다) |
| AC-14 | `scripts/run.cmd`로 실행한 앱에 휴대폰·다른 PC에서 Tailscale(`tailscale serve`)로 접속된다 | 재부팅 후 서비스 자동 시작 |

### 10.4 테스트용 시드 데이터

목업과 5.5 계산 예시에 쓴 가상 데이터다. 실제 종목·시세가 아니며, 계산 검증과 화면 확인 용도로만 쓴다.

| 종목명 | 그룹 | 수량 (주) | 평균매입단가 (원) | 현재가 (원) |
| --- | --- | --- | --- | --- |
| KOSPI200 ETF | 주식 | 600 | 36,000 | 40,000 |
| 미국 S&P500 ETF | 주식 | 1,200 | 17,500 | 20,000 |
| 반도체 개별주 A | 주식 | 100 | 130,000 | 120,000 |
| 국고채 10년 ETF | 채권 | 100 | 102,000 | 100,000 |
| 미국채 10년 ETF | 채권 | 1,000 | 10,500 | 10,000 |
| 고배당 ETF | 배당 | 800 | 13,000 | 15,000 |
| 리츠 ETF | 배당 | 1,600 | 5,500 | 5,000 |

목표 비중: 주식 50%, 채권 30%, 배당 20%. 시드에서는 현재가를 `IPriceProvider`의 가짜 구현(FakePriceProvider)으로 공급해 KIS 없이 1단계를 끝낼 수 있게 한다.

### 10.5 새 세션 시작 프롬프트

```
포트폴리오 대시보드를 개발하려고 해.
저장소 https://github.com/RREGEGE/Stock-Manager.git 를 기준으로 작업하고,
먼저 docs/design.md (설계서)와 docs/mockup/ (화면 목업 원본)을 읽고 그대로 따라줘.
저장소 사용 규칙은 docs/design.md 10.1.1에 있어.

이번 세션 범위: 설계서 8.3의 1단계 (데이터·계산).
- 8.2 솔루션 구조대로 저장소 루트에 프로젝트를 만들고, 10.4 시드 데이터와 FakePriceProvider를 넣어줘.
- 10.3의 AC-01~AC-04를 xUnit 테스트로 작성하고 통과시켜줘.
- 환경: Windows, .NET 10, C#.
설계서와 다르게 해야 할 이유가 생기면 바꾸기 전에 먼저 물어봐.
```

2단계 이후는 같은 형식으로 '이번 세션 범위'와 해당 AC 번호만 바꿔 쓴다. 개발 착수 전 8.4 미확정 사항 중 채권 보유 형태와 해외주식 보유 여부는 정해 두는 것이 좋다. 둘 다 시세 조회 방식에 영향을 준다.
