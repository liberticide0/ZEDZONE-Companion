using System.Text;
using Companion;

internal static class RecipeSmoke
{
    public static void Run(Action<bool, string> check)
    {
        const string fixture = """
            {"schema":1,"build":"42","items":[
              {"id":1,"korean":"예제 탄약","english":"Example Ammo","defaultUnlock":false,
               "research":[{"name":"예제 연구","default":false,"prerequisites":["선행 연구"]}],"recipes":[
                {"outputId":1,"output":3,"platform":0,"toolType":0,"intelligence":0,"crafting":0,"difficulty":0,
                 "materials":[{"id":2,"quantity":0.75},{"id":2,"quantity":0.5},{"id":3,"quantity":-0.25}]},
                {"outputId":1,"output":6,"platform":3,"toolType":0,"intelligence":0,"crafting":0,"difficulty":0,
                 "materials":[{"id":2,"quantity":2}]},
                {"outputId":2,"output":1,"platform":2,"toolType":0,"intelligence":0,"crafting":0,"difficulty":0,
                 "materials":[{"id":3,"quantity":1}]}]},
              {"id":2,"korean":"예제 재료","english":"Example Input","defaultUnlock":true,"recipes":[]},
              {"id":3,"korean":"예제 도구","english":"Example Tool","defaultUnlock":true,"recipes":[]}
            ],"guides":[{"category":"skill","korean":"예제 스킬","english":"Example Skill","chinese":"",
                         "textId":77,"details":"예제 설명"}]}
            """;

        using var data = new MemoryStream(Encoding.UTF8.GetBytes(fixture));
        var snapshot = RecipeSnapshot.Read(data, "42");
        var catalog = new Catalog();
        var local = new List<Entry> { new(Category.Item, "예제 탄약", "Example Ammo", "", "설명", "CSV", 1, InferredId: true) };
        catalog.UseSteamLocalization((local, "42"), snapshot);
        var item = catalog.Find("예제 탄약", Category.Item).Single();
        var recipe = catalog.Find("예제 탄약", Category.Recipe).Single();
        check(item is { InferredId: false, Recipes.Length: 3 } && ReferenceEquals(item.Recipes, recipe.Recipes),
            "item and recipe filters expose the same three variants with a confirmed ID");
        check(ReferenceEquals(item, catalog.Find("탄약 압착기", Category.Recipe).Single(entry => entry.ItemId == 1)),
            "station keyword is indexed against the same item result");
        var hand = recipe.Recipes![0].Batch!;
        var handPlan = hand.Calculate(4);
        check(hand.Partial && handPlan.Output == 6 && handPlan.Materials.Single().PerBatch == 2.5m,
            "partial calculation groups repeated fractional ingredients and excludes unknown negative rows");
        var press = recipe.Recipes[1].Batch!;
        check(!press.Partial && press.Calculate(7).Output == 12 && press.Calculate(7).Materials.Single().PerBatch == 4,
            "alternative station uses its own production yield");
        check(recipe.Recipes[2].Batch is null && recipe.Recipes[2].Details.Contains("계산 보류"),
            "conflicting result ID is displayed without a calculation");
        check(recipe.Recipes[0].Details.Contains("예제 연구") && recipe.Recipes[0].Details.Contains("선행 연구"),
            "research and immediate prerequisite are shown for an item");
        check(catalog.Find("예제 스킬", Category.Skill).Single().Source.Contains("77"),
            "local guide retains category and text provenance");

        var conflicting = new Catalog();
        conflicting.UseSteamLocalization(([local[0] with { English = "Different English Name" }], "42"), snapshot);
        var unified = conflicting.Find("예제 탄약", null).Single();
        check(unified is { English: "Example Ammo", Recipes.Length: 3 } &&
            ReferenceEquals(unified, conflicting.Find("Different English Name", Category.Item).Single()) &&
            unified.Details.Contains("제작법 연결에서 제외"),
            "conflicting names remain attributed on one confirmed item row");

        var stale = new Catalog();
        stale.UseSteamLocalization((local, "43"), snapshot);
        check(stale.Find("예제 탄약", Category.Item).Single().Recipes is { Length: 0 }, "different Steam build ignores the cache");
        CheckRejected(fixture, "43", check, "snapshot header build mismatch is rejected");
        CheckRejected(fixture.Replace("\"id\":2,\"quantity\":2", "\"id\":999,\"quantity\":2"),
            "42", check, "missing ingredient ID is rejected");
    }

    private static void CheckRejected(string fixture, string build, Action<bool, string> check, string name)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fixture));
        try
        {
            RecipeSnapshot.Read(stream, build);
            check(false, name);
        }
        catch (InvalidDataException)
        {
            check(true, name);
        }
    }
}
