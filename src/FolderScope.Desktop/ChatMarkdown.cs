using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FolderScope.Desktop;

internal static class ChatMarkdown
{
    private static readonly Regex Heading = new(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$");
    private static readonly Regex Bullet = new(@"^\s*(?:[-+*]|\d+[.)])\s+(.+)$");
    private static readonly Regex Inline = new(@"(\*\*(.+?)\*\*|__(.+?)__|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\))");
    private static readonly Regex Divider = new(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$");

    internal static FlowDocumentScrollViewer Create(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), LineHeight = 23,
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(35, 45, 53)) };
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var paragraph = new System.Text.StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0) return;
            document.Blocks.Add(Paragraph(paragraph.ToString()));
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
            {
                Flush();
                var fence = line.TrimStart()[..3];
                var code = new System.Text.StringBuilder();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith(fence)) code.AppendLine(lines[i]);
                document.Blocks.Add(new Paragraph(new Run(code.ToString().TrimEnd())) { FontFamily = new FontFamily("Consolas"),
                    FontSize = 12, Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)),
                    Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 12) });
                continue;
            }
            var heading = Heading.Match(line);
            if (heading.Success)
            {
                Flush();
                var p = Paragraph(heading.Groups[2].Value);
                p.FontSize = heading.Groups[1].Length <= 2 ? 19 : 16;
                p.FontWeight = FontWeights.SemiBold;
                p.Margin = new Thickness(0, 14, 0, 7);
                document.Blocks.Add(p);
                continue;
            }
            if (i + 1 < lines.Length && line.Contains('|') && Divider.IsMatch(lines[i + 1]))
            {
                Flush();
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 12) };
                var group = new TableRowGroup();
                table.RowGroups.Add(group);
                void Row(string row, bool header)
                {
                    var result = new TableRow();
                    foreach (var cell in row.Trim().Trim('|').Split('|'))
                    {
                        result.Cells.Add(new TableCell(Paragraph(cell.Trim())) { Padding = new Thickness(7),
                            BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1),
                            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                            Background = header ? new SolidColorBrush(Color.FromRgb(239, 244, 246)) : Brushes.Transparent });
                    }
                    group.Rows.Add(result);
                }
                Row(line, true);
                i++;
                while (i + 1 < lines.Length && lines[i + 1].Contains('|') && !string.IsNullOrWhiteSpace(lines[i + 1]))
                    Row(lines[++i], false);
                document.Blocks.Add(table);
                continue;
            }
            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                Flush();
                var p = Paragraph(line.TrimStart().StartsWith('-') || line.TrimStart().StartsWith('*') || line.TrimStart().StartsWith('+')
                    ? "•  " + bullet.Groups[1].Value : line.Trim());
                p.Margin = new Thickness(10, 2, 0, 4);
                document.Blocks.Add(p);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            if (Regex.IsMatch(line.Trim(), @"^(---+|\*\*\*+|___+)$")) { Flush(); continue; }
            if (line.TrimStart().StartsWith('>'))
            {
                Flush();
                var p = Paragraph(line.TrimStart().TrimStart('>').TrimStart());
                p.BorderThickness = new Thickness(3, 0, 0, 0);
                p.BorderBrush = Brushes.LightGray;
                p.Padding = new Thickness(10, 0, 0, 0);
                document.Blocks.Add(p);
                continue;
            }
            if (paragraph.Length > 0) paragraph.AppendLine();
            paragraph.Append(line);
        }
        Flush();
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, FocusVisualStyle = null };
        return viewer;
    }

    private static Paragraph Paragraph(string text)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 9) };
        var position = 0;
        foreach (Match match in Inline.Matches(text))
        {
            p.Inlines.Add(new Run(text[position..match.Index]));
            if (match.Groups[2].Success || match.Groups[3].Success)
                p.Inlines.Add(new Bold(new Run(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value)));
            else if (match.Groups[4].Success)
                p.Inlines.Add(new Run(match.Groups[4].Value) { FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromRgb(238, 241, 244)) });
            else
                p.Inlines.Add(new Run(match.Groups[5].Value + " (" + match.Groups[6].Value + ")"));
            position = match.Index + match.Length;
        }
        p.Inlines.Add(new Run(text[position..]));
        return p;
    }
}