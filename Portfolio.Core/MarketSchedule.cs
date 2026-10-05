namespace Portfolio.Core;

public enum PollKind { None, Intraday, Close }

public sealed record PollPlan(PollKind Kind, DateTimeOffset NextCheck);

// 시세 조회 시점 판단 (설계서 4.3)
// - 장중(평일 09:00~15:30, 한국 시간): 설정 주기마다 조회
// - 장외: 15:40에 1회 조회해 종가 저장 후 다음 개장까지 중지
// 공휴일은 판단하지 않는다 (조회해도 같은 가격이 오므로 무해).
public static class MarketSchedule
{
    public static readonly TimeSpan Kst = TimeSpan.FromHours(9);
    public static readonly TimeSpan Open = new(9, 0, 0);
    public static readonly TimeSpan Close = new(15, 30, 0);
    public static readonly TimeSpan CloseFetch = new(15, 40, 0);

    public static bool IsTradingDay(DateTimeOffset now) =>
        now.ToOffset(Kst).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static bool IsOpen(DateTimeOffset now)
    {
        var t = now.ToOffset(Kst).TimeOfDay;
        return IsTradingDay(now) && t >= Open && t <= Close;
    }

    public static DateOnly KstDate(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Kst).DateTime);

    // lastCloseFetch: 종가 조회를 마지막으로 한 날짜(한국 시간). 시작 직후에는 null.
    public static PollPlan Plan(DateTimeOffset now, DateOnly? lastCloseFetch, TimeSpan interval)
    {
        var kst = now.ToOffset(Kst);
        var t = kst.TimeOfDay;
        var today = KstDate(now);

        if (IsTradingDay(now))
        {
            if (t >= Open && t <= Close)
                return new PollPlan(PollKind.Intraday, now + interval);
            if (t < Open)
                return new PollPlan(PollKind.None, At(kst, Open));
            if (t < CloseFetch)
                return new PollPlan(PollKind.None, At(kst, CloseFetch));
            if (lastCloseFetch != today)
                return new PollPlan(PollKind.Close, NextOpen(kst));
        }
        return new PollPlan(PollKind.None, NextOpen(kst));
    }

    private static DateTimeOffset At(DateTimeOffset kst, TimeSpan timeOfDay) =>
        new(kst.Date + timeOfDay, Kst);

    private static DateTimeOffset NextOpen(DateTimeOffset kst)
    {
        var day = kst.Date.AddDays(1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            day = day.AddDays(1);
        return new DateTimeOffset(day + Open, Kst);
    }
}
