using System.Text.Json;
using System.Text.RegularExpressions;

namespace UploaderCore;

/// <summary>Bir fotoğrafın arayüzde gösterilen, hesaplanmış hâli.</summary>
public record PhotoRow(
    string Name, bool Include, bool Analyzed, string? Date, IReadOnlyList<MatchedPlayer> Players, IReadOnlyList<UnresolvedNumber> Unresolved,
    bool Manual, string Title, bool CustomTitle, IReadOnlyList<string> Categories, UploadResult? Uploaded, string? UploadError, string? Error);

public enum ActivityKind { Download, Analysis, Upload }

public record Activity(ActivityKind Kind, string Message, double? Fraction);

public record UploadCheck(IReadOnlyList<string> MissingCategories, IReadOnlyList<string> TakenTitles);

/// <summary>Kimlik bilgisinin işletim sistemine özgü güvenli deposu.</summary>
public interface ICredentialStore
{
    string? Load();
    void Save(string value);
    void Clear();
}

/// <summary>
/// Açık bir fotoğraf klasörü üzerindeki tüm işlemler: satırların hesaplanması, kadro, analiz,
/// oturum ve yükleme. Arayüzden bağımsızdır; <see cref="Changed"/> herhangi bir iş parçacığından gelebilir.
/// </summary>
public sealed partial class Session(ICredentialStore? credentials = null, string? modelDirectory = null) : IDisposable
{
    public string? Folder { get; private set; }
    public Project Project { get; private set; } = new();
    public string? Username { get; private set; }
    public bool LoggedIn => _commons != null;
    /// <summary>Proje verisi değiştiğinde (analiz ya da yükleme ilerledikçe) tetiklenir.</summary>
    public event Action? Changed;

    private CommonsClient? _commons;
    private FlorenceOcr? _ocr;
    private readonly string _modelDirectory = modelDirectory ?? Environment.GetEnvironmentVariable("CDU_MODEL_DIR") ?? ModelStore.DefaultDirectory;
    private readonly Lock _saveLock = new();

    // MARK: Klasör

    public void Open(string folder)
    {
        var project = Project.Load(folder);
        project.Sync(Project.ImageFiles(folder));
        Folder = folder;
        Project = project;
        Changed?.Invoke();
    }

    public void Save()
    {
        if (Folder == null) return;
        lock (_saveLock) Project.Save(Folder);
    }

    /// <summary>Arayüz projeyi değiştirdikten sonra çağırır.</summary>
    public void Touch()
    {
        Save();
        Changed?.Invoke();
    }

    // MARK: Hesaplanan satırlar

    private List<MatchedPlayer> Players(PhotoRecord record)
    {
        var teams = Project.Match.Teams;
        if (record.Manual is { } manual)
        {
            return manual.Select(m => (m, p: teams.FirstOrDefault(t => t.Key == m.Team)?.Players.FirstOrDefault(p => p.Number == m.Number)))
                .Where(x => x.p != null)
                .Select(x => new MatchedPlayer { Team = x.m.Team, Number = x.p!.Number, Name = x.p.Name, Qid = x.p.Qid, Category = x.p.Category }).ToList();
        }
        return record.Analysis == null ? [] : Matcher.Match(record.Analysis, teams).Players;
    }

    public List<string> BaseCategories()
    {
        var m = Project.Match;
        var list = m.Category.Length > 0 ? [m.Category] : m.Teams.Select(t => t.Category).Where(c => c.Length > 0).ToList();
        return [.. list, .. m.ExtraCategories];
    }

    public List<PhotoRow> Rows()
    {
        var m = Project.Match;
        var baseCategories = BaseCategories();
        // Maç başlığı girilmediyse dosya adları klasör adından türetilir.
        var matchTitle = m.Title.Length > 0 ? m.Title : Path.GetFileName(Folder?.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        var rows = new List<PhotoRow>();
        var index = 0;
        foreach (var name in Project.Photos.Keys.Order(StringComparer.Ordinal))
        {
            index++;
            var record = Project.Photos[name];
            var players = Players(record);
            var unresolved = record.Manual == null && record.Analysis != null ? Matcher.Match(record.Analysis, m.Teams).Unresolved : [];
            var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            var title = record.Title ?? PageBuilder.Title(m.TitleTemplate, players, matchTitle, index, ext);
            var categories = players.Select(p => p.Category).OfType<string>().Concat(baseCategories).Where(c => c.Length > 0).Distinct().ToList();
            rows.Add(new PhotoRow(name, record.Include, record.Analysis != null, record.Date, players, unresolved, record.Manual != null,
                title, record.Title != null, categories, record.Uploaded, record.UploadError, record.Error));
        }
        return rows;
    }

    /// <summary>Oyuncu bulmayı engelleyen eksikler: kadrosu ya da forma rengi olmayan takımlar. Arayüz bunları uyarı olarak gösterir.</summary>
    public List<string> SetupIssues()
    {
        var issues = new List<string>();
        var teams = Project.Match.Teams;
        for (var i = 0; i < teams.Count; i++)
        {
            var name = teams[i].Label.Length > 0 ? teams[i].Label : L.T("{0}. takım", i + 1);
            if (teams[i].Players.Count == 0) issues.Add(L.T("{0}: kadro yok. \"Maç\" adımında takımı arayıp seçin ya da \"Kadrolar\" adımında oyuncuları elle ekleyin.", name));
            else if (teams[i].Colors.Count == 0) issues.Add(L.T("{0}: forma rengi seçilmedi. \"Maç\" adımında bu maçtaki forma renklerini işaretleyin; renk olmadan numaralar takımla eşleştirilemez.", name));
        }
        return issues;
    }

    /// <summary>Bir fotoğrafın kart başlığı: bulunan oyuncular ya da neden bulunamadığı.</summary>
    public static string Summary(PhotoRow row)
    {
        if (row.Players.Count > 0) return string.Join(", ", row.Players.Select(p => $"{p.Number} {p.Name}"));
        if (!row.Analyzed) return L.T("Analiz bekliyor");
        if (row.Unresolved.Count > 0) return L.T("Okunan numara kadroyla eşleşmedi: {0}", string.Join(", ", row.Unresolved.Select(u => u.Number).Distinct()));
        return L.T("Numara okunamadı");
    }

    public static List<PhotoRow> Ready(IEnumerable<PhotoRow> rows) => rows.Where(r => r.Include && r.Uploaded == null).ToList();

    public string Wikitext(PhotoRow row)
    {
        var m = Project.Match;
        var user = Username ?? L.T("Kullanıcı");
        return PageBuilder.Wikitext(PageBuilder.Describe(row.Players, m.Title, "tr"), PageBuilder.Describe(row.Players, m.Title, "en"),
            row.Date, m.Author.Length > 0 ? m.Author : $"[[User:{user}|{user}]]", m.License, row.Categories);
    }

    // MARK: Takımlar

    [GeneratedRegex(@"\s*\(.*\)$")] private static partial Regex Parenthetical();

    public async Task SetTeam(string key, string qid, CancellationToken ct = default)
    {
        var r = await Wikidata.Roster(qid, ct);
        var team = Project.Match.Teams.First(t => t.Key == key);
        var label = Parenthetical().Replace(r.Team.Label, "");
        team.Qid = qid; team.Label = label; team.Category = r.Team.Category ?? "";
        team.Players = r.Players; team.RosterSource = r.Source;
        if (team.Keywords.Count == 0) team.Keywords = [label];
        Touch();
    }

    // MARK: Analiz

    /// <summary>Verilen fotoğrafları (ya da henüz analiz edilmemişlerin tümünü) analiz eder.</summary>
    public async Task Analyze(IReadOnlyCollection<string>? only, IProgress<Activity>? progress, CancellationToken ct)
    {
        if (Folder is not { } folder) return;
        var names = Project.Photos.Where(p => only?.Contains(p.Key) ?? p.Value.Analysis == null).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return;
        progress?.Report(new Activity(ActivityKind.Download, L.T("Model hazırlanıyor…"), null));
        if (!FlorenceOcr.IsInstalled(_modelDirectory))
        {
            await ModelStore.Ensure(_modelDirectory, new Progress<(double Fraction, string File)>(p =>
                progress?.Report(new Activity(ActivityKind.Download, L.T("Model indiriliyor (yaklaşık 1 GB): {0}", p.File), p.Fraction))), ct);
        }
        if (_ocr == null)
        {
            progress?.Report(new Activity(ActivityKind.Analysis, L.T("Model yükleniyor…"), null));
            _ocr = await Task.Run(() => new FlorenceOcr(_modelDirectory), ct);
        }
        var analyzer = new ImageAnalyzer(_ocr);
        for (var i = 0; i < names.Count && !ct.IsCancellationRequested; i++)
        {
            var name = names[i];
            progress?.Report(new Activity(ActivityKind.Analysis, $"{name} ({i + 1}/{names.Count})", (double)i / names.Count));
            var path = Path.Combine(folder, name);
            var record = Project.Photos[name];
            try
            {
                var (analysis, date) = await Task.Run(() => (analyzer.Analyze(path), ImageLoader.CaptureDate(path)), CancellationToken.None);
                record.Analysis = analysis; record.Date = date; record.Error = null;
            }
            catch (Exception e) { record.Error = e.Message; }
            Touch();
        }
    }

    // MARK: Commons

    private string Serialize(CommonsAuth auth) => JsonSerializer.Serialize(auth, Project.Json);

    /// <summary>Saklanan kimlik bilgisiyle sessizce oturum açmayı dener.</summary>
    public async Task<bool> RestoreLogin(CancellationToken ct = default)
    {
        try
        {
            if (credentials?.Load() is not { } stored || JsonSerializer.Deserialize<CommonsAuth>(stored, Project.Json) is not { } auth) return false;
            await Login(auth, ct);
            return true;
        }
        catch (Exception) { return false; }
    }

    public async Task Login(CommonsAuth auth, CancellationToken ct = default)
    {
        var client = new CommonsClient(auth, authChanged: updated => credentials?.Save(Serialize(updated)));
        try { Username = await client.Login(ct); }
        catch { client.Dispose(); throw; }
        _commons?.Dispose();
        _commons = client;
        credentials?.Save(Serialize(auth));
        Changed?.Invoke();
    }

    public void Logout()
    {
        _commons?.Dispose();
        _commons = null; Username = null;
        credentials?.Clear();
        Changed?.Invoke();
    }

    /// <summary>Yüklenecek dosyaların kategorileri var mı, dosya adları boş mu?</summary>
    public async Task<UploadCheck> Check(CancellationToken ct = default)
    {
        var ready = Ready(Rows());
        var categories = ready.SelectMany(r => r.Categories).Distinct().ToList();
        var client = _commons ?? new CommonsClient(null);
        try
        {
            var existing = await client.Existing(categories.Select(c => $"Category:{c}").Concat(ready.Select(r => $"File:{r.Title}")), ct);
            return new UploadCheck(categories.Where(c => !existing.GetValueOrDefault($"Category:{c}")).ToList(), ready.Select(r => r.Title).Where(t => existing.GetValueOrDefault($"File:{t}")).ToList());
        }
        finally { if (client != _commons) client.Dispose(); }
    }

    /// <summary>Dahil edilen ve henüz yüklenmemiş fotoğrafları yükler. Maç kategorisi hatası <paramref name="warning"/> ile bildirilir.</summary>
    public async Task Upload(IProgress<Activity>? progress, Action<string>? warning, CancellationToken ct)
    {
        if (Folder is not { } folder || _commons is not { } commons) throw new ApiException(L.T("Önce Commons oturumu açın"));
        var list = Ready(Rows());
        var match = Project.Match;
        progress?.Report(new Activity(ActivityKind.Upload, L.T("Yükleme başlıyor…"), 0));
        try
        {
            if (match.CreateCategory && match.Category.Length > 0 && !(await commons.Existing([$"Category:{match.Category}"], ct)).GetValueOrDefault($"Category:{match.Category}"))
            {
                var parents = string.Join("\n", match.Teams.Select(t => t.Category).Where(c => c.Length > 0).Select(c => $"[[Category:{c}]]"));
                await commons.CreateCategory(match.Category, parents + "\n", ct);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { warning?.Invoke(L.T("Maç kategorisi oluşturulamadı: {0}", e.Message)); }

        for (var i = 0; i < list.Count && !ct.IsCancellationRequested; i++)
        {
            var row = list[i];
            var record = Project.Photos[row.Name];
            var index = i;
            var label = $"{row.Title} ({i + 1}/{list.Count})";
            progress?.Report(new Activity(ActivityKind.Upload, label, (double)i / list.Count));
            try
            {
                var outcome = await commons.Upload(Path.Combine(folder, row.Name), row.Title, Wikitext(row), "Uploaded with Commons Diversified Uploader",
                    new Progress<double>(f => progress?.Report(new Activity(ActivityKind.Upload, label, (index + f) / list.Count))), ct);
                if (outcome.Success)
                {
                    record.Uploaded = new UploadResult(outcome.Title!, outcome.Url);
                    record.UploadError = null;
                    if (outcome.PageId is { } pageId)
                    {
                        try { await commons.AddDepicts(pageId, row.Players.Select(p => p.Qid).OfType<string>(), ct); }
                        catch (Exception e) when (e is not OperationCanceledException) { record.UploadError = L.T("Yüklendi, ancak görünen kişiler eklenemedi: {0}", e.Message); }
                    }
                }
                else record.UploadError = outcome.BlockedReason;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { record.UploadError = e.Message; }
            Touch();
        }
    }

    public void Dispose()
    {
        _commons?.Dispose();
        _ocr?.Dispose();
    }
}
