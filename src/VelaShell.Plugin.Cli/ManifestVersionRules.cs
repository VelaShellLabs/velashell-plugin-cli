using System.Globalization;
using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Manifest;
using VelaShell.PluginSdk.Versioning;

namespace VelaShell.Plugin.Cli;

/// <summary>
/// <c>plugin.json</c> 里三个版本字段(<c>version</c> / <c>minHostVersion</c> / <c>minSdkVersion</c>)的收紧校验:
/// 在 <see cref="PluginManifestReader" /> 的正则之上,再要求 <see cref="SemanticVersion.TryParse" /> 能解析。
/// <para>
/// 为什么要收紧:SDK 清单正则 <c>^\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?$</c> 比 <see cref="SemanticVersion" /> 宽 ——
/// <c>1.0.0-beta.01</c>(预发布里的数字标识符带前导零)、<c>1.0.0-a..b</c>(空标识符)、
/// 超出 int 的数字段、非 ASCII 数字都能过正则,却没法与别的版本比出先后。商店与 <c>vela-plugin update</c>
/// 按 <see cref="SemanticVersion" /> 排序,于是这种版本永远排在最旧,也永远不会被当成「有更新」。
/// 另外正则的 <c>$</c> 允许结尾多一个换行(<c>"1.0.0\n"</c>),这里一并拒掉。
/// </para>
/// <para>
/// 接受与否**只看 <see cref="SemanticVersion.TryParse" />**(加上不许首尾空白),不在这里另写一套规则;
/// 本类自己写的只是「为什么不行」的那句解释。商店上传审核用的是同一口径。
/// 等 SDK 的 <see cref="PluginManifestReader" /> 自己收紧之后,这个类可以删掉。
/// </para>
/// </summary>
internal static class ManifestVersionRules
{
    /// <summary>
    /// 校验已经过 <see cref="PluginManifestReader" /> 的清单;不合格抛 <see cref="PluginManifestException" />,
    /// 消息里写明是哪个字段、哪一段、怎么改。
    /// </summary>
    public static void Require(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Check("version", manifest.Version);
        if (manifest.MinHostVersion is { } minHost)
        {
            Check("minHostVersion", minHost);
        }
        if (manifest.MinSdkVersion is { } minSdk)
        {
            Check("minSdkVersion", minSdk);
        }
    }

    /// <summary>
    /// 这个版本字符串为什么不合格;合格时返回 <see langword="null" />。
    /// 返回的句子不带字段名,以句号结尾,末尾附一个改法示例。
    /// </summary>
    public static string? Explain(string? value)
    {
        if (value is null)
        {
            return "it is missing." + Example(null);
        }
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            string trimmed = value.Trim();
            return "it has leading or trailing whitespace (a trailing newline counts)."
                   + Example(Explain(trimmed) is null ? trimmed : null);
        }
        if (SemanticVersion.TryParse(value, out _))
        {
            return null;
        }
        int dash = value.IndexOf('-', StringComparison.Ordinal);
        string core = dash < 0 ? value : value[..dash];
        string? preRelease = dash < 0 ? null : value[(dash + 1)..];
        foreach (string part in core.Split('.'))
        {
            if (part.Length > 0 && part.Any(char.IsDigit) && !part.All(char.IsAsciiDigit))
            {
                return $"numeric part '{part}' uses digits other than ASCII 0-9." + Example(null);
            }
            if (part.Length > 0 && part.All(char.IsAsciiDigit)
                && !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return $"numeric part '{part}' is larger than {int.MaxValue}." + Example(null);
            }
        }
        if (preRelease is not null)
        {
            string[] identifiers = preRelease.Split('.');
            if (identifiers.Any(id => id.Length == 0))
            {
                return $"pre-release suffix '{preRelease}' has an empty identifier "
                       + "(two dots in a row, or a dot at its start or end)." + Example(Repair(core, identifiers));
            }
            if (identifiers.FirstOrDefault(HasLeadingZero) is { } zeroPadded)
            {
                return $"pre-release identifier '{zeroPadded}' is a number with a leading zero, which SemVer 2.0 forbids "
                       + $"(it cannot be ordered against '{TrimZeros(zeroPadded)}')." + Example(Repair(core, identifiers));
            }
        }
        return "it is not a valid version." + Example(null);
    }

    private static void Check(string field, string value)
    {
        if (Explain(value) is { } reason)
        {
            throw new PluginManifestException($"Invalid {field} '{Printable(value)}': {reason}");
        }
    }

    private static bool HasLeadingZero(string identifier) =>
        identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit);

    private static string TrimZeros(string identifier) => identifier.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";

    /// <summary>去掉空标识符、去掉数字标识符的前导零;修出来的仍不合法就不给建议。</summary>
    private static string? Repair(string core, IEnumerable<string> identifiers)
    {
        string[] kept = [.. identifiers.Where(id => id.Length > 0).Select(id => HasLeadingZero(id) ? TrimZeros(id) : id)];
        string candidate = kept.Length == 0 ? core : $"{core}-{string.Join('.', kept)}";
        return SemanticVersion.TryParse(candidate, out _) ? candidate : null;
    }

    private static string Example(string? suggestion) =>
        suggestion is not null
            ? $" Use e.g. \"{suggestion}\"."
            : " Expected e.g. \"1.2.0\" or \"1.2.0-beta.1\".";

    /// <summary>报错里把换行等控制字符写成转义,免得一条错误被拆成几行、看不出到底多了什么。</summary>
    private static string Printable(string value) =>
        value.Any(char.IsControl)
            ? string.Concat(value.Select(c => char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString()))
            : value;
}
