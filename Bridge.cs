using System.Net;
using System.Text;
using System.Text.Json;
namespace UniDownloader;

public class AddReq
{
    public string Url { get; set; } = "";
    public string Referrer { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Kind { get; set; } = "auto";
    public string Quality { get; set; } = "best";
    public bool Playlist { get; set; }
}

// جسر محلي (127.0.0.1) تتصل به إضافة المتصفح. يقبل الطلبات القادمة من الإضافات فقط
public static class Bridge
{
    public const int Port = 38475;
    static HttpListener listener;
    static Action showWindow;
    static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };
    static readonly string[] Qualities = { "best", "2160", "1440", "1080", "720", "480", "360", "mp3" };

    public static void Start(Action show)
    {
        showWindow = show;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
        }
        catch { listener = null; return; }
        Task.Run(Loop);
    }

    static async Task Loop()
    {
        while (listener != null && listener.IsListening)
        {
            HttpListenerContext c;
            try { c = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(c));
        }
    }

    static void Respond(HttpListenerResponse res, int code, string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = code;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
        finally { try { res.Close(); } catch { } }
    }

    static async Task Handle(HttpListenerContext c)
    {
        try
        {
            var req = c.Request; var res = c.Response;
            string origin = req.Headers["Origin"];
            bool ext = origin != null && (origin.StartsWith("chrome-extension://") || origin.StartsWith("moz-extension://"));
            if (ext)
            {
                res.Headers["Access-Control-Allow-Origin"] = origin;
                res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                res.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
                res.Headers["Vary"] = "Origin";
            }
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = ext ? 204 : 403; res.Close(); return; }
            // أي صفحة ويب عادية (لها Origin وليست إضافة) مرفوضة
            if (origin != null && !ext) { Respond(res, 403, "{\"ok\":false}"); return; }

            string path = req.Url.AbsolutePath;
            if (path == "/ping")
            {
                Respond(res, 200, "{\"ok\":true}");
            }
            else if (path == "/show")
            {
                showWindow?.Invoke();
                Respond(res, 200, "{\"ok\":true}");
            }
            else if (path == "/add" && req.HttpMethod == "POST")
            {
                if (!ext) { Respond(res, 403, "{\"ok\":false}"); return; }
                string body;
                using (var sr = new StreamReader(req.InputStream, Encoding.UTF8)) body = await sr.ReadToEndAsync();
                var a = JsonSerializer.Deserialize<AddReq>(body, Opt);
                if (a == null || !(a.Url ?? "").StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Respond(res, 400, "{\"ok\":false}");
                    return;
                }
                bool video = a.Kind == "video" || (a.Kind == "auto" && Fmt.IsVideoUrl(a.Url));
                string q = Qualities.Contains(a.Quality) ? a.Quality : "best";
                Manager.Add(a.Url, a.Referrer, a.Cookie, a.UserAgent, video, q, a.Playlist);
                MainForm.NotifyFromAnyThread("بدأ التحميل", video ? "فيديو عبر yt-dlp" : a.Url);
                Respond(res, 200, "{\"ok\":true}");
            }
            else Respond(res, 404, "{\"ok\":false}");
        }
        catch
        {
            try { c.Response.StatusCode = 500; c.Response.Close(); } catch { }
        }
    }
}
