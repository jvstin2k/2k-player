namespace TwoKPlayer.Core.Updates;

/// <summary>
/// Reads the change list out of the release notes the release workflow writes for every version:
/// the "- …" lines under "### Changes", without the install and license text around them.
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<string> Changes(string? markdown)
    {
        var changes = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
            return changes;

        var inChanges = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#'))
                inChanges = line.TrimStart('#').Trim().Equals("Changes", StringComparison.OrdinalIgnoreCase);
            else if (inChanges && line.StartsWith("- ") && line.Length > 2)
                changes.Add(line[2..].Trim());
        }
        return changes;
    }
}
