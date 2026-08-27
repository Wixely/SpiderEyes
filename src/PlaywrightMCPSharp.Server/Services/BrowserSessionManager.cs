using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PlaywrightMCPSharp.Server.Configuration;

namespace PlaywrightMCPSharp.Server.Services;

public sealed class BrowserSessionManager : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<BrowserInstanceKey, SessionEntry> _sessions = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly IOptionsMonitor<PlaywrightMCPSharpOptions> _optionsMonitor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BrowserSessionManager> _logger;
    private readonly PlaywrightRuntimeService _playwrightRuntimeService;

    public BrowserSessionManager(
        IOptionsMonitor<PlaywrightMCPSharpOptions> optionsMonitor,
        ILoggerFactory loggerFactory,
        PlaywrightRuntimeService playwrightRuntimeService)
    {
        _optionsMonitor = optionsMonitor;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<BrowserSessionManager>();
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
        var isDefault = string.Equals(normalized, BrowserInstanceName.Default, StringComparison.Ordinal);
        if (!isDefault && !NamedInstancesEnabled)
        {
            throw new InvalidOperationException(BuildNamedInstancesDisabledMessage(normalized));
        }

        var key = new BrowserInstanceKey(sessionId, normalized);
        if (_sessions.TryGetValue(key, out var existing) && existing.TryAcquire())
        {
            return existing.Session;
        }

        if (!isDefault)
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
        if (!NamedInstancesEnabled && !string.Equals(normalized, BrowserInstanceName.Default, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(BuildNamedInstancesDisabledMessage(normalized));
        }

        return await CreateCoreAsync(sessionId, normalized, overrides, failIfExists: true, cancellationToken);
    }

    public IReadOnlyList<BrowserSession> ListInstances(string sessionId)
        => _sessions
            .Where(pair => string.Equals(pair.Key.SessionId, sessionId, StringComparison.Ordinal) && !pair.Value.IsClaimed)
            .Select(static pair => pair.Value.Session)
            .OrderBy(static session => session.InstanceName, StringComparer.Ordinal)
            .ToArray();

    public BrowserSession GetInstance(string sessionId, string instanceName)
    {
        var normalized = BrowserInstanceName.Normalize(instanceName);
        if (_sessions.TryGetValue(new BrowserInstanceKey(sessionId, normalized), out var entry) && !entry.IsClaimed)
        {
            return entry.Session;
        }

        throw new InvalidOperationException($"Unknown browser instance '{normalized}' for this MCP session.");
    }

    public async Task<bool> CloseInstanceAsync(string sessionId, string instanceName)
    {
        var normalized = BrowserInstanceName.Normalize(instanceName);
        var key = new BrowserInstanceKey(sessionId, normalized);

        // Claiming before removal means the idle sweep can never dispose the same instance
        // concurrently: exactly one claimer wins, and the winner owns the teardown.
        if (!_sessions.TryGetValue(key, out var entry) || !entry.TryClaim())
        {
            return false;
        }

        Remove(key, entry);
        await entry.Session.DisposeAsync();
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
            // A claimed entry is already on its way out; treat it as absent and replace it.
            if (_sessions.TryGetValue(key, out var existing) && existing.TryAcquire())
            {
                if (failIfExists)
                {
                    throw new InvalidOperationException($"Browser instance '{normalizedInstanceName}' already exists for this MCP session.");
                }

                return existing.Session;
            }

            var options = _optionsMonitor.CurrentValue;
            var perSession = _sessions.Count(pair =>
                string.Equals(pair.Key.SessionId, sessionId, StringComparison.Ordinal) && !pair.Value.IsClaimed);
            if (perSession >= options.Session.MaxInstancesPerSession)
            {
                throw new InvalidOperationException(
                    $"This MCP session already has the maximum {options.Session.MaxInstancesPerSession} browser instances. Close one with browser_instance_close first.");
            }

            if (_sessions.Count(static pair => !pair.Value.IsClaimed) >= options.Session.MaxGlobalInstances)
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
            _sessions[key] = new SessionEntry(created);
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
                await Task.Delay(CleanupInterval, stoppingToken);
                await CleanupIdleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The sweep is best-effort. Faulting this loop would take the whole host down
                // under the default BackgroundServiceExceptionBehavior, so log and keep sweeping.
                // The delay above runs first on every iteration, so this cannot spin.
                _logger.LogError(ex, "Idle browser instance cleanup failed. The sweep will continue.");
            }
        }
    }

    public async Task CleanupIdleSessionsAsync(CancellationToken cancellationToken)
    {
        // Honour the configured timeout exactly. The handoff needs no extra grace: TryAcquire
        // refreshes LastAccessUtc under the same gate that TryClaimIfIdle tests it under, so a
        // caller that has been handed an instance has already moved it out of the cutoff window.
        var idleTimeout = _optionsMonitor.CurrentValue.Session.IdleTimeout;
        var cutoff = DateTimeOffset.UtcNow - idleTimeout;
        foreach (var pair in _sessions.ToArray())
        {
            // The idleness test and the claim happen under the entry's gate, the same gate that
            // hands an instance to a caller, so a caller that resolved this instance either wins
            // (the entry survives) or never receives it - it can never be handed an instance that
            // is about to be disposed. Once claimed we always dispose, even under cancellation,
            // rather than leaking the browser process.
            if (!pair.Value.TryClaimIfIdle(cutoff))
            {
                continue;
            }

            Remove(pair.Key, pair.Value);
            await DisposeClaimedAsync(pair.Value, "idle timeout");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            foreach (var pair in _sessions.ToArray())
            {
                if (!pair.Value.TryClaim())
                {
                    continue;
                }

                Remove(pair.Key, pair.Value);
                await DisposeClaimedAsync(pair.Value, "server shutdown");
            }
        }
    }

    /// <summary>
    /// Tears down an instance the caller has already claimed. One instance that refuses to die
    /// must not abort the rest of the sweep or the shutdown, so every failure is contained here.
    /// </summary>
    private async Task DisposeClaimedAsync(SessionEntry entry, string reason)
    {
        var session = entry.Session;
        try
        {
            await session.DisposeAsync();
            _logger.LogDebug(
                "Disposed browser instance '{InstanceName}' for MCP session {SessionId} ({Reason}).",
                session.InstanceName,
                session.SessionId,
                reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to dispose browser instance '{InstanceName}' for MCP session {SessionId} ({Reason}). It has been dropped from the registry; the browser process may need cleaning up manually.",
                session.InstanceName,
                session.SessionId,
                reason);
        }
    }

    /// <summary>
    /// Removes the entry only when the registry still holds this exact entry, so a replacement
    /// instance created for the same name is never removed by an older entry's teardown.
    /// </summary>
    private void Remove(BrowserInstanceKey key, SessionEntry entry)
        => _sessions.TryRemove(new KeyValuePair<BrowserInstanceKey, SessionEntry>(key, entry));

    /// <summary>
    /// True when the browser_instance_* lifecycle tools are actually registered. This mirrors
    /// ConfigureToolCatalog in Program.cs: the Claude-compatible catalog never registers them,
    /// and the NamedInstances feature flag gates them for the standard catalog. When they are
    /// not registered a non-default instanceName can never be satisfied, so it is rejected up
    /// front rather than pointing the caller at a tool that does not exist.
    /// </summary>
    private bool NamedInstancesEnabled
    {
        get
        {
            var features = _optionsMonitor.CurrentValue.Features;
            return features.NamedInstances && !features.ClaudeCompatibleToolCatalog;
        }
    }

    private string BuildNamedInstancesDisabledMessage(string normalizedInstanceName)
    {
        var reason = _optionsMonitor.CurrentValue.Features.ClaudeCompatibleToolCatalog
            ? "the Claude-compatible tool catalog does not include the browser instance lifecycle tools"
            : $"'{PlaywrightMCPSharpOptions.SectionName}:Features:NamedInstances' is disabled on this server";

        return $"Named browser instances are unavailable because {reason}, so instance '{normalizedInstanceName}' cannot be used. "
            + "Omit instanceName to use the default instance.";
    }

    /// <summary>
    /// Registry entry that serializes "hand this instance to a caller" against "evict this
    /// instance". Both run under the same gate, which is what makes the idle sweep's
    /// check-then-dispose a single atomic step. An entry is claimed exactly once; the claimer
    /// owns removal and disposal, and every other party sees the entry as gone.
    /// </summary>
    private sealed class SessionEntry
    {
        private readonly object _gate = new();
        private bool _claimed;

        public SessionEntry(BrowserSession session) => Session = session;

        public BrowserSession Session { get; }

        /// <summary>True once the entry has been claimed for teardown.</summary>
        public bool IsClaimed
        {
            get
            {
                lock (_gate)
                {
                    return _claimed;
                }
            }
        }

        /// <summary>
        /// Marks the instance as in use and refreshes its idle timer. Returns false when the
        /// entry has already been claimed for teardown, in which case the caller must not use it.
        /// </summary>
        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_claimed)
                {
                    return false;
                }

                Session.Touch();
                return true;
            }
        }

        /// <summary>Claims the entry for teardown regardless of how recently it was used.</summary>
        public bool TryClaim()
        {
            lock (_gate)
            {
                if (_claimed)
                {
                    return false;
                }

                _claimed = true;
                return true;
            }
        }

        /// <summary>Claims the entry for teardown only while it is still idle and command-free.</summary>
        public bool TryClaimIfIdle(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                if (_claimed || Session.LastAccessUtc >= cutoff || Session.HasActiveCommand)
                {
                    return false;
                }

                _claimed = true;
                return true;
            }
        }
    }

    private readonly record struct BrowserInstanceKey(string SessionId, string InstanceName);
}
