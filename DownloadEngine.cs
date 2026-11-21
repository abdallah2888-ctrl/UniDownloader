using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
namespace UniDownloader;

public static class Limiter
{
    static readonly object L = new();
    static long next = 0;

    public static async Task Wait(int bytes, CancellationToken ct)
    {
        long kb = AppSettings.Current.SpeedLimitKB;
        if (kb <= 0) return;
        long add = (long)(bytes / (kb * 1024.0) * Stopwatch.Frequency);
        long now = Stopwatch.GetTimestamp();
        long start;
        lock (L)
        {
            start = Math.Max(next, now);
            next = start + add;
        }
        long ms = (start - now) * 1000 / Stopwatch.Frequency;
        if (ms > 0) await Task.Delay((int)Math.Min(ms, 10000), ct);
    }
}

public static class DownloadEngine
{
    public static readonly HttpClient Http = Make();

    static HttpClient Make()
    {
        var h = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 128,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 15,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(25)
        };
        return new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
    }

    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    static HttpRequestMessage Req(Job j, long? from, long? to)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, j.Url);
        r.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrEmpty(j.UserAgent) ? UA : j.UserAgent);
        if (!string.IsNullOrEmpty(j.Referrer)) r.Headers.TryAddWithoutValidation("Referer", j.Referrer);
        if (!string.IsNullOrEmpty(j.Cookie)) r.Headers.TryAddWithoutValidation("Cookie", j.Cookie);
        if (from.HasValue) r.Headers.Range = new RangeHeaderValue(from, to);
        return r;
    }

    class State
    {
        public long Total { get; set; }
        public long Chunk { get; set; }
        public List<int> Done { get; set; } = new();
    }

    static string NameFrom(HttpResponseMessage r, string url)
    {
        string n = null;
        var cd = r.Content.Headers.ContentDisposition;
        if (cd != null) n = cd.FileNameStar ?? cd.FileName;
        if (!string.IsNullOrWhiteSpace(n)) n = n.Trim('"');
        else
        {
            try { n = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath)); } catch { n = ""; }
        }
        if (string.IsNullOrWhiteSpace(n)) n = "download";
        return Fmt.SafeName(n);
    }

    static void Progress(Job j)
    {
        j.Percent = j.Total > 0 ? Math.Min(100.0, j.Downloaded * 100.0 / j.Total) : 0;
        j.Eta = (j.Speed > 1 && j.Total > 0) ? Fmt.Eta((j.Total - j.Downloaded) / j.Speed) : "";
    }

    public static async Task RunAsync(Job j, CancellationToken ct)
    {
        var cfg = AppSettings.Current;
        Directory.CreateDirectory(cfg.DownloadFolder);
        long total = -1;
        bool ranges = false;

        using (var probe = await Http.SendAsync(Req(j, 0, 0), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!probe.IsSuccessStatusCode) throw new Exception("HTTP " + (int)probe.StatusCode);
            j.Url = probe.RequestMessage.RequestUri.ToString();
            if (probe.StatusCode == HttpStatusCode.PartialContent && probe.Content.Headers.ContentRange?.Length is long len)
            {
                total = len; ranges = true;
            }
            else total = probe.Content.Headers.ContentLength ?? -1;
            if (string.IsNullOrEmpty(j.FileName)) j.FileName = NameFrom(probe, j.Url);
        }

        j.Total = total;
        if (string.IsNullOrEmpty(j.SavePath))
        {
            var dir = Fmt.FolderFor(j.FileName);
            Directory.CreateDirectory(dir);
            j.SavePath = Fmt.Unique(Path.Combine(dir, j.FileName));
        }
        var part = j.SavePath + ".part";
        var stPath = j.SavePath + ".ud";

        if (!ranges || total < 1024 * 1024 || cfg.Connections <= 1)
            await Single(j, part, ct);
        else
            await Multi(j, part, stPath, total, ct);

        File.Move(part, j.SavePath, true);
        try { File.Delete(stPath); } catch { }
    }

    static async Task Single(Job j, string part, CancellationToken ct)
    {
        using var resp = await Http.SendAsync(Req(j, null, null), HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var st = await resp.Content.ReadAsStreamAsync(ct);
        using var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);
        var buf = new byte[128 * 1024];
        int n; long dl = 0; long lastB = 0; long lastT = 0;
        var sw = Stopwatch.StartNew();
        while ((n = await st.ReadAsync(buf, 0, buf.Length, ct)) > 0)
        {
            await fs.WriteAsync(buf, 0, n, ct);
            dl += n; j.Downloaded = dl;
            await Limiter.Wait(n, ct);
            long t = sw.ElapsedMilliseconds;
            if (t - lastT > 500)
            {
                j.Speed = (dl - lastB) * 1000.0 / (t - lastT);
                lastB = dl; lastT = t;
                Progress(j);
            }
        }
        j.Downloaded = dl;
        if (j.Total <= 0) j.Total = dl;
    }

    static async Task Multi(Job j, string part, string stPath, long total, CancellationToken ct)
    {
        var cfg = AppSettings.Current;
        int conn = Math.Clamp(cfg.Connections, 1, 32);
        long chunk = Math.Clamp(total / (conn * 4), 512L * 1024, 16L * 1024 * 1024);
        var done = new HashSet<int>();

        if (File.Exists(stPath) && File.Exists(part))
        {
            try
            {
                var s = JsonSerializer.Deserialize<State>(File.ReadAllText(stPath));
                if (s != null && s.Total == total && s.Chunk > 0)
                {
                    chunk = s.Chunk;
                    foreach (var d in s.Done) done.Add(d);
                }
            }
            catch { }
        }
        else
        {
            try { File.Delete(part); } catch { }
        }

        int count = (int)((total + chunk - 1) / chunk);
        var queue = new ConcurrentQueue<int>(Enumerable.Range(0, count).Where(i => !done.Contains(i)));
        long downloaded = 0;
        foreach (var i in done) downloaded += Math.Min(chunk, total - i * chunk);
        j.Downloaded = downloaded;

        using var fs = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1, FileOptions.None);
        if (fs.Length != total) fs.SetLength(total);
        var handle = fs.SafeFileHandle;

        using var wcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var wtok = wcts.Token;
        Exception firstError = null;

        void SaveState()
        {
            try
            {
                List<int> snap;
                lock (done) snap = done.ToList();
                File.WriteAllText(stPath, JsonSerializer.Serialize(new State { Total = total, Chunk = chunk, Done = snap }));
            }
            catch { }
        }

        async Task Worker()
        {
            var buf = new byte[128 * 1024];
            while (!wtok.IsCancellationRequested && queue.TryDequeue(out int idx))
            {
                long start = idx * chunk;
                long end = Math.Min(total - 1, start + chunk - 1);
                long pos = start;
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        using var rcts = CancellationTokenSource.CreateLinkedTokenSource(wtok);
                        rcts.CancelAfter(30000);
                        using var resp = await Http.SendAsync(Req(j, pos, end), HttpCompletionOption.ResponseHeadersRead, rcts.Token);
                        if (resp.StatusCode != HttpStatusCode.PartialContent)
                            throw new IOException("HTTP " + (int)resp.StatusCode);
                        using var st = await resp.Content.ReadAsStreamAsync(rcts.Token);
                        while (true)
                        {
                            rcts.CancelAfter(30000);
                            int n = await st.ReadAsync(buf, 0, buf.Length, rcts.Token);
                            if (n <= 0) break;
                            RandomAccess.Write(handle, new ReadOnlySpan<byte>(buf, 0, n), pos);
                            pos += n;
                            Interlocked.Add(ref downloaded, n);
                            await Limiter.Wait(n, wtok);
                        }
                        if (pos <= end) throw new IOException("اتصال مقطوع");
                        break;
                    }
                    catch (Exception) when (wtok.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        if (++attempts > 8) throw;
                        await Task.Delay(Math.Min(5000, 500 * attempts), wtok);
                    }
                }
                lock (done) done.Add(idx);
            }
        }

        using var stop = new CancellationTokenSource();
        var progress = Task.Run(async () =>
        {
            long lastB = Interlocked.Read(ref downloaded);
            long lastT = 0; int tick = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(500, stop.Token);
                    long b = Interlocked.Read(ref downloaded);
                    long t = sw.ElapsedMilliseconds;
                    double inst = (b - lastB) * 1000.0 / Math.Max(1, t - lastT);
                    j.Speed = j.Speed * 0.6 + inst * 0.4;
                    lastB = b; lastT = t;
                    j.Downloaded = b;
                    Progress(j);
                    if (++tick % 4 == 0) SaveState();
                }
            }
            catch (OperationCanceledException) { }
        });

        var workers = Enumerable.Range(0, conn).Select(_ => Task.Run(async () =>
        {
            try { await Worker(); }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref firstError, ex, null);
                wcts.Cancel();
                throw;
            }
        })).ToArray();

        try
        {
            try { await Task.WhenAll(workers); }
            catch
            {
                if (firstError != null) ExceptionDispatchInfo.Capture(firstError).Throw();
                throw;
            }
        }
        finally
        {
            stop.Cancel();
            try { await progress; } catch { }
            SaveState();
        }

        if (done.Count != count) throw new IOException("التحميل غير مكتمل");
        j.Downloaded = total;
        j.Speed = 0;
        fs.Flush();
    }
}
