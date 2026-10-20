using System.Text;

namespace CarbonFreeze.Core.Steam;

/// <summary>A node in a parsed Valve KeyValues (VDF) document: either a string value or an object with children.</summary>
internal sealed class VdfNode
{
    public VdfNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>String value, or null if this node is an object.</summary>
    public string? Value { get; }

    public List<VdfNode> Children { get; } = new();

    public bool IsObject => Value is null;

    /// <summary>First child with this key (case-insensitive), or null.</summary>
    public VdfNode? this[string key] =>
        Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public string? GetString(string key) => this[key]?.Value;

    public override string ToString() => IsObject ? $"{Key} {{{Children.Count}}}" : $"{Key} = {Value}";
}

/// <summary>
/// Minimal parser for Valve's KeyValues text format: quoted (or bare) keys and values, nested
/// braces, // comments, backslash escapes. Returns a root node whose children are the top-level keys.
/// </summary>
internal static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var root = new VdfNode("");
        var pos = 0;
        ParseBlock(text, ref pos, root, topLevel: true);
        return root;
    }

    /// <summary>Parses a file, returning null if it is missing or malformed.</summary>
    public static VdfNode? TryParseFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private enum TokKind { String, Open, Close }

    private readonly record struct Token(TokKind Kind, string Text, int Offset);

    private static void ParseBlock(string s, ref int pos, VdfNode parent, bool topLevel)
    {
        while (true)
        {
            var tok = NextToken(s, ref pos);
            if (tok is null)
            {
                if (topLevel) return;
                throw new FormatException("Unexpected end of VDF data: missing '}'.");
            }

            switch (tok.Value.Kind)
            {
                case TokKind.Close:
                    if (topLevel) continue; // tolerate a stray closing brace
                    return;
                case TokKind.Open:
                    throw new FormatException($"Unexpected '{{' at offset {tok.Value.Offset}.");
            }

            var key = tok.Value.Text;
            var next = NextToken(s, ref pos);
            if (next is null)
                throw new FormatException($"Key '{key}' at offset {tok.Value.Offset} has no value.");

            switch (next.Value.Kind)
            {
                case TokKind.Open:
                {
                    var node = new VdfNode(key);
                    parent.Children.Add(node);
                    ParseBlock(s, ref pos, node, topLevel: false);
                    break;
                }
                case TokKind.String:
                    parent.Children.Add(new VdfNode(key, next.Value.Text));
                    break;
                default:
                    throw new FormatException($"Key '{key}' at offset {tok.Value.Offset} is followed by '}}'.");
            }
        }
    }

    private static Token? NextToken(string s, ref int pos)
    {
        while (true)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
            if (pos >= s.Length) return null;

            var c = s[pos];

            // // line comment
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                SkipLine(s, ref pos);
                continue;
            }

            // #base / #include directives: ignore the line
            if (c == '#')
            {
                SkipLine(s, ref pos);
                continue;
            }

            // [$WIN32]-style conditionals: ignore
            if (c == '[')
            {
                while (pos < s.Length && s[pos] != ']') pos++;
                if (pos < s.Length) pos++;
                continue;
            }

            var start = pos;
            if (c == '{') { pos++; return new Token(TokKind.Open, "{", start); }
            if (c == '}') { pos++; return new Token(TokKind.Close, "}", start); }

            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < s.Length && s[pos] != '"')
                {
                    var ch = s[pos];
                    if (ch == '\\' && pos + 1 < s.Length)
                    {
                        pos++;
                        var e = s[pos];
                        sb.Append(e switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            '\\' => '\\',
                            '"' => '"',
                            _ => e,
                        });
                        pos++;
                        continue;
                    }
                    sb.Append(ch);
                    pos++;
                }
                if (pos >= s.Length) throw new FormatException($"Unterminated string starting at offset {start}.");
                pos++; // closing quote
                return new Token(TokKind.String, sb.ToString(), start);
            }

            // bare token
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}' && s[pos] != '"')
            {
                if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/') break;
                pos++;
            }
            return new Token(TokKind.String, s[start..pos], start);
        }
    }

    private static void SkipLine(string s, ref int pos)
    {
        while (pos < s.Length && s[pos] != '\n') pos++;
    }
}
