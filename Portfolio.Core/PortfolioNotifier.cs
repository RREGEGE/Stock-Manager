namespace Portfolio.Core;

// 시세 갱신·데이터 변경을 열려 있는 화면에 알린다 (설계서 3장: 접속 중인 브라우저로 푸시).
// Blazor Interactive Server의 화면 연결이 SignalR이므로, 화면은 이 이벤트를 받아 다시 그리기만 하면 된다.
public sealed class PortfolioNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch { /* 닫히는 중인 화면의 오류가 다른 화면 갱신을 막지 않게 한다 */ }
        }
    }
}
