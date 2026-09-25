namespace TokenGauge;

/// <summary>
/// Polls each enabled provider on its own interval. Start it on the UI thread: loops resume there,
/// so results are applied and <see cref="Changed"/> is raised on the UI thread.
/// </summary>
public sealed class UsageMonitor : IDisposable
{
    sealed class Item(ServiceState state)
    {
        public ServiceState State { get; } = state;
        public DateTimeOffset LastRun { get; set; } = DateTimeOffset.MinValue;
        public bool FetchNow { get; set; }
        public CancellationTokenSource? Sleep { get; set; }
    }

    readonly CancellationTokenSource _cts = new();
    readonly List<Item> _items;

    public event Action? Changed;
    public IReadOnlyList<ServiceState> Services { get; }

    public UsageMonitor(IEnumerable<IUsageProvider> providers)
    {
        _items = providers.Select(p => new Item(new ServiceState(p))).ToList();
        Services = _items.Select(i => i.State).ToList();
    }

    public void Start()
    {
        foreach (var item in _items) _ = RunLoopAsync(item);
    }

    /// <summary>Checks every enabled service now.</summary>
    public void RefreshNow()
    {
        foreach (var item in _items) Wake(item, fetchNow: true);
    }

    /// <summary>Checks one service now, e.g. after its setup window closes.</summary>
    public void Refresh(ServiceState state) => Wake(_items.First(i => i.State == state), fetchNow: true);

    /// <summary>Re-reads intervals and enabled flags. A service that was just enabled is checked straight away.</summary>
    public void Reschedule()
    {
        foreach (var item in _items) Wake(item, fetchNow: false);
    }

    static void Wake(Item item, bool fetchNow)
    {
        item.FetchNow |= fetchNow;
        item.Sleep?.Cancel();
    }

    async Task RunLoopAsync(Item item)
    {
        var provider = item.State.Provider;
        while (!_cts.IsCancellationRequested)
        {
            var enabled = provider.Settings.Enabled;
            if (enabled && (item.FetchNow || DateTimeOffset.UtcNow - item.LastRun >= provider.Interval))
            {
                item.FetchNow = false;
                item.LastRun = DateTimeOffset.UtcNow;
                await FetchAsync(item);
                continue;
            }

            var wait = enabled ? item.LastRun + provider.Interval - DateTimeOffset.UtcNow : Timeout.InfiniteTimeSpan;
            if (wait < TimeSpan.Zero && wait != Timeout.InfiniteTimeSpan) wait = TimeSpan.Zero;

            using var sleep = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            item.Sleep = sleep;
            try { await Task.Delay(wait, sleep.Token); }
            catch (OperationCanceledException) { }
            finally { item.Sleep = null; }
        }
    }

    async Task FetchAsync(Item item)
    {
        var provider = item.State.Provider;
        UsageSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() => provider.FetchAsync(_cts.Token));
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            snapshot = UsageSnapshot.Failed(provider.Name, UsageStatus.Error, ex.Message);
        }

        item.State.Latest = snapshot;
        if (snapshot.Status == UsageStatus.Ok) item.State.LastGood = snapshot;
        Changed?.Invoke();
    }

    public void Dispose() => _cts.Cancel();
}
