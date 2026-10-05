using System.Text;
using Portfolio.Core;

namespace Portfolio.Kis;

// KIS 종목 마스터 파일(kospi_code.mst, kosdaq_code.mst) 파서 (설계서 5.4-5)
// 형식: cp949, 줄바꿈 LF, 레코드마다 고정 바이트 폭.
//   0~9 단축코드(공백 채움) / 9~21 표준코드 / 21~61 한글종목명(40바이트) / 61~63 그룹코드 / 이후 부가 정보
// 공식 예제는 디코딩한 문자열을 문자 단위로 잘라 한글이 섞이면 위치가 어긋날 수 있어, 바이트 단위로 자른다.
public static class SymbolMasterParser
{
    public const string Kospi = "KOSPI";
    public const string Kosdaq = "KOSDAQ";

    private const int CodeEnd = 9, NameStart = 21, NameEnd = 61, GroupEnd = 63;

    // 포함: 주식, ETF, ETN, 리츠, 인프라·선박 등 투자회사, 해외 원주·DR
    // 제외: 수익증권 펀드(BC), 신주인수권증권·증서(SW, SR)
    private static readonly HashSet<string> IncludedGroups = ["ST", "EF", "EN", "RT", "IF", "MF", "PF", "FS", "DR"];

    private static readonly Encoding Cp949;

    static SymbolMasterParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static IReadOnlyList<SymbolInfo> Parse(ReadOnlySpan<byte> content, string market)
    {
        var result = new List<SymbolInfo>();
        while (!content.IsEmpty)
        {
            int nl = content.IndexOf((byte)'\n');
            var line = nl < 0 ? content : content[..nl];
            content = nl < 0 ? [] : content[(nl + 1)..];
            if (!line.IsEmpty && line[^1] == (byte)'\r') line = line[..^1];
            if (line.Length < GroupEnd) continue;

            string group = Encoding.ASCII.GetString(line[NameEnd..GroupEnd]);
            if (!IncludedGroups.Contains(group)) continue;

            string code = Encoding.ASCII.GetString(line[..CodeEnd]).Trim();
            string name = DecodeName(line[NameStart..NameEnd]);
            if (code.Length > 0 && name.Length > 0)
                result.Add(new SymbolInfo(code, name, market));
        }
        return result;
    }

    // 40바이트를 넘는 종목명은 한글 중간에서 잘려 있으므로 짝이 없는 마지막 선행 바이트를 버린다.
    private static string DecodeName(ReadOnlySpan<byte> field)
    {
        int len = field.Length;
        while (len > 0 && field[len - 1] == (byte)' ') len--;

        int i = 0;
        while (i < len)
        {
            if (field[i] < 0x81) { i++; continue; }
            if (i + 1 >= len) { len = i; break; }   // 잘린 2바이트 문자
            i += 2;
        }
        return Cp949.GetString(field[..len]).Trim();
    }
}
