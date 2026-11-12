namespace Companion;

internal sealed class Overlay : Form
{
    private static readonly string AppTitle = "ZED ZONE Companion" + UpdateFixture.TitleSuffix
#if LOCAL_BUILD
        + " " + ((System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            typeof(Overlay).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute)))
            ?.InformationalVersion.Split('+')[0]
#endif
        ;
    private const string PrepareLabel = "Steam 제작법 불러오기";
    private const string ImportLabel = "자료 파일 가져오기";
    private const string PrepareHint = "설치된 ZED ZONE에서 제작법을 읽습니다. Steam 업데이트 후 다시 실행하세요.";
    private const string ImportHint = "한국어 현지화 CSV 또는 아이템 ID 표를 직접 선택합니다.";
    private static readonly Color Ink = Color.FromArgb(235, 241, 238);
    private static readonly Color Muted = Color.FromArgb(162, 181, 171);
    private static readonly Color Surface = Color.FromArgb(28, 39, 35);
    private static readonly Color Accent = Color.FromArgb(164, 223, 170);
    private static readonly Category?[] Filters =
        [null, Category.Item, Category.Recipe, Category.Skill, Category.Map, Category.Boss, Category.Construction,
            Category.Research, Category.Effect, Category.Trait];

    private readonly Catalog catalog = new();
    private readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 500 };
    private bool settingSearch;
    private bool bindingResults;
    private readonly SearchTextBox query = new()
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
    private readonly PictureBox itemIcon = new()
    {
        Size = new Size(42, 42), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 8, 0),
        Visible = false
    };
    private string? iconPath;
    private readonly Label metadata = Caption("");
    private readonly Button wishlistToggle = ActionButton("찜목록");
    private readonly Button sizeToggle = ActionButton("작게");
    private readonly Button saveGoal = ActionButton("목표 저장");
    private readonly Button recipeUsesToggle = ActionButton("사용되는 제작법");
    private readonly Button prepareRecipes = ActionButton(PrepareLabel);
    private readonly Button checkUpdate = ActionButton("업데이트 확인");
    private readonly UpdatePopup updatePopup;
    private TableLayoutPanel content = null!;
    private readonly ToolTip hints = new() { ShowAlways = true };
    private readonly CancellationTokenSource preparationLifetime = new();
    private readonly Panel side = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly ListBox goals = new()
    {
        AccessibleName = "찜목록 목표", BackColor = Surface, ForeColor = Ink,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false
    };
    private readonly RichTextBox planText = new()
    {
        AccessibleName = "찜목록 합산 제작 계획", BackColor = Surface, ForeColor = Ink,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ReadOnly = true, DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };
    private readonly ListView materialChecklist = new()
    {
        AccessibleName = "직접 확보할 재료 체크리스트", BackColor = Surface, ForeColor = Ink,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, CheckBoxes = true,
        FullRowSelect = true, MultiSelect = false, View = View.Details, HeaderStyle = ColumnHeaderStyle.None
    };
    private readonly RowStyle materialRow = new(SizeType.Percent, 42);
    private readonly Font collectedFont;
    private Control detailView = null!;
    private Control wishlistView = null!;
    private WishlistState wishlist = WishlistState.Empty;
    private bool bindingRecipes;
    private bool showingRecipeUses;
    private IReadOnlyList<RecipeUse> recipeUses = [];
    private bool wishlistUnreadable;
    private bool showingWishlist;
    private bool bindingMaterials;
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
    private readonly Panel detailBody = new() { Dock = DockStyle.Fill };
    private readonly ComboBox researchLinks = new()
    {
        AccessibleName = "연구 경로 이동", DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Ink, Visible = false
    };
    private readonly FlowLayoutPanel recipeUseLinks = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Visible = false, AccessibleName = "사용되는 제작법"
    };

    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private int hotkeyId;
    private string shortcut = "";
    private bool detectingSteam = true;
    private bool preparingRecipes;
    private bool updating;
    private string? updateStatus;
    private string? steamGameDirectory;
    private string? preparationStatus;
    private nint previous = Native.GetForegroundWindow();
    private uint previousPid;
    private bool exiting;
    private bool fileDialogOpen;

    public Overlay(string? updateWarning = null)
    {
        Text = AppTitle;
        Icon = SystemIcons.Information;
        BackColor = Color.FromArgb(16, 24, 21);
        ForeColor = Ink;
        Font = new Font("Malgun Gothic", 10);
        updatePopup = new UpdatePopup(Font, Surface, Ink, Accent, ActionButton(""), ActionButton(""));
        collectedFont = new Font(Font, FontStyle.Strikeout);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(880, 570);
        MinimumSize = new Size(595, 410);
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.98;
        TopMost = true;

        menu = new ContextMenuStrip();
        menu.Items.Add("검색 열기", null, (_, _) => OpenPanel());
        if (UpdateWorker.IsPortable)
            menu.Items.Add("업데이트 확인", null, async (_, _) =>
            {
                OpenPanel();
                await CheckUpdateAsync();
            });
        menu.Items.Add("종료", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = Icon, Text = AppTitle, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenPanel();

        if (previous != 0)
            Native.GetWindowThreadProcessId(previous, out previousPid);

        BuildLayout();
        Controls.Add(updatePopup);
        ClientSizeChanged += (_, _) => updatePopup.Recenter();
        DpiChanged += (_, _) => updatePopup.Recenter();
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
        recipeUsesToggle.Click += (_, _) =>
        {
            showingRecipeUses = !showingRecipeUses;
            ShowDetails();
        };
        prepareRecipes.Visible = RecipePreparation.IsAvailable;
        prepareRecipes.Enabled = false;
        prepareRecipes.Click += async (_, _) => await PrepareRecipesAsync();
        checkUpdate.Visible = UpdateWorker.IsPortable;
        checkUpdate.Click += async (_, _) => await CheckUpdateAsync();
        goals.DoubleClick += (_, _) => OpenGoal();
        goals.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenGoal();
                e.SuppressKeyPress = true;
            }
        };
        filter.SelectedIndexChanged += (_, _) =>
        {
            if (!settingSearch)
                RefreshResults();
        };
        query.TextChanged += (_, _) => QueueSearch();
        query.CompositionEnded += (_, _) => QueueSearch();
        searchDelay.Tick += (_, _) => RefreshResults();
        query.KeyDown += (_, e) =>
        {
            if (!query.IsComposing && e.KeyData == Keys.Down && results.Items.Count > 0)
            {
                results.Focus();
                e.SuppressKeyPress = true;
            }
        };
        results.SelectedIndexChanged += (_, _) =>
        {
            if (!bindingResults)
                SelectEntry();
        };
        researchLinks.SelectionChangeCommitted += (_, _) =>
        {
            if (researchLinks.SelectedItem is RelatedEntry link)
                OpenEntry(link.Entry.Category, link.Entry.ItemId ?? link.Entry.GuideId, link.Entry.Title);
        };
        recipeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!bindingRecipes)
                showingRecipeUses = false;
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
            if (updateWarning is not null)
                ShowUpdatePopup("업데이트 안내", updateWarning);
            var (detected, recipes, game) = await Task.Run(ReadInstalledData);
            if (IsDisposed || Disposing)
                return;

            detectingSteam = false;
            steamGameDirectory = game;
            prepareRecipes.Enabled = !updating && game is not null;
            if (catalog.UseSteamLocalization(detected, recipes))
                RefreshResults();
            RefreshWishlist();
            UpdateStatus();
        };
        RefreshResults();
        RefreshWishlist();
        UpdateStatus();
    }

    private static ((List<Entry> Entries, string Build)? Localization, RecipeSnapshot? Recipes, string? Game)
        ReadInstalledData()
    {
        var localization = Catalog.ReadSteamLocalization(SteamInstall.LocalRoots(), out string? game);
        return (localization, localization is { } data ? RecipeSnapshot.ReadInstalled(data.Build) : null,
            localization is { Build.Length: > 0 } ? game : null);
    }

    private async Task PrepareRecipesAsync()
    {
        if (updating || preparingRecipes || steamGameDirectory is not { } game)
            return;
        preparingRecipes = true;
        prepareRecipes.Enabled = false;
        preparationStatus = "Steam 제작법을 불러오는 중…";
        UpdateStatus();
        try
        {
            int exit = await RecipePreparation.RunAsync(game, allowDownload: false, preparationLifetime.Token);
            if (exit == RecipePreparation.PackagesRequired)
            {
                if (!Visible)
                {
                    preparationStatus = $"변환 도구가 필요합니다. {PrepareLabel}를 다시 선택하세요.";
                    return;
                }
                fileDialogOpen = true;
                DialogResult choice;
                try
                {
                    choice = MessageBox.Show(this,
                        "이 PC에 변환 도구가 없습니다. 최초 한 번 PyPI 배포처에서 약 17MB를 내려받아 해시를 확인합니다. 내려받을까요?",
                        AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                }
                finally
                {
                    fileDialogOpen = false;
                }
                if (choice != DialogResult.Yes)
                {
                    preparationStatus = "다운로드를 건너뛰었습니다.";
                    return;
                }
                exit = await RecipePreparation.RunAsync(game, allowDownload: true, preparationLifetime.Token);
            }
            if (exit != 0)
            {
                preparationStatus = "제작법을 불러오지 못했습니다. 기존 자료를 유지합니다.";
                return;
            }
            var (localization, recipes, currentGame) = await Task.Run(ReadInstalledData);
            if (IsDisposed || Disposing)
                return;
            if (recipes is null)
            {
                preparationStatus = "현재 Steam 빌드의 변환 자료를 열 수 없습니다.";
                return;
            }
            steamGameDirectory = currentGame;
            catalog.UseSteamLocalization(localization, recipes);
            RefreshResults();
            RefreshWishlist();
            preparationStatus = null;
            if (!Directory.Exists(ItemIconCache.DirectoryFor(recipes.Build)))
            {
                preparationStatus = "제작법 준비됨 · 아이콘 준비 중…";
                UpdateStatus();
                try
                {
                    await RecipePreparation.RunAsync(game, allowDownload: false, preparationLifetime.Token, icons: true);
                }
                catch (Exception error) when (Catalog.IsImportError(error) || error is System.ComponentModel.Win32Exception
                    or TimeoutException or InvalidOperationException)
                {
                    // Recipe data has already been loaded, so icons can fail independently.
                }
                if (!IsDisposed && !Disposing)
                {
                    preparationStatus = null;
                    iconPath = null;
                    ShowItemIcon(results.SelectedItem as Entry);
                }
            }
        }
        catch (OperationCanceledException) when (preparationLifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (Catalog.IsImportError(error) || error is System.ComponentModel.Win32Exception
            or TimeoutException or InvalidOperationException)
        {
            preparationStatus = "제작법을 불러오지 못했습니다. 기존 자료를 유지합니다.";
        }
        finally
        {
            preparingRecipes = false;
            if (!IsDisposed && !Disposing)
            {
                prepareRecipes.Enabled = steamGameDirectory is not null;
                UpdateStatus();
            }
        }
    }

    private async Task CheckUpdateAsync()
    {
        if (updating || fileDialogOpen || updatePopup.Visible)
            return;
        if (preparingRecipes)
        {
            ShowUpdatePopup("제작법을 불러오는 중", "제작법 준비가 끝난 뒤 업데이트를 확인해 주세요.");
            return;
        }
        Control? origin = ActiveControl;
        updating = true;
        checkUpdate.Enabled = false;
        prepareRecipes.Enabled = false;
        updateStatus = "업데이트 확인 중…";
        UpdateStatus();
        try
        {
            var installed = typeof(Overlay).Assembly.GetName().Version!;
            var current = new Version(installed.Major, installed.Minor, installed.Build);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(preparationLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var release = await UpdateRelease.CheckAsync(current, timeout.Token);
            if (IsDisposed || Disposing || !Visible || fileDialogOpen)
                return;
            if (release is null)
            {
                await AskUpdatePopupAsync("새 업데이트 없음",
                    $"현재 버전  v{current}\n새 정식 업데이트가 없습니다.");
                return;
            }
            if (!await AskUpdatePopupAsync("새 Companion 버전",
                $"현재 버전  v{current}\n새 버전      v{release.Version}\n\n패키지를 확인한 뒤 같은 폴더에서 Companion을 다시 시작합니다.",
                "업데이트 후 다시 시작", "나중에"))
                return;
            using var download = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            ShowUpdatePopup("업데이트 준비 중", $"v{release.Version} 패키지를 내려받고 확인하는 중입니다.",
                cancel: "취소", completed: _ => download.Cancel());
            try
            {
                await UpdateWorker.HandOffAsync(release, download.Token);
                ExitApp();
            }
            catch (OperationCanceledException) when (download.IsCancellationRequested && !timeout.IsCancellationRequested) { }
            finally
            {
                if (!IsDisposed && !Disposing && updatePopup.Visible)
                    updatePopup.Close(false);
            }
        }
        catch (Exception error) when (UpdateWorker.IsUpdateError(error))
        {
            if (!IsDisposed && !Disposing && Visible)
            {
                string detail = error switch
                {
                    UnauthorizedAccessException => "이 설치 폴더에 쓸 수 없습니다. ZIP을 쓰기 가능한 사용자 폴더에 풀어 주세요.",
                    HttpRequestException or OperationCanceledException => "GitHub 연결을 확인한 뒤 다시 시도해 주세요.",
                    InvalidDataException => error.Message,
                    _ => "패키지 파일이나 설치 폴더를 확인할 수 없습니다."
                };
                await AskUpdatePopupAsync("업데이트를 준비하지 못했습니다",
                    $"{detail}\n\n현재 버전으로 계속 사용할 수 있습니다.");
            }
        }
        finally
        {
            updating = false;
            updateStatus = null;
            if (!IsDisposed && !Disposing)
            {
                checkUpdate.Enabled = true;
                prepareRecipes.Enabled = steamGameDirectory is not null;
                UpdateStatus();
                if (origin?.CanFocus == true)
                    origin.Focus();
            }
        }
    }

    private Task<bool> AskUpdatePopupAsync(string title, string message, string? accept = null, string cancel = "확인")
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ShowUpdatePopup(title, message, accept, cancel, accepted => answer.SetResult(accepted));
        return answer.Task;
    }

    private void ShowUpdatePopup(string title, string message, string? accept = null, string cancel = "확인",
        Action<bool>? completed = null)
    {
        Control? previousControl = ActiveControl;
        // These native controls otherwise paint a light system background while disabled.
        detailBody.Hide();
        planText.Hide();
        materialChecklist.Hide();
        content.Enabled = false;
        updatePopup.Open(title, message, accept, cancel, accepted =>
        {
            if (!exiting && !IsDisposed && !Disposing)
            {
                content.Enabled = true;
                detailBody.Show();
                planText.Show();
                materialChecklist.Show();
                if (previousControl?.CanFocus == true)
                    previousControl.Focus();
                else
                    query.Focus();
            }
            completed?.Invoke(accepted);
        });
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
        content = root;

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

        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        query.Dock = DockStyle.Fill;
        filter.Items.AddRange(["전체", "아이템", "제작법", "스킬", "지도", "보스", "건설", "연구", "상태 효과", "특성"]);
        filter.SelectedIndex = 0;
        var import = ActionButton(ImportLabel);
        import.AccessibleDescription = ImportHint;
        prepareRecipes.AccessibleDescription = PrepareHint;
        hints.SetToolTip(import, ImportHint);
        hints.SetToolTip(prepareRecipes, PrepareHint);
        import.Click += (_, _) => Import();
        searchRow.Controls.Add(query, 0, 0);
        searchRow.Controls.Add(filter, 1, 0);
        searchRow.Controls.Add(import, 2, 0);
        searchRow.Controls.Add(prepareRecipes, 3, 0);
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
        results.Resize += (_, _) => results.Invalidate();
        panes.Controls.Add(results, 0, 0);
        var detailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(13), BackColor = Surface, ColumnCount = 1, RowCount = 6 };
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        var titleRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.Controls.Add(itemIcon, 0, 0);
        titleRow.Controls.Add(title, 1, 0);
        title.TextAlign = ContentAlignment.MiddleLeft;
        detailPanel.Controls.Add(titleRow, 0, 0);
        detailPanel.Controls.Add(metadata, 0, 1);
        var detailActions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        detailActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detailActions.Controls.Add(recipeSelector, 0, 0);
        detailActions.Controls.Add(recipeUsesToggle, 1, 0);
        detailPanel.Controls.Add(detailActions, 0, 2);
        detailBody.Controls.Add(details);
        detailBody.Controls.Add(recipeUseLinks);
        recipeUseLinks.SizeChanged += (_, _) => FitRecipeUseLinks();
        detailPanel.Controls.Add(researchLinks, 0, 3);
        detailPanel.Controls.Add(detailBody, 0, 4);
        calculation.Controls.Add(Caption("목표 수량"));
        calculation.Controls.Add(target);
        calculation.Controls.Add(saveGoal);
        detailPanel.Controls.Add(calculation, 0, 5);
        detailView = detailPanel;
        wishlistView = BuildWishlist();
        side.Controls.Add(detailView);
        side.Controls.Add(wishlistView);
        wishlistView.Hide();
        panes.Controls.Add(side, 1, 0);
        root.Controls.Add(panes, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize = false;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        footer.Controls.Add(status, 0, 0);
        sizeToggle.AccessibleName = "창 크기 전환";
        sizeToggle.Margin = new Padding(3, 0, 0, 0);
        sizeToggle.Click += (_, _) => ToggleSize();
        var help = ActionButton("단축키 · 도움말");
        help.Margin = new Padding(3, 0, 0, 0);
        hints.SetToolTip(help, "단축키와 자료 버튼 안내 (F1)");
        help.Click += (_, _) => ShowHelpGuide();
        checkUpdate.Margin = new Padding(3, 0, 0, 0);
        footer.Controls.Add(checkUpdate, 1, 0);
        footer.Controls.Add(help, 2, 0);
        footer.Controls.Add(sizeToggle, 3, 0);
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
            Dock = DockStyle.Fill, Padding = new Padding(13), BackColor = Surface, ColumnCount = 1, RowCount = 5
        };
        view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        view.RowStyles.Add(materialRow);
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
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
        var materials = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        materials.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        materials.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        materials.Controls.Add(Caption("직접 확보할 재료 · 모으면 체크", Accent), 0, 0);
        materialChecklist.Columns.Add("재료");
        materialChecklist.Resize += (_, _) => materialChecklist.Columns[0].Width =
            Math.Max(0, materialChecklist.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        materialChecklist.ItemCheck += CheckMaterial;
        materials.Controls.Add(materialChecklist, 0, 1);
        view.Controls.Add(materials, 0, 3);
        view.Controls.Add(planText, 0, 4);
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
        OpenItem(row.Goal.Id, row.Goal.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    private bool SaveWishlist(WishlistState candidate, bool refresh = true)
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
            if (refresh)
                RefreshWishlist();
            return true;
        }
        catch (Exception error) when (Catalog.IsImportError(error))
        {
            status.Text = "찜목록 저장 실패 · 기존 목표를 유지합니다.";
            return false;
        }
    }

    private void CheckMaterial(object? sender, ItemCheckEventArgs e)
    {
        if (bindingMaterials)
            return;
        var row = materialChecklist.Items[e.Index];
        var key = (MaterialCheck)row.Tag!;
        bool collected = e.NewValue == CheckState.Checked;
        var checks = (wishlist.Collected ?? []).Where(check => check.Build == key.Build &&
            (check.Id != key.Id || check.Name != key.Name));
        if (!SaveWishlist(wishlist with { Collected = collected ? [.. checks, key] : checks.ToArray() }, refresh: false))
        {
            e.NewValue = e.CurrentValue;
            return;
        }
        row.Font = collected ? collectedFont : Font;
        row.ForeColor = collected ? Muted : Ink;
    }

    private void ShowMaterialChecklist(CraftAmount[] amounts)
    {
        bindingMaterials = true;
        materialChecklist.BeginUpdate();
        try
        {
            materialChecklist.Items.Clear();
            foreach (var amount in amounts)
            {
                var key = new MaterialCheck(catalog.RecipeBuild, amount.Id, amount.Name, amount.Quantity);
                bool collected = wishlist.Collected?.Contains(key) == true;
                materialChecklist.Items.Add(new ListViewItem($"× {amount.Quantity:0.######}    {amount.Name}")
                {
                    Tag = key, Checked = collected, Font = collected ? collectedFont : Font,
                    ForeColor = collected ? Muted : Ink
                });
            }
            materialRow.SizeType = amounts.Length == 0 ? SizeType.Absolute : SizeType.Percent;
            materialRow.Height = amounts.Length == 0 ? 0 : 42;
        }
        finally
        {
            materialChecklist.EndUpdate();
            bindingMaterials = false;
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
            var document = new DetailDocument();
            ShowMaterialChecklist(plan.Materials);
            if (plan.Tools.Length > 0)
            {
                document.Heading("필요 도구");
                foreach (var tool in plan.Tools)
                    document.Field($"× {tool.Quantity:0.######}", tool.Name, indent: 1440);
            }
            if (plan.Steps.Length > 0)
            {
                document.Heading("제작 순서");
                int number = 0;
                foreach (var step in plan.Steps)
                    document.Step($"{++number}. {step.Name}",
                        $"{step.Station}   ·   {step.Batches:N0}회 제작 → {step.Output:N0}개");
            }
            if (plan.Warnings.Length > 0)
            {
                document.Heading("확인이 필요한 항목");
                foreach (string warning in plan.Warnings)
                    document.Body(warning);
            }
            if (plan is { Materials.Length: 0, Tools.Length: 0, Steps.Length: 0, Warnings.Length: 0 })
                document.Body("아이템 상세에서 목표 수량을 입력하고 저장하세요.");
            planText.Rtf = document.ToString();
            planText.Select(0, 0);
            planText.ScrollToCaret();
        }
        catch (OverflowException)
        {
            ShowMaterialChecklist([]);
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
        var bounds = Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(9), 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int tagWidth = LogicalToDeviceUnits(53);
        var tag = new Rectangle(bounds.Right - tagWidth, bounds.Top + LogicalToDeviceUnits(5),
            tagWidth, bounds.Height - LogicalToDeviceUnits(10));
        using var tagBackground = new SolidBrush(selected ? Color.FromArgb(61, 89, 73) : Color.FromArgb(40, 56, 48));
        e.Graphics.FillRectangle(tagBackground, tag);
        TextRenderer.DrawText(e.Graphics, entry.CategoryName, Font, tag, Muted, flags | TextFormatFlags.HorizontalCenter);
        bounds.Width -= tagWidth + LogicalToDeviceUnits(9);
        TextRenderer.DrawText(e.Graphics, entry.Title, Font, bounds, Ink, flags);
        e.DrawFocusRectangle();
    }

    private void QueueSearch()
    {
        searchDelay.Stop();
        if (!settingSearch && !query.IsComposing)
            searchDelay.Start();
    }

    private void OpenItem(int id, string searchText) => OpenEntry(Category.Item, id, searchText);

    private void OpenEntry(Category category, int? id, string searchText)
    {
        settingSearch = true;
        try
        {
            filter.SelectedIndex = Array.IndexOf(Filters, category);
            query.Text = searchText;
        }
        finally
        {
            settingSearch = false;
        }
        RefreshResults(id);
        results.Focus();
    }

    private void RefreshResults(int? selectedId = null)
    {
        searchDelay.Stop();
        if (query.IsComposing)
            return;
        var found = catalog.Find(query.Text, Filters[filter.SelectedIndex]);
        bindingResults = true;
        results.BeginUpdate();
        try
        {
            results.Items.Clear();
            results.Items.AddRange(found.Cast<object>().ToArray());
            int selected = 0;
            if (selectedId.HasValue)
            {
                for (int i = 0; i < found.Count; i++)
                    if ((found[i].ItemId ?? found[i].GuideId) == selectedId)
                    {
                        selected = i;
                        break;
                    }
            }
            results.SelectedIndex = found.Count == 0 ? -1 : selected;
        }
        finally
        {
            results.EndUpdate();
            bindingResults = false;
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
            var selected = results.SelectedItem as Entry;
            int? id = selected?.ItemId;
            researchLinks.Items.Clear();
            if (selected is not null)
                researchLinks.Items.AddRange(catalog.RelatedResearch(selected).Cast<object>().ToArray());
            researchLinks.Visible = researchLinks.Items.Count > 0;
            if (researchLinks.Items.Count > 0)
            {
                researchLinks.Items.Insert(0, "연구 경로 이동");
                researchLinks.SelectedIndex = 0;
            }
            recipeUses = selected is { Category: Category.Item, ItemId: int ingredientId }
                ? catalog.RecipeUses(ingredientId) : [];
            showingRecipeUses = recipeUses.Count > 0 && selected?.Recipes is not { Length: > 0 };
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
        ShowItemIcon(results.SelectedItem as Entry);
        bool showLinks = showingRecipeUses && recipeUses.Count > 0;
        details.Visible = !showLinks;
        recipeUseLinks.Visible = showLinks;
        if (results.SelectedItem is not Entry entry)
        {
            recipeUsesToggle.Visible = false;
            calculation.Visible = false;
            title.Text = "검색 결과 없음";
            metadata.Text = "";
            details.Text = Filters[filter.SelectedIndex] == Category.Research && !catalog.HasResearch
                ? "Steam 제작법을 다시 불러오면 연구 비용과 해금 경로를 검색할 수 있습니다."
                : "검색어를 줄이거나 분류를 전체로 바꿔 보세요.";
            return;
        }

        int choiceIndex = recipeSelector.SelectedIndex - 1;
        var choice = entry.Recipes is { } choices && choiceIndex >= 0 && choiceIndex < choices.Length
            ? choices[choiceIndex] : null;
        var batch = choice is null ? entry.Batch : choice.Batch;
        bool canSaveGoal = entry.Category == Category.Item && entry.ItemId.HasValue;
        recipeUsesToggle.Visible = canSaveGoal;
        recipeUsesToggle.Text = showingRecipeUses ? "아이템 상세" : recipeUses.Count > 0
            ? $"사용되는 제작법 {recipeUses.Count:N0}개" : "사용되는 제작법";
        calculation.Visible = !showingRecipeUses && (canSaveGoal || batch is not null);
        saveGoal.Visible = canSaveGoal;
        title.Text = entry.Title;
        metadata.Text = entry.ItemId is int id
            ? $"{entry.CategoryName}     {(entry.InferredId ? "추정 ID" : "ID")} {id}"
            : entry.CategoryName;
        string aliases = string.Join("  ·  ", new[] { entry.English, entry.Chinese, entry.ChineseAlias }
            .Where(name => name.Length > 0).Distinct());
        var document = new DetailDocument();
        if (showingRecipeUses)
        {
            if (showLinks)
            {
                ShowRecipeUseLinks();
                return;
            }
            document.Heading($"사용되는 제작법 ({recipeUses.Count:N0}개)");
            document.Body(catalog.RecipeBuild.Length == 0
                ? "Steam 제작법을 불러오면 재료가 사용되는 제작법을 확인할 수 있습니다."
                : "불러온 제작법에서 이 아이템을 소모하는 항목을 찾지 못했습니다.");
            RenderDetails(document);
            return;
        }
        if (choice is not null)
        {
            document.Heading("제작 방법");
            foreach (string line in choice.Details.ReplaceLineEndings("\n").Split('\n'))
            {
                int separator = line.IndexOf("  ", StringComparison.Ordinal);
                if (separator > 0)
                    document.Field(line[..separator], line[(separator + 2)..]);
                else if (line.Length > 0)
                    document.Body(line);
            }
        }
        if (batch is not null)
        {
            document.Heading(batch.Partial ? "목표 수량 · 부분 계산" : "목표 수량 계산");
            if (int.TryParse(target.Text, out int desired) && desired >= target.Minimum && desired <= target.Maximum)
            {
                var plan = batch.Calculate(desired);
                document.Field("제작 횟수", $"{plan.Batches:N0}회 → {plan.Output:N0}개 생산");
                if (plan.Materials.Length > 0)
                    document.Field("확인된 재료", string.Join(", ", plan.Materials.Select(m => $"{m.Name} {m.PerBatch:0.######}개")));
            }
            else
            {
                document.Body($"목표 수량은 {target.Minimum}~{target.Maximum}개로 입력하세요.");
            }
        }
        if (entry.Details.Length > 0 || aliases.Length > 0)
        {
            document.Heading(entry.Category switch
            {
                Category.Item => "아이템 정보",
                Category.Recipe => "제작 방법",
                _ => $"{entry.CategoryName} 정보"
            });
            if (aliases.Length > 0)
                document.Aliases(aliases);
            if (entry.Category is Category.Construction or Category.Research or Category.Effect or Category.Trait or Category.Boss or Category.Skill)
            {
                foreach (string line in entry.Details.Split('\n'))
                {
                    int separator = line.IndexOf("  ", StringComparison.Ordinal);
                    if (separator > 0)
                        document.Field(line[..separator], line[(separator + 2)..]);
                    else if (line.Length > 0)
                        document.Body(line);
                }
            }
            else if (entry.Details.Length > 0)
                document.Body(entry.Details);
        }
        if (entry.Facts is { Length: > 0 })
        {
            document.Heading("아이템 속성");
            foreach (string line in entry.Facts)
            {
                if (line?.Split("  ", 2, StringSplitOptions.None) is [var label, var value])
                    document.Field(label, value);
            }
        }
        if (entry.Acquisition is { Length: > 0 })
        {
            document.Heading("재료 획득처");
            document.Note("고정 목록과 확률은 자산에 기록된 값입니다. 월드 조건과 실제 지급 여부는 미검증입니다.");
            foreach (string line in entry.Acquisition)
                if (line?.Split("  ", 2, StringSplitOptions.None) is [var label, var value])
                    document.Field(label, value);
        }
        if (choice is not null)
            document.Note($"제작법 출처  {choice.Source}");
        document.Note($"출처  {entry.Source}");
        RenderDetails(document);
    }

    private void ShowItemIcon(Entry? entry)
    {
        string? path = catalog.RecipeBuild.Length > 0 &&
            entry is { Category: Category.Item, VerifiedAsset: true, ItemId: int id }
            ? Path.Combine(ItemIconCache.DirectoryFor(catalog.RecipeBuild), $"{id}.png") : null;
        if (iconPath == path)
            return;
        iconPath = path;
        var previousIcon = itemIcon.Image;
        itemIcon.Image = path is null ? null : ItemIconCache.Read(path);
        itemIcon.Visible = itemIcon.Image is not null;
        previousIcon?.Dispose();
    }

    private void ShowRecipeUseLinks()
    {
        recipeUseLinks.SuspendLayout();
        try
        {
            while (recipeUseLinks.Controls.Count > 0)
                recipeUseLinks.Controls[0].Dispose();
            recipeUseLinks.AutoScrollPosition = Point.Empty;
            var heading = Caption($"사용되는 제작법 ({recipeUses.Count:N0}개)", Accent);
            heading.Margin = new Padding(0, 4, 0, 12);
            recipeUseLinks.Controls.Add(heading);
            foreach (var group in recipeUses.GroupBy(use => use.Product))
            {
                Entry product = group.Key;
                var link = new LinkLabel
                {
                    Text = product.Title, AutoSize = true, LinkColor = Accent, ActiveLinkColor = Ink,
                    VisitedLinkColor = Accent, Margin = Padding.Empty
                };
                link.LinkClicked += (_, _) => OpenItem(product.ItemId!.Value, product.Title);
                recipeUseLinks.Controls.Add(link);
                var variants = Caption(string.Join("\n", group.Select(use =>
                    $"{use.Choice.Label} · 회당 {use.PerBatch:0.######}개" +
                    (use.Choice.Batch is null ? " · 산출물 미확인" : ""))));
                variants.Margin = new Padding(8, 2, 0, 9);
                recipeUseLinks.Controls.Add(variants);
            }
            recipeUseLinks.Controls.Add(Caption("설치된 기본 게임에서 이 아이템이 직접 소모되는 제작법입니다."));
            FitRecipeUseLinks();
        }
        finally
        {
            recipeUseLinks.ResumeLayout();
        }
    }

    private void FitRecipeUseLinks()
    {
        int width = Math.Max(1, recipeUseLinks.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control row in recipeUseLinks.Controls)
            row.MaximumSize = new Size(width, 0);
    }

    private void RenderDetails(DetailDocument document)
    {
        details.Rtf = document.ToString();
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
                {
                    if (!updatePopup.Visible)
                        HidePanel();
                }
                else
                    OpenPanel();
            }
            return;
        }
        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (updatePopup.Visible)
        {
            updatePopup.HandleKey(keyData);
            return true;
        }
        if (query.Focused && query.IsComposing)
            return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Escape && !filter.DroppedDown && !recipeSelector.DroppedDown && !researchLinks.DroppedDown)
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
        if (keyData == Keys.F1)
        {
            ShowHelpGuide();
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
            if (updatePopup.Visible)
                updatePopup.Close(false);
            else
                HidePanel();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        preparationLifetime.Cancel();
        updatePopup.Close(false);
        preparationLifetime.Dispose();
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        hints.Dispose();
        itemIcon.Image?.Dispose();
        searchDelay.Dispose();
        collectedFont.Dispose();
        base.OnFormClosed(e);
    }

    private void UpdateStatus()
    {
        string key = hotkeyId == 0 ? "단축키 충돌 · 알림 영역에서 열기" : shortcut;
        string data = detectingSteam ? "Steam 확인 중…"
            : catalog.RecipeBuild.Length > 0 ? "제작법 준비됨"
            : steamGameDirectory is not null && RecipePreparation.IsAvailable ? "제작법 불러오기 필요"
            : "아이템 검색 가능";
        status.Text = wishlistUnreadable ? "찜목록 파일 읽기 실패 · 기존 파일을 보존합니다." :
            updateStatus ?? preparationStatus ?? $"{key}  ·  Esc 닫기  ·  {data}";
    }

    private void ShowHelpGuide()
    {
        fileDialogOpen = true;
        try
        {
            string open = hotkeyId == 0 ? "Windows 알림 영역 아이콘" : shortcut;
            MessageBox.Show(this,
                $"""
                단축키

                {open}   오버레이 열기
                Esc   오버레이 닫기
                Ctrl+F   검색어 입력으로 이동
                ↓   검색 결과 목록으로 이동
                Ctrl+R   제작법 선택으로 이동 (제작법이 있는 항목)
                Ctrl+D   현재 아이템의 목표 수량 저장
                Ctrl+L   찜목록 열기 / 상세로 돌아가기
                Ctrl+O   자료 파일 가져오기
                Ctrl+-   창 크기 전환
                F1   이 안내 열기

                자료 버튼

                {PrepareLabel}
                {PrepareHint} 변환 도구가 없으면 다운로드 여부를 묻습니다.

                {ImportLabel}
                {ImportHint} Steam 설치가 감지되면 파일 선택 없이 검색할 수 있습니다.
                """,
                "단축키 및 자료 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            fileDialogOpen = false;
        }
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
        if (updatePopup.Visible)
            updatePopup.FocusAction();
        else
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
