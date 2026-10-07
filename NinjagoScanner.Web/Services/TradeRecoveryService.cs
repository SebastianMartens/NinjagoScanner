namespace NinjagoScanner.Web.Services;

/// <summary>
/// Background sweep that re-drives trades stuck in Executing (e.g. the process died between the
/// optimistic status flip and the completion transaction): runs shortly after startup and then
/// periodically, calling <see cref="TradeService.RecoverStaleExecutingAsync"/>.
/// </summary>
internal sealed class TradeRecoveryService(
    IServiceScopeFactory scopeFactory,
    ILogger<TradeRecoveryService> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var tradeService = scope.ServiceProvider.GetRequiredService<TradeService>();
                var resolved = await tradeService.RecoverStaleExecutingAsync(cancellationToken: stoppingToken);
                if (resolved > 0)
                {
                    logger.LogInformation("Trade recovery resolved {Count} stale trade(s).", resolved);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Trade recovery sweep failed; will retry.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
