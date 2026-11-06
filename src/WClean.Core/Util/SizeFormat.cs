namespace WClean;

public static class SizeFormat
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string ToText(long bytes)
    {
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.##} {Units[i]}";
    }
}
