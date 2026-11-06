namespace WClean;

public static class BackupStore
{
    public static string Directory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WClean", "Backups");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string NewFilePath(string label, string extension)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
        if (label.Length > 60) label = label[..60];
        return Path.Combine(Directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{label}{extension}");
    }
}
