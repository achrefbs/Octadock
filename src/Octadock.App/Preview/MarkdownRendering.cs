using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Octadock.App.Preview;

/// <summary>Brushes and fonts the markdown renderer uses, supplied by the host card.</summary>
/// <param name="Text">Body text brush.</param>
/// <param name="Muted">Secondary text brush (quotes, rules).</param>
/// <param name="Accent">Accent brush (links, inline-code text).</param>
/// <param name="CodeBackground">Background for code runs and fenced blocks.</param>
/// <param name="Rule">Brush for horizontal rules and quote bars.</param>
internal sealed record MarkdownPalette(
    Brush Text,
    Brush Muted,
    Brush Accent,
    Brush CodeBackground,
    Brush Rule);

/// <summary>
/// Renders a useful subset of Markdown into a WPF <see cref="FlowDocument"/> for
/// the preview card: ATX headings, fenced code blocks, bullet/numbered lists,
/// block quotes, horizontal rules, paragraphs, and inline bold / italic /
/// strikethrough / code / links. Links are intentionally not clickable — the
/// destination shows as a tooltip so a preview can never navigate anywhere.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MarkdownRendering
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    /// <summary>Builds the rendered document.</summary>
    public static FlowDocument BuildDocument(string markdown, MarkdownPalette palette)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(20, 16, 20, 18),
            FontSize = 13.5,
            Foreground = palette.Text,
            PageWidth = double.NaN,
        };

        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                i = AppendCodeBlock(document, lines, i, palette);
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                document.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Border
                {
                    Height = 1,
                    Background = palette.Rule,
                    Margin = new Thickness(0, 6, 0, 6),
                }));
                i++;
                continue;
            }

            int headingLevel = HeadingLevel(trimmed);
            if (headingLevel > 0)
            {
                document.Blocks.Add(BuildHeading(trimmed, headingLevel, palette));
                i++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                i = AppendBlockQuote(document, lines, i, palette);
                continue;
            }

            if (IsBulletItem(trimmed) || IsOrderedItem(trimmed, out _))
            {
                i = AppendList(document, lines, i, palette);
                continue;
            }

            i = AppendParagraph(document, lines, i, palette);
        }

        return document;
    }

    // ---- Blocks ------------------------------------------------------------

    private static int AppendCodeBlock(FlowDocument document, string[] lines, int start, MarkdownPalette palette)
    {
        int i = start + 1;
        var code = new System.Text.StringBuilder();
        while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (code.Length > 0)
            {
                code.Append('\n');
            }

            code.Append(lines[i]);
            i++;
        }

        var paragraph = new Paragraph(new Run(code.ToString()))
        {
            FontFamily = MonoFont,
            FontSize = 12,
            Background = palette.CodeBackground,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 8),
        };
        document.Blocks.Add(paragraph);

        return i < lines.Length ? i + 1 : i;
    }

    private static Paragraph BuildHeading(string trimmed, int level, MarkdownPalette palette)
    {
        string text = trimmed[level..].TrimStart().TrimEnd('#', ' ');
        var paragraph = new Paragraph
        {
            FontSize = level switch
            {
                1 => 21,
                2 => 18,
                3 => 16,
                4 => 14.5,
                _ => 13.5,
            },
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, level <= 2 ? 12 : 8, 0, 4),
        };
        AppendInlines(paragraph.Inlines, text, palette);
        return paragraph;
    }

    private static int AppendBlockQuote(FlowDocument document, string[] lines, int start, MarkdownPalette palette)
    {
        int i = start;
        var text = new System.Text.StringBuilder();
        while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
        {
            string content = lines[i].TrimStart().TrimStart('>').TrimStart();
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(content);
            i++;
        }

        var paragraph = new Paragraph
        {
            Foreground = palette.Muted,
            BorderBrush = palette.Rule,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Margin = new Thickness(0, 4, 0, 8),
        };
        AppendInlines(paragraph.Inlines, text.ToString(), palette);
        document.Blocks.Add(paragraph);
        return i;
    }

    private static int AppendList(FlowDocument document, string[] lines, int start, MarkdownPalette palette)
    {
        bool ordered = IsOrderedItem(lines[start].TrimStart(), out _);
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(6, 4, 0, 8),
            Padding = new Thickness(18, 0, 0, 0),
        };

        int i = start;
        while (i < lines.Length)
        {
            string trimmed = lines[i].TrimStart();
            string? content = null;
            if (!ordered && IsBulletItem(trimmed))
            {
                content = trimmed[2..].TrimStart();
            }
            else if (ordered && IsOrderedItem(trimmed, out int markerLength))
            {
                content = trimmed[markerLength..].TrimStart();
            }

            if (content is null)
            {
                break;
            }

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            AppendInlines(paragraph.Inlines, content, palette);
            list.ListItems.Add(new ListItem(paragraph));
            i++;
        }

        document.Blocks.Add(list);
        return i;
    }

    private static int AppendParagraph(FlowDocument document, string[] lines, int start, MarkdownPalette palette)
    {
        int i = start;
        var text = new System.Text.StringBuilder();
        while (i < lines.Length)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("```", StringComparison.Ordinal) ||
                HeadingLevel(trimmed) > 0 ||
                trimmed.StartsWith('>') ||
                IsBulletItem(trimmed) ||
                IsOrderedItem(trimmed, out _) ||
                IsHorizontalRule(trimmed))
            {
                break;
            }

            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(trimmed.TrimEnd());
            i++;
        }

        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        AppendInlines(paragraph.Inlines, text.ToString(), palette);
        document.Blocks.Add(paragraph);
        return i;
    }

    private static int HeadingLevel(string trimmed)
    {
        int level = 0;
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
        {
            level++;
        }

        return level > 0 && level < trimmed.Length && trimmed[level] == ' ' ? level : 0;
    }

    private static bool IsBulletItem(string trimmed)
        => trimmed.Length >= 2 &&
           trimmed[0] is '-' or '*' or '+' &&
           trimmed[1] == ' ';

    private static bool IsOrderedItem(string trimmed, out int markerLength)
    {
        markerLength = 0;
        int digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits is 0 or > 3 || digits + 1 >= trimmed.Length)
        {
            return false;
        }

        if (trimmed[digits] != '.' || trimmed[digits + 1] != ' ')
        {
            return false;
        }

        markerLength = digits + 1;
        return true;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        char c = trimmed[0];
        if (c is not ('-' or '*' or '_'))
        {
            return false;
        }

        foreach (char ch in trimmed)
        {
            if (ch != c && ch != ' ')
            {
                return false;
            }
        }

        return trimmed.Count(ch => ch == c) >= 3;
    }

    // ---- Inlines -------------------------------------------------------------

    /// <summary>Parses inline markdown (code, bold, italic, strikethrough, links) into WPF inlines.</summary>
    internal static void AppendInlines(InlineCollection target, string text, MarkdownPalette palette)
    {
        int position = 0;
        while (position < text.Length)
        {
            (int index, string marker) = FindNextMarker(text, position);
            if (index < 0)
            {
                target.Add(new Run(text[position..]));
                return;
            }

            if (index > position)
            {
                target.Add(new Run(text[position..index]));
            }

            position = marker switch
            {
                "`" => AppendCode(target, text, index, palette),
                "**" or "__" => AppendEmphasis(target, text, index, marker, bold: true, palette),
                "*" or "_" => AppendEmphasis(target, text, index, marker, bold: false, palette),
                "~~" => AppendStrike(target, text, index, palette),
                "[" => AppendLink(target, text, index, palette),
                _ => index + marker.Length,
            };
        }
    }

    private static (int Index, string Marker) FindNextMarker(string text, int from)
    {
        int best = -1;
        string marker = string.Empty;
        foreach (string candidate in (string[])["`", "**", "__", "*", "_", "~~", "["])
        {
            int i = text.IndexOf(candidate, from, StringComparison.Ordinal);
            if (i >= 0 && (best < 0 || i < best ||
                (i == best && candidate.Length > marker.Length)))
            {
                best = i;
                marker = candidate;
            }
        }

        return (best, marker);
    }

    private static int AppendCode(InlineCollection target, string text, int index, MarkdownPalette palette)
    {
        int close = text.IndexOf('`', index + 1);
        if (close < 0)
        {
            target.Add(new Run("`"));
            return index + 1;
        }

        target.Add(new Run(text[(index + 1)..close])
        {
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = palette.Accent,
            Background = palette.CodeBackground,
        });
        return close + 1;
    }

    private static int AppendEmphasis(
        InlineCollection target, string text, int index, string marker, bool bold, MarkdownPalette palette)
    {
        int contentStart = index + marker.Length;
        int close = text.IndexOf(marker, contentStart, StringComparison.Ordinal);
        if (close < 0 || close == contentStart)
        {
            target.Add(new Run(marker));
            return contentStart;
        }

        var span = new Span();
        if (bold)
        {
            span.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            span.FontStyle = FontStyles.Italic;
        }

        AppendInlines(span.Inlines, text[contentStart..close], palette);
        target.Add(span);
        return close + marker.Length;
    }

    private static int AppendStrike(InlineCollection target, string text, int index, MarkdownPalette palette)
    {
        int contentStart = index + 2;
        int close = text.IndexOf("~~", contentStart, StringComparison.Ordinal);
        if (close < 0 || close == contentStart)
        {
            target.Add(new Run("~~"));
            return contentStart;
        }

        var span = new Span { TextDecorations = TextDecorations.Strikethrough };
        AppendInlines(span.Inlines, text[contentStart..close], palette);
        target.Add(span);
        return close + 2;
    }

    private static int AppendLink(InlineCollection target, string text, int index, MarkdownPalette palette)
    {
        int closeBracket = text.IndexOf(']', index + 1);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
        {
            target.Add(new Run("["));
            return index + 1;
        }

        int closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
        {
            target.Add(new Run("["));
            return index + 1;
        }

        string label = text[(index + 1)..closeBracket];
        string url = text[(closeBracket + 2)..closeParen];

        var span = new Span
        {
            Foreground = palette.Accent,
            TextDecorations = TextDecorations.Underline,
        };
        AppendInlines(span.Inlines, label.Length > 0 ? label : url, palette);
        if (url.Length > 0)
        {
            span.ToolTip = url;
        }

        target.Add(span);
        return closeParen + 1;
    }
}
