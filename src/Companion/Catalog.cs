using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Companion;

internal enum Category { Item, Recipe, Skill, Map, Boss }

internal sealed record Entry(
    Category Category, string Korean, string English, string Chinese, string Details, string Source,
    int? ItemId = null, RecipeBatch? Batch = null, bool InferredId = false, string ChineseAlias = "")
{
    public string Title => Korean.Length > 0 ? Korean : English.Length > 0 ? English : Chinese;
    public string CategoryName => Category switch
    {
        Category.Item => "아이템",
        Category.Recipe => "제작법",
        Category.Skill => "스킬",
        Category.Map => "지도",
        Category.Boss => "보스",
        _ => ""
    };
    public override string ToString() => $"{CategoryName}  ·  {Title}";
}

internal sealed record Material(string Name, int PerBatch);

internal sealed record RecipeBatch(int Output, Material[] Materials)
{
    public (int Batches, int Output, Material[] Materials) Calculate(int target)
    {
        int batches = (target - 1) / Output + 1;
        return (batches, batches * Output,
            Materials.Select(material => material with { PerBatch = material.PerBatch * batches }).ToArray());
    }
}

internal sealed class Catalog
{
    private static readonly StringComparer KoreanOrder = StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false);
    private List<Entry> localization = [];
    private List<Entry> official = [];
    private bool manuallyLoadedLocalization;
    private string localizationLabel = "";
    public string DataStatus
    {
        get
        {
            string status = localization.Count == 0 ? "기본 자료" : $"{localizationLabel} 후보 {localization.Count:N0}";
            return official.Count == 0 ? status : $"{status} · 공식 ID {official.Count:N0}";
        }
    }

    private IEnumerable<Entry> Entries()
    {
        var items = localization.ToDictionary(e => e.ItemId!.Value);
        foreach (var entry in Starter.Entries)
        {
            if (entry.Category == Category.Item && entry.ItemId is int id &&
                items.TryGetValue(id, out var item) && SameEnglishName(entry, item))
            {
                items[id] = item with { Chinese = entry.Chinese, Source = item.Source + " / 중국어는 기본 자료 별칭" };
            }
            else
            {
                yield return entry;
            }
        }

        foreach (var reference in official)
        {
            int id = reference.ItemId!.Value;
            if (items.TryGetValue(id, out var item) && SameEnglishName(item, reference))
            {
                if (reference.Chinese.Length > 0)
                    items[id] = item with { Chinese = reference.Chinese, ChineseAlias = item.Chinese,
                        Source = item.Source + " / 공식 ID 표(버전 미상)와 ID·영문명 일치" };
                continue;
            }
            // A shared ID alone does not establish that names from two builds describe the same item.
            yield return reference;
        }

        foreach (var item in items.Values)
            yield return item;
    }

    private static bool SameEnglishName(Entry first, Entry second) =>
        first.English.Length > 0 && Normalize(first.English) == Normalize(second.English);

    public IReadOnlyList<Entry> Find(string query, Category? category)
    {
        var terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var entries = Entries().Where(e => category is null || e.Category == category);

        if (terms.Length == 0)
            return entries.OrderBy(e => e.Category).ThenBy(e => e.Korean.Length == 0)
                .ThenBy(e => e.Title, KoreanOrder).ToArray();

        return entries.Select(e => (Entry: e, Score: Score(e, terms)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Entry.Title, KoreanOrder)
            .Select(result => result.Entry).ToArray();
    }

    public (int Localization, int Official) Import(string path)
    {
        ValidateFileSize(path);

        // Replace each table only after a complete, valid read.
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".csv":
                localization = ReadLocalization(path, "직접 선택한 한국어 현지화 표 · 빌드 미상");
                manuallyLoadedLocalization = true;
                localizationLabel = "수동 현지화";
                break;
            case ".md":
                official = ReadOfficialReference(path);
                break;
            default:
                throw new InvalidDataException("한국어 현지화 CSV 또는 개발사의 영문 아이템 ID 표를 선택하세요.");
        }
        return (localization.Count, official.Count);
    }

    internal static (List<Entry> Entries, string Build)? ReadSteamLocalization(IEnumerable<string> roots)
    {
        foreach (var (path, build) in SteamInstall.FindLocalization(roots))
        {
            try
            {
                ValidateFileSize(path);
                string source = build.Length == 0 ? "Steam 설치 현지화 · 빌드 미상" : $"Steam 설치 현지화 · 빌드 {build}";
                return (ReadLocalization(path, source), build);
            }
            catch (Exception error) when (IsImportError(error))
            {
                // A stale or unreadable library can coexist with another usable installation.
            }
        }
        return null;
    }

    internal bool UseSteamLocalization((List<Entry> Entries, string Build)? detected)
    {
        if (manuallyLoadedLocalization || detected is not { } data)
            return false;

        localization = data.Entries;
        localizationLabel = data.Build.Length == 0 ? "Steam 현지화" : $"Steam {data.Build}";
        return true;
    }

    internal static bool IsImportError(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or ArgumentException or MalformedLineException or System.Security.SecurityException;

    private static void ValidateFileSize(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("자료 파일 크기는 16MB 이하여야 합니다.");
    }

    private static List<Entry> ReadOfficialReference(string path)
    {
        using var lines = File.ReadLines(path, new UTF8Encoding(false, true)).GetEnumerator();
        if (!lines.MoveNext() || lines.Current.Trim() != "# Item ID Reference")
            throw new InvalidDataException("개발사의 영문 Item ID Reference 문서를 선택하세요.");

        var entries = new Dictionary<int, Entry>();
        while (lines.MoveNext())
        {
            var fields = lines.Current.Trim().Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length < 2 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                continue;
            if (fields is not ["", _, var en, var zh, ""] || id >= 100_000 ||
                !entries.TryAdd(id, new Entry(Category.Item, "", en == "—" ? "" : en, zh == "—" ? "" : zh,
                    "공식 MOD 문서에 수록된 이름입니다. 현재 설치 버전과 다를 수 있습니다.",
                    "직접 선택한 공식 MOD 아이템 ID 표", id)))
                throw new InvalidDataException("공식 ID 표에 잘못되었거나 중복된 행이 있습니다.");
        }

        if (entries.Count == 0)
            throw new InvalidDataException("공식 ID 표에서 아이템을 찾을 수 없습니다.");
        return entries.Values.ToList();
    }

    private static List<Entry> ReadLocalization(string path, string source)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true));
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        if (parser.ReadFields() is not ["#format", "zedzone-localization-v1"])
            throw new InvalidDataException("ZED ZONE 현지화 CSV 형식을 찾을 수 없습니다.");

        bool korean = false;
        string[]? fields;
        while ((fields = parser.ReadFields()) is { Length: > 0 } && fields[0].StartsWith('#'))
            korean |= fields is ["#target_lang", _, "ko"];

        if (!korean || fields is not ["id", "source", "target"])
            throw new InvalidDataException("한국어 현지화 CSV를 선택하세요.");

        var rows = new Dictionary<int, (string English, string Korean)>();
        while ((fields = parser.ReadFields()) is not null)
        {
            if (fields is not [var id, var english, var translated] ||
                !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int textId) ||
                !rows.TryAdd(textId, (english, translated)))
                throw new InvalidDataException("현지화 CSV에 잘못되었거나 중복된 행이 있습니다.");
        }

        var entries = new List<Entry>();
        foreach (var (textId, name) in rows)
        {
            if (textId is < 1_000_000 or >= 1_100_000 ||
                !rows.TryGetValue(textId + 100_000, out var description))
                continue;

            string en = Clean(name.English);
            string ko = Clean(name.Korean);
            if (en.Length == 0 && ko.Length == 0)
                continue;

            string body = Clean(description.Korean);
            if (body.Length == 0)
                body = Clean(description.English);

            entries.Add(new Entry(Category.Item, ko, en, "", body,
                source + " · 아이템 후보, 텍스트 ID 기준", textId - 1_000_000, InferredId: true));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("인식할 수 있는 아이템 이름과 설명 쌍이 없습니다.");
        return entries;
    }

    private static string Clean(string text)
    {
        if (text is "[NO_SOURCE]")
            return "";
        return text.Replace(@"\n", "\n").Replace(@"\t", "\t").Trim();
    }

    private static string Normalize(string text) => text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static int Score(Entry entry, string[] terms)
    {
        var names = new[] { Normalize(entry.Korean), Normalize(entry.English), Normalize(entry.Chinese), Normalize(entry.ChineseAlias) };
        string id = entry.ItemId?.ToString(CultureInfo.InvariantCulture) ?? "";
        string details = Normalize(entry.Details);
        int total = 0;
        foreach (string term in terms)
        {
            int score = id == term ? 100 : details.Contains(term, StringComparison.Ordinal) ? 1 : 0;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                int match = name == term ? 90 : name.StartsWith(term, StringComparison.Ordinal) ? 60 :
                    name.Contains(term, StringComparison.Ordinal) ? 30 : 0;
                if (match > 0)
                    score = Math.Max(score, match + (i == 0 ? 5 : 0));
            }

            if (score == 0)
                return 0;
            total += score;
        }

        return total;
    }
}
