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
