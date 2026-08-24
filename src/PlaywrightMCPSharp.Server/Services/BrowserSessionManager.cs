using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PlaywrightMCPSharp.Server.Configuration;

namespace PlaywrightMCPSharp.Server.Services;

public sealed class BrowserSessionManager : BackgroundService
{
    private readonly ConcurrentDictionary<BrowserInstanceKey, BrowserSession> _sessions = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly IOptionsMonitor<PlaywrightMCPSharpOptions> _optionsMonitor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PlaywrightRuntimeService _playwrightRuntimeService;

    public BrowserSessionManager(
        IOptionsMonitor<PlaywrightMCPSharpOptions> optionsMonitor,
        ILoggerFactory loggerFactory,
        PlaywrightRuntimeService playwrightRuntimeService)
    {
        _optionsMonitor = optionsMonitor;
        _loggerFactory = loggerFactory;
        _playwrightRuntimeService = playwrightRuntimeService;
    }

    /// <summary>
    /// Resolves the browser instance addressed by an MCP call. A null or blank name maps to
    /// the session's 'default' instance, which is created on demand for backward compatibility.
    /// Any other name must have been created explicitly with browser_instance_create.
    /// </summary>
    public async Task<BrowserSession> GetAsync(string sessionId, string? instanceName, CancellationToken cancellationToken)
    {
        var normalized = BrowserInstanceName.NormalizeOrDefault(instanceName);
        var key = new BrowserInstanceKey(sessionId, normalized);
        if (_sessions.TryGetValue(key, out var existing))
        {
            existing.Touch();
            return existing;
        }

        if (!string.Equals(normalized, BrowserInstanceName.Default, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unknown browser instance '{normalized}' for this MCP session. Create it first with browser_instance_create, or omit instanceName to use the default instance.");
        }

        return await CreateCoreAsync(sessionId, normalized, overrides: null, failIfExists: false, cancellationToken);
    }

    /// <summary>
    /// Explicitly and atomically creates a named instance, failing when the name already
    /// exists or a resource cap would be exceeded.
    /// </summary>
    public async Task<BrowserSession> CreateInstanceAsync(
        string sessionId,
        string instanceName,
        BrowserInstanceOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var normalized = BrowserInstanceName.Normalize(instanceName);
        return await CreateCoreAsync(sessionId, normalized, overrides, failIfExists: true, cancellationToken);
    }

    public IReadOnlyList<BrowserSession> ListInstances(string sessionId)
        => _sessions
            .Where(pair => string.Equals(pair.Key.SessionId, sessionId, StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .OrderBy(static session => session.InstanceName, StringComparer.Ordinal)
            .ToArray();

    public BrowserSession GetInstance(string sessionId, string instanceName)
    {
        var normalized = BrowserInstanceName.Normalize(instanceName);
        if (_sessions.TryGetValue(new BrowserInstanceKey(sessionId, normalized), out var session))
        {
            return session;
        }

        throw new InvalidOperationException($"Unknown browser instance '{normalized}' for this MCP session.");
    }

    public async Task<bool> CloseInstanceAsync(string sessionId, string instanceName)
    {
        var normalized = BrowserInstanceName.Normalize(instanceName);
        if (!_sessions.TryRemove(new BrowserInstanceKey(sessionId, normalized), out var session))
        {
            return false;
        }

        await session.DisposeAsync();
        return true;
    }

    private async Task<BrowserSession> CreateCoreAsync(
        string sessionId,
        string normalizedInstanceName,
        BrowserInstanceOverrides? overrides,
        bool failIfExists,
        CancellationToken cancellationToken)
    {
        var key = new BrowserInstanceKey(sessionId, normalizedInstanceName);
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                if (failIfExists)
                {
                    throw new InvalidOperationException($"Browser instance '{normalizedInstanceName}' already exists for this MCP session.");
                }

                existing.Touch();
                return existing;
            }

            var options = _optionsMonitor.CurrentValue;
            var perSession = _sessions.Keys.Count(k => string.Equals(k.SessionId, sessionId, StringComparison.Ordinal));
            if (perSession >= options.Session.MaxInstancesPerSession)
            {
                throw new InvalidOperationException(
                    $"This MCP session already has the maximum {options.Session.MaxInstancesPerSession} browser instances. Close one with browser_instance_close first.");
            }

            if (_sessions.Count >= options.Session.MaxGlobalInstances)
            {
                throw new InvalidOperationException(
                    $"The server already has the maximum {options.Session.MaxGlobalInstances} browser instances across all sessions.");
            }

            var created = new BrowserSession(
                sessionId,
                normalizedInstanceName,
                overrides,
                options,
                _playwrightRuntimeService,
                _loggerFactory.CreateLogger<BrowserSession>());
            _sessions[key] = created;
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
            if (pair.Value.LastAccessUtc >= cutoff || pair.Value.HasActiveCommand)
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

    private readonly record struct BrowserInstanceKey(string SessionId, string InstanceName);
}
