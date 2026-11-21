namespace UniDownloader;

public class SettingsForm : Form
{
    readonly AppSettings cfg = AppSettings.Current;
    readonly TextBox folder = new() { ReadOnly = true };
    readonly NumericUpDown conn = new() { Minimum = 1, Maximum = 32 };
    readonly NumericUpDown par = new() { Minimum = 1, Maximum = 10 };
    readonly NumericUpDown speed = new() { Minimum = 0, Maximum = 1000000 };
    readonly CheckBox cat = new() { Text = "تصنيف الملفات تلقائياً في مجلدات حسب النوع", AutoSize = true };
    readonly CheckBox auto = new() { Text = "تشغيل البرنامج مع ويندوز", AutoSize = true };
    readonly CheckBox clip = new() { Text = "التقاط الروابط من الحافظة", AutoSize = true };
    readonly TextBox repo = new();

    public SettingsForm()
    {
        Text = "الإعدادات";
        Width = 560; Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;

        folder.Text = cfg.DownloadFolder;
        conn.Value = Math.Clamp(cfg.Connections, 1, 32);
        par.Value = Math.Clamp(cfg.MaxParallel, 1, 10);
        speed.Value = Math.Clamp(cfg.SpeedLimitKB, 0, 1000000);
        cat.Checked = cfg.Categorize; auto.Checked = cfg.StartWithWindows; clip.Checked = cfg.WatchClipboard;
        repo.Text = cfg.UpdateRepo;

        folder.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = folder.Text };
            if (d.ShowDialog() == DialogResult.OK) folder.Text = d.SelectedPath;
        };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        void Row(string label, Control c)
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            int r = t.RowCount - 1;
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(4, 8, 4, 4) }, 0, r);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            c.Margin = new Padding(4);
            t.Controls.Add(c, 1, r);
        }
        void Wide(Control c)
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            int r = t.RowCount - 1;
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
            c.Margin = new Padding(4);
        }

        Row("مجلد التحميل (انقر للتغيير)", folder);
        Row("عدد الاتصالات للملف الواحد (1-32)", conn);
        Row("أقصى عدد تحميلات متزامنة", par);
        Row("تحديد السرعة بالكيلوبايت/ث (0 = بلا حد)", speed);
        Row("مستودع التحديث (اسم/UniDownloader)", repo);
        Wide(cat); Wide(auto); Wide(clip);

        var bExt = new Button { Text = "تثبيت إضافة كروم / إيدج", AutoSize = true };
        bExt.Click += (s, e) => Setup.InstallExtension();
        var bYt = new Button { Text = "تحديث yt-dlp الآن", AutoSize = true };
        bYt.Click += async (s, e) =>
        {
            bYt.Enabled = false;
            bool ok = await VideoEngine.ForceUpdateAsync();
            bYt.Enabled = true;
            MessageBox.Show(ok ? "تم تحديث/تجهيز yt-dlp." : "تعذر التحديث، تحقق من الاتصال.", "UniDownloader");
        };
        var bUp = new Button { Text = "البحث عن تحديث للبرنامج", AutoSize = true };
        bUp.Click += async (s, e) => { cfg.UpdateRepo = repo.Text.Trim(); await Updater.CheckAsync(true); };
        var fl = new FlowLayoutPanel { AutoSize = true, RightToLeft = RightToLeft.Yes, WrapContents = true };
        fl.Controls.Add(bExt); fl.Controls.Add(bYt); fl.Controls.Add(bUp);
        Wide(fl);

        var ok2 = new Button { Text = "حفظ", DialogResult = DialogResult.OK, Width = 110, Height = 32 };
        ok2.Click += (s, e) => Save();
        var cancel = new Button { Text = "إلغاء", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10), RightToLeft = RightToLeft.Yes };
        bottom.Controls.Add(ok2); bottom.Controls.Add(cancel);
        AcceptButton = ok2; CancelButton = cancel;

        Controls.Add(t);
        Controls.Add(bottom);
    }

    void Save()
    {
        cfg.DownloadFolder = folder.Text;
        cfg.Connections = (int)conn.Value;
        cfg.MaxParallel = (int)par.Value;
        cfg.SpeedLimitKB = (int)speed.Value;
        cfg.Categorize = cat.Checked;
        cfg.StartWithWindows = auto.Checked;
        cfg.WatchClipboard = clip.Checked;
        cfg.UpdateRepo = repo.Text.Trim();
        cfg.Save();
        Setup.SetAutoStart(cfg.StartWithWindows);
    }
}
