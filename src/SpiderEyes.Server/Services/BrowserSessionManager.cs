using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;

namespace SpiderEyes.Server.Services;

public sealed class BrowserSessionManager : BackgroundService
{
    private readonly ConcurrentDictionary<string, BrowserSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly IOptionsMonitor<SpiderEyesOptions> _optionsMonitor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PlaywrightRuntimeService _playwrightRuntimeService;

    public BrowserSessionManager(
        IOptionsMonitor<SpiderEyesOptions> optionsMonitor,
        ILoggerFactory loggerFactory,
        PlaywrightRuntimeService playwrightRuntimeService)
    {
        _optionsMonitor = optionsMonitor;
        _loggerFactory = loggerFactory;
        _playwrightRuntimeService = playwrightRuntimeService;
    }

    public async Task<BrowserSession> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            existing.Touch();
            return existing;
        }

        await _createLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(sessionId, out existing))
            {
                existing.Touch();
                return existing;
            }

            var created = new BrowserSession(
                sessionId,
                _optionsMonitor.CurrentValue,
                _playwrightRuntimeService,
                _loggerFactory.CreateLogger<BrowserSession>());
            _sessions[sessionId] = created;
            return created;
        }
        finally
        {
            _createLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await CleanupIdleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task CleanupIdleSessionsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - _optionsMonitor.CurrentValue.Session.IdleTimeout;
        foreach (var pair in _sessions.ToArray())
        {
            if (pair.Value.LastAccessUtc >= cutoff)
            {
                continue;
            }

            if (_sessions.TryRemove(pair.Key, out var session))
            {
                await session.DisposeAsync();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }
    }
}
