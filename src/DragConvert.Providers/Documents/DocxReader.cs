using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DragConvert.Providers.Documents;

/// <summary>
/// Walks a Word document's body into simple blocks (headings, paragraphs, list items, tables) with bold, italic and
/// links kept, and renders each as plain text, Markdown or HTML.
/// </summary>
internal sealed class DocxReader(WordprocessingDocument document)
{
    private readonly Dictionary<string, string> _styleNames = document.MainDocumentPart?.StyleDefinitionsPart?.Styles?.Elements<Style>()
        .Where(s => s.StyleId?.Value is not null)
        .ToDictionary(s => s.StyleId!.Value!, s => s.StyleName?.Val?.Value?.ToLowerInvariant() ?? string.Empty, StringComparer.OrdinalIgnoreCase) ?? [];

    public IEnumerable<Block> Blocks()
    {
        Body? body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            yield break;
        }

        foreach (OpenXmlElement element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    yield return ReadParagraph(paragraph);
                    break;
                case Table table:
                    yield return new TableBlock(table.Elements<TableRow>()
                        .Select(row => (IReadOnlyList<string>)row.Elements<TableCell>().Select(cell => string.Join(" ", cell.Elements<Paragraph>().Select(p => Inline.Plain(ReadInlines(p))))).ToList())
                        .ToList());
                    break;
            }
        }
    }

    private Block ReadParagraph(Paragraph paragraph)
    {
        IReadOnlyList<Inline> inlines = ReadInlines(paragraph);
        string? styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        string styleName = styleId is not null && _styleNames.TryGetValue(styleId, out string? name) ? name : styleId?.ToLowerInvariant() ?? string.Empty;
        if (styleName == "title")
        {
            return new HeadingBlock(1, inlines);
        }

        // Built-in heading styles keep English names ("heading 2") whatever the document's language.
        if ((styleName.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(styleName.AsSpan(8), out int level))
            || (styleName.StartsWith("heading", StringComparison.Ordinal) && int.TryParse(styleName.AsSpan(7), out level)))
        {
            return new HeadingBlock(Math.Clamp(level, 1, 6), inlines);
        }

        if (paragraph.ParagraphProperties?.NumberingProperties is { } numbering)
        {
            int depth = numbering.NumberingLevelReference?.Val?.Value ?? 0;
            return new ListItemBlock(depth, IsOrdered(numbering), inlines);
        }

        return new ParagraphBlock(inlines);
    }

    private bool IsOrdered(NumberingProperties numbering)
    {
        int? numId = numbering.NumberingId?.Val?.Value;
        int level = numbering.NumberingLevelReference?.Val?.Value ?? 0;
        Numbering? definitions = document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
        if (numId is null || definitions is null)
        {
            return false;
        }

        int? abstractId = definitions.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId)?.AbstractNumId?.Val?.Value;
        Level? lvl = definitions.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId)?
            .Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        NumberFormatValues? format = lvl?.NumberingFormat?.Val?.Value;
        return format is not null && format != NumberFormatValues.Bullet;
    }

    private IReadOnlyList<Inline> ReadInlines(OpenXmlElement container)
    {
        var inlines = new List<Inline>();
        foreach (OpenXmlElement child in container.ChildElements)
        {
            if (child is Run run)
            {
                AddRun(run, null);
            }
            else if (child is Hyperlink link)
            {
                string? url = link.Id?.Value is { } id
                    ? document.MainDocumentPart?.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri.ToString()
                    : null;
                foreach (Run linkRun in link.Elements<Run>())
                {
                    AddRun(linkRun, url);
                }
            }
        }

        return inlines;

        void AddRun(Run run, string? url)
        {
            var text = new StringBuilder();
            foreach (OpenXmlElement part in run.ChildElements)
            {
                switch (part)
                {
                    case Text t:
                        text.Append(t.Text);
                        break;
                    case TabChar:
                        text.Append('\t');
                        break;
                    case Break:
                        text.Append('\n');
                        break;
                }
            }

            if (text.Length > 0)
            {
                RunProperties? props = run.RunProperties;
                bool bold = props?.Bold is { } b && (b.Val is null || b.Val.Value);
                bool italic = props?.Italic is { } i && (i.Val is null || i.Val.Value);
                inlines.Add(new Inline(text.ToString(), bold, italic, url));
            }
        }
    }

    internal sealed record Inline(string Text, bool Bold, bool Italic, string? Url)
    {
        public static string Plain(IEnumerable<Inline> inlines) => string.Concat(inlines.Select(i => i.Text));

        public static string Markdown(IEnumerable<Inline> inlines) => string.Concat(inlines.Select(i =>
        {
            string t = i.Text;
            if (t.Trim().Length == 0)
            {
                return t;
            }

            if (i.Bold && i.Italic)
            {
                t = $"***{t}***";
            }
            else if (i.Bold)
            {
                t = $"**{t}**";
            }
            else if (i.Italic)
            {
                t = $"*{t}*";
            }

            return i.Url is null ? t : $"[{t}]({i.Url})";
        }));

        public static string Html(IEnumerable<Inline> inlines) => string.Concat(inlines.Select(i =>
        {
            string t = WebUtility.HtmlEncode(i.Text).Replace("\n", "<br>", StringComparison.Ordinal);
            if (i.Bold)
            {
                t = $"<strong>{t}</strong>";
            }

            if (i.Italic)
            {
                t = $"<em>{t}</em>";
            }

            return i.Url is null ? t : $"<a href=\"{WebUtility.HtmlEncode(i.Url)}\">{t}</a>";
        }));
    }

    internal abstract record Block
    {
        public abstract string ToText();

        public abstract string ToMarkdown();

        public abstract string ToHtml();
    }

    private sealed record HeadingBlock(int Level, IReadOnlyList<Inline> Inlines) : Block
    {
        public override string ToText() => Inline.Plain(Inlines) + "\n\n";

        public override string ToMarkdown() => $"{new string('#', Level)} {Inline.Plain(Inlines)}\n\n";

        public override string ToHtml() => $"<h{Level}>{Inline.Html(Inlines)}</h{Level}>\n";
    }

    private sealed record ParagraphBlock(IReadOnlyList<Inline> Inlines) : Block
    {
        public override string ToText() => Inlines.Count == 0 ? "\n" : Inline.Plain(Inlines) + "\n\n";

        public override string ToMarkdown() => Inlines.Count == 0 ? string.Empty : Inline.Markdown(Inlines) + "\n\n";

        public override string ToHtml() => Inlines.Count == 0 ? string.Empty : $"<p>{Inline.Html(Inlines)}</p>\n";
    }

    private sealed record ListItemBlock(int Depth, bool Ordered, IReadOnlyList<Inline> Inlines) : Block
    {
        private string Indent => new(' ', Depth * 2);

        public override string ToText() => $"{Indent}{(Ordered ? "1." : "-")} {Inline.Plain(Inlines)}\n";

        public override string ToMarkdown() => $"{new string(' ', Depth * 4)}{(Ordered ? "1." : "-")} {Inline.Markdown(Inlines)}\n";

        // Each item is its own one-item list: simple and valid, if not as tidy as grouped lists.
        public override string ToHtml() => Ordered ? $"<ol><li>{Inline.Html(Inlines)}</li></ol>\n" : $"<ul><li>{Inline.Html(Inlines)}</li></ul>\n";
    }

    private sealed record TableBlock(IReadOnlyList<IReadOnlyList<string>> Rows) : Block
    {
        public override string ToText() => string.Join('\n', Rows.Select(r => string.Join('\t', r))) + "\n\n";

        public override string ToMarkdown()
        {
            if (Rows.Count == 0)
            {
                return string.Empty;
            }

            int columns = Rows.Max(r => r.Count);
            string Line(IReadOnlyList<string> row) => "| " + string.Join(" | ", Enumerable.Range(0, columns).Select(c => c < row.Count ? row[c].Replace("|", "\\|", StringComparison.Ordinal) : string.Empty)) + " |";
            var sb = new StringBuilder();
            sb.Append(Line(Rows[0])).Append('\n');
            sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", columns))).Append(" |\n");
            foreach (IReadOnlyList<string> row in Rows.Skip(1))
            {
                sb.Append(Line(row)).Append('\n');
            }

            return sb.Append('\n').ToString();
        }

        public override string ToHtml() =>
            "<table>\n" + string.Concat(Rows.Select(r => "<tr>" + string.Concat(r.Select(c => $"<td>{WebUtility.HtmlEncode(c)}</td>")) + "</tr>\n")) + "</table>\n";
    }
}
