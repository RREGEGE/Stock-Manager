using Portfolio.Core;

namespace Portfolio.Tests;

// 설계서 9.3 표시 규칙
public class DisplayFormatTests
{
    [Fact]
    public void 금액은_천_단위_쉼표와_원()
    {
        Assert.Equal("100,000,000원", DisplayFormat.Won(100_000_000m));
        Assert.Equal("0원", DisplayFormat.Won(0m));
        Assert.Equal("+4,500,000원", DisplayFormat.SignedWon(4_500_000m));
        Assert.Equal("-1,000원", DisplayFormat.SignedWon(-1_000m));
        Assert.Equal("1,600", DisplayFormat.Number(1_600m));
    }

    [Fact]
    public void 비중은_소수점_1자리_손익률은_소수점_2자리와_부호()
    {
        Assert.Equal("60.0%", DisplayFormat.Percent1(0.6m));
        Assert.Equal("27.3%", DisplayFormat.Percent1(30m / 110m));
        Assert.Equal("+4.71%", DisplayFormat.SignedPercent2(4_500_000m / 95_500_000m));
        Assert.Equal("-7.69%", DisplayFormat.SignedPercent2(-10_000m / 130_000m));
        Assert.Equal("+0.00%", DisplayFormat.SignedPercent2(0m));
    }

    [Fact]
    public void 차이는_소수점_1자리_퍼센트포인트()
    {
        Assert.Equal("+10.0%p", DisplayFormat.SignedPoint1(0.1m));
        Assert.Equal("-10.0%p", DisplayFormat.SignedPoint1(-0.1m));
        Assert.Equal("0.0%p", DisplayFormat.SignedPoint1(0m));
    }

    [Theory]
    [InlineData(100_000_000, "1억 원")]
    [InlineData(123_400_000, "1.2억 원")]
    [InlineData(1_250_000_000, "12.5억 원")]
    [InlineData(5_500_000, "550만 원")]
    [InlineData(9_000, "9,000원")]
    public void 좁은_자리용_금액은_억_만_단위로_줄인다(decimal value, string expected)
    {
        Assert.Equal(expected, DisplayFormat.ShortWon(value));
    }

    [Fact]
    public void 날짜와_시각은_한국_시간_기준이다()
    {
        var utc = new DateTimeOffset(2026, 9, 28, 0, 41, 0, TimeSpan.Zero);   // 09:41 KST
        Assert.Equal("9월 28일", DisplayFormat.MonthDay(utc));
        Assert.Equal("09:41", DisplayFormat.Time(utc));

        var lateUtc = new DateTimeOffset(2026, 9, 28, 16, 0, 0, TimeSpan.Zero);   // 다음 날 01:00 KST
        Assert.Equal("9월 29일", DisplayFormat.MonthDay(lateUtc));
    }

    [Theory]
    [InlineData("1,600", 1600)]
    [InlineData("10000000원", 10_000_000)]
    [InlineData("33.5", 33.5)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData(null, 0)]
    public void 입력값에서_숫자만_읽는다(string? text, decimal expected)
    {
        Assert.Equal(expected, DisplayFormat.ParseNumber(text));
    }
}
