namespace Mocksmith.Core.Services;

/// <summary>
/// Parses and evaluates collection tag queries: tags combined with AND / OR (AND binds
/// tighter) and optional parentheses, e.g. <c>dark AND (dashboard OR admin)</c>.
/// Tag terms are normalized through <see cref="TagNormalizer"/> so queries match the
/// catalog vocabulary regardless of input casing or separators.
/// </summary>
public sealed class TagQuery
{
    private abstract record Node
    {
        public abstract bool Evaluate(IReadOnlySet<string> tags);
    }

    private sealed record TagNode(string Tag) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => tags.Contains(Tag);
    }

    private sealed record AndNode(Node Left, Node Right) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => Left.Evaluate(tags) && Right.Evaluate(tags);
    }

    private sealed record OrNode(Node Left, Node Right) : Node
    {
        public override bool Evaluate(IReadOnlySet<string> tags) => Left.Evaluate(tags) || Right.Evaluate(tags);
    }

    private readonly Node _root;

    private TagQuery(Node root) => _root = root;

    /// <summary>Parses a query, throwing <see cref="FormatException"/> with a readable message on invalid syntax.</summary>
    public static TagQuery Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new FormatException("The query is empty.");
        }

        var tokens = Tokenize(query);

        var position = 0;
        var root = ParseOr(tokens, ref position);
        if (position != tokens.Count)
        {
            throw new FormatException($"Unexpected '{tokens[position]}' at position {position + 1}.");
        }

        return new TagQuery(root);
    }

    public static bool TryParse(string? query, out string? error)
    {
        try
        {
            Parse(query);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Raw query membership for a sample's tag set. Collection membership additionally
    /// applies pins: (query AND NOT excluded) OR included — see CollectionService.
    /// </summary>
    public bool Matches(IEnumerable<string> sampleTags)
        => _root.Evaluate(sampleTags as IReadOnlySet<string> ?? new HashSet<string>(sampleTags, StringComparer.Ordinal));

    private static List<string> Tokenize(string query)
    {
        // Split on any whitespace (tabs/newlines arrive via copy-paste), not just spaces.
        return query.Replace("(", " ( ").Replace(")", " ) ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static Node ParseOr(List<string> tokens, ref int position)
    {
        var left = ParseAnd(tokens, ref position);
        while (position < tokens.Count && tokens[position].Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            position++;
            var right = ParseAnd(tokens, ref position);
            left = new OrNode(left, right);
        }

        return left;
    }

    private static Node ParseAnd(List<string> tokens, ref int position)
    {
        var left = ParseTerm(tokens, ref position);
        while (position < tokens.Count && tokens[position].Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            position++;
            var right = ParseTerm(tokens, ref position);
            left = new AndNode(left, right);
        }

        return left;
    }

    private static Node ParseTerm(List<string> tokens, ref int position)
    {
        if (position >= tokens.Count)
        {
            throw new FormatException("The query ends where a tag was expected.");
        }

        var token = tokens[position];
        if (token == "(")
        {
            position++;
            var inner = ParseOr(tokens, ref position);
            if (position >= tokens.Count || tokens[position] != ")")
            {
                throw new FormatException("Missing closing parenthesis.");
            }

            position++;
            return inner;
        }

        if (token == ")" || token.Equals("AND", StringComparison.OrdinalIgnoreCase) || token.Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Expected a tag but found '{token}'.");
        }

        position++;
        var normalized = TagNormalizer.Normalize(token);
        if (normalized.Length == 0)
        {
            throw new FormatException($"'{token}' is not a usable tag.");
        }

        return new TagNode(normalized);
    }
}
