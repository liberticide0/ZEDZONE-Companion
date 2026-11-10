using System.Text.Json;

namespace Companion;

internal sealed record CraftGoal(int Id, string Name, int Quantity);
internal sealed record RecipePreference(int Id, string Build, int Index);
internal sealed record MaterialCheck(string Build, int? Id, string Name, decimal Quantity);
internal sealed record WishlistState(int Version, CraftGoal[] Goals, RecipePreference[] Preferences,
    MaterialCheck[]? Collected = null)
{
    public static WishlistState Empty => new(1, [], []);
}

internal static class WishlistStore
{
    public static string PathForUser => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZEDZONE.Companion", "wishlist.json");

    public static WishlistState Load(string path)
    {
        if (!File.Exists(path))
            return WishlistState.Empty;

        using var stream = File.OpenRead(path);
        if (stream.Length > 1_048_576)
            throw new InvalidDataException("찜목록 파일이 너무 큽니다.");
        var state = JsonSerializer.Deserialize<WishlistState>(stream);
        if (state is not { Version: 1, Goals: not null, Preferences: not null } ||
            state.Goals.Any(goal => goal is not { Id: >= 0 and < 100_000, Quantity: >= 1 and <= 999, Name: not null }) ||
            state.Preferences.Any(choice => choice is not { Id: >= 0 and < 100_000, Index: >= 0, Build: not null }) ||
            state.Collected?.Any(material => material is not { Build: not null, Name: not null, Quantity: > 0 }) == true ||
            state.Goals.Select(goal => goal.Id).Distinct().Count() != state.Goals.Length ||
            state.Preferences.Select(choice => choice.Id).Distinct().Count() != state.Preferences.Length)
            throw new InvalidDataException("찜목록 형식이 올바르지 않습니다.");
        return state;
    }

    public static void Save(string path, WishlistState state)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".wishlist-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, state);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

internal sealed record CraftAmount(int? Id, string Name, decimal Quantity);
internal sealed record CraftStep(int Id, string Name, string Station, decimal Batches, decimal Output);
internal sealed record CraftPlan(CraftStep[] Steps, CraftAmount[] Materials, CraftAmount[] Tools, string[] Warnings);

internal static class CraftPlanner
{
    public static int ChoiceIndex(Entry entry, string build, IEnumerable<RecipePreference> preferences)
    {
        var preferred = preferences.FirstOrDefault(choice => choice.Id == entry.ItemId && choice.Build == build);
        if (entry.Recipes is not { Length: > 0 } choices)
            return 0;
        if (preferred is not null && preferred.Index < choices.Length)
            return preferred.Index;
        return Math.Max(0, Array.FindIndex(choices, choice => choice.Batch is not null));
    }

    public static CraftPlan Calculate(WishlistState state, string build, Func<int, Entry?> find)
    {
        var selected = new Dictionary<int, (Entry? Entry, RecipeChoice? Recipe)>();
        var order = new List<int>();
        var path = new List<int>();
        var cycles = new HashSet<int>();
        var tooDeep = new HashSet<int>();
        var warnings = new HashSet<string>();
        var demands = new Dictionary<int, decimal>();
        var materials = new Dictionary<(int? Id, string Name), decimal>();
        var tools = new Dictionary<(int? Id, string Name), decimal>();
        var steps = new List<CraftStep>();
        var roots = new HashSet<int>();

        void Visit(int id)
        {
            if (selected.ContainsKey(id))
            {
                int loop = path.IndexOf(id);
                if (loop >= 0)
                    cycles.UnionWith(path.Skip(loop));
                return;
            }
            var entry = find(id);
            var recipe = entry?.Recipes is { Length: > 0 } variants
                ? variants[ChoiceIndex(entry, build, state.Preferences)] : null;
            selected.Add(id, (entry, recipe));
            if (path.Count >= 128)
            {
                tooDeep.Add(id);
                order.Add(id);
                return;
            }
            path.Add(id);
            if (recipe?.Batch is { } batch)
            {
                foreach (var input in batch.Materials)
                    if (input.Id is int inputId)
                        Visit(inputId);
            }
            path.RemoveAt(path.Count - 1);
            order.Add(id);
        }

        foreach (var goal in state.Goals)
        {
            var item = find(goal.Id);
            if (item is null || !string.Equals(item.Title, goal.Name, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{goal.Name} (ID {goal.Id}) · 현재 아이템과 대조되지 않습니다. 검색에서 목표를 다시 등록하세요.");
                continue;
            }

            roots.Add(goal.Id);
            demands.Add(goal.Id, goal.Quantity);
            Visit(goal.Id);
        }

        // Each consumer precedes its ingredients, so every shared demand is known before rounding.
        foreach (int id in order.AsEnumerable().Reverse())
        {
            if (!demands.TryGetValue(id, out decimal required))
                continue;
            var (entry, recipe) = selected[id];
            string name = entry?.Title ?? $"ID {id}";
            if (cycles.Contains(id))
            {
                warnings.Add($"{name} · 제작법이 순환하여 {required:0.######}개 계산 보류");
                continue;
            }
            if (tooDeep.Contains(id))
            {
                warnings.Add($"{name} · 제작 단계가 너무 깊어 {required:0.######}개 계산 보류");
                continue;
            }

            if (recipe?.Batch is not { } batch)
            {
                if (recipe is not null || roots.Contains(id) || entry is null)
                    warnings.Add($"{name} · {required:0.######}개 필요, 계산 가능한 제작법 없음");
                else
                    Add(materials, (id, name), required);
                continue;
            }

            decimal batches = Math.Ceiling(required / batch.Output);
            steps.Add(new CraftStep(id, name, recipe.Station, batches, batches * batch.Output));
            if (batch.Partial)
                warnings.Add($"{name} · 확인된 재료만 계산했습니다.");
            foreach (var tool in recipe.Tools ?? [])
            {
                var key = (tool.Id, tool.Name);
                tools[key] = Math.Max(tools.GetValueOrDefault(key), tool.PerBatch);
            }
            foreach (var input in batch.Materials)
            {
                decimal quantity = input.PerBatch * batches;
                if (input.Id is int inputId)
                    Add(demands, inputId, quantity);
                else
                    Add(materials, (null, input.Name), quantity);
            }
        }

        steps.Reverse();
        return new CraftPlan(steps.ToArray(), Amounts(materials), Amounts(tools), warnings.ToArray());
    }

    private static void Add<TKey>(Dictionary<TKey, decimal> values, TKey key, decimal amount) where TKey : notnull =>
        values[key] = values.GetValueOrDefault(key) + amount;

    private static CraftAmount[] Amounts(Dictionary<(int? Id, string Name), decimal> values) =>
        values.Select(pair => new CraftAmount(pair.Key.Id, pair.Key.Name, pair.Value))
            .OrderBy(amount => amount.Name).ToArray();
}
