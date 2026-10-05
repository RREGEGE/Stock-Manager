using System.IO.Compression;
using Portfolio.Core;

namespace Portfolio.Kis;

// KIS 공식 저장소가 안내하는 공개 주소에서 종목 마스터 zip을 내려받는다 (인증 불필요).
public sealed class KisSymbolMasterClient(HttpClient http)
{
    public static readonly IReadOnlyList<(string Market, Uri Url)> Sources =
    [
        (SymbolMasterParser.Kospi, new Uri("https://new.real.download.dws.co.kr/common/master/kospi_code.mst.zip")),
        (SymbolMasterParser.Kosdaq, new Uri("https://new.real.download.dws.co.kr/common/master/kosdaq_code.mst.zip")),
    ];

    // 시장 하나라도 실패하면 예외를 던진다 (일부만 갱신하면 다른 시장 종목이 사라져 보일 수 있음).
    public async Task<IReadOnlyList<SymbolInfo>> DownloadAsync(CancellationToken ct = default)
    {
        var all = new List<SymbolInfo>();
        foreach (var (market, url) in Sources)
        {
            var zipBytes = await http.GetByteArrayAsync(url, ct);
            var symbols = SymbolMasterParser.Parse(ExtractFirstEntry(zipBytes), market);
            if (symbols.Count == 0)
                throw new InvalidDataException($"{market} 종목 마스터에 종목이 없습니다.");
            all.AddRange(symbols);
        }
        return all;
    }

    public static byte[] ExtractFirstEntry(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault() ?? throw new InvalidDataException("zip 파일이 비어 있습니다.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
