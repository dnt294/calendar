using System.Net;
using System.Text.RegularExpressions;

namespace Calendar.Core.Models;

/// <summary>Event descriptions from Google may be HTML; the app shows and edits them as plain text.</summary>
public static partial class HtmlText
{
    /// <summary>
    /// Line breaks for &lt;br&gt;, &lt;p&gt;, &lt;div&gt; and &lt;li&gt;; links keep their URL when it differs
    /// from the text; other tags dropped; entities decoded; blank lines collapsed.
    /// Plain text (no tags) is returned unchanged apart from entity decoding.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        if (!TagRegex().IsMatch(html))
            return WebUtility.HtmlDecode(html).Replace(' ', ' ').Trim();

        var text = AnchorRegex().Replace(html, m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups["url"].Value);
            var label = TagRegex().Replace(m.Groups["text"].Value, "");
            label = WebUtility.HtmlDecode(label).Trim();
            return label.Length == 0 || label == url ? url : $"{label} ({url})";
        });
        text = BreakRegex().Replace(text, "\n");
        text = BlockEndRegex().Replace(text, "\n");
        text = ListItemRegex().Replace(text, "\n• ");
        text = TagRegex().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = SpacesBeforeNewlineRegex().Replace(text, "\n");
        text = BlankLinesRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("""<a\s[^>]*href\s*=\s*["'](?<url>[^"']*)["'][^>]*>(?<text>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"</(p|div|ul|ol|h[1-6])\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex SpacesBeforeNewlineRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();
}
