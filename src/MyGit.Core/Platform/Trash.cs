using Microsoft.VisualBasic.FileIO;

namespace MyGit.Core.Platform;

/// <summary>Puts a file in the Recycle Bin (Windows) or the Trash (macOS), so a discard can still be undone.</summary>
public static class Trash
{
    public static void Move(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MoveToMacTrash(fullPath);
        }
        else
        {
            File.Delete(fullPath);
        }
    }

    // The Trash is ~/.Trash; a name that's taken gets " 2", " 3", ... like Finder does.
    private static void MoveToMacTrash(string fullPath)
    {
        var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        Directory.CreateDirectory(trash);

        var name = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        var target = Path.Combine(trash, name + extension);
        for (var number = 2; File.Exists(target) || Directory.Exists(target); number++)
        {
            target = Path.Combine(trash, $"{name} {number}{extension}");
        }

        File.Move(fullPath, target);
    }
}
