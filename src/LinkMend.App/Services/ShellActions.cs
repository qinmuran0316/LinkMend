using System.Diagnostics;

namespace LinkMend.App.Services;

public static class ShellActions
{
    public static void OpenContainingFolder(string filePath)
    {
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add($"/select,{filePath}");
        Process.Start(startInfo);
    }

    public static void OpenFolder(string folderPath)
    {
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(folderPath);
        Process.Start(startInfo);
    }
}
