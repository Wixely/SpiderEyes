using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;

namespace SpiderEyes.Server.Services;

public sealed class FileAccessService
{
    private readonly IOptionsMonitor<SpiderEyesOptions> _optionsMonitor;

    public FileAccessService(IOptionsMonitor<SpiderEyesOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public async Task<string> ResolveReadablePathAsync(
        BrowserSession session,
        McpServer server,
        string path,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAllowedPathAsync(session, server, path, cancellationToken);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"File '{resolved}' was not found.", resolved);
        }

        return resolved;
    }

    public async Task<IReadOnlyList<string>> ResolveReadablePathsAsync(
        BrowserSession session,
        McpServer server,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        foreach (var path in paths)
        {
            results.Add(await ResolveReadablePathAsync(session, server, path, cancellationToken));
        }

        return results;
    }

    public async Task<string> ResolveAllowedPathAsync(
        BrowserSession session,
        McpServer server,
        string path,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path));
        if (options.Features.AllowUnrestrictedFileAccess)
        {
            return fullPath;
        }

        var roots = await GetAllowedRootsAsync(session, server, cancellationToken);
        if (roots.Any(root => IsDescendantOf(fullPath, root)))
        {
            return fullPath;
        }

        throw new InvalidOperationException($"Path '{fullPath}' is outside the allowed MCP roots.");
    }

    private async Task<IReadOnlyList<string>> GetAllowedRootsAsync(
        BrowserSession session,
        McpServer server,
        CancellationToken cancellationToken)
    {
        var roots = new List<string> { session.ArtifactDirectory };

        try
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
            foreach (var root in result.Roots ?? [])
            {
                if (!Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri) || !uri.IsFile)
                {
                    continue;
                }

                roots.Add(Path.GetFullPath(uri.LocalPath));
            }
        }
        catch
        {
            roots.Add(Path.GetFullPath(Environment.CurrentDirectory));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsDescendantOf(string path, string root)
    {
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
