using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using SpiderEyes.Server.Models;

namespace SpiderEyes.Server.Services;

public sealed class RunCodeService
{
    private readonly FileAccessService _fileAccessService;

    public RunCodeService(FileAccessService fileAccessService)
    {
        _fileAccessService = fileAccessService;
    }

    public async Task<BrowserActionPayload> ExecuteAsync(
        BrowserSession session,
        McpServer server,
        string? code,
        string? fileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Provide either inline code or a filename.");
        }

        var source = code;
        if (source is null)
        {
            var resolved = await _fileAccessService.ResolveReadablePathAsync(session, server, fileName!, cancellationToken);
            source = await File.ReadAllTextAsync(resolved, cancellationToken);
        }

        var page = await session.GetPageAsync(null, createIfMissing: true, cancellationToken);
        var globals = new BrowserScriptGlobals(session, page, cancellationToken);
        var options = ScriptOptions.Default
            .AddImports(
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Text.Json",
                "System.Threading",
                "System.Threading.Tasks",
                "Microsoft.Playwright")
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(JsonSerializer).Assembly,
                typeof(IPage).Assembly,
                typeof(BrowserScriptGlobals).Assembly);

        var state = await CSharpScript.RunAsync(source, options, globals, typeof(BrowserScriptGlobals), cancellationToken);
        var result = state.ReturnValue is JsonElement jsonElement
            ? JsonSerializer.Deserialize<object>(jsonElement.GetRawText())
            : state.ReturnValue;

        return new BrowserActionPayload(
            Data: new
            {
                result,
                fileName,
            },
            Message: "C# script executed successfully.",
            Artifacts: globals.Artifacts.ToArray());
    }
}

public sealed class BrowserScriptGlobals
{
    private readonly BrowserSession _session;
    private readonly CancellationToken _cancellationToken;

    public BrowserScriptGlobals(BrowserSession session, IPage page, CancellationToken cancellationToken)
    {
        _session = session;
        _cancellationToken = cancellationToken;
        this.page = page;
        context = page.Context;
        browser = page.Context.Browser;
        playwright = typeof(Playwright).Assembly.GetName().Name ?? "Microsoft.Playwright";
        sessionId = session.SessionId;
    }

    public IPage page { get; }

    public IBrowserContext context { get; }

    public IBrowser? browser { get; }

    public string playwright { get; }

    public string sessionId { get; }

    public CancellationToken ct => _cancellationToken;

    public List<ArtifactInfo> Artifacts { get; } = [];

    public async Task<string> SaveTextAsync(string fileName, string content)
    {
        var path = _session.CreateArtifactPath(fileName, ".txt");
        await File.WriteAllTextAsync(path, content, _cancellationToken);
        Artifacts.Add(new ArtifactInfo { Name = Path.GetFileName(path), Path = path, MimeType = "text/plain" });
        return path;
    }

    public async Task<string> SaveJsonAsync(string fileName, object value)
    {
        var path = _session.CreateArtifactPath(fileName, ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), _cancellationToken);
        Artifacts.Add(new ArtifactInfo { Name = Path.GetFileName(path), Path = path, MimeType = "application/json" });
        return path;
    }
}
