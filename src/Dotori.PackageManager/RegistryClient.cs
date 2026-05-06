using System.Text;
using System.Text.Json;
using Dotori.PackageManager.Config;

namespace Dotori.PackageManager;

/// <summary>
/// npm registry protocol client.
/// Supports GitHub Packages, GitLab, JFrog, Verdaccio, and any npm-compatible registry.
/// </summary>
public sealed class NpmRegistryClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public NpmRegistryClient(string registryUrl, string? token = null)
    {
        _baseUrl = registryUrl.TrimEnd('/');
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("dotori/1.0");
        if (token is not null)
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    /// <summary>Create a client routed to the correct registry for the given scoped package name.</summary>
    public static NpmRegistryClient ForPackage(string scopedName, DotoriConfig config)
    {
        var url = config.ResolveRegistryUrl(scopedName);
        var reg = config.GetRegistry(url);
        return new NpmRegistryClient(url, reg.Token);
    }

    public static NpmRegistryClient FromConfig(string? registryUrl = null)
    {
        var config = DotoriConfigManager.Load();
        var reg = config.GetRegistry(registryUrl);
        return new NpmRegistryClient(reg.Url, reg.Token);
    }

    /// <summary>GET /@scope/name → packument (all versions + dist metadata).</summary>
    public async Task<NpmPackument?> GetPackumentAsync(string scopedName, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(scopedName);
            var json = await _http.GetStringAsync($"{_baseUrl}/{encoded}", ct);
            return JsonSerializer.Deserialize(json, NpmDtoContext.Default.NpmPackument);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Download tarball from resolved URL (from packument.versions[v].dist.tarball).</summary>
    public async Task<Stream> DownloadTarballAsync(string tarballUrl, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(tarballUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>PUT /@scope/name → npm publish.</summary>
    public async Task PublishAsync(string scopedName, NpmPublishBody body, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(scopedName);
        var json = JsonSerializer.Serialize(body, NpmDtoContext.Default.NpmPublishBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.PutAsync($"{_baseUrl}/{encoded}", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync(ct);
            throw new RegistryException($"Publish failed ({(int)resp.StatusCode}): {error}");
        }
    }

    /// <summary>GET /-/v1/search → npm search format.</summary>
    public async Task<NpmSearchResult> SearchAsync(
        string query, int size = 20, int from = 0, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/-/v1/search?text={Uri.EscapeDataString(query)}&size={size}&from={from}";
            var json = await _http.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize(json, NpmDtoContext.Default.NpmSearchResult)
                ?? new NpmSearchResult();
        }
        catch (HttpRequestException)
        {
            return new NpmSearchResult();
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class RegistryException(string message) : Exception(message);
