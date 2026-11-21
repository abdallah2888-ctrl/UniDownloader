using System.Diagnostics;
using System.Reflection;
namespace UniDownloader;

public class MainForm : Form
{
    public static MainForm Instance;
    readonly DataGridView grid = new();
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new();
    readonly ToolStripStatusLabel status = new();
    bool allowShow, reallyExit, balloonShown;
    string lastClip = "", clipUrl = "";
    int tick;

    public MainForm(bool hidden)
    {
        Instance = this;
        allowShow = !hidden;
        Text = "UniDownloader";
        Width = 1000; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

        BuildUi();
        BuildTray();
        Manager.Finished += OnFinished;
        timer.Interval = 500;
        timer.Tick += (s, e) => OnTick();
        timer.Start();
        if (hidden) CreateHandle();
        Bridge.Start(() => { if (IsHandleCreated) BeginInvoke(new Action(ShowMain)); });

        Shown += async (s, e) =>
        {
            VideoEngine.UpdateInBackground();
            await Updater.CheckAsync(false);
        };
        if (hidden) VideoEngine.UpdateInBackground();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    public void ShowMain()
    {
        allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public static void NotifyFromAnyThread(string title, string text)
    {
        var f = Instance;
        if (f == null || !f.IsHandleCreated) return;
        try { f.BeginInvoke(new Action(() => f.Notify(title, text))); } catch { }
    }

    public void Notify(string title, string text)
    {
        try { tray.ShowBalloonTip(2500, title, text, ToolTipIcon.Info); } catch { }
    }

    void BuildUi()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Font = new Font("Segoe UI", 10f) };
        ToolStripButton B(string t, Action a)
        {
            var b = new ToolStripButton(t) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (s, e) => a();
            return b;
        }
        bar.Items.Add(B("إضافة رابط", AddLink));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(B("إيقاف مؤقت", () => Each(Manager.Pause)));
        bar.Items.Add(B("استئناف", () => Each(Manager.Resume)));
        bar.Items.Add(B("حذف", RemoveSelected));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(B("فتح المجلد", OpenFolder));
        bar.Items.Add(B("الإعدادات", OpenSettings));

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 30;
        string[] cols = { "الاسم", "الحجم", "التقدم", "السرعة", "المتبقي", "الحالة" };
        int[] w = { 38, 12, 20, 12, 10, 18 };
        for (int i = 0; i < cols.Length; i++)
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = cols[i], FillWeight = w[i], SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.CellPainting += OnCellPaint;
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        try
        {
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(grid, true);
        }
        catch { }

        var ss = new StatusStrip();
        ss.Items.Add(status);

        Controls.Add(grid);
        Controls.Add(ss);
        Controls.Add(bar);
    }

    void BuildTray()
    {
        tray.Text = "UniDownloader";
        if (Icon != null) tray.Icon = Icon;
        tray.Visible = true;
        var m = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        m.Items.Add("فتح", null, (s, e) => ShowMain());
        m.Items.Add("إضافة رابط", null, (s, e) => { ShowMain(); AddLink(); });
        m.Items.Add("الإعدادات", null, (s, e) => { ShowMain(); OpenSettings(); });
        m.Items.Add("-");
        m.Items.Add("خروج", null, (s, e) => ExitApp());
        tray.ContextMenuStrip = m;
        tray.DoubleClick += (s, e) => ShowMain();
        tray.BalloonTipClicked += (s, e) =>
        {
            if (!string.IsNullOrEmpty(clipUrl))
            {
                var u = clipUrl; clipUrl = "";
                Manager.Add(u, "", "", "", Fmt.IsVideoUrl(u), "best", false);
            }
        };
    }

    void ExitApp()
    {
        reallyExit = true;
        Manager.PauseAll();
        tray.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!balloonShown)
            {
                balloonShown = true;
                Notify("UniDownloader", "البرنامج يعمل في الخلفية بجانب الساعة. للخروج: زر الفأرة الأيمن على الأيقونة ثم خروج.");
            }
            return;
        }
        Manager.PauseAll();
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    void OnFinished(Job j)
    {
        if (!IsHandleCreated) return;
        try { BeginInvoke(new Action(() => Notify("اكتمل التحميل", string.IsNullOrEmpty(j.FileName) ? j.Url : j.FileName))); } catch { }
    }

    void OnTick()
    {
        tick++;
        if (Visible) RefreshGrid();
        if (AppSettings.Current.WatchClipboard && tick % 2 == 0) CheckClipboard();
    }

    void CheckClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var t = Clipboard.GetText().Trim();
            if (t == lastClip) return;
            lastClip = t;
            if (Fmt.LooksLikeDownload(t))
            {
                clipUrl = t;
                tray.ShowBalloonTip(4000, "رابط تحميل في الحافظة", "انقر هنا لتحميله بـ UniDownloader", ToolTipIcon.Info);
            }
        }
        catch { }
    }

    static string StateText(Job j)
    {
        switch (j.State)
        {
            case JobState.Queued: return "في الانتظار";
            case JobState.Running: return string.IsNullOrEmpty(j.Note) ? "جارٍ التحميل" : j.Note;
            case JobState.Paused: return "متوقف";
            case JobState.Done: return "مكتمل";
            case JobState.Canceled: return "ملغى";
            default:
                var e = j.Error ?? "";
                if (e.Length > 40) e = e.Substring(0, 40) + "…";
                return "خطأ: " + e;
        }
    }

    void RefreshGrid()
    {
        var jobs = Manager.Snapshot();
        while (grid.Rows.Count > jobs.Count) grid.Rows.RemoveAt(grid.Rows.Count - 1);
        double totalSpeed = 0; int running = 0;
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            object[] v =
            {
                string.IsNullOrEmpty(j.FileName) ? j.Url : j.FileName,
                j.Total > 0 ? Fmt.Size(j.Total) : "—",
                j.Percent,
                Fmt.Speed(j.Speed),
                j.Eta,
                StateText(j)
            };
            if (i >= grid.Rows.Count)
            {
                int idx = grid.Rows.Add(v);
                grid.Rows[idx].Tag = j;
            }
            else
            {
                var r = grid.Rows[i];
                r.Tag = j;
                for (int c = 0; c < v.Length; c++)
                    if (!Equals(r.Cells[c].Value, v[c])) r.Cells[c].Value = v[c];
            }
            if (j.State == JobState.Running) { running++; totalSpeed += j.Speed; }
        }
        status.Text = $"  التحميلات: {jobs.Count}   |   نشطة: {running}   |   السرعة الكلية: {Fmt.Speed(totalSpeed)}";
    }

    void OnCellPaint(object sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
        e.PaintBackground(e.CellBounds, true);
        double p = e.Value is double d ? d : 0;
        var r = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 6, e.CellBounds.Width - 8, e.CellBounds.Height - 12);
        e.Graphics.FillRectangle(Brushes.Gainsboro, r);
        var f = new Rectangle(r.X, r.Y, (int)(r.Width * Math.Min(100, Math.Max(0, p)) / 100), r.Height);
        e.Graphics.FillRectangle(Brushes.SeaGreen, f);
        TextRenderer.DrawText(e.Graphics, p.ToString("0") + "%", e.CellStyle.Font, r, Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }

    List<Job> Selected() =>
        grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Job).Where(j => j != null).ToList();

    void Each(Action<Job> a)
    {
        foreach (var j in Selected()) a(j);
        RefreshGrid();
    }

    void AddLink()
    {
        using var d = new AddDialog();
        if (d.ShowDialog(this) != DialogResult.OK) return;
        foreach (var u in d.Urls)
            Manager.Add(u, "", "", "", d.ForceVideo || Fmt.IsVideoUrl(u), d.Quality, d.Playlist);
        RefreshGrid();
    }

    void RemoveSelected()
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        var r = MessageBox.Show("نعم = حذف من القائمة مع حذف الملف\nلا = حذف من القائمة فقط", "حذف",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (r == DialogResult.Cancel) return;
        foreach (var j in sel) Manager.Remove(j, r == DialogResult.Yes);
        RefreshGrid();
    }

    void OpenFolder()
    {
        var j = Selected().FirstOrDefault();
        try
        {
            if (j != null && !string.IsNullOrEmpty(j.SavePath) && File.Exists(j.SavePath))
                Process.Start("explorer.exe", "/select,\"" + j.SavePath + "\"");
            else
            {
                Directory.CreateDirectory(AppSettings.Current.DownloadFolder);
                Process.Start("explorer.exe", "\"" + AppSettings.Current.DownloadFolder + "\"");
            }
        }
        catch { }
    }

    void OpenSelected()
    {
        var j = Selected().FirstOrDefault();
        if (j == null || j.State != JobState.Done || !File.Exists(j.SavePath)) return;
        try { Process.Start(new ProcessStartInfo(j.SavePath) { UseShellExecute = true }); } catch { }
    }

    void OpenSettings()
    {
        using var f = new SettingsForm();
        f.ShowDialog(this);
    }
}
