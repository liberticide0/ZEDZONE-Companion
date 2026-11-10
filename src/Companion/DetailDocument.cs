using System.Globalization;
using System.Text;

namespace Companion;

// Render headings, definition rows, and notes in the native text control.
internal sealed class DetailDocument
{
    private readonly StringBuilder rtf = new(
        @"{\rtf1\ansi\uc1{\fonttbl{\f0 Malgun Gothic;}}" +
        @"{\colortbl;\red235\green241\blue238;\red164\green223\blue170;\red184\green203\blue192;\red154\green173\blue159;}");

    public void Heading(string text) => Paragraph(@"\sb160\sa85\cf2\fs23\b ", text);
    public void Body(string text) => Paragraph(@"\sa115\cf1\fs20 ", text);
    public void Step(string title, string description)
    {
        Paragraph(@"\sb70\sa20\cf1\fs20\b ", title);
        Paragraph(@"\li180\sa90\cf3\fs19 ", description);
    }
    public void Aliases(string text) => Paragraph(@"\sa95\cf3\fs19 ", text);
    public void Note(string text) => Paragraph(@"\sb85\sa40\cf4\fs17 ", text);

    public void Field(string label, string value, int indent = 1860)
    {
        rtf.Append(CultureInfo.InvariantCulture, $@"\pard\sa70\li{indent}\fi-{indent}\tx{indent}\fs20\cf3\b ");
        AppendText(label);
        rtf.Append(@"\b0\cf1\tab ");
        AppendText(value);
        rtf.Append(@"\par ");
    }

    private void Paragraph(string style, string text)
    {
        rtf.Append(@"\pard\plain ").Append(style);
        AppendText(text);
        rtf.Append(@"\par ");
    }

    private void AppendText(string text)
    {
        foreach (char character in text.ReplaceLineEndings("\n"))
        {
            switch (character)
            {
                case '\n': rtf.Append(@"\line "); break;
                case '\t': rtf.Append(@"\tab "); break;
                case '\\' or '{' or '}': rtf.Append('\\').Append(character); break;
                default:
                    if (character is >= ' ' and <= '~')
                        rtf.Append(character);
                    else
                        rtf.Append(@"\u").Append(unchecked((short)character).ToString(CultureInfo.InvariantCulture)).Append('?');
                    break;
            }
        }
    }

    public override string ToString() => rtf.ToString() + "}";
}
