using TwoKPlayer.Core.Updates;

namespace TwoKPlayer.Core.Tests;

public class ReleaseNotesTests
{
    [Fact]
    public void ReadsOnlyTheChangeList()
    {
        const string notes = "Windows IPTV player.\r\n\r\n**Already installed?** Click *Update*.\r\n\r\n### Changes\r\n- Add update popup\r\n- Fix EPG cache\r\n\r\n### Source and license\r\n- not a change\r\n";

        Assert.Equal(["Add update popup", "Fix EPG cache"], ReleaseNotes.Changes(notes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No headings here\n- just a list")]
    public void EmptyWithoutChangesSection(string? notes)
    {
        Assert.Empty(ReleaseNotes.Changes(notes));
    }
}
