using Companion;

static void Check(bool condition, string scenario)
{
    if (!condition)
        throw new InvalidOperationException(scenario);
    Console.WriteLine($"PASS  {scenario}");
}

var catalog = new Catalog();
foreach (string query in new[] { "돌망치", "stone hammer", "石锤", "３０３" })
    Check(catalog.Find(query, Category.Item).FirstOrDefault()?.ItemId == 303, $"item search: {query}");

Check(catalog.Find("Area 61", Category.Map).First().Korean == "61구역", "map filter and name ranking");
Check(catalog.Find("no-such-item", null).Count == 0, "empty result");
foreach (string query in new[] { "12 게이지", "12 gauge", "12号散弹", "100" })
{
    Check(catalog.Find(query, Category.Item).FirstOrDefault()?.ItemId == 100 &&
        catalog.Find(query, Category.Recipe).FirstOrDefault()?.ItemId == 100,
        $"12 gauge in both item and recipe filters: {query}");
}
Check(catalog.Find("12 게이지", null).Count(e => e.ItemId == 100) == 2,
    "12 gauge in the default search without imported data");

var batch = catalog.Find("방사능 폭탄", Category.Recipe).Single().Batch!;
foreach (var (target, batches, output) in new[] { (1, 1, 2), (2, 1, 2), (3, 2, 4), (999, 500, 1000) })
{
    var plan = batch.Calculate(target);
    Check(plan.Batches == batches && plan.Output == output &&
        plan.Materials.Single().PerBatch == batches, $"batch rounding: {target}");
}

var temporary = Directory.CreateTempSubdirectory("zed-companion-test-");
string csv = Path.Combine(temporary.FullName, "local.csv");
string md = Path.Combine(temporary.FullName, "reference.md");
try
{
    File.WriteAllText(csv, """
        #format,zedzone-localization-v1
        #target_lang,한국어,ko
        id,source,target
        1000400,Example Rifle,예제 소총
        1100400,"A test, with a comma","예제, 첫 줄
        둘째 줄"
        1000401,New item,새 아이템
        1100401,Version-specific item,버전별 아이템
        """);
    File.WriteAllText(md, """
        # Item ID Reference
        | ID | English | 中文名 |
        |---|---|---|
        | 400 | example rifle | 示例步枪 |
        | 401 | Old item | 旧物品 |
        """);
    Check(catalog.Import(csv).Localization == 2 && catalog.Find("둘째 줄", Category.Item).Single().ItemId == 400,
        "CSV quotes and multiline field");
    Check(catalog.Import(md).Official == 2 &&
        catalog.Find("示例步枪", Category.Item).Single().Title == "예제 소총", "EN/ID match adds the Chinese name");
    Check(catalog.Find("401", Category.Item).Count == 2, "conflicting cross-build names stay separate");

    File.AppendAllText(csv, "\n1000400,Duplicate,중복");
    try
    {
        catalog.Import(csv);
        throw new InvalidOperationException("duplicate input accepted");
    }
    catch (InvalidDataException)
    {
        Check(catalog.Find("예제 소총", Category.Item).Single().ItemId == 400, "failed import retains active data");
    }

    string steamapps = Directory.CreateDirectory(Path.Combine(temporary.FullName, "steamapps")).FullName;
    string library = Directory.CreateDirectory(Path.Combine(temporary.FullName, "second library")).FullName;
    string libraryApps = Directory.CreateDirectory(Path.Combine(library, "steamapps")).FullName;
    string installed = Directory.CreateDirectory(Path.Combine(libraryApps, "common", "Test Game",
        "ZEDZONE_Data", "StreamingAssets", "Localization")).FullName;
    string libraries = Path.Combine(steamapps, "libraryfolders.vdf");
    string manifest = Path.Combine(libraryApps, "appmanifest_1211600.acf");
    string installedCsv = Path.Combine(installed, "Localization_en_to_ko.csv");
    try
    {
        string escaped = library.Replace(@"\", @"\\");
        File.WriteAllText(libraries, $$"""
            "libraryfolders" { "1" { "path" "{{escaped}}" "apps" { "1211600" "1" } } }
            """);
        File.WriteAllText(manifest, """
            "AppState" { "appid" "1211600" "installdir" "Test Game" "buildid" "67890" }
            """);
        File.WriteAllText(installedCsv, """
            #format,zedzone-localization-v1
            #target_lang,한국어,ko
            id,source,target
            1000100,Different 12 Gauge,다른 탄약
            1100100,Example description,예제 설명
            1000444,Example Part,예제 부품
            1100444,Example description,예제 설명
            """);
        var detected = Catalog.ReadSteamLocalization([temporary.FullName]);
        var automatic = new Catalog();
        Check(automatic.UseSteamLocalization(detected) && automatic.DataStatus.Contains("67890"),
            "Steam secondary library with build provenance");
        Check(automatic.Find("예제 부품", Category.Item).Single() is { ItemId: 444, InferredId: true },
            "Steam item is searchable with its inferred ID");
        Check(automatic.Find("100", Category.Item).Count == 2, "built-in name conflict keeps both entries");
        Check(!catalog.UseSteamLocalization(detected), "Steam discovery cannot overwrite a manual CSV");

        File.WriteAllText(installedCsv, "invalid");
        var fallback = new Catalog();
        Check(!fallback.UseSteamLocalization(Catalog.ReadSteamLocalization([temporary.FullName])) &&
            fallback.Find("12 게이지", Category.Item).Count == 1, "broken Steam CSV retains built-in search");
        File.WriteAllText(manifest, """
            "AppState" { "appid" "1211600" "installdir" "../Test Game" "buildid" "67890" }
            """);
        Check(!SteamInstall.FindLocalization([temporary.FullName]).Any(), "manifest path traversal is ignored");
    }
    finally
    {
        File.Delete(installedCsv);
        File.Delete(manifest);
        File.Delete(libraries);
        for (var directory = new DirectoryInfo(installed); directory.FullName != library; directory = directory.Parent!)
            directory.Delete();
        Directory.Delete(library);
        Directory.Delete(steamapps);
    }
}
finally
{
    File.Delete(csv);
    File.Delete(md);
    temporary.Delete();
}

foreach (string path in args)
{
    var loaded = catalog.Import(path);
    Console.WriteLine($"PASS  supplied source (localization={loaded.Localization}, official={loaded.Official})");
}
