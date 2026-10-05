using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;

namespace Portfolio.Tests;

public class SymbolMasterTests
{
    private static readonly Encoding Cp949;

    static SymbolMasterTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    // 실제 마스터 파일과 같은 배치의 가상 레코드: 코드 9 / 표준코드 12 / 종목명 40 / 그룹 2 / 나머지 채움
    private static byte[] Record(string code, string name, string group, int totalLength = 288)
    {
        var bytes = Enumerable.Repeat((byte)' ', totalLength).ToArray();
        Encoding.ASCII.GetBytes(code.PadRight(9)).CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("KR7000000000").CopyTo(bytes, 9);
        var nameBytes = Cp949.GetBytes(name);
        nameBytes.AsSpan(0, Math.Min(40, nameBytes.Length)).CopyTo(bytes.AsSpan(21));
        Encoding.ASCII.GetBytes(group).CopyTo(bytes, 61);
        return bytes;
    }

    private static byte[] File(params byte[][] records) =>
        records.SelectMany(r => r.Append((byte)'\n')).ToArray();

    [Fact]
    public void 고정_바이트_위치로_코드와_종목명을_읽는다()
    {
        var content = File(
            Record("005930", "삼성전자", "ST"),
            Record("0000H0", "KODEX 인도Nifty미드캡100", "EF"),
            Record("Q500067", "신한 레버리지 10년 국채선물 ETN", "EN"));

        var symbols = SymbolMasterParser.Parse(content, SymbolMasterParser.Kospi);

        Assert.Equal(
        [
            new SymbolInfo("005930", "삼성전자", "KOSPI"),
            new SymbolInfo("0000H0", "KODEX 인도Nifty미드캡100", "KOSPI"),
            new SymbolInfo("Q500067", "신한 레버리지 10년 국채선물 ETN", "KOSPI"),
        ], symbols);
    }

    [Fact]
    public void 펀드와_신주인수권은_제외한다()
    {
        var content = File(
            Record("F70100030", "가상 수익증권(A)", "BC"),
            Record("J0036221D", "가상 신주인수권", "SW"),
            Record("J0036222D", "가상 신주인수증서", "SR"),
            Record("088260", "가상 리츠", "RT"),
            Record("900110", "가상 해외원주", "FS"));

        var codes = SymbolMasterParser.Parse(content, SymbolMasterParser.Kospi).Select(s => s.SymbolCode);

        Assert.Equal(["088260", "900110"], codes);
    }

    [Fact]
    public void 한글_중간에서_잘린_40바이트_종목명은_잘린_글자를_버린다()
    {
        // 39바이트까지 채운 뒤 2바이트 한글이 오면 40바이트째에 선행 바이트만 남는다
        string name = "TIGER1 미국배당다우존스타겟데일리커버드콜";   // ASCII 7바이트 + 한글 17자 = 41바이트
        Assert.Equal(41, Cp949.GetByteCount(name));

        var symbol = Assert.Single(SymbolMasterParser.Parse(File(Record("0008S0", name, "EF")), "KOSPI"));

        Assert.True(Cp949.GetByteCount(symbol.SymbolName) <= 40);
        Assert.StartsWith(symbol.SymbolName, name);
        Assert.DoesNotContain('?', symbol.SymbolName);
        Assert.DoesNotContain('�', symbol.SymbolName);
    }

    [Fact]
    public void 코스닥_레코드_길이와_CRLF_줄바꿈도_처리한다()
    {
        var content = Record("0001A0", "가상 코스닥 종목", "ST", totalLength: 282)
            .Concat("\r\n"u8.ToArray())
            .Concat(Record("000250", "가상 제약", "ST", totalLength: 282)).ToArray();   // 마지막 줄 줄바꿈 없음

        var symbols = SymbolMasterParser.Parse(content, SymbolMasterParser.Kosdaq);

        Assert.Equal(["0001A0", "000250"], symbols.Select(s => s.SymbolCode));
        Assert.All(symbols, s => Assert.Equal("KOSDAQ", s.Market));
    }

    [Fact]
    public void 짧은_줄과_빈_줄은_건너뛴다()
    {
        var content = File([], "short"u8.ToArray(), Record("005930", "삼성전자", "ST"));

        Assert.Single(SymbolMasterParser.Parse(content, "KOSPI"));
    }

    [Fact]
    public void zip의_첫_항목을_꺼낸다()
    {
        var mst = File(Record("005930", "삼성전자", "ST"));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("kospi_code.mst").Open();
            s.Write(mst);
        }

        Assert.Equal(mst, KisSymbolMasterClient.ExtractFirstEntry(buffer.ToArray()));
    }

    [Fact]
    public async Task 갱신하면_받은_시장의_종목만_교체하고_갱신_시각을_저장한다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);   // SymbolMaster에 SEED 시장 7종목
        var repo = new SymbolMasterRepository(db);
        var t1 = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        await repo.ReplaceAsync([new("005930", "삼성전자", "KOSPI"), new("000660", "SK하이닉스", "KOSPI")], t1);
        await repo.ReplaceAsync([new("005930", "삼성전자", "KOSPI"), new("0001A0", "가상 코스닥", "KOSDAQ")], t1.AddDays(7));

        var rows = await db.Context.SymbolMasters.AsNoTracking().ToListAsync();
        Assert.Equal(7, rows.Count(r => r.Market == "SEED"));
        Assert.Equal(["005930"], rows.Where(r => r.Market == "KOSPI").Select(r => r.SymbolCode));
        Assert.Equal(["0001A0"], rows.Where(r => r.Market == "KOSDAQ").Select(r => r.SymbolCode));
        Assert.Equal(t1.AddDays(7), await repo.GetUpdatedAtAsync());
    }

    [Fact]
    public void 마지막_갱신_후_7일이_지나면_갱신한다()
    {
        var now = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

        Assert.True(SymbolMasterRepository.IsRefreshDue(null, now));
        Assert.True(SymbolMasterRepository.IsRefreshDue(now.AddDays(-7), now));
        Assert.False(SymbolMasterRepository.IsRefreshDue(now.AddDays(-6.9), now));
    }
}
