namespace UniDownloader;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Directory.CreateDirectory(AppSettings.DataDir);

        // أول تشغيل: تثبيت تلقائي ثم تشغيل النسخة المثبتة
        if (Setup.FirstRun()) return;

        using var mutex = new Mutex(true, "UniDownloader3_SingleInstance", out bool created);
        if (!created)
        {
            try
            {
                using var c = new HttpClient();
                c.GetAsync($"http://127.0.0.1:{Bridge.Port}/show").Wait(2000);
            }
            catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (s, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

        Manager.Load();
        bool hidden = args.Contains("--hidden");
        Application.Run(new MainForm(hidden));
    }

    static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppSettings.DataDir, "crash.log"), DateTime.Now + "\r\n" + ex + "\r\n\r\n");
        }
        catch { }
    }
}
