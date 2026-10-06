using Microsoft.AspNetCore.Components;
using Portfolio.Core;
using Portfolio.Web.Services;

namespace Portfolio.Web.Components;

// 시세 갱신·데이터 변경 알림을 받으면 데이터를 다시 읽어 새로고침 없이 화면을 다시 그린다 (AC-09).
public abstract class LiveComponentBase : ComponentBase, IDisposable
{
    [Inject] protected PortfolioService Portfolio { get; set; } = default!;
    [Inject] protected PortfolioNotifier Notifier { get; set; } = default!;
    [Inject] protected CurrentAccount Account { get; set; } = default!;

    protected PortfolioViewModel? Model { get; private set; }
    private bool _disposed;

    protected override async Task OnInitializedAsync()
    {
        Notifier.Changed += OnPortfolioChanged;
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        Model = new PortfolioViewModel(await Portfolio.LoadAsync(Account.Id));
        Account.Id = Model.State.Account.Id;   // 고른 계좌가 없어졌으면 대신 보여 준 계좌로 맞춘다
        OnModelLoaded();
    }

    // 다시 읽은 뒤 화면별 후처리 (입력 중인 값은 건드리지 않는다)
    protected virtual void OnModelLoaded() { }

    private void OnPortfolioChanged()
    {
        if (_disposed) return;
        _ = InvokeAsync(async () =>
        {
            try
            {
                if (_disposed) return;
                await ReloadAsync();
                StateHasChanged();
            }
            catch (ObjectDisposedException) { /* 화면이 닫히는 중 */ }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Notifier.Changed -= OnPortfolioChanged;
        GC.SuppressFinalize(this);
    }
}
