namespace VelaShell.Plugin.Cli;

/// <summary>
/// 版本号排序:数字段逐段比;数字段相同时**带预发布后缀的排在后面**
/// (<c>1.5.0</c> &gt; <c>1.5.0-preview.1</c>),两个都带后缀就按序数比字符串。
/// <para>
/// 刻意**不是**完整的 SemVer 优先级规则(不拆点分标识符、不区分数字段与字母段)。理由:
/// 这里只用来"从商店给的版本列表里挑最新的那个",而它的数字口径必须与宿主判兼容用的
/// <c>IsOlder</c> 一致 —— 两边口径不同会得到"CLI 说该升、宿主说没变"这种没法解释的结果。
/// 真要按 SemVer 严格排序,该先让宿主那边一起改。
/// </para>
/// </summary>
internal sealed class VersionOrder : IComparer<string>
{
    /// <summary>共用实例(无状态)。</summary>
    public static readonly VersionOrder Instance = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }
        string[] left = x.Split('-', 2);
        string[] right = y.Split('-', 2);
        if (ParseNumeric(left[0]) is { } leftNumber && ParseNumeric(right[0]) is { } rightNumber
            && leftNumber.CompareTo(rightNumber) is var byNumber && byNumber != 0)
        {
            return byNumber;
        }
        return (left.Length > 1, right.Length > 1) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            (true, true) => string.CompareOrdinal(left[1], right[1]),
            _ => string.CompareOrdinal(x, y)
        };
    }

    private static Version? ParseNumeric(string value)
    {
        // "2" 这种一段式解析不出 Version,补一段再试 —— 与 Program.IsOlder 同一套补法。
        string numeric = value.Contains('.', StringComparison.Ordinal) ? value : value + ".0";
        return Version.TryParse(numeric, out Version? parsed) ? parsed : null;
    }
}
