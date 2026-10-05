using Portfolio.Core;

namespace Portfolio.Tests;

public class MarketScheduleTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    // 2026-10-02는 금요일
    private static DateTimeOffset Kst(int day, int hour, int minute, int second = 0) =>
        new(2026, 10, day, hour, minute, second, TimeSpan.FromHours(9));

    private static readonly DateOnly Friday = new(2026, 10, 2);

    [Theory]
    [InlineData(9, 0)]
    [InlineData(12, 0)]
    [InlineData(15, 30)]
    public void 평일_장중에는_주기마다_조회한다(int hour, int minute)
    {
        var now = Kst(2, hour, minute);
        var plan = MarketSchedule.Plan(now, null, Interval);

        Assert.Equal(new PollPlan(PollKind.Intraday, now + Interval), plan);
    }

    [Fact]
    public void 개장_전에는_09시까지_기다린다()
    {
        Assert.Equal(new PollPlan(PollKind.None, Kst(2, 9, 0)), MarketSchedule.Plan(Kst(2, 8, 59, 30), null, Interval));
    }

    [Fact]
    public void 장_마감_후_15시40분까지는_기다린다()
    {
        Assert.Equal(new PollPlan(PollKind.None, Kst(2, 15, 40)), MarketSchedule.Plan(Kst(2, 15, 30, 1), null, Interval));
    }

    [Fact]
    public void 오후_3시40분에_종가를_1회_조회하고_다음_개장까지_중지한다()
    {
        var plan = MarketSchedule.Plan(Kst(2, 15, 40), null, Interval);
        Assert.Equal(new PollPlan(PollKind.Close, Kst(5, 9, 0)), plan);   // 금요일 → 다음 월요일 09:00

        var after = MarketSchedule.Plan(Kst(2, 15, 41), Friday, Interval);
        Assert.Equal(new PollPlan(PollKind.None, Kst(5, 9, 0)), after);
    }

    [Fact]
    public void 장_마감_후에_시작하면_종가를_1회_조회한다()
    {
        Assert.Equal(PollKind.Close, MarketSchedule.Plan(Kst(2, 22, 0), null, Interval).Kind);
        Assert.Equal(PollKind.Close, MarketSchedule.Plan(Kst(2, 22, 0), new DateOnly(2026, 10, 1), Interval).Kind);
    }

    [Theory]
    [InlineData(3, 12)]   // 토요일
    [InlineData(4, 12)]   // 일요일
    public void 주말에는_조회하지_않고_월요일_09시까지_기다린다(int day, int hour)
    {
        Assert.Equal(new PollPlan(PollKind.None, Kst(5, 9, 0)), MarketSchedule.Plan(Kst(day, hour, 0), null, Interval));
    }

    [Fact]
    public void UTC_시각도_한국_시간으로_판단한다()
    {
        var utc = new DateTimeOffset(2026, 10, 2, 0, 30, 0, TimeSpan.Zero);   // 09:30 KST

        Assert.True(MarketSchedule.IsOpen(utc));
        Assert.Equal(PollKind.Intraday, MarketSchedule.Plan(utc, null, Interval).Kind);
    }
}
