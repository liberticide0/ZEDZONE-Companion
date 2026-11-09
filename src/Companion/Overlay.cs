namespace Companion;

internal sealed class Overlay : Form
{
    private const string AppTitle = "ZED ZONE Companion";
    private static readonly Color Ink = Color.FromArgb(235, 241, 238);
    private static readonly Color Muted = Color.FromArgb(162, 181, 171);
    private static readonly Color Surface = Color.FromArgb(28, 39, 35);
    private static readonly Color Accent = Color.FromArgb(164, 223, 170);
    private static readonly Category?[] Filters =
        [null, Category.Item, Category.Recipe, Category.Skill, Category.Map, Category.Boss];

    private readonly Catalog catalog = new();
    private readonly TextBox query = new()
    {
        AccessibleName = "검색어", PlaceholderText = "한국어, English, 中文 또는 아이템 ID",
        BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ComboBox filter = new()
    {
        AccessibleName = "분류", DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = Surface, ForeColor = Ink, Width = 112
    };
    private readonly ListBox results = new()
    {
        AccessibleName = "검색 결과", BackColor = Surface, ForeColor = Ink,
        BorderStyle = BorderStyle.None, IntegralHeight = false
    };
    private readonly Label count = Caption("");
    private readonly Label status = Caption("");
    private readonly Label title = Caption("", Ink);
    private readonly Label metadata = Caption("");
    private readonly FlowLayoutPanel calculation = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly NumericUpDown target = new()
    {
        AccessibleName = "목표 제작 수량", Minimum = 1, Maximum = 999, Value = 1, Width = 66,
        BackColor = Surface, ForeColor = Ink
    };
    private readonly RichTextBox details = new()
    {
        AccessibleName = "선택한 항목 설명과 출처", ReadOnly = true, DetectUrls = false,
        BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical
    };

    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private int hotkeyId;
    private string shortcut = "";
    private bool detectingSteam = true;
    private nint previous = Native.GetForegroundWindow();
    private uint previousPid;
    private bool exiting;
    private bool fileDialogOpen;

    public Overlay()
    {
        Text = AppTitle;
        Icon = SystemIcons.Information;
        BackColor = Color.FromArgb(16, 24, 21);
        ForeColor = Ink;
        Font = new Font("Malgun Gothic", 10);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 505);
        MinimumSize = new Size(595, 410);
        TopMost = true;

        menu = new ContextMenuStrip();
        menu.Items.Add("검색 열기", null, (_, _) => OpenPanel());
        menu.Items.Add("종료", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = Icon, Text = AppTitle, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenPanel();

        if (previous != 0)
            Native.GetWindowThreadProcessId(previous, out previousPid);

        BuildLayout();
        filter.SelectedIndexChanged += (_, _) => RefreshResults();
        query.TextChanged += (_, _) => RefreshResults();
        query.KeyDown += (_, e) =>
        {
            if (e.KeyData == Keys.Down && results.Items.Count > 0)
            {
                results.Focus();
                e.SuppressKeyPress = true;
            }
        };
        results.SelectedIndexChanged += (_, _) => ShowDetails();
        target.TextChanged += (_, _) => ShowDetails();
        Shown += async (_, _) =>
        {
            FocusSearch();
            var detected = await Task.Run(() => Catalog.ReadSteamLocalization(SteamInstall.LocalRoots()));
            if (IsDisposed || Disposing)
                return;

            detectingSteam = false;
            if (catalog.UseSteamLocalization(detected))
                RefreshResults();
            UpdateStatus();
        };
        RefreshResults();
        UpdateStatus();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 12), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        var heading = Caption(AppTitle, Ink);
        heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
        root.Controls.Add(heading, 0, 0);

        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        query.Dock = DockStyle.Fill;
        filter.Items.AddRange(["전체", "아이템", "제작법", "스킬", "지도", "보스"]);
        filter.SelectedIndex = 0;
        var import = new Button { Text = "자료 열기", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink };
        import.FlatAppearance.BorderColor = Muted;
        import.Click += (_, _) => Import();
        searchRow.Controls.Add(query, 0, 0);
        searchRow.Controls.Add(filter, 1, 0);
        searchRow.Controls.Add(import, 2, 0);
        root.Controls.Add(searchRow, 0, 1);
        root.Controls.Add(count, 0, 2);

        var panes = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        results.Dock = DockStyle.Fill;
        results.Margin = new Padding(0, 0, 8, 0);
        results.DrawMode = DrawMode.OwnerDrawFixed;
        results.ItemHeight = Font.Height + 14;
        results.DrawItem += DrawResult;
        panes.Controls.Add(results, 0, 0);
        var detailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(13), BackColor = Surface, ColumnCount = 1, RowCount = 4 };
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        title.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        metadata.ForeColor = Accent;
        metadata.Padding = new Padding(0, 0, 0, 9);
        details.Dock = DockStyle.Fill;
        detailPanel.Controls.Add(title, 0, 0);
        detailPanel.Controls.Add(metadata, 0, 1);
        detailPanel.Controls.Add(details, 0, 2);
        calculation.Controls.Add(Caption("목표 수량"));
        calculation.Controls.Add(target);
        detailPanel.Controls.Add(calculation, 0, 3);
        panes.Controls.Add(detailPanel, 1, 0);
        root.Controls.Add(panes, 0, 3);
        root.Controls.Add(status, 0, 4);
    }

    private static Label Caption(string text, Color? color = null) =>
        new() { Text = text, ForeColor = color ?? Muted, AutoSize = true, Anchor = AnchorStyles.Left };

    private void DrawResult(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || results.Items[e.Index] is not Entry entry)
            return;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(49, 74, 62) : Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -9, 0);
        TextRenderer.DrawText(e.Graphics, entry.ToString(), Font,
            bounds, Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    private void RefreshResults()
    {
        var found = catalog.Find(query.Text, Filters[filter.SelectedIndex]);
        results.BeginUpdate();
        try
        {
            results.Items.Clear();
            results.Items.AddRange(found.Cast<object>().ToArray());
            results.SelectedIndex = found.Count == 0 ? -1 : 0;
        }
        finally
        {
            results.EndUpdate();
        }
        count.Text = $"{found.Count:N0}개 결과";
        ShowDetails();
    }

    private void ShowDetails()
    {
        calculation.Visible = results.SelectedItem is Entry { Batch: not null };
        if (results.SelectedItem is not Entry entry)
        {
            title.Text = "검색 결과 없음";
            metadata.Text = "";
            details.Text = "검색어를 줄이거나 분류를 전체로 바꿔 보세요.";
            return;
        }

        title.Text = entry.Title;
        metadata.Text = entry.ItemId is int id
            ? $"{entry.CategoryName}   /   {(entry.InferredId ? "추정 ID" : "ID")} {id}"
            : entry.CategoryName;
        string aliases = string.Join("  ·  ", new[] { entry.English, entry.Chinese, entry.ChineseAlias }
            .Where(name => name.Length > 0).Distinct());
        string estimate = "";
        if (entry.Batch is { } batch)
        {
            if (int.TryParse(target.Text, out int desired) && desired >= target.Minimum && desired <= target.Maximum)
            {
                var plan = batch.Calculate(desired);
                string materials = string.Join(", ", plan.Materials.Select(m => $"{m.Name} {m.PerBatch}개"));
                estimate = $"부분 계산  {plan.Batches}회 제작 → {plan.Output}개 생산\n확인된 재료  {materials}\n\n";
            }
            else
            {
                estimate = $"목표 수량은 {target.Minimum}~{target.Maximum}개로 입력하세요.\n\n";
            }
        }
        details.Text = (aliases.Length == 0 ? "" : $"{aliases}\n\n") +
            $"{estimate}{entry.Details}\n\n출처  {entry.Source}";
        details.Select(0, 0);
        details.ScrollToCaret();
    }

    private void Import()
    {
        fileDialogOpen = true;
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "한국어 현지화 CSV 또는 개발사 영문 아이템 ID 표 선택",
                Filter = "지원하는 자료 (*.csv;*.md)|*.csv;*.md", CheckFileExists = true, RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            catalog.Import(dialog.FileName);
            RefreshResults();
            UpdateStatus();
        }
        catch (Exception error) when (Catalog.IsImportError(error))
        {
            MessageBox.Show(this, $"자료를 열 수 없습니다.\n{error.Message}", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            fileDialogOpen = false;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Native.RegisterHotKey(Handle, 1, Native.CtrlShiftNoRepeat, (uint)Keys.Space))
        {
            hotkeyId = 1;
            shortcut = "Ctrl+Shift+Space";
        }
        else if (Native.RegisterHotKey(Handle, 2, Native.CtrlShiftNoRepeat, (uint)Keys.F10))
        {
            hotkeyId = 2;
            shortcut = "Ctrl+Shift+F10";
        }
        UpdateStatus();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkeyId != 0)
            Native.UnregisterHotKey(Handle, hotkeyId);
        hotkeyId = 0;
        shortcut = "";
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.HotkeyMessage && hotkeyId != 0 && message.WParam == hotkeyId)
        {
            if (!fileDialogOpen)
            {
                if (Visible && Native.GetForegroundWindow() == Handle)
                    HidePanel();
                else
                    OpenPanel();
            }
            return;
        }
        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !filter.DroppedDown)
        {
            HidePanel();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            FocusSearch();
            return true;
        }
        if (keyData == (Keys.Control | Keys.O))
        {
            Import();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HidePanel();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        base.OnFormClosed(e);
    }

    private void UpdateStatus()
    {
        string key = hotkeyId == 0 ? "단축키 충돌 · 알림 영역에서 열기" : shortcut;
        string data = detectingSteam ? "Steam 자료 확인 중" : catalog.DataStatus;
        status.Text = $"{key}   ·   Esc 닫기   ·   {data}";
    }

    private void OpenPanel()
    {
        if (fileDialogOpen)
            return;

        nint foreground = Native.GetForegroundWindow();
        if (foreground != Handle)
        {
            previous = foreground;
            previousPid = 0;
            if (previous != 0)
                Native.GetWindowThreadProcessId(previous, out previousPid);

            if (!Visible)
            {
                var area = Screen.FromHandle(previous).WorkingArea;
                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2),
                    area.Top + Math.Max(0, (area.Height - Height) / 2));
            }
        }
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        FocusSearch();
    }

    private void HidePanel()
    {
        bool focused = Native.GetForegroundWindow() == Handle;
        if (focused && previousPid != 0 && Native.IsWindowVisible(previous))
        {
            Native.GetWindowThreadProcessId(previous, out uint pid);
            if (pid == previousPid)
                Native.SetForegroundWindow(previous);
        }
        Hide();
        previous = 0;
        previousPid = 0;
    }

    private void FocusSearch()
    {
        query.Focus();
        query.SelectAll();
    }

    private void ExitApp()
    {
        exiting = true;
        Close();
    }
}
