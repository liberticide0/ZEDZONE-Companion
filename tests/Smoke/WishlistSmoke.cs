using Companion;

internal static class WishlistSmoke
{
    private static RecipeChoice Recipe(string station, int output, params Material[] materials) =>
        new(station, "", "합성", new RecipeBatch(output, materials, Partial: false), station);
    private static Entry Item(int id, string name, params RecipeChoice[] recipes) =>
        new(Category.Item, name, "", "", "", "합성", id, Recipes: recipes);

    public static void Run(Action<bool, string> check)
    {
        Entry[] fixtures =
        [
            Item(1, "첫 목표", Recipe("작업대", 1, new Material("중간품", 1, 3))),
            Item(2, "둘째 목표", Recipe("선반", 1, new Material("중간품", 1, 3), new Material("원재료", 2, 4))),
            Item(3, "중간품", Recipe("손 제작", 4, new Material("원재료", 3, 4)),
                Recipe("압착기", 6, new Material("원재료", 1.5m, 4))),
            Item(4, "원재료"),
            Item(5, "순환 A", Recipe("작업대", 1, new Material("순환 B", 1, 6))),
            Item(6, "순환 B", Recipe("작업대", 1, new Material("순환 A", 1, 5))),
            Item(7, "계산 보류", new RecipeChoice("작업대", "", "합성", null, "작업대")),
            Item(8, "도구 사례", Recipe("작업대", 1) with { Tools = [new Material("도구", 1, 9)] }),
            Item(9, "도구")
        ];
        var items = fixtures.ToDictionary(entry => entry.ItemId!.Value);
        var goals = new WishlistState(1, [new(1, "첫 목표", 4), new(2, "둘째 목표", 1), new(3, "중간품", 1)], []);
        var plan = CraftPlanner.Calculate(goals, "42", items.GetValueOrDefault);
        check(plan.Steps[0] is { Id: 3, Batches: 2, Output: 8, Station: "손 제작" } &&
            plan.Materials.Single() is { Id: 4, Quantity: 8 }, "shared and direct intermediate demand is summed before rounding");

        var alternate = goals with { Preferences = [new RecipePreference(3, "42", 1)] };
        var switched = CraftPlanner.Calculate(alternate, "42", items.GetValueOrDefault);
        var stale = CraftPlanner.Calculate(alternate, "43", items.GetValueOrDefault);
        check(switched.Steps[0] is { Batches: 1, Station: "압착기" } &&
            switched.Materials.Single().Quantity == 3.5m && stale.Steps[0].Station == "손 제작",
            "intermediate alternative and stale build fallback");

        var mixed = goals with { Goals = [.. goals.Goals, new(5, "순환 A", 1), new(7, "계산 보류", 1),
            new(8, "도구 사례", 4), new(999, "누락", 1), new(4, "이름이 달라진 항목", 1)] };
        var guarded = CraftPlanner.Calculate(mixed, "42", items.GetValueOrDefault);
        check(guarded.Steps.All(step => step.Id is not (5 or 6 or 7)) &&
            guarded.Warnings.Any(w => w.Contains("순환")) && guarded.Warnings.Any(w => w.Contains("계산 가능한 제작법 없음")) &&
            guarded.Warnings.Any(w => w.Contains("누락")) && guarded.Warnings.Any(w => w.Contains("이름이 달라진")) &&
            guarded.Materials.Single().Quantity == 8, "cycle, missing and changed IDs do not hide independent totals");
        check(guarded.Tools.Single() is { Id: 9, Quantity: 1 }, "reusable tool is not multiplied by crafting batches");

        var temporary = Directory.CreateTempSubdirectory("zed-wishlist-smoke-");
        string file = Path.Combine(temporary.FullName, "wishlist.json");
        try
        {
            check(WishlistStore.Load(file).Goals.Length == 0, "first launch has an empty wishlist");
            WishlistStore.Save(file, alternate);
            var restored = WishlistStore.Load(file);
            check(restored.Goals.SequenceEqual(alternate.Goals) &&
                restored.Preferences.SequenceEqual(alternate.Preferences) && restored.Collected is null,
                "existing wishlist without checklist restores");
            var collected = new MaterialCheck("42", 4, "원재료", 3.5m);
            WishlistStore.Save(file, alternate with { Collected = [collected] });
            var checks = WishlistStore.Load(file).Collected;
            check(checks is not null && checks.SequenceEqual(new[] { collected }) &&
                !checks.Contains(collected with { Quantity = 5 }) && !checks.Contains(collected with { Build = "43" }),
                "checklist restores for the same build and required quantity");
            WishlistStore.Save(file, WishlistState.Empty);
            check(WishlistStore.Load(file).Goals.Length == 0 &&
                Directory.GetFiles(temporary.FullName).Length == 1, "wishlist overwrite leaves no temporary files");
            File.WriteAllText(file, """{"Version":99,"Goals":[],"Preferences":[]}""");
            try
            {
                WishlistStore.Load(file);
                check(false, "unsupported wishlist version is rejected");
            }
            catch (InvalidDataException)
            {
                check(File.ReadAllText(file).Contains("99"), "unsupported wishlist version is rejected without overwriting");
            }
        }
        finally
        {
            File.Delete(file);
            temporary.Delete();
        }
    }
}
