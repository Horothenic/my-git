using MyGit.Core.Git;
using MyGit.Core.Operations;

namespace MyGit.Core.Tests.Operations;

public sealed class ConflictFileTests
{
    private const string TwoConflicts = """
        public class Inventory
        {
        <<<<<<< HEAD
            int size = 16;
        =======
            int size = 24;
        >>>>>>> a1b2c3d (Bigger inventory)
            List<Item> items;
        <<<<<<< HEAD
            // main's note
        =======
            // my note
            // second line
        >>>>>>> a1b2c3d (Bigger inventory)
        }

        """;

    [Fact]
    public void Parse_FindsConflictsAndTheirSides()
    {
        var file = ConflictFile.Parse(TwoConflicts);

        Assert.NotNull(file);
        Assert.Equal(2, file.Conflicts.Count);
        Assert.Equal(["    int size = 16;"], file.Conflicts[0].Ours);
        Assert.Equal(["    int size = 24;"], file.Conflicts[0].Theirs);
        Assert.Equal("a1b2c3d (Bigger inventory)", file.Conflicts[0].TheirsLabel);
        Assert.Equal(2, file.Conflicts[1].Theirs.Count);
    }

    [Fact]
    public void Build_AppliesEachChoice()
    {
        var file = ConflictFile.Parse(TwoConflicts)!;

        var result = file.Build([ConflictChoice.Theirs, ConflictChoice.Both]);

        Assert.Equal("""
            public class Inventory
            {
                int size = 24;
                List<Item> items;
                // main's note
                // my note
                // second line
            }

            """, result);
    }

    [Fact]
    public void Build_KeepsMarkersForUnresolvedConflicts()
    {
        var file = ConflictFile.Parse(TwoConflicts)!;

        var result = file.Build([ConflictChoice.Ours, ConflictChoice.Unresolved]);

        Assert.Contains("int size = 16;", result);
        Assert.Contains("<<<<<<< HEAD\n    // main's note\n=======", result.ReplaceLineEndings("\n"));
        Assert.NotNull(ConflictFile.Parse(result)); // still a valid conflicted file
    }

    [Fact]
    public void Parse_AndBuild_KeepWindowsLineEndings_AndDiff3Base()
    {
        var text = "a\r\n<<<<<<< HEAD\r\nours\r\n||||||| base\r\noriginal\r\n=======\r\ntheirs\r\n>>>>>>> topic\r\nz\r\n";
        var file = ConflictFile.Parse(text)!;

        Assert.Equal(["original"], file.Conflicts[0].Base);
        Assert.Equal("a\r\ntheirs\r\nz\r\n", file.Build([ConflictChoice.Theirs]));
        Assert.Equal(text, file.Build([ConflictChoice.Unresolved]));
    }

    [Theory]
    [InlineData("no conflicts here\n")]
    [InlineData("<<<<<<< HEAD\nonly a start\n")]
    [InlineData("a line with <<<<<<< in the middle\n")]
    public void Parse_ReturnsNull_WithoutCompleteConflicts(string text)
    {
        Assert.Null(ConflictFile.Parse(text));
    }

    [Fact]
    public async Task Parse_ReadsARealGitConflict()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync("f.txt", "a\nshared\nz\n", "base");
        await repository.RunAsync("checkout", "-q", "-b", "topic");
        await repository.CommitFileAsync("f.txt", "a\ntopic\nz\n", "topic");
        await repository.RunAsync("checkout", "-q", "main");
        await repository.CommitFileAsync("f.txt", "a\nmain\nz\n", "main");
        await new GitRunner().RunAsync(repository.Path, ["merge", "topic"]);

        var file = ConflictFile.Parse(await File.ReadAllTextAsync(Path.Combine(repository.Path, "f.txt")))!;

        Assert.Equal(["main"], file.Conflicts[0].Ours);
        Assert.Equal(["topic"], file.Conflicts[0].Theirs);
        Assert.Equal("a\nmain\ntopic\nz\n", file.Build([ConflictChoice.Both]));
    }
}
