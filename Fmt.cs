namespace UniDownloader;

public static class Fmt
{
    public static string Size(long b)
    {
        if (b < 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0") : v.ToString("0.##")) + " " + u[i];
    }

    public static string Speed(double bps) => bps <= 0 ? "" : Size((long)bps) + "/ث";

    public static string Eta(double s)
    {
        if (s <= 0 || double.IsInfinity(s) || double.IsNaN(s)) return "";
        var t = TimeSpan.FromSeconds(s);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string SafeName(string n)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().TrimEnd('.');
        return n.Length == 0 ? "download" : n;
    }

    public static string FolderFor(string fileName)
    {
        var root = AppSettings.Current.DownloadFolder;
        if (!AppSettings.Current.Categorize) return root;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string cat = ext switch
        {
            ".exe" or ".msi" or ".apk" or ".iso" or ".dmg" or ".msix" => "برامج",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" => "مضغوطات",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".ts" => "فيديو",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma" => "موسيقى",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".epub" => "مستندات",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "صور",
            _ => "أخرى"
        };
        return Path.Combine(root, cat);
    }

    public static string Unique(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".part")) return path;
        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 10000; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p) && !File.Exists(p + ".part")) return p;
        }
        return path;
    }

    static readonly string[] VideoHosts =
    {
        "youtube.com", "youtu.be", "facebook.com", "fb.watch", "instagram.com", "tiktok.com",
        "twitter.com", "x.com", "vimeo.com", "dailymotion.com", "twitch.tv", "soundcloud.com",
        "reddit.com", "bilibili.com", "ok.ru", "vk.com"
    };

    public static bool IsVideoUrl(string url)
    {
        try
        {
            var h = new Uri(url).Host.ToLowerInvariant();
            foreach (var v in VideoHosts)
                if (h == v || h.EndsWith("." + v)) return true;
        }
        catch { }
        return false;
    }

    public static bool LooksLikeDownload(string t)
    {
        if (string.IsNullOrWhiteSpace(t) || t.Length > 2000 || t.Contains('\n')) return false;
        if (!Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != "http" && u.Scheme != "https") return false;
        if (IsVideoUrl(t)) return true;
        var ext = Path.GetExtension(u.AbsolutePath).ToLowerInvariant();
        string[] ok = { ".exe", ".msi", ".zip", ".rar", ".7z", ".iso", ".apk", ".mp4", ".mkv", ".mp3", ".pdf", ".avi", ".dmg", ".tar", ".gz" };
        return ok.Contains(ext);
    }
}
