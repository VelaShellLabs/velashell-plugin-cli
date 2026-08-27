using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace VelaShell.Plugin.Cli;

/// <summary>商店列表/搜索的一页。</summary>
internal sealed record MarketPage
{
    /// <summary>命中总数(不是本页条数)。</summary>
    public int Total { get; init; }

    /// <summary>页码,从 1 起。</summary>
    public int Page { get; init; }

    /// <summary>每页条数。</summary>
    public int Size { get; init; }

    /// <summary>本页的条目。</summary>
    public MarketListing[] Items { get; init; } = [];
}

/// <summary>列表里的一条。字段只有列表页需要的那些,兼容性细节要看详情接口。</summary>
internal sealed record MarketListing
{
    /// <summary>插件 id。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>一句话简介。</summary>
    public string? Summary { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>标签。</summary>
    public string[] Tags { get; init; } = [];

    /// <summary>最新版本号。</summary>
    public string? LatestVersion { get; init; }

    /// <summary>最新版本声明的 apiLevel。</summary>
    public int LatestApiLevel { get; init; }

    /// <summary>最新版本要求的最低宿主版本(可空)。</summary>
    public string? LatestMinHostVersion { get; init; }

    /// <summary>累计下载数。</summary>
    public int Downloads { get; init; }

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>插件详情:比列表多出发布者、许可、主页与**完整版本列表**。</summary>
internal sealed record MarketPlugin
{
    /// <summary>插件 id。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>一句话简介。</summary>
    public string? Summary { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>发布者。</summary>
    public string? Publisher { get; init; }

    /// <summary>标签。</summary>
    public string[] Tags { get; init; } = [];

    /// <summary>主页 / 源码地址。</summary>
    public string? Homepage { get; init; }

    /// <summary>许可证标识。</summary>
    public string? License { get; init; }

    /// <summary>累计下载数。</summary>
    public int Downloads { get; init; }

    /// <summary>全部已发布版本(商店按新到旧给)。</summary>
    public MarketVersion[] Versions { get; init; } = [];
}

/// <summary>某个插件的一个已发布版本。</summary>
internal sealed record MarketVersion
{
    /// <summary>版本号。</summary>
    public string Version { get; init; } = "";

    /// <summary>该版本声明的 apiLevel。</summary>
    public int ApiLevel { get; init; }

    /// <summary>要求的最低宿主版本(可空)。</summary>
    public string? MinHostVersion { get; init; }

    /// <summary>宿主模式(<c>InProcess</c> / <c>Isolated</c>)。</summary>
    public string? HostMode { get; init; }

    /// <summary><c>.vpx</c> 文件字节数。</summary>
    public long PackageSize { get; init; }

    /// <summary>容器内载荷的 SHA-256(与包头里的那个是同一个值)。</summary>
    public string? PayloadSha256 { get; init; }

    /// <summary>整个 <c>.vpx</c> 文件的 SHA-256。</summary>
    public string? FileSha256 { get; init; }

    /// <summary>商店侧收包时验出来的签名结论(<c>Trusted</c> / <c>Unsigned</c> / …)。</summary>
    public string? Signature { get; init; }

    /// <summary>发布时间。</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>该版本的下载数。</summary>
    public int Downloads { get; init; }
}

/// <summary>下载票据:一条**限时**直链,外加商店声明的两个摘要与字节数。</summary>
internal sealed record MarketDownload
{
    /// <summary>下载地址(预签名,分钟级过期,取到就用)。</summary>
    public string Url { get; init; } = "";

    /// <summary>容器内载荷的 SHA-256。</summary>
    public string? PayloadSha256 { get; init; }

    /// <summary>整个 <c>.vpx</c> 文件的 SHA-256。</summary>
    public string? FileSha256 { get; init; }

    /// <summary><c>.vpx</c> 文件字节数。</summary>
    public long PackageSize { get; init; }
}

/// <summary>商店接口返回的错误体。</summary>
internal sealed record MarketError
{
    /// <summary>人话错误消息。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 插件商店的只读客户端。三个接口就够装一个插件:
/// <list type="bullet">
///   <item><c>GET /api/plugins?q=</c> —— 搜索/列表</item>
///   <item><c>GET /api/plugins/{id}</c> —— 详情,含完整版本列表</item>
///   <item><c>GET /api/plugins/{id}/versions/{version}/download</c> —— 换一条限时直链 + 摘要</item>
/// </list>
/// 刻意**全同步**(<see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)" />):
/// CLI 其余部分都是同步的,为三次网络调用把整条命令链改成 async 不划算,
/// 而 `.GetAwaiter().GetResult()` 只是把同样的阻塞藏起来。
/// </summary>
internal sealed class Marketplace : IDisposable
{
    /// <summary>官方商店。<c>--source</c> 或 <c>VELA_PLUGIN_MARKET</c> 可以指到自建的一份。</summary>
    public const string DefaultBaseUrl = "https://market.easilynet.top";

    /// <summary>覆盖商店地址的环境变量名。</summary>
    public const string BaseUrlEnvironmentVariable = "VELA_PLUGIN_MARKET";

    private readonly HttpClient _http;

    /// <summary>商店根地址(已去掉结尾斜杠)。</summary>
    public string BaseUrl { get; }

    /// <summary>元数据接口的超时。小请求,卡住就是服务端有问题,不该让人等。</summary>
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(30);

    /// <summary>单个包的下载超时。慢网 + 几十兆的包,给足。</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>下载体积硬上限:比容器自身的载荷上限略宽,挡住"商店说 1MB 结果流个没完"。</summary>
    private const long MaxDownloadBytes = 640L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 商店发 camelCase,DTO 是 PascalCase。大小写不敏感一条就够,不必给每个属性挂特性。
        PropertyNameCaseInsensitive = true
    };

    /// <param name="baseUrl">商店根地址;<see langword="null" /> 时按 <see cref="ResolveBaseUrl" /> 取默认。</param>
    public Marketplace(string? baseUrl = null)
    {
        BaseUrl = ResolveBaseUrl(baseUrl);
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // 直链常常在别的域(对象存储),必须跟重定向;限次数,别跟进环里。
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            // 超时逐次由 CancellationToken 控制:元数据 30 秒、下载 15 分钟,
            // 用一个 HttpClient.Timeout 无法同时满足这两者。
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("vela-plugin", ToolVersion));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// 商店地址的三级取值:显式参数 → 环境变量 → 内置默认。
    /// 只放行 http/https —— 这个值会被当成 URL 去发请求,file:// 之类不该走到网络栈。
    /// </summary>
    /// <param name="option"><c>--source</c> 传进来的值,可空。</param>
    public static string ResolveBaseUrl(string? option)
    {
        // 空的环境变量当"没配";而**显式**传进来的空 --source 是错误,不能悄悄回落到官方商店 ——
        // `--source "$MY_MARKET"` 碰上变量没设时,那等于把内网商店的请求发去公网。
        string value = option
                       ?? (Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable) is { Length: > 0 } fromEnvironment
                           ? fromEnvironment
                           : DefaultBaseUrl);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CliException($"'{value}' is not a valid http(s) marketplace URL.");
        }
        return value.TrimEnd('/');
    }

    /// <summary>搜索/列表。<paramref name="query" /> 为空则按商店默认顺序列出。</summary>
    /// <param name="query">搜索词。</param>
    /// <param name="page">页码,从 1 起。</param>
    /// <param name="size">每页条数。</param>
    public MarketPage Search(string? query, int page = 1, int size = 20)
    {
        string path = $"/api/plugins?page={page}&size={size}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += $"&q={Uri.EscapeDataString(query)}";
        }
        return GetJson<MarketPage>(BaseUrl + path, MetadataTimeout);
    }

    /// <summary>取插件详情(含完整版本列表)。</summary>
    /// <param name="id">插件 id。</param>
    public MarketPlugin Get(string id) =>
        GetJson<MarketPlugin>($"{BaseUrl}/api/plugins/{Uri.EscapeDataString(id)}", MetadataTimeout);

    /// <summary>换一条限时下载直链。</summary>
    /// <param name="id">插件 id。</param>
    /// <param name="version">版本号。</param>
    public MarketDownload GetDownload(string id, string version) =>
        GetJson<MarketDownload>(
            $"{BaseUrl}/api/plugins/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(version)}/download",
            MetadataTimeout);

    /// <summary>
    /// 把包下到 <paramref name="destination" />,边下边算 SHA-256,返回小写十六进制摘要。
    /// 只负责"完整地拿到字节并告诉你它是什么",**不做**任何信任判断 —— 那是调用方的事。
    /// </summary>
    /// <param name="ticket">下载票据。</param>
    /// <param name="destination">落盘路径(所在目录须已存在)。</param>
    /// <param name="showProgress">是否在 stderr 上打进度。</param>
    public string Download(MarketDownload ticket, string destination, bool showProgress)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!Uri.TryCreate(ticket.Url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CliException($"The marketplace returned a download URL that is not http(s): '{ticket.Url}'");
        }
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            Program.Warn($"the download URL is plain HTTP ({uri.Host}); the package digest is still checked, "
                         + "but nobody can vouch for who served it.");
        }

        using var cts = new CancellationTokenSource(DownloadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = Send(request, cts.Token);
        EnsureSuccess(response, uri.ToString());

        long? expected = response.Content.Headers.ContentLength ?? (ticket.PackageSize > 0 ? ticket.PackageSize : null);
        if (expected > MaxDownloadBytes)
        {
            throw new CliException($"The package is {Format.Bytes(expected.Value)}, over the "
                                   + $"{Format.Bytes(MaxDownloadBytes)} download limit.");
        }

        using Stream source = response.Content.ReadAsStream(cts.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            using (FileStream file = File.Create(destination))
            {
                byte[] buffer = new byte[128 * 1024];
                int read;
                long lastReported = -1;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new CliException($"The download exceeded the {Format.Bytes(MaxDownloadBytes)} limit; aborted.");
                    }
                    hash.AppendData(buffer, 0, read);
                    file.Write(buffer, 0, read);
                    if (showProgress && total - lastReported >= 64 * 1024)
                    {
                        lastReported = total;
                        ReportProgress(total, expected);
                    }
                }
            }
            if (showProgress)
            {
                ReportProgress(total, expected);
                Console.Error.WriteLine();
            }
        }
        catch
        {
            // 半截文件比没有文件更坏:下次跑缓存命中检查会读到它,又得靠摘要不符才发现。
            TryDelete(destination);
            throw;
        }
        // 商店声明了大小就必须对得上。对不上不等于内容被改(下面还要验摘要),
        // 但它已经说明"拿到的东西不是商店以为它在给的东西",没有继续的理由。
        if (ticket.PackageSize > 0 && total != ticket.PackageSize)
        {
            TryDelete(destination);
            throw new CliException($"Downloaded {total} bytes but the marketplace declared {ticket.PackageSize}.");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ReportProgress(long done, long? total)
    {
        string line = total is { } size and > 0
            ? $"  downloading {done * 100 / size,3}%  {Format.Bytes(done)} / {Format.Bytes(size)}"
            : $"  downloading {Format.Bytes(done)}";
        // 补空格再回车:上一行更长时(百分比从 100% 掉回两位数不会发生,但字节数会变短)会留下残字。
        Console.Error.Write($"\r{line}    ");
    }

    private T GetJson<T>(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = Send(request, cts.Token);
        EnsureSuccess(response, url);
        using Stream stream = response.Content.ReadAsStream(cts.Token);
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                   ?? throw new CliException($"The marketplace returned an empty body for {url}.");
        }
        catch (JsonException ex)
        {
            // 十有八九是把 SPA 的 index.html 当成了 API —— --source 指错时就是这个症状。
            throw new CliException($"The marketplace returned a body that is not JSON ({url}): {ex.Message}. "
                                   + "Is --source pointing at the site root instead of a VelaShell marketplace?");
        }
    }

    private HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return _http.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new CliException($"Timed out talking to {request.RequestUri?.Host}.");
        }
        catch (HttpRequestException ex)
        {
            throw new CliException($"Cannot reach {request.RequestUri?.Host}: {ex.Message}");
        }
    }

    /// <summary>把 HTTP 错误翻成人话。商店的错误体是 <c>{"error":"…"}</c>,优先用它。</summary>
    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        string? detail = null;
        try
        {
            using Stream stream = response.Content.ReadAsStream();
            detail = JsonSerializer.Deserialize<MarketError>(stream, JsonOptions)?.Error;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            // 错误体读不出来不重要,状态码本身已经够定位了。
        }
        throw new CliException(response.StatusCode == HttpStatusCode.NotFound && detail is null
            ? $"Not found on the marketplace: {url}"
            : $"The marketplace answered {(int)response.StatusCode} {response.ReasonPhrase}"
              + (detail is null ? $" for {url}" : $": {detail}"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就算了,调用方已经在抛更重要的错。
        }
    }

    /// <summary>本工具的包版本,拼进 User-Agent 让商店那边能看出客户端版本分布。</summary>
    private static string ToolVersion =>
        typeof(Marketplace).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Marketplace).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>输出用的小格式化器。</summary>
internal static class Format
{
    /// <summary>字节数 → 人读的量级。</summary>
    /// <param name="value">字节数。</param>
    public static string Bytes(long value) => value switch
    {
        < 1024 => $"{value} B",
        < 1024 * 1024 => $"{value / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{value / (1024.0 * 1024):0.#} MB",
        _ => $"{value / (1024.0 * 1024 * 1024):0.##} GB"
    };
}
