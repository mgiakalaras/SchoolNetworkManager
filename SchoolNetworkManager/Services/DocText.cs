using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Απλό κείμενο κανονισμών ↔ HTML ↔ Word.
/// Σύνταξη: «# Επικεφαλίδα», «## Υποενότητα», «- κουκκίδα», «1. αρίθμηση», κενή γραμμή = νέα παράγραφος.
/// </summary>
public static partial class DocText
{
    [GeneratedRegex(@"^(\d+)[.)]\s+(.*)$")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"^(.*?)[:\s]*_{4,}\s*$")]
    private static partial Regex FieldLine();

    private enum Kind { H1, H2, Bullet, Number, Text, Blank, Note, Field }

    private static (Kind Kind, string Text, string? Number) Classify(string raw)
    {
        var line = raw.TrimEnd();
        var trimmed = line.TrimStart();

        if (trimmed.Length == 0) return (Kind.Blank, string.Empty, null);
        if (trimmed.StartsWith("## ")) return (Kind.H2, trimmed[3..].Trim(), null);
        if (trimmed.StartsWith("# ")) return (Kind.H1, trimmed[2..].Trim(), null);
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("• ") || trimmed.StartsWith("* ")) return (Kind.Bullet, trimmed[2..].Trim(), null);
        if (trimmed.StartsWith("> ")) return (Kind.Note, trimmed[2..].Trim(), null);

        var field = FieldLine().Match(trimmed);
        if (field.Success) return (Kind.Field, field.Groups[1].Value.Trim(), null);

        var numbered = NumberedLine().Match(trimmed);
        if (numbered.Success) return (Kind.Number, numbered.Groups[2].Value.Trim(), numbered.Groups[1].Value);

        return (Kind.Text, trimmed, null);
    }

    /// <summary>Ασφαλές HTML (όλο το κείμενο κωδικοποιείται· **έντονα** επιτρέπονται).</summary>
    public static string ToHtml(string? body)
    {
        var html = new StringBuilder();
        string? openList = null;

        void CloseList()
        {
            if (openList != null)
            {
                html.Append(openList == "ul" ? "</ul>" : "</ol>");
                openList = null;
            }
        }

        foreach (var raw in (body ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            var (kind, text, number) = Classify(raw);
            var safe = Inline(text);

            switch (kind)
            {
                case Kind.H1:
                    CloseList();
                    html.Append("<h2>").Append(safe).Append("</h2>");
                    break;
                case Kind.H2:
                    CloseList();
                    html.Append("<h3>").Append(safe).Append("</h3>");
                    break;
                case Kind.Bullet:
                    if (openList != "ul") { CloseList(); html.Append("<ul>"); openList = "ul"; }
                    html.Append("<li>").Append(safe).Append("</li>");
                    break;
                case Kind.Number:
                    if (openList != "ol") { CloseList(); html.Append($"<ol start=\"{number}\">"); openList = "ol"; }
                    html.Append("<li>").Append(safe).Append("</li>");
                    break;
                case Kind.Text:
                    CloseList();
                    html.Append("<p>").Append(safe).Append("</p>");
                    break;
                case Kind.Note:
                    CloseList();
                    html.Append("<div class=\"doc-note\">").Append(safe).Append("</div>");
                    break;
                case Kind.Field:
                    CloseList();
                    html.Append("<div class=\"doc-field\"><span>").Append(safe).Append("</span><i></i></div>");
                    break;
                default:
                    CloseList();
                    break;
            }
        }

        CloseList();
        return html.ToString();
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        return Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
    }

    // ---------------- Δομή για την εκτύπωση ----------------

    public record DocBlock(string Type, string Html, List<string> Items, int Start = 1);

    public class DocSection
    {
        public string? Title { get; set; }
        public List<DocBlock> Blocks { get; } = new();
        public List<(string Title, List<DocBlock> Blocks)> Subsections { get; } = new();
    }

    /// <summary>Χωρίζει το κείμενο σε ενότητες (# ...) με παραγράφους και λίστες, για όμορφη εκτύπωση.</summary>
    public static List<DocSection> Parse(string? body)
    {
        var sections = new List<DocSection> { new() };
        List<DocBlock> target = sections[0].Blocks;
        DocBlock? list = null;

        foreach (var raw in (body ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            var (kind, text, number) = Classify(raw);
            var safe = Inline(text);

            switch (kind)
            {
                case Kind.H1:
                    var section = new DocSection { Title = safe };
                    sections.Add(section);
                    target = section.Blocks;
                    list = null;
                    break;
                case Kind.H2:
                    var sub = (safe, new List<DocBlock>());
                    sections[^1].Subsections.Add(sub);
                    target = sub.Item2;
                    list = null;
                    break;
                case Kind.Bullet:
                case Kind.Number:
                    var type = kind == Kind.Bullet ? "ul" : "ol";
                    if (list == null || list.Type != type)
                    {
                        list = new DocBlock(type, string.Empty, new List<string>(), number != null && int.TryParse(number, out var n) ? n : 1);
                        target.Add(list);
                    }
                    list.Items.Add(safe);
                    break;
                case Kind.Text:
                    target.Add(new DocBlock("p", safe, new List<string>()));
                    list = null;
                    break;
                case Kind.Note:
                    target.Add(new DocBlock("note", safe, new List<string>()));
                    list = null;
                    break;
                case Kind.Field:
                    if (list == null || list.Type != "fields")
                    {
                        list = new DocBlock("fields", string.Empty, new List<string>());
                        target.Add(list);
                    }
                    list.Items.Add(safe);
                    break;
                default:
                    list = null;
                    break;
            }
        }

        return sections.Where(s => s.Title != null || s.Blocks.Count > 0 || s.Subsections.Count > 0).ToList();
    }

    [GeneratedRegex(@"^(?:(Άρθρο|ΑΡΘΡΟ|Ενότητα)\s+(\d+)|(\d+)[.)])\s*[-–—.:]?\s*(.+)$")]
    private static partial Regex HeadingNumber();

    /// <summary>«Άρθρο 3 - Πρόσβαση» → ("Άρθρο", "3", "Πρόσβαση") · «2. Κατά τη διάρκεια» → (null, "2", "Κατά τη διάρκεια»).</summary>
    public static (string? Label, string? Number, string Text) SplitHeading(string? title)
    {
        var text = title ?? string.Empty;
        var match = HeadingNumber().Match(text);

        if (!match.Success)
        {
            return (null, null, text);
        }

        return match.Groups[1].Success
            ? (match.Groups[1].Value, match.Groups[2].Value, match.Groups[4].Value)
            : (null, match.Groups[3].Value, match.Groups[4].Value);
    }

    /// <summary>Το «μήνυμα» στο τέλος: πλαίσια (>) στο τέλος της τελευταίας ενότητας ή παράγραφοι μετά τη λίστα της.</summary>
    public static List<DocBlock> Closing(DocSection? last)
    {
        if (last == null || last.Subsections.Count > 0)
        {
            return new List<DocBlock>();
        }

        var notes = last.Blocks.AsEnumerable().Reverse().TakeWhile(b => b.Type == "note").Reverse().ToList();

        if (notes.Count > 0)
        {
            return notes;
        }

        return last.Blocks.SkipWhile(b => b.Type == "p").SkipWhile(b => b.Type != "p").Where(b => b.Type == "p").ToList();
    }

    /// <summary>Εικονίδιο για ενότητα της αφίσας, από τις λέξεις του τίτλου.</summary>
    public static string SectionIcon(string? title, int index)
    {
        var t = (title ?? string.Empty).ToLowerInvariant();
        (string Key, string Icon)[] map =
        {
            ("μπαίν", "🚪"), ("είσοδ", "🚪"), ("πρόσβασ", "🔑"), ("διάρκεια", "💻"), ("χρήσ", "🖱️"),
            ("φεύγ", "👋"), ("έξοδ", "👋"), ("βλάβ", "🛠️"), ("πρόβλημ", "🛠️"), ("λογισμικ", "💿"), ("ασφάλ", "🛡️"),
            ("ευθύν", "⚖️"), ("γενικ", "📌"), ("έγκρισ", "✍️"), ("διαδίκτυ", "🌐"), ("κωδικ", "🔐"), ("καθαρ", "🧹"),
            ("πριν", "🚪"), ("τέλος", "👋"), ("ψηφιακ", "🌐"), ("ποτέ", "🚫"), ("παράβασ", "⚖️"), ("δήλωσ", "✍️"),
            ("δεδομέν", "📷"), ("φωτογρ", "📷"), ("υγιειν", "🩺"), ("εκτυπώσ", "🖨️"), ("αναλώσ", "🖨️"), ("σκοπ", "🎯"),
            ("αρχές", "🧭"), ("ρόλος", "👩‍🏫"), ("ισχύ", "📅"), ("φθορ", "⚖️"), ("εξοπλισμ", "🖥️")
        };

        foreach (var (key, icon) in map)
        {
            if (t.Contains(key)) return icon;
        }

        var fallback = new[] { "📌", "💡", "✅", "⭐", "📋", "🔹" };
        return fallback[index % fallback.Length];
    }

    // ---------------- Word ----------------

    public static byte[] ToDocx(string title, string? subtitle, string? body)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            AddStyles(main);

            var docBody = new Body();

            docBody.Append(Para(title, "Title"));

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                docBody.Append(Para(subtitle!, "Subtitle"));
            }

            foreach (var raw in (body ?? string.Empty).Replace("\r", "").Split('\n'))
            {
                var (kind, text, number) = Classify(raw);

                switch (kind)
                {
                    case Kind.H1: docBody.Append(Para(text, "Heading1")); break;
                    case Kind.H2: docBody.Append(Para(text, "Heading2")); break;
                    case Kind.Bullet: docBody.Append(Para("• " + text, "ListItem")); break;
                    case Kind.Number: docBody.Append(Para($"{number}. {text}", "ListItem")); break;
                    case Kind.Text: docBody.Append(Para(text, null)); break;
                    case Kind.Note: docBody.Append(Para(text, "Note")); break;
                    case Kind.Field: docBody.Append(Para($"{text}: ____________________________________", "Field")); break;
                }
            }

            docBody.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 708, Footer = 708, Gutter = 0 }));

            main.Document = new Document(docBody);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph Para(string text, string? style)
    {
        var paragraph = new Paragraph();

        if (style != null)
        {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        }

        // **έντονα** → bold runs
        var parts = Regex.Split(text, @"(\*\*.+?\*\*)");

        foreach (var part in parts.Where(p => p.Length > 0))
        {
            var bold = part.StartsWith("**") && part.EndsWith("**") && part.Length > 4;
            var run = new Run();

            if (bold)
            {
                run.Append(new RunProperties(new Bold()));
            }

            run.Append(new Text(bold ? part[2..^2] : part) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
        }

        return paragraph;
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();

        Style Make(string id, string name, int sizeHalfPoints, bool bold, string? color = null, int before = 0, int after = 120, int indent = 0)
        {
            var runProps = new StyleRunProperties(
                new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                new FontSize { Val = sizeHalfPoints.ToString() });

            if (bold) runProps.Append(new Bold());
            if (color != null) runProps.Append(new Color { Val = color });

            var paraProps = new StyleParagraphProperties(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() });

            if (indent > 0)
            {
                paraProps.Append(new Indentation { Left = indent.ToString(), Hanging = "284" });
            }

            var style = new Style(new StyleName { Val = name }, new PrimaryStyle(), paraProps, runProps)
            {
                Type = StyleValues.Paragraph,
                StyleId = id
            };

            if (id == "Normal")
            {
                style.Default = true;
            }

            return style;
        }

        stylesPart.Styles = new Styles(
            Make("Normal", "Normal", 22, false),
            Make("Title", "Title", 36, true, "1F3864", 0, 60),
            Make("Subtitle", "Subtitle", 22, false, "6C757D", 0, 240),
            Make("Heading1", "heading 1", 28, true, "1F3864", 240, 100),
            Make("Heading2", "heading 2", 24, true, "2E5597", 180, 80),
            Make("ListItem", "List Paragraph", 22, false, null, 0, 60, 567),
            Make("Note", "Intense Quote", 22, true, "1F3864", 120, 160, 284),
            Make("Field", "Form Field", 22, false, null, 200, 200));
        stylesPart.Styles.Save();
    }

    /// <summary>Αποτέλεσμα ανάγνωσης Word: τίτλος (αν βρέθηκε) και σώμα στην απλή σύνταξη.</summary>
    public record DocxImport(string? Title, string Body);

    /// <summary>Διαβάζει ένα .docx πίσω σε απλό κείμενο με την ίδια σύνταξη.</summary>
    public static string FromDocx(Stream stream) => ImportDocx(stream).Body;

    /// <summary>
    /// Διαβάζει Word: επικεφαλίδες, λίστες (κουκκίδες / αρίθμηση), παραγράφους,
    /// πλαίσια (πίνακας ενός κελιού → «> σημείωση») και φόρμες (Ετικέτα | ..... → «Ετικέτα: ____»).
    /// Αγνοεί την κεφαλίδα-πίνακα στην αρχή και τον πίνακα υπογραφών (τις βάζει η εκτύπωση).
    /// </summary>
    public static DocxImport ImportDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var main = document.MainDocumentPart;
        var body = main?.Document?.Body;

        if (body == null)
        {
            return new DocxImport(null, string.Empty);
        }

        var lines = new List<string>();
        var titleParts = new List<string>();
        var seenContent = false;     // κάτι μετά τον τίτλο (επικεφαλίδα, λίστα, κείμενο)
        var counter = 0;

        void Blank()
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
        }

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph paragraph)
            {
                var text = ParagraphText(paragraph);
                var style = StyleOf(paragraph, main!);

                if (text.Length == 0)
                {
                    continue;
                }

                if (style.Contains("subtitle"))
                {
                    continue;
                }

                if (style.Contains("title") || (!seenContent && (style == "normal" || style.Length == 0) && titleParts.Count < 3 && text.Length <= 70 && !text.EndsWith('.')))
                {
                    titleParts.Add(text);
                    continue;
                }

                if (style.StartsWith("heading") || style.StartsWith("επικεφαλίδα"))
                {
                    Blank();
                    lines.Add((style.EndsWith("1") ? "# " : "## ") + text);
                    counter = 0;
                    seenContent = true;
                    continue;
                }

                var hasNumbering = paragraph.ParagraphProperties?.NumberingProperties != null;
                var isNumbered = style.Contains("number") || (hasNumbering && IsDecimal(paragraph, main!));
                var isBullet = !isNumbered && (style.Contains("bullet") || style.Contains("list") || hasNumbering || text.StartsWith("• ") || text.StartsWith("- "));

                if (isNumbered)
                {
                    counter++;
                    lines.Add($"{counter}. {NumberedLine().Replace(text, "$2")}");
                }
                else if (isBullet)
                {
                    lines.Add("- " + (text.StartsWith("• ") || text.StartsWith("- ") ? text[2..].Trim() : text));
                }
                else
                {
                    Blank();
                    lines.Add(text);
                    counter = 0;
                }

                seenContent = true;
            }
            else if (element is Table table)
            {
                var rows = table.Elements<TableRow>()
                    .Select(r => r.Elements<TableCell>().Select(CellText).ToList())
                    .ToList();
                var all = string.Join(" ", rows.SelectMany(r => r));

                if (all.Trim().Length == 0)
                {
                    continue;
                }

                // Κεφαλίδα εγγράφου (λογότυπο / σχολείο / έκδοση) πριν από το περιεχόμενο
                if (!seenContent && titleParts.Count == 0 && rows.Count == 1 && rows[0].Count >= 2)
                {
                    continue;
                }

                // Υπογραφές υπευθύνων — τις βάζει μόνη της η εκτύπωση
                if (Regex.IsMatch(all, "Διευθυντ|Υπεύθυνος/η|Υπογραφή υπευθύνου", RegexOptions.IgnoreCase) && Regex.IsMatch(all, @"\.{6,}|_{6,}"))
                {
                    continue;
                }

                // Φόρμα: Ετικέτα | ..........
                if (rows.All(r => r.Count == 2 && Regex.IsMatch(r[1], @"^[\s.…_]*$")))
                {
                    Blank();
                    foreach (var r in rows.Where(r => r[0].Length > 0))
                    {
                        lines.Add($"{r[0].TrimEnd(':')}: ____");
                    }
                    seenContent = true;
                    continue;
                }

                // Πλαίσιο ενός κελιού → σημείωση
                if (rows.Count == 1 && rows[0].Count == 1)
                {
                    Blank();
                    lines.Add("> " + rows[0][0]);
                    seenContent = true;
                    continue;
                }

                Blank();
                foreach (var cell in rows.SelectMany(r => r).Where(c => c.Length > 0))
                {
                    lines.Add(cell);
                }
                seenContent = true;
            }
        }

        var title = titleParts.Count == 0 ? null : Regex.Replace(string.Join(" ", titleParts), @"\s+", " ").Trim();

        return new DocxImport(title, string.Join("\n", lines).Trim());
    }

    private static string ParagraphText(Paragraph paragraph)
    {
        var builder = new StringBuilder();

        foreach (var node in paragraph.Descendants())
        {
            switch (node)
            {
                case Text t: builder.Append(t.Text); break;
                case TabChar: builder.Append(' '); break;
                case Break: builder.Append(' '); break;
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static string CellText(TableCell cell) =>
        string.Join(" ", cell.Elements<Paragraph>().Select(ParagraphText).Where(t => t.Length > 0)).Trim();

    /// <summary>Όνομα στυλ σε μικρά (π.χ. «heading 1», «list bullet»), από το id ή από τον ορισμό του.</summary>
    private static string StyleOf(Paragraph paragraph, MainDocumentPart main)
    {
        var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        if (string.IsNullOrEmpty(id))
        {
            return "normal";
        }

        var name = main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .FirstOrDefault(st => st.StyleId?.Value == id)?.StyleName?.Val?.Value;

        return (name ?? id).ToLowerInvariant().Replace(" ", "");
    }

    private static bool IsDecimal(Paragraph paragraph, MainDocumentPart main)
    {
        try
        {
            var numPr = paragraph.ParagraphProperties?.NumberingProperties;
            var numId = numPr?.NumberingId?.Val?.Value;
            var level = numPr?.NumberingLevelReference?.Val?.Value ?? 0;
            var numbering = main.NumberingDefinitionsPart?.Numbering;

            if (numId == null || numbering == null)
            {
                return false;
            }

            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractId = instance?.AbstractNumId?.Val?.Value;
            var abstractNum = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
            var format = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level)?.NumberingFormat?.Val?.Value;

            return format == NumberFormatValues.Decimal;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------------- Αρχικά κείμενα ----------------

    public static (string Title, string Body) Default(string key) => key switch
    {
        Models.SchoolDocument.StudentRules => ("Υποχρεώσεις Μαθητών/τριών — Οδηγίες χρήσης του Εργαστηρίου Πληροφορικής", StudentRulesText),
        _ => ("Κανονισμός - Καταστατικό Λειτουργίας Εργαστηρίων Πληροφορικής", LabRulesText)
    };

    /// <summary>Το κείμενο είναι ακόμη το πρώτο πρότυπο της 2.9 (δεν το πείραξε κανείς) → μπορεί να αντικατασταθεί.</summary>
    public static bool IsLegacyDefault(string key, string? body)
    {
        static string Norm(string? x) => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim();
        var legacy = key == Models.SchoolDocument.StudentRules ? LegacyStudentRulesText : LegacyLabRulesText;
        return Norm(body) == Norm(legacy);
    }

    // Κείμενα από τα έγγραφα του σχολείου (Οκτώβριος 2026)
    private const string LabRulesText = """
> Σκοπός του παρόντος κανονισμού είναι η ασφαλής, παιδαγωγικά ορθή και οργανωμένη λειτουργία του Εργαστηρίου Πληροφορικής, η προστασία του εξοπλισμού και η καλλιέργεια υπεύθυνης ψηφιακής συμπεριφοράς.

# Άρθρο 1 - Σκοπός και πεδίο εφαρμογής

Ο κανονισμός ισχύει για όλους/ες τους/τις μαθητές/τριες, τους/τις εκπαιδευτικούς και κάθε άλλο πρόσωπο που χρησιμοποιεί το Εργαστήριο Πληροφορικής του σχολείου.

Το εργαστήριο χρησιμοποιείται κατά προτεραιότητα για τη διδασκαλία μαθημάτων Πληροφορικής, για εκπαιδευτικές δράσεις, ομίλους, εργασίες, επιμορφώσεις και δραστηριότητες που εγκρίνονται από τη σχολική μονάδα.

# Άρθρο 2 - Βασικές αρχές λειτουργίας
- Το εργαστήριο είναι χώρος μάθησης, δημιουργίας και συνεργασίας. Η χρήση του γίνεται με σεβασμό στον εξοπλισμό, στους συμμαθητές/τριες και στους/στις εκπαιδευτικούς.
- Η πρόσβαση και η χρήση του εργαστηρίου γίνεται μόνο παρουσία ή με σαφή άδεια εκπαιδευτικού.
- Κάθε χρήστης φροντίζει να αφήνει τη θέση εργασίας καθαρή, τακτοποιημένη και λειτουργική για τον επόμενο χρήστη.
- Οι κανόνες του σχολείου ισχύουν πλήρως και μέσα στο εργαστήριο.

# Άρθρο 3 - Πρόσβαση στον χώρο
- Οι μαθητές/τριες εισέρχονται στο εργαστήριο με ηρεμία, χωρίς φαγητά, ποτά ή αντικείμενα που δεν χρειάζονται στο μάθημα.
- Η τοποθέτηση στις θέσεις εργασίας γίνεται σύμφωνα με τις οδηγίες του/της εκπαιδευτικού.
- Δεν επιτρέπεται η παραμονή μαθητών/τριών στο εργαστήριο κατά τα διαλείμματα ή εκτός μαθήματος χωρίς επίβλεψη.
- Η χρήση προσωπικών συσκευών γίνεται μόνο εφόσον επιτρέπεται από τον/την εκπαιδευτικό και εξυπηρετεί συγκεκριμένο εκπαιδευτικό σκοπό.

# Άρθρο 4 - Χρήση υπολογιστών και εξοπλισμού
- Οι υπολογιστές, οθόνες, πληκτρολόγια, ποντίκια, καλώδια, διαδραστικοί πίνακες, εκτυπωτές, δικτυακός εξοπλισμός και περιφερειακές συσκευές χρησιμοποιούνται με προσοχή.
- Δεν επιτρέπεται η μετακίνηση, αποσύνδεση ή αλλαγή καλωδίων και συσκευών χωρίς άδεια.
- Δεν επιτρέπεται η αλλαγή ρυθμίσεων συστήματος, δικτύου, οθόνης, ήχου, εμφάνισης ή ασφάλειας χωρίς σχετική οδηγία.
- Οποιαδήποτε βλάβη, δυσλειτουργία ή ασυνήθιστη συμπεριφορά αναφέρεται αμέσως στον/στην εκπαιδευτικό. Ο μαθητής/τρια δεν επιχειρεί μόνος/η του/της επισκευή.

# Άρθρο 5 - Λογισμικό, λογαριασμοί και αρχεία
- Η εγκατάσταση, απεγκατάσταση ή εκτέλεση άγνωστου λογισμικού επιτρέπεται μόνο από τον/την υπεύθυνο/η εκπαιδευτικό ή μετά από ρητή άδεια.
- Απαγορεύεται η χρήση πειρατικού λογισμικού, εργαλείων παράκαμψης περιορισμών, κακόβουλων προγραμμάτων ή εφαρμογών που δεν σχετίζονται με το μάθημα.
- Οι προσωπικοί κωδικοί δεν κοινοποιούνται. Κάθε χρήστης είναι υπεύθυνος για τις ενέργειες που γίνονται από τον λογαριασμό του.
- Τα αρχεία αποθηκεύονται στους χώρους που υποδεικνύονται από τον/την εκπαιδευτικό. Το σχολείο δεν εγγυάται μόνιμη φύλαξη προσωπικών αρχείων σε κοινόχρηστους υπολογιστές.
- Δεν επιτρέπεται η πρόσβαση, αντιγραφή, διαγραφή ή αλλοίωση αρχείων άλλων χρηστών.

# Άρθρο 6 - Διαδίκτυο και ψηφιακή συμπεριφορά
- Η πρόσβαση στο διαδίκτυο γίνεται για εκπαιδευτικούς σκοπούς και σύμφωνα με τις οδηγίες του/της εκπαιδευτικού.
- Απαγορεύεται η επίσκεψη σε ακατάλληλες, παράνομες ή μη σχετικές με το μάθημα ιστοσελίδες.
- Απαγορεύεται η παρενόχληση, προσβολή, εκφοβισμός, δημοσίευση προσωπικών δεδομένων ή οποιαδήποτε συμπεριφορά που παραβιάζει την αξιοπρέπεια άλλων προσώπων.
- Η λήψη αρχείων από το διαδίκτυο γίνεται μόνο όταν είναι απαραίτητη για το μάθημα και με άδεια του/της εκπαιδευτικού.

# Άρθρο 7 - Προσωπικά δεδομένα και φωτογράφιση
- Δεν επιτρέπεται η φωτογράφιση, βιντεοσκόπηση ή ηχογράφηση μαθητών/τριών, εκπαιδευτικών, οθονών ή εργασιών χωρίς προηγούμενη άδεια.
- Δεν δημοσιεύεται υλικό από το εργαστήριο σε κοινωνικά δίκτυα ή ιστοσελίδες χωρίς έγκριση της σχολικής μονάδας και τις προβλεπόμενες συναινέσεις.
- Οι εργασίες μαθητών/τριών αντιμετωπίζονται ως προσωπικό εκπαιδευτικό υλικό και δεν κοινοποιούνται χωρίς άδεια.

# Άρθρο 8 - Ασφάλεια, υγιεινή και εργονομία
- Δεν καταναλώνονται τρόφιμα ή ροφήματα μέσα στο εργαστήριο.
- Οι διάδρομοι και οι έξοδοι παραμένουν ελεύθεροι. Τσάντες και προσωπικά αντικείμενα τοποθετούνται σε ασφαλές σημείο, ώστε να μην εμποδίζουν την κίνηση.
- Οι χρήστες κάθονται σωστά, αποφεύγουν την έντονη πίεση σε πληκτρολόγια/ποντίκια και κάνουν ορθή χρήση της οθόνης.
- Σε περίπτωση κινδύνου, διακοπής ρεύματος, καπνού, μυρωδιάς καμένου ή άλλου έκτακτου περιστατικού, ακολουθούνται άμεσα οι οδηγίες του/της εκπαιδευτικού.

# Άρθρο 9 - Καθαριότητα και συντήρηση
- Μετά το τέλος κάθε μαθήματος οι θέσεις εργασίας παραδίδονται καθαρές και τακτοποιημένες.
- Πληκτρολόγια, ποντίκια, οθόνες και καρέκλες παραμένουν στη θέση τους.
- Η συστηματική συντήρηση, ο έλεγχος εξοπλισμού και η καταγραφή προβλημάτων οργανώνονται από τον/την υπεύθυνο/η του εργαστηρίου σε συνεργασία με τη διεύθυνση.

# Άρθρο 10 - Φθορές, απώλειες και ευθύνη χρήσης
- Κάθε σκόπιμη φθορά, αφαίρεση εξαρτήματος ή κακή χρήση εξοπλισμού αναφέρεται στη διεύθυνση και αντιμετωπίζεται σύμφωνα με τον σχολικό κανονισμό.
- Σε περίπτωση ατυχήματος ή ζημιάς, ο/η μαθητής/τρια ενημερώνει αμέσως τον/την εκπαιδευτικό, χωρίς να αποκρύπτει το περιστατικό.
- Η ευθύνη δεν είναι τιμωρητική αλλά παιδαγωγική: στόχος είναι να προστατεύεται ο κοινός εξοπλισμός και να καλλιεργείται σεβασμός στη σχολική περιουσία.

# Άρθρο 11 - Εκτυπώσεις και αναλώσιμα
- Οι εκτυπώσεις γίνονται μόνο για εκπαιδευτικές ανάγκες και μετά από άδεια του/της εκπαιδευτικού.
- Η άσκοπη χρήση χαρτιού, μελανιών, τόνερ ή άλλων αναλωσίμων αποφεύγεται.
- Οι μαθητές/τριες ενημερώνονται για την αξία της οικονομίας πόρων και της κυκλικής διαχείρισης ηλεκτρονικού εξοπλισμού.

# Άρθρο 12 - Ρόλος εκπαιδευτικού και υπεύθυνου/ης εργαστηρίου
- Ο/Η εκπαιδευτικός οργανώνει τη χρήση του χώρου, καθοδηγεί τους/τις μαθητές/τριες και φροντίζει για την τήρηση των κανόνων κατά τη διάρκεια του μαθήματος.
- Ο/Η υπεύθυνος/η εργαστηρίου καταγράφει ανάγκες, βλάβες, συντηρήσεις και προτάσεις αναβάθμισης του εξοπλισμού.
- Η διεύθυνση του σχολείου μεριμνά, στο μέτρο των δυνατοτήτων της, για την υποστήριξη, ασφάλεια και εύρυθμη λειτουργία του εργαστηρίου.

# Άρθρο 13 - Ισχύς και αναθεώρηση

Ο παρών κανονισμός ισχύει από την έναρξη του σχολικού έτους 2026-2027. Μπορεί να αναθεωρείται όταν προκύπτουν νέες ανάγκες, αλλαγές στον εξοπλισμό, νέες οδηγίες ή εμπειρία από την καθημερινή λειτουργία του εργαστηρίου.

> Σύντομο μήνυμα προς όλους: το εργαστήριο είναι κοινός χώρος. Το παραδίδουμε πάντα όπως θα θέλαμε να το βρούμε.
""";

    private const string StudentRulesText = """
> Μπαίνουμε στο εργαστήριο για να μάθουμε, να συνεργαστούμε και να δημιουργήσουμε. Ο εξοπλισμός είναι κοινός και τον προστατεύουμε όλοι/ες.

# 1. Πριν ξεκινήσει το μάθημα
1. Μπαίνω στο εργαστήριο ήρεμα και μόνο όταν το επιτρέψει ο/η εκπαιδευτικός.
2. Κάθομαι στη θέση εργασίας που μου έχει οριστεί ή μου υποδεικνύεται εκείνη τη στιγμή.
3. Δεν φέρνω φαγητά, καφέδες, αναψυκτικά ή νερά πάνω στους πάγκους με τους υπολογιστές.
4. Ελέγχω γρήγορα αν ο υπολογιστής, η οθόνη, το πληκτρολόγιο και το ποντίκι είναι σε καλή κατάσταση.
5. Αν δω πρόβλημα, σπασμένο εξάρτημα, αποσυνδεδεμένο καλώδιο ή περίεργη λειτουργία, ενημερώνω αμέσως τον/την εκπαιδευτικό.

# 2. Κατά τη διάρκεια του μαθήματος
1. Χρησιμοποιώ τον υπολογιστή μόνο για τις δραστηριότητες του μαθήματος.
2. Δεν αλλάζω ρυθμίσεις, φόντο, κωδικούς, όνομα υπολογιστή, γλώσσα, ανάλυση οθόνης, ήχο ή δικτυακές ρυθμίσεις.
3. Δεν εγκαθιστώ, κατεβάζω ή εκτελώ προγράμματα, παιχνίδια, επεκτάσεις ή αρχεία χωρίς άδεια.
4. Δεν επισκέπτομαι ιστοσελίδες που δεν σχετίζονται με το μάθημα ή είναι ακατάλληλες για το σχολικό περιβάλλον.
5. Δεν πειράζω καλώδια, πρίζες, πολύπριζα, οθόνες, routers, switches, διαδραστικούς πίνακες ή άλλες συσκευές.
6. Δεν χρησιμοποιώ τον υπολογιστή άλλου μαθητή/τριας και δεν πειράζω τα αρχεία του/της.
7. Κρατάω τους προσωπικούς μου κωδικούς μυστικούς και δεν χρησιμοποιώ λογαριασμό άλλου προσώπου.
8. Μιλάω χαμηλόφωνα, συνεργάζομαι και δεν ενοχλώ τις άλλες ομάδες εργασίας.

# 3. Ψηφιακή συμπεριφορά
1. Σέβομαι τους/τις συμμαθητές/τριές μου και τον/την εκπαιδευτικό και στον ψηφιακό χώρο.
2. Δεν γράφω προσβλητικά σχόλια, δεν κοροϊδεύω, δεν απειλώ και δεν ενοχλώ κανέναν/καμία μέσα από εφαρμογές, πλατφόρμες ή κοινόχρηστα αρχεία.
3. Δεν δημοσιεύω προσωπικά δεδομένα, φωτογραφίες, βίντεο, εργασίες ή στοιχεία άλλων χωρίς άδεια.
4. Δεν φωτογραφίζω ή βιντεοσκοπώ μέσα στο εργαστήριο χωρίς έγκριση του/της εκπαιδευτικού.
5. Χρησιμοποιώ το διαδίκτυο υπεύθυνα και θυμάμαι ότι το εργαστήριο είναι σχολικός χώρος.

# 4. Αν παρουσιαστεί πρόβλημα
1. Σταματώ τη χρήση του υπολογιστή αν παρατηρήσω βλάβη, μήνυμα σφάλματος, μυρωδιά καμένου, υπερθέρμανση ή ασυνήθιστο θόρυβο.
2. Δεν ανοίγω κουτιά υπολογιστών και δεν προσπαθώ να διορθώσω μόνος/η μου τεχνικό πρόβλημα.
3. Σηκώνω χέρι και ενημερώνω τον/την εκπαιδευτικό με σαφήνεια για το τι συνέβη.
4. Αν έκανα κάτι κατά λάθος, το λέω αμέσως. Τα λάθη διορθώνονται πιο εύκολα όταν δεν κρύβονται.

# 5. Στο τέλος του μαθήματος
1. Αποθηκεύω την εργασία μου στον χώρο που έχει οριστεί από τον/την εκπαιδευτικό.
2. Κάνω αποσύνδεση από λογαριασμούς, εφαρμογές και πλατφόρμες που χρησιμοποίησα.
3. Κλείνω προγράμματα και ακολουθώ τις οδηγίες για τερματισμό ή παραμονή του υπολογιστή σε λειτουργία.
4. Τακτοποιώ πληκτρολόγιο, ποντίκι, καρέκλα και πάγκο εργασίας.
5. Παίρνω τα προσωπικά μου αντικείμενα και αφήνω τη θέση μου καθαρή για το επόμενο τμήμα.

# 6. Τι δεν κάνουμε ποτέ στο εργαστήριο
- Δεν τρώμε και δεν πίνουμε πάνω ή δίπλα στους υπολογιστές.
- Δεν κάνουμε πλάκες με καλώδια, πρίζες, διακόπτες, περιφερειακά ή καρέκλες.
- Δεν ανοίγουμε άγνωστα αρχεία ή συνδέσμους.
- Δεν διαγράφουμε ή αλλάζουμε εργασίες άλλων.
- Δεν αφήνουμε σκουπίδια, χαρτιά ή προσωπικά αντικείμενα στις θέσεις εργασίας.
- Δεν αντιμετωπίζουμε τον εξοπλισμό σαν κάτι δεδομένο: είναι εργαλείο για όλους και πρέπει να αντέξει.

# 7. Σε περίπτωση παράβασης

Η μη τήρηση των οδηγιών αντιμετωπίζεται παιδαγωγικά και σύμφωνα με τον σχολικό κανονισμό. Ανάλογα με την περίπτωση, μπορεί να ζητηθεί αλλαγή θέσης, προσωρινή διακοπή χρήσης υπολογιστή, ενημέρωση γονέων/κηδεμόνων ή αναφορά στη διεύθυνση.

> Χρυσός κανόνας: αφήνω το εργαστήριο λίγο καλύτερο από ό,τι το βρήκα.

# Δήλωση ενημέρωσης μαθητή/τριας

Δηλώνω ότι ενημερώθηκα για τις υποχρεώσεις μου στο Εργαστήριο Πληροφορικής και κατανοώ ότι η υπεύθυνη χρήση του εξοπλισμού είναι απαραίτητη για τη σωστή λειτουργία του μαθήματος.

Ονοματεπώνυμο μαθητή/τριας: ____
Τμήμα: ____
Ημερομηνία: ____
Υπογραφή: ____
""";

    private const string LegacyLabRulesText = """
# Γενικά
Τα εργαστήρια πληροφορικής είναι κοινός χώρος του σχολείου. Ο κανονισμός ισχύει για όλους όσοι τα χρησιμοποιούν: εκπαιδευτικούς, μαθητές και επισκέπτες.

- Υπεύθυνος/η εργαστηρίου: **[όνομα]**
- Ο κανονισμός αναρτάται σε εμφανές σημείο κάθε εργαστηρίου.

# Πρόσβαση και χρήση
1. Οι μαθητές μπαίνουν στο εργαστήριο μόνο με την παρουσία εκπαιδευτικού.
2. Η χρήση εκτός ωρολογίου προγράμματος γίνεται μετά από συνεννόηση με τον υπεύθυνο εργαστηρίου.
3. Κάθε τμήμα κάθεται στις ίδιες θέσεις, σύμφωνα με το πλάνο θέσεων του εργαστηρίου.
4. Ο εκπαιδευτικός της τελευταίας ώρας ελέγχει ότι οι υπολογιστές, οι οθόνες και ο προβολέας είναι κλειστοί και ότι η πόρτα και τα παράθυρα είναι κλειστά.

# Λογισμικό και ασφάλεια
- Εγκατάσταση ή απεγκατάσταση λογισμικού γίνεται **μόνο** από τον υπεύθυνο εργαστηρίου ή την ομάδα πληροφορικής.
- Οι κωδικοί διαχειριστή δεν κοινοποιούνται σε μαθητές. Αν υπάρχει υποψία ότι έγιναν γνωστοί, ενημερώνεται αμέσως ο υπεύθυνος.
- Δεν απενεργοποιούνται προγράμματα προστασίας ή διαχείρισης της τάξης (π.χ. antivirus, Veyon).
- Τα αρχεία των μαθητών αποθηκεύονται στον φάκελο που ορίζει ο εκπαιδευτικός· ο υπεύθυνος μπορεί να καθαρίζει περιοδικά τους υπολογιστές.

# Βλάβες και προβλήματα
1. Κάθε βλάβη ή δυσλειτουργία δηλώνεται στη σελίδα αναφοράς βλαβών του σχολείου, με τη συσκευή και μια σύντομη περιγραφή.
2. Δεν επιχειρείται επισκευή ή άνοιγμα εξοπλισμού από μαθητές.
3. Σε περίπτωση κινδύνου (καπνός, μυρωδιά καμένου, σπινθήρες) διακόπτεται αμέσως η παροχή ρεύματος και ενημερώνεται η Διεύθυνση.

# Ευθύνη
- Ο εκπαιδευτικός που χρησιμοποιεί το εργαστήριο είναι υπεύθυνος για την τήρηση του κανονισμού κατά τη διάρκεια της ώρας του.
- Σκόπιμη φθορά εξοπλισμού αντιμετωπίζεται σύμφωνα με τον Κανονισμό Λειτουργίας του σχολείου.

## Έγκριση
Εγκρίθηκε από τον Σύλλογο Διδασκόντων στις **[ημερομηνία]**.
""";

    private const string LegacyStudentRulesText = """
# Μπαίνοντας στο εργαστήριο
1. Περιμένω τον καθηγητή μου και μπαίνω μαζί του.
2. Κάθομαι στη θέση μου, όπως ορίζει το πλάνο του τμήματος.
3. Αν ο υπολογιστής μου ή η θέση μου έχει πρόβλημα, το λέω **αμέσως** στον καθηγητή.

# Κατά τη διάρκεια του μαθήματος
- Δεν τρώω και δεν πίνω δίπλα στους υπολογιστές.
- Δεν αλλάζω ρυθμίσεις, φόντο, κωδικούς ή καλώδια.
- Δεν εγκαθιστώ προγράμματα, παιχνίδια ή επεκτάσεις.
- Δεν κλείνω και δεν πειράζω προγράμματα του σχολείου (π.χ. Veyon, antivirus).
- Χρησιμοποιώ το διαδίκτυο μόνο για ό,τι ζητά ο καθηγητής.
- Δεν βάζω USB ή άλλες συσκευές χωρίς άδεια.
- Φυλάω τους κωδικούς μου και δεν χρησιμοποιώ λογαριασμούς άλλων.

# Φεύγοντας
1. Αποθηκεύω τη δουλειά μου εκεί που μου είπε ο καθηγητής.
2. Κάνω αποσύνδεση και κλείνω τον υπολογιστή, αν μου το ζητήσουν.
3. Αφήνω καθαρή τη θέση μου και βάζω την καρέκλα στη θέση της.

# Αν δω βλάβη
- Το λέω στον καθηγητή ή το δηλώνω στη σελίδα αναφοράς βλαβών του σχολείου.
- Δεν προσπαθώ να το φτιάξω μόνος/η μου.

**Ο εξοπλισμός είναι κοινός — τον προσέχουμε για να τον βρουν σε καλή κατάσταση και οι επόμενοι.**
""";
}
