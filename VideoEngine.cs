using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace UniDownloader;

public static class VideoEngine
{
    static string BinDir => Path.Combine(AppSettings.DataDir, "bin");
    public static string YtDlp => Path.Combine(BinDir, "yt-dlp.exe");
    public static string Ffmpeg => Path.Combine(BinDir, "ffmpeg.exe");

    static async Task DownloadTo(string url, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        var tmp = target + ".tmp";
        using (var r = await DownloadEngine.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            r.EnsureSuccessStatusCode();
            using var s = await r.Content.ReadAsStreamAsync(ct);
            using var f = File.Create(tmp);
            await s.CopyToAsync(f, ct);
        }
        File.Move(tmp, target, true);
    }

    public static async Task EnsureYtDlp(CancellationToken ct)
    {
        if (File.Exists(YtDlp)) return;
        await DownloadTo("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", YtDlp, ct);
    }

    public static async Task EnsureFfmpeg(CancellationToken ct)
    {
        if (File.Exists(Ffmpeg)) return;
        var zip = Path.Combine(BinDir, "ffmpeg.zip");
        await DownloadTo("https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip", zip, ct);
        using (var za = ZipFile.OpenRead(zip))
        {
            foreach (var e in za.Entries)
            {
                var n = e.FullName.Replace('\\', '/');
                if (n.EndsWith("/bin/ffmpeg.exe") || n.EndsWith("/bin/ffprobe.exe"))
                    e.ExtractToFile(Path.Combine(BinDir, Path.GetFileName(n)), true);
            }
        }
        try { File.Delete(zip); } catch { }
        if (!File.Exists(Ffmpeg)) throw new Exception("تعذر تجهيز ffmpeg");
    }

    // يحدّث yt-dlp مرة كل يوم في الخلفية حتى لا يتعطل التحميل من يوتيوب
    public static void UpdateInBackground()
    {
        Task.Run(async () =>
        {
            try
            {
                var cfg = AppSettings.Current;
                if (!File.Exists(YtDlp)) return;
                if ((DateTime.Now - cfg.LastYtDlpUpdate).TotalHours < 24) return;
                await RunUpdate();
                cfg.LastYtDlpUpdate = DateTime.Now; cfg.Save();
            }
            catch { }
        });
    }

    static async Task RunUpdate()
    {
        var psi = new ProcessStartInfo(YtDlp) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-U");
        using var p = Process.Start(psi);
        await p.WaitForExitAsync();
    }

    public static async Task<bool> ForceUpdateAsync()
    {
        try
        {
            if (!File.Exists(YtDlp)) await EnsureYtDlp(CancellationToken.None);
            else await RunUpdate();
            AppSettings.Current.LastYtDlpUpdate = DateTime.Now; AppSettings.Current.Save();
            return true;
        }
        catch { return false; }
    }

    static readonly Regex Prog = new(@"\[download\]\s+(\d+(?:\.\d+)?)%\s+of\s+~?\s*([\d\.]+)\s*(B|KiB|MiB|GiB|TiB)(?:\s+at\s+([\d\.]+)\s*(B|KiB|MiB|GiB)/s)?(?:\s+ETA\s+(\S+))?");

    static double Mult(string u) => u switch
    {
        "KiB" => 1024.0,
        "MiB" => 1048576.0,
        "GiB" => 1073741824.0,
        "TiB" => 1099511627776.0,
        _ => 1.0
    };

    public static async Task RunAsync(Job j, CancellationToken ct)
    {
        if (!j.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) throw new Exception("رابط غير صالح");
        var cfg = AppSettings.Current;

        j.Note = "تجهيز yt-dlp...";
        await EnsureYtDlp(ct);
        j.Note = "تجهيز ffmpeg (مرة واحدة)...";
        await EnsureFfmpeg(ct);
        j.Note = "جارٍ التحميل";

        bool audio = j.Quality == "mp3";
        string root = cfg.DownloadFolder;
        string dir = cfg.Categorize ? Path.Combine(root, audio ? "موسيقى" : "فيديو") : root;
        Directory.CreateDirectory(dir);

        var psi = new ProcessStartInfo(YtDlp)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        var a = psi.ArgumentList;
        a.Add("--newline"); a.Add("--windows-filenames"); a.Add("--no-mtime");
        a.Add(j.Playlist ? "--yes-playlist" : "--no-playlist");
        a.Add("--concurrent-fragments"); a.Add("8");
        a.Add("--ffmpeg-location"); a.Add(BinDir);
        a.Add("-P"); a.Add(dir);
        a.Add("-o");
        a.Add(j.Playlist ? "%(playlist_title).80s/%(playlist_index)s - %(title).100s.%(ext)s" : "%(title).120s.%(ext)s");

        if (audio)
        {
            a.Add("-f"); a.Add("ba/b"); a.Add("-x"); a.Add("--audio-format"); a.Add("mp3"); a.Add("--audio-quality"); a.Add("0");
        }
        else if (int.TryParse(j.Quality, out int h))
        {
            a.Add("-f"); a.Add($"bv*[height<={h}]+ba/b[height<={h}]"); a.Add("--merge-output-format"); a.Add("mp4");
        }
        else
        {
            a.Add("-f"); a.Add("bv*+ba/b"); a.Add("--merge-output-format"); a.Add("mp4");
        }
        a.Add("--"); a.Add(j.Url);

        using var p = Process.Start(psi);
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        string lastErr = "";
        var errTask = Task.Run(async () =>
        {
            string l;
            while ((l = await p.StandardError.ReadLineAsync()) != null)
                if (l.Contains("ERROR")) lastErr = l;
        });

        string line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null)
        {
            var m = Prog.Match(line);
            if (m.Success)
            {
                double pct = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double size = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * Mult(m.Groups[3].Value);
                j.Total = (long)size; j.Percent = pct; j.Downloaded = (long)(size * pct / 100.0);
                if (m.Groups[4].Success)
                    j.Speed = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * Mult(m.Groups[5].Value);
                j.Eta = m.Groups[6].Success ? m.Groups[6].Value : "";
                j.Note = "جارٍ التحميل";
            }
            else if (line.StartsWith("[download] Destination:"))
            {
                var path = line.Substring("[download] Destination:".Length).Trim();
                j.SavePath = path; j.FileName = Path.GetFileName(path);
            }
            else if (line.StartsWith("[Merger]"))
            {
                j.Note = "دمج الصوت والفيديو...";
                var mm = Regex.Match(line, "into \"(.+)\"");
                if (mm.Success) { j.SavePath = mm.Groups[1].Value; j.FileName = Path.GetFileName(j.SavePath); }
            }
            else if (line.StartsWith("[ExtractAudio]"))
            {
                j.Note = "تحويل إلى MP3...";
                var mm = Regex.Match(line, "Destination:\\s*(.+)$");
                if (mm.Success) { j.SavePath = mm.Groups[1].Value.Trim(); j.FileName = Path.GetFileName(j.SavePath); }
            }
        }
        await p.WaitForExitAsync();
        await errTask;
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new Exception(string.IsNullOrEmpty(lastErr) ? "فشل yt-dlp" : lastErr);
        if (!string.IsNullOrEmpty(j.SavePath) && File.Exists(j.SavePath)) j.Total = new FileInfo(j.SavePath).Length;
    }
}
