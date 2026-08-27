using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaywrightMCPSharp.Server.Configuration;
using PlaywrightMCPSharp.Server.Services;

namespace PlaywrightMCPSharp.Server.Tests;

public sealed class BrowserInstanceLifecycleTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly PlaywrightMCPSharpOptions _options;
    private readonly BrowserSessionManager _manager;

    public BrowserInstanceLifecycleTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), "PlaywrightMCPSharp.Tests.Instances", Guid.NewGuid().ToString("N"));
        _options = new PlaywrightMCPSharpOptions();
        _options.Session.ArtifactRoot = _artifactRoot;
        _options.Session.MaxInstancesPerSession = 3;
        _options.Session.MaxGlobalInstances = 4;

        var optionsMonitor = new StaticOptionsMonitor(_options);
        _manager = new BrowserSessionManager(
            optionsMonitor,
            NullLoggerFactory.Instance,
            new PlaywrightRuntimeService(optionsMonitor, NullLogger<PlaywrightRuntimeService>.Instance));
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("agent-a", "agent-a")]
    [InlineData("  Agent-A  ", "agent-a")]
    [InlineData("job_1.retry", "job_1.retry")]
    public void Normalize_AcceptsValidNames(string input, string expected)
        => Assert.Equal(expected, BrowserInstanceName.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-starts-with-dash")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("..")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void Normalize_RejectsInvalidNames(string input)
        => Assert.Throws<ArgumentException>(() => BrowserInstanceName.Normalize(input));

    [Fact]
    public async Task GetAsync_AutoCreatesDefaultInstance_ButFailsForUnknownNames()
    {
        var defaultSession = await _manager.GetAsync("session-1", null, CancellationToken.None);
        Assert.Equal(BrowserInstanceName.Default, defaultSession.InstanceName);
        Assert.Same(defaultSession, await _manager.GetAsync("session-1", "default", CancellationToken.None));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.GetAsync("session-1", "agent-a", CancellationToken.None));
        Assert.Contains("browser_instance_create", exception.Message);
    }

    [Fact]
    public async Task CreateInstanceAsync_IsExplicit_AndRejectsDuplicates()
    {
        var created = await _manager.CreateInstanceAsync("session-1", "Agent-A", null, CancellationToken.None);
        Assert.Equal("agent-a", created.InstanceName);
        Assert.Same(created, await _manager.GetAsync("session-1", "agent-a", CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None));
    }

    [Fact]
    public async Task Instances_AreScopedToTheirSession()
    {
        await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.GetAsync("session-2", "agent-a", CancellationToken.None));

        var sameName = await _manager.CreateInstanceAsync("session-2", "agent-a", null, CancellationToken.None);
        Assert.NotSame(await _manager.GetAsync("session-1", "agent-a", CancellationToken.None), sameName);
        Assert.Single(_manager.ListInstances("session-2"));
    }

    [Fact]
    public async Task CreateInstanceAsync_EnforcesPerSessionAndGlobalCaps()
    {
        await _manager.CreateInstanceAsync("session-1", "a", null, CancellationToken.None);
        await _manager.CreateInstanceAsync("session-1", "b", null, CancellationToken.None);
        await _manager.CreateInstanceAsync("session-1", "c", null, CancellationToken.None);

        var perSession = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CreateInstanceAsync("session-1", "d", null, CancellationToken.None));
        Assert.Contains("maximum 3", perSession.Message);

        await _manager.CreateInstanceAsync("session-2", "a", null, CancellationToken.None);
        var global = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CreateInstanceAsync("session-2", "b", null, CancellationToken.None));
        Assert.Contains("maximum 4", global.Message);
    }

    [Fact]
    public async Task CloseInstanceAsync_RemovesInstance_AndLeavesOthersIntact()
    {
        await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);
        var untouched = await _manager.CreateInstanceAsync("session-1", "agent-b", null, CancellationToken.None);

        Assert.True(await _manager.CloseInstanceAsync("session-1", "agent-a"));
        Assert.False(await _manager.CloseInstanceAsync("session-1", "agent-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.GetAsync("session-1", "agent-a", CancellationToken.None));
        Assert.Same(untouched, await _manager.GetAsync("session-1", "agent-b", CancellationToken.None));
        Assert.False(untouched.IsDisposed);
    }

    [Fact]
    public async Task ClosedInstance_FailsCommandsDeterministically()
    {
        var session = await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);
        Assert.True(await _manager.CloseInstanceAsync("session-1", "agent-a"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunExclusiveAsync<object?>((_, _) => Task.FromResult<object?>(null), CancellationToken.None));
        Assert.Contains("closed", exception.Message);
    }

    [Fact]
    public async Task CleanupIdleSessions_SkipsBusyInstances()
    {
        _options.Session.IdleTimeout = TimeSpan.Zero;
        var session = await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);

        using var commandStarted = new SemaphoreSlim(0, 1);
        using var releaseCommand = new SemaphoreSlim(0, 1);
        var running = session.RunExclusiveAsync<object?>(async (_, ct) =>
        {
            commandStarted.Release();
            await releaseCommand.WaitAsync(ct);
            return null;
        }, CancellationToken.None);

        await commandStarted.WaitAsync();
        await _manager.CleanupIdleSessionsAsync(CancellationToken.None);
        Assert.False(session.IsDisposed);
        Assert.Same(session, await _manager.GetAsync("session-1", "agent-a", CancellationToken.None));

        releaseCommand.Release();
        await running;

        await _manager.CleanupIdleSessionsAsync(CancellationToken.None);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task ArtifactDirectories_AreIsolatedPerInstance()
    {
        var a = await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);
        var b = await _manager.CreateInstanceAsync("session-1", "agent-b", null, CancellationToken.None);

        Assert.NotEqual(a.ArtifactDirectory, b.ArtifactDirectory);
        Assert.True(Directory.Exists(a.ArtifactDirectory));
        Assert.True(Directory.Exists(b.ArtifactDirectory));
    }

    [Fact]
    public async Task DownloadsDirectories_AreIsolatedPerInstance()
    {
        var a = await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);
        var b = await _manager.CreateInstanceAsync("session-1", "agent-b", null, CancellationToken.None);

        Assert.NotEqual(a.DownloadsDirectory, b.DownloadsDirectory);
        Assert.True(Directory.Exists(a.DownloadsDirectory));
        Assert.True(Directory.Exists(b.DownloadsDirectory));
    }

    [Fact]
    public async Task StopAsync_DisposesEveryInstance()
    {
        var a = await _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None);
        var b = await _manager.CreateInstanceAsync("session-2", "agent-b", null, CancellationToken.None);
        var defaultSession = await _manager.GetAsync("session-3", null, CancellationToken.None);

        await _manager.StopAsync(CancellationToken.None);

        Assert.True(a.IsDisposed);
        Assert.True(b.IsDisposed);
        Assert.True(defaultSession.IsDisposed);
        Assert.Empty(_manager.ListInstances("session-1"));
    }

    [Fact]
    public async Task ConcurrentCreate_OfTheSameName_AdmitsExactlyOne()
    {
        // Creation is explicit and atomic: the losers must fail rather than quietly returning
        // the winner's instance, which would hand two agents the same browser.
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None)))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts.Select(async task =>
        {
            try
            {
                return (Session: await task, Failed: false);
            }
            catch (InvalidOperationException)
            {
                return (Session: null!, Failed: true);
            }
        }));

        var created = outcomes.Where(o => !o.Failed).ToArray();
        Assert.Single(created);
        Assert.Equal(7, outcomes.Count(o => o.Failed));
        Assert.Single(_manager.ListInstances("session-1"));
        Assert.Same(created[0].Session, await _manager.GetAsync("session-1", "agent-a", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentCreateAndClose_LeaveConsistentState()
    {
        // Interleaving create and close on one name must never leave a live entry pointing at a
        // disposed session, nor a disposed session reachable through GetAsync.
        for (var round = 0; round < 20; round++)
        {
            var create = Task.Run(() => _manager.CreateInstanceAsync("session-1", "agent-a", null, CancellationToken.None));
            var close = Task.Run(() => _manager.CloseInstanceAsync("session-1", "agent-a"));

            BrowserSession? created = null;
            try
            {
                created = await create;
            }
            catch (InvalidOperationException)
            {
                // Lost the race against an existing instance; nothing to assert for this round.
            }

            await close;

            var live = _manager.ListInstances("session-1");
            Assert.All(live, session => Assert.False(session.IsDisposed));

            if (created is not null && live.Contains(created))
            {
                Assert.False(created.IsDisposed);
            }

            await _manager.CloseInstanceAsync("session-1", "agent-a");
            Assert.Empty(_manager.ListInstances("session-1"));
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<PlaywrightMCPSharpOptions>
    {
        public StaticOptionsMonitor(PlaywrightMCPSharpOptions value) => CurrentValue = value;

        public PlaywrightMCPSharpOptions CurrentValue { get; }

        public PlaywrightMCPSharpOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<PlaywrightMCPSharpOptions, string?> listener) => null;
    }
}
