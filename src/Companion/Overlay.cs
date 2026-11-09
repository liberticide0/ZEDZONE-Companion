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
    private readonly Button wishlistToggle = ActionButton("찜목록");
    private readonly Button sizeToggle = ActionButton("작게");
    private readonly Button saveGoal = ActionButton("목표 저장");
    private readonly Panel side = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly ListBox goals = new()
    {
        AccessibleName = "찜목록 목표", BackColor = Surface, ForeColor = Ink,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false
    };
    private readonly RichTextBox planText = new()
    {
        AccessibleName = "찜목록 합산 제작 계획", BackColor = Surface, ForeColor = Ink,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ReadOnly = true, DetectUrls = false
    };
    private Control detailView = null!;
    private Control wishlistView = null!;
    private WishlistState wishlist = WishlistState.Empty;
    private bool bindingRecipes;
    private bool wishlistUnreadable;
    private bool showingWishlist;
    private bool compact;
    private readonly ComboBox recipeSelector = new()
    {
        AccessibleName = "제작법 선택", DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Ink, Visible = false
    };
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
        ClientSize = new Size(880, 570);
        MinimumSize = new Size(595, 410);
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.96;
        TopMost = true;

        menu = new ContextMenuStrip();
        menu.Items.Add("검색 열기", null, (_, _) => OpenPanel());
        menu.Items.Add("종료", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = Icon, Text = AppTitle, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenPanel();

        if (previous != 0)
            Native.GetWindowThreadProcessId(previous, out previousPid);

        BuildLayout();
        try
        {
            wishlist = WishlistStore.Load(WishlistStore.PathForUser);
        }
        catch (Exception error) when (Catalog.IsImportError(error) || error is System.Text.Json.JsonException)
        {
            wishlistUnreadable = true;
        }
        wishlistToggle.Click += (_, _) => ToggleWishlist();
        saveGoal.Click += (_, _) => SaveGoal();
        goals.DoubleClick += (_, _) => OpenGoal();
        goals.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenGoal();
                e.SuppressKeyPress = true;
            }
        };
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
        results.SelectedIndexChanged += (_, _) => SelectEntry();
        recipeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!bindingRecipes && recipeSelector.SelectedIndex > 0 &&
                results.SelectedItem is Entry { ItemId: int id })
            {
                var preference = new RecipePreference(id, catalog.RecipeBuild, recipeSelector.SelectedIndex - 1);
                if (!wishlist.Preferences.Contains(preference))
                    SaveWishlist(wishlist with { Preferences = wishlist.Preferences.Where(choice => choice.Id != id)
                        .Append(preference).ToArray() });
            }
            ShowDetails();
        };
        target.TextChanged += (_, _) => ShowDetails();
        Shown += async (_, _) =>
        {
            FocusSearch();
            var (detected, recipes) = await Task.Run(() =>
            {
                var localization = Catalog.ReadSteamLocalization(SteamInstall.LocalRoots());
                return (localization, localization is { } data ? RecipeSnapshot.ReadInstalled(data.Build) : null);
            });
            if (IsDisposed || Disposing)
                return;

            detectingSteam = false;
            if (catalog.UseSteamLocalization(detected, recipes))
                RefreshResults();
            RefreshWishlist();
            UpdateStatus();
        };
        RefreshResults();
        RefreshWishlist();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = Caption(AppTitle, Ink);
        heading.Dock = DockStyle.Fill;
        heading.TextAlign = ContentAlignment.MiddleLeft;
        heading.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
        heading.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Native.DragWindow(Handle);
        };
        var close = ActionButton("×");
        close.AccessibleName = "오버레이 닫기";
        close.Click += (_, _) => HidePanel();
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(wishlistToggle, 1, 0);
        header.Controls.Add(close, 2, 0);
        root.Controls.Add(header, 0, 0);

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
        var detailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(13), BackColor = Surface, ColumnCount = 1, RowCount = 5 };
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        title.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        title.AutoSize = false;
        title.AutoEllipsis = true;
        title.Height = 30;
        title.Dock = DockStyle.Fill;
        metadata.ForeColor = Accent;
        metadata.Padding = new Padding(0, 0, 0, 9);
        details.Dock = DockStyle.Fill;
        detailPanel.Controls.Add(title, 0, 0);
        detailPanel.Controls.Add(metadata, 0, 1);
        detailPanel.Controls.Add(recipeSelector, 0, 2);
        detailPanel.Controls.Add(details, 0, 3);
        calculation.Controls.Add(Caption("목표 수량"));
        calculation.Controls.Add(target);
        calculation.Controls.Add(saveGoal);
        detailPanel.Controls.Add(calculation, 0, 4);
        detailView = detailPanel;
        wishlistView = BuildWishlist();
        side.Controls.Add(detailView);
        side.Controls.Add(wishlistView);
        wishlistView.Hide();
        panes.Controls.Add(side, 1, 0);
        root.Controls.Add(panes, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize = false;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        footer.Controls.Add(status, 0, 0);
        sizeToggle.AccessibleName = "창 크기 전환";
        sizeToggle.Margin = new Padding(3, 0, 0, 0);
        sizeToggle.Click += (_, _) => ToggleSize();
        footer.Controls.Add(sizeToggle, 1, 0);
        root.Controls.Add(footer, 0, 4);
    }

    private static Label Caption(string text, Color? color = null) =>
        new() { Text = text, ForeColor = color ?? Muted, AutoSize = true, Anchor = AnchorStyles.Left };

    private static Button ActionButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Surface, ForeColor = Ink, Anchor = AnchorStyles.Left };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 94, 80);
        return button;
    }

    private void ToggleSize()
    {
        compact = !compact;
        var area = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(LogicalToDeviceUnits(compact ? 650 : 880), area.Width);
        int height = Math.Min(LogicalToDeviceUnits(compact ? 460 : 570), area.Height);
        Size = new Size(width, height);
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        sizeToggle.Text = compact ? "크게" : "작게";
    }

    private Control BuildWishlist()
    {
        var view = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(13), BackColor = Surface, ColumnCount = 1, RowCount = 4
        };
        view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        view.Controls.Add(Caption("제작 목표", Accent), 0, 0);
        view.Controls.Add(goals, 0, 1);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var edit = ActionButton("상세 열기");
        var remove = ActionButton("목표 제거");
        edit.Click += (_, _) => OpenGoal();
        remove.Click += (_, _) =>
        {
            if (goals.SelectedItem is GoalRow row)
                SaveWishlist(wishlist with { Goals = wishlist.Goals.Where(goal => goal.Id != row.Goal.Id).ToArray() });
        };
        actions.Controls.Add(edit);
        actions.Controls.Add(remove);
        view.Controls.Add(actions, 0, 2);
        view.Controls.Add(planText, 0, 3);
        return view;
    }

    private sealed record GoalRow(CraftGoal Goal)
    {
        public override string ToString() => $"{Goal.Name}  × {Goal.Quantity:N0}";
    }

    private void ToggleWishlist()
    {
        showingWishlist = !showingWishlist;
        detailView.Visible = !showingWishlist;
        wishlistView.Visible = showingWishlist;
        if (showingWishlist)
        {
            wishlistView.BringToFront();
            RefreshWishlist();
            goals.Focus();
        }
        else
        {
            detailView.BringToFront();
            RefreshWishlist();
            FocusSearch();
        }
    }

    private void OpenGoal()
    {
        if (goals.SelectedItem is not GoalRow row)
            return;
        filter.SelectedIndex = 1;
        query.Text = row.Goal.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (showingWishlist)
            ToggleWishlist();
    }

    private void SaveGoal()
    {
        if (results.SelectedItem is not Entry { Category: Category.Item, ItemId: int id } entry ||
            !int.TryParse(target.Text, out int amount) || amount is < 1 or > 999)
            return;

        if (SaveWishlist(wishlist with { Goals = wishlist.Goals.Where(goal => goal.Id != id)
            .Append(new CraftGoal(id, entry.Title, amount)).ToArray() }))
            saveGoal.Text = "저장됨";
    }

    private bool SaveWishlist(WishlistState candidate)
    {
        if (wishlistUnreadable)
        {
            status.Text = "찜목록을 읽을 수 없어 기존 파일을 보존했습니다.";
            return false;
        }
        try
        {
            WishlistStore.Save(WishlistStore.PathForUser, candidate);
            wishlist = candidate;
            RefreshWishlist();
            return true;
        }
        catch (Exception error) when (Catalog.IsImportError(error))
        {
            status.Text = "찜목록 저장 실패 · 기존 목표를 유지합니다.";
            return false;
        }
    }

    private void RefreshWishlist()
    {
        wishlistToggle.Text = showingWishlist ? $"상세로 · 목표 {wishlist.Goals.Length}" : $"찜목록 {wishlist.Goals.Length}";
        if (!showingWishlist)
            return;
        int? selected = (goals.SelectedItem as GoalRow)?.Goal.Id;
        goals.Items.Clear();
        goals.Items.AddRange(wishlist.Goals.Select(goal => (object)new GoalRow(goal)).ToArray());
        if (goals.Items.Count > 0)
        {
            int index = Array.FindIndex(wishlist.Goals, goal => goal.Id == selected);
            goals.SelectedIndex = Math.Max(0, index);
        }

        try
        {
            var plan = CraftPlanner.Calculate(wishlist, catalog.RecipeBuild, catalog.Item);
            var lines = new List<string>();
            if (plan.Materials.Length > 0)
            {
                lines.Add("직접 확보할 재료");
                lines.AddRange(plan.Materials.Select(m => $"  {m.Name}  × {m.Quantity:0.######}"));
            }
            if (plan.Tools.Length > 0)
            {
                lines.Add("\n필요 도구");
                lines.AddRange(plan.Tools.Select(m => $"  {m.Name}  × {m.Quantity:0.######}"));
            }
            if (plan.Steps.Length > 0)
            {
                lines.Add("\n제작 순서");
                lines.AddRange(plan.Steps.Select(s => $"  {s.Name} · {s.Station} · {s.Batches:N0}회 → {s.Output:N0}개"));
            }
            if (plan.Warnings.Length > 0)
            {
                lines.Add("\n확인이 필요한 항목");
                lines.AddRange(plan.Warnings.Select(warning => "  " + warning));
            }
            planText.Text = lines.Count == 0 ? "아이템 상세에서 목표 수량을 입력하고 저장하세요." :
                string.Join(Environment.NewLine, lines);
        }
        catch (OverflowException)
        {
            planText.Text = "필요 재료 수가 계산 범위를 넘습니다. 목표 수량을 줄여 주세요.";
        }
    }
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
        SelectEntry();
    }

    private void SelectEntry()
    {
        bindingRecipes = true;
        try
        {
            recipeSelector.Items.Clear();
            int? id = (results.SelectedItem as Entry)?.ItemId;
            target.Value = wishlist.Goals.FirstOrDefault(goal => goal.Id == id)?.Quantity ?? 1;
            saveGoal.Text = wishlist.Goals.Any(goal => goal.Id == id) ? "목표 수정" : "목표 저장";
            if (results.SelectedItem is Entry { Recipes: { Length: > 0 } choices } entry)
            {
                recipeSelector.Items.Add("아이템 설명");
                recipeSelector.Items.AddRange(choices.Select(choice => (object)choice.Label).ToArray());
                int index = CraftPlanner.ChoiceIndex(entry, catalog.RecipeBuild, wishlist.Preferences);
                recipeSelector.SelectedIndex = filter.SelectedIndex == 2 ||
                    wishlist.Preferences.Any(choice => choice.Id == id && choice.Build == catalog.RecipeBuild)
                    ? index + 1 : 0;
                recipeSelector.Visible = true;
            }
            else
            {
                recipeSelector.Visible = false;
            }
        }
        finally
        {
            bindingRecipes = false;
        }
        ShowDetails();
    }

    private void ShowDetails()
    {
        if (bindingRecipes)
            return;
        if (results.SelectedItem is not Entry entry)
        {
            calculation.Visible = false;
            title.Text = "검색 결과 없음";
            metadata.Text = "";
            details.Text = "검색어를 줄이거나 분류를 전체로 바꿔 보세요.";
            return;
        }

        int choiceIndex = recipeSelector.SelectedIndex - 1;
        var choice = entry.Recipes is { } choices && choiceIndex >= 0 && choiceIndex < choices.Length
            ? choices[choiceIndex] : null;
        var batch = choice is null ? entry.Batch : choice.Batch;
        bool canSaveGoal = entry.Category == Category.Item && entry.ItemId.HasValue;
        calculation.Visible = canSaveGoal || batch is not null;
        saveGoal.Visible = canSaveGoal;
        title.Text = entry.Title;
        metadata.Text = entry.ItemId is int id
            ? $"{entry.CategoryName}   /   {(entry.InferredId ? "추정 ID" : "ID")} {id}"
            : entry.CategoryName;
        string aliases = string.Join("  ·  ", new[] { entry.English, entry.Chinese, entry.ChineseAlias }
            .Where(name => name.Length > 0).Distinct());
        string estimate = "";
        if (batch is not null)
        {
            if (int.TryParse(target.Text, out int desired) && desired >= target.Minimum && desired <= target.Maximum)
            {
                var plan = batch.Calculate(desired);
                string materials = string.Join(", ", plan.Materials.Select(m => $"{m.Name} {m.PerBatch:0.######}개"));
                string label = batch.Partial ? "부분 계산" : "목표 계산";
                estimate = $"{label}  {plan.Batches}회 제작 → {plan.Output}개 생산\n확인된 재료  {materials}\n\n";
            }
            else
            {
                estimate = $"목표 수량은 {target.Minimum}~{target.Maximum}개로 입력하세요.\n\n";
            }
        }
        details.Text = (aliases.Length == 0 ? "" : $"{aliases}\n\n") +
            (choice is null
                ? $"{entry.Details}\n\n출처  {entry.Source}"
                : $"{estimate}{choice.Details}\n\n제작법 출처  {choice.Source}\n\n아이템 정보\n{entry.Details}\n\n출처  {entry.Source}");
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
            RefreshWishlist();
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
        if (keyData == Keys.Escape && !filter.DroppedDown && !recipeSelector.DroppedDown)
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
        if (keyData == (Keys.Control | Keys.R) && recipeSelector.Visible)
        {
            recipeSelector.Focus();
            return true;
        }
        if (keyData == (Keys.Control | Keys.L))
        {
            ToggleWishlist();
            return true;
        }
        if (keyData == (Keys.Control | Keys.D) && !showingWishlist)
        {
            SaveGoal();
            return true;
        }
        if (keyData == (Keys.Control | Keys.OemMinus))
        {
            ToggleSize();
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
        status.Text = wishlistUnreadable ? "찜목록 파일 읽기 실패 · 기존 파일을 보존합니다." :
            $"{key}   ·   Esc 닫기   ·   {data}";
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
                if (Width > area.Width || Height > area.Height)
                    Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                if (!area.Contains(Bounds))
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
