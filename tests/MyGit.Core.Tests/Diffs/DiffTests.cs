using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Diffs;

public sealed class DiffTests
{
    private const string SampleDiff = """
        diff --git a/Inventory.cs b/Inventory.cs
        index 1111111..2222222 100644
        --- a/Inventory.cs
        +++ b/Inventory.cs
        @@ -12,3 +12,4 @@ namespace Game
         public class Inventory
        -    int size = 10;
        +    int size = 24;
        +    Slot[] slots;
         }
        """;

    [Fact]
    public void Parse_ReadsHunksAndLineNumbers()
    {
        var diff = DiffParser.Parse("Inventory.cs", SampleDiff);

        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal(12, hunk.OldStart);
        Assert.Equal(
            [
                (DiffLineKind.Context, "public class Inventory", (int?)12, (int?)12),
                (DiffLineKind.Removed, "    int size = 10;", 13, null),
                (DiffLineKind.Added, "    int size = 24;", null, 13),
                (DiffLineKind.Added, "    Slot[] slots;", null, 14),
                (DiffLineKind.Context, "}", 14, 15),
            ],
            hunk.Lines.Select(line => (line.Kind, line.Text, line.OldNumber, line.NewNumber)));
    }

    [Fact]
    public void Parse_DetectsBinary()
    {
        var diff = DiffParser.Parse("Icon.png", "diff --git a/Icon.png b/Icon.png\nBinary files a/Icon.png and b/Icon.png differ\n");

        Assert.True(diff.IsBinary);
    }

    [Fact]
    public void SideBySide_PairsRemovedAndAddedLines()
    {
        var rows = SideBySide.Build(DiffParser.Parse("Inventory.cs", SampleDiff));

        Assert.Equal(SideBySideRowKind.HunkHeader, rows[0].Kind);
        Assert.Equal("@@ line 12 @@", rows[0].HunkHeader);

        // Row 2: "size = 10" on the left next to "size = 24" on the right.
        Assert.Equal("    int size = 10;", rows[2].Left?.Text);
        Assert.Equal("    int size = 24;", rows[2].Right?.Text);

        // Row 3: the extra added line has nothing on the left.
        Assert.Null(rows[3].Left);
        Assert.Equal("    Slot[] slots;", rows[3].Right?.Text);

        Assert.Equal(SideBySideRowKind.Context, rows[4].Kind);
    }

    [Fact]
    public async Task GetDiffAsync_ShowsUnstagedStagedAndNewFiles()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "one\ntwo\n", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "one\nTWO\n");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "new.txt"), "hello\nworld\n");
        var service = new DiffService(new GitRunner());

        var unstaged = await service.GetDiffAsync(repository.Path, new FileChange("a.txt", ChangeKind.Modified), staged: false);
        Assert.Contains(unstaged.Hunks[0].Lines, line => line is { Kind: DiffLineKind.Added, Text: "TWO" });

        await repository.RunAsync("add", "a.txt");
        var staged = await service.GetDiffAsync(repository.Path, new FileChange("a.txt", ChangeKind.Modified), staged: true);
        Assert.Contains(staged.Hunks[0].Lines, line => line is { Kind: DiffLineKind.Removed, Text: "two" });

        var newFile = await service.GetDiffAsync(repository.Path, new FileChange("new.txt", ChangeKind.Untracked), staged: false);
        Assert.Equal(["hello", "world"], newFile.Hunks[0].Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task GetDiffAsync_TreatsNewBinaryFileAsBinary()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllBytesAsync(Path.Combine(repository.Path, "icon.png"), [0x89, 0x50, 0x00, 0x01]);

        var diff = await new DiffService(new GitRunner()).GetDiffAsync(repository.Path, new FileChange("icon.png", ChangeKind.Untracked), staged: false);

        Assert.True(diff.IsBinary);
    }
}
