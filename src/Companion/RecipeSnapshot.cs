using System.Text.Json;
using System.Text.Json.Serialization;

namespace Companion;

internal sealed record RecipeMaterial([property: JsonRequired] int Id, decimal Quantity);
internal sealed record LocalRecipe([property: JsonRequired] int OutputId, int Output,
    [property: JsonRequired] int Platform, [property: JsonRequired] int ToolType,
    decimal Intelligence, decimal Crafting, int Difficulty, RecipeMaterial[] Materials)
{
    public string Station => Platform switch
    {
        0 => "손 제작", 1 => "간이 작업대", 2 => "작업대", 3 => "탄약 압착기",
        4 => "용광로", 5 => "간이 선반", 6 => "고급 선반", 7 => "사출 성형기",
        8 => "요리", 9 => "화학 작업대", 10 => "의약품 작업대", 11 => "통조림 밀봉기",
        12 => "플라스틱 3D 프린터", 13 => "금속 3D 프린터", _ => $"미확인 작업대 코드 {Platform}"
    };
}
internal sealed record ResearchUnlock(string Name, bool Default, string[] Prerequisites);
internal sealed record RecipeAsset([property: JsonRequired] int Id, string Korean, string English,
    [property: JsonRequired] bool DefaultUnlock, LocalRecipe[] Recipes, ResearchUnlock[]? Research = null);
internal sealed record RecipeChoice(string Label, string Details, string Source, RecipeBatch? Batch,
    string Station, Material[]? Tools = null);

internal sealed class RecipeSnapshot
{
    private sealed record GuideAsset(string Category, string Korean, string English, string Chinese, int TextId, string Details);
    private sealed record SnapshotFile(int Schema, string Build, RecipeAsset[] Items, GuideAsset[]? Guides);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Dictionary<int, RecipeAsset> items;
    private readonly Dictionary<int, RecipeChoice[]> choices;

    private RecipeSnapshot(string build, Dictionary<int, RecipeAsset> items, Entry[] guides)
    {
        Build = build;
        this.items = items;
        Guides = guides;
        choices = items.Values.Where(item => item.Recipes.Length > 0)
            .ToDictionary(item => item.Id, item => item.Recipes.Select((recipe, index) =>
                Describe(item, recipe, index + 1)).ToArray());
    }

    public string Build { get; }
    public int VariantCount => choices.Values.Sum(rows => rows.Length);
    public Entry[] Guides { get; }
    public IEnumerable<RecipeAsset> Items => items.Values;
    public RecipeChoice[] For(int id) => choices.TryGetValue(id, out var rows) ? rows : [];

    public static RecipeSnapshot? ReadInstalled(string build)
    {
        if (build.Length is < 1 or > 20 || !build.All(char.IsAsciiDigit))
            return null;

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZEDZONE.Companion", $"recipes-{build}.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024)
                return null;
            using var stream = File.OpenRead(path);
            return Read(stream, build);
        }
        catch (Exception error) when (Catalog.IsImportError(error) || error is JsonException)
        {
            return null;
        }
    }

    internal static RecipeSnapshot Read(Stream stream, string expectedBuild)
    {
        var data = JsonSerializer.Deserialize<SnapshotFile>(stream, Options);
        if (data is not { Schema: 1, Items.Length: > 0 } || data.Build != expectedBuild)
            throw new InvalidDataException("제작법 자료 형식 또는 Steam 빌드 불일치");

        var assets = new Dictionary<int, RecipeAsset>();
        foreach (var item in data.Items)
        {
            if (item is not { Id: >= 0 and < 100_000, Korean: not null, English: not null, Recipes: not null } ||
                !assets.TryAdd(item.Id, item))
                throw new InvalidDataException("제작법 자료에 잘못되었거나 중복된 아이템 ID가 있습니다.");
        }

        foreach (var asset in assets.Values)
        foreach (var recipe in asset.Recipes)
        {
            // An unlisted output can still describe a variant of a known owner. Describe keeps it out of calculations.
            if (recipe is not { Output: > 0 and <= 100_000, OutputId: >= 0 and < 100_000,
                Platform: >= 0, ToolType: >= 0, Materials: not null } ||
                recipe.Materials.Any(m => m is null || !assets.ContainsKey(m.Id) || m.Quantity == 0 ||
                    m.Quantity is > 1_000_000 or < -1_000_000))
                throw new InvalidDataException("제작법 자료에 확인할 수 없는 산출물 또는 재료가 있습니다.");
        }
        var guides = new List<Entry>();
        foreach (var guide in data.Guides ?? [])
        {
            if (guide is not { Korean: not null, English: not null, Chinese: not null, Details: not null })
                continue;
            var category = guide.Category switch
            {
                "skill" => Category.Skill,
                "map" => Category.Map,
                "boss" => Category.Boss,
                "construction" => Category.Construction,
                _ => (Category?)null
            };
            if (category is { } kind)
            {
                string source = kind switch
                {
                    Category.Boss => "몬스터 자산",
                    Category.Construction => "배치물 자산",
                    _ => "현지화 ID"
                };
                guides.Add(new Entry(kind, guide.Korean, guide.English, guide.Chinese, guide.Details,
                    $"로컬 Steam 빌드 {data.Build} · {source} {guide.TextId}"));
            }
        }
        return new RecipeSnapshot(data.Build, assets, guides.ToArray());
    }

    private string Name(int id)
    {
        var asset = items[id];
        return asset.Korean.Length > 0 ? asset.Korean : asset.English.Length > 0 ? asset.English : $"ID {id}";
    }

    private RecipeChoice Describe(RecipeAsset item, LocalRecipe recipe, int number)
    {
        // The in-game sample confirms this one negative quantity represents a required tool.
        bool verifiedMultitool(RecipeMaterial material) =>
            Build == "26528677" && item.Id == 100 && recipe.Platform == 0 && material is { Id: 54, Quantity: -1 };

        var inputs = recipe.Materials.Where(material => material.Quantity > 0)
            .GroupBy(material => material.Id)
            .Select(group => new Material(Name(group.Key), group.Sum(material => material.Quantity), group.Key)).ToArray();
        var tools = recipe.Materials.Where(verifiedMultitool).ToArray();
        var unknown = recipe.Materials.Where(material => material.Quantity < 0 && !verifiedMultitool(material)).ToArray();
        var lines = new List<string> { $"작업대  {recipe.Station}", $"회당 생산  {recipe.Output:N0}개" };
        if (inputs.Length > 0)
            lines.Add("회당 재료  " + string.Join(", ", inputs.Select(m => $"{m.Name} {m.PerBatch:0.######}개")));
        if (tools.Length > 0)
            lines.Add("필요 도구  " + string.Join(", ", tools.Select(m => $"{Name(m.Id)} 1개")));
        if (unknown.Length > 0)
            lines.Add("용도 미확인 항목  " + string.Join(", ", unknown.Select(m => $"{Name(m.Id)} ({m.Quantity:0.######})")));
        if (recipe.ToolType != 0)
            lines.Add($"필요 도구 유형 코드  {recipe.ToolType} · 의미 미검증");
        if (recipe.Intelligence != 0 || recipe.Crafting != 0)
            lines.Add($"자산 요구 수치  지능 {recipe.Intelligence:0.######} / 제작 {recipe.Crafting:0.######}");
        lines.Add($"기본 해금 플래그  {(item.DefaultUnlock ? "켜짐" : "꺼짐")}");
        foreach (var research in item.Research ?? [])
        {
            if (research is not { Name: not null, Prerequisites: not null })
                continue;
            string state = research.Default ? "기본 활성" : "연구 필요";
            string prerequisites = research.Prerequisites.Length == 0 ? "" :
                $" · 직전 선행 {string.Join(", ", research.Prerequisites)}";
            lines.Add($"연결 연구  {research.Name} ({state}){prerequisites}");
        }
        if (Build == "26528677" && recipe.Platform == 3 && item.Id is 122 or 123)
            lines.Add($"해금  {Name(item.Id)} 장전 매뉴얼 읽기 · 게임 내 매뉴얼 안내");
        else if (item.Research is not { Length: > 0 })
            lines.Add("실제 해금 조건 미검증");
        if (item.Id != recipe.OutputId)
        {
            lines.Add($"산출물 ID {recipe.OutputId}와 소유 아이템 ID {item.Id} 불일치 · 계산 보류");
            if (!items.ContainsKey(recipe.OutputId))
                lines.Add("산출물이 설치 빌드의 아이템 자산 목록에 없습니다.");
        }

        var batch = item.Id == recipe.OutputId
            ? new RecipeBatch(recipe.Output, inputs, Partial: unknown.Length > 0 || recipe.ToolType != 0)
            : null;
        return new RecipeChoice($"{number}. {recipe.Station}", string.Join("\n", lines),
            $"로컬 Steam 빌드 {Build} · 아이템 자산의 정적 제작법", batch, recipe.Station,
            tools.Select(tool => new Material(Name(tool.Id), -tool.Quantity, tool.Id)).ToArray());
    }
}
