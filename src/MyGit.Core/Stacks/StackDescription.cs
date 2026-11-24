namespace MyGit.Core.Stacks;

/// <summary>
/// The stack list at the end of each stacked PR's description, e.g. "Stack (from main up): #14 → **#15** → #16",
/// kept between markers so it can be replaced when the stack changes without touching the rest.
/// </summary>
public static class StackDescription
{
    public const string Start = "<!-- my-git-stack -->";
    public const string End = "<!-- /my-git-stack -->";

    /// <summary>
    /// The section for the PR at <paramref name="current"/> in <paramref name="numbers"/> (bottom first).
    /// <paramref name="merged"/> ones are struck through, e.g. "~~#14~~ → **#15** → #16".
    /// </summary>
    public static string Section(string trunk, IReadOnlyList<int> numbers, int current, IReadOnlySet<int>? merged = null)
    {
        var links = numbers.Select((number, index) =>
            index == current ? $"**#{number}**" : merged?.Contains(number) == true ? $"~~#{number}~~" : $"#{number}");
        return $"{Start}\n---\nStack (from {trunk} up): {string.Join(" → ", links)}\n{End}";
    }

    /// <summary>The body with its stack section replaced, or added at the end when it has none.</summary>
    public static string Apply(string? body, string section)
    {
        var text = (body ?? string.Empty).ReplaceLineEndings("\n");
        var start = text.IndexOf(Start, StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf(End, start, StringComparison.Ordinal);

        if (start >= 0 && end >= 0)
        {
            return text[..start] + section + text[(end + End.Length)..];
        }

        return text.TrimEnd().Length == 0 ? section : $"{text.TrimEnd()}\n\n{section}";
    }
}
