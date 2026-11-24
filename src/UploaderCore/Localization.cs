using System.Globalization;

namespace UploaderCore;

/// <summary>
/// Kullanıcıya gösterilen metinler. Kaynak dil Türkçedir: anahtar metnin kendisidir,
/// arayüz dili Türkçe değilse İngilizce karşılığı kullanılır. Uygulamalar kendi
/// metinlerini <see cref="Register"/> ile ekler.
/// </summary>
public static class L
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["İstek başarısız"] = "Request failed",
        ["Beklenmeyen yanıt"] = "Unexpected response",
        ["bilinmeyen hata"] = "unknown error",
        ["Oturum açılamadı: {0}"] = "Could not log in: {0}",
        ["Oturum açılamadı: kimlik bilgisi kabul edilmedi"] = "Could not log in: the credentials were not accepted",
        ["Bu kimlik bilgisinin dosya yükleme izni yok"] = "These credentials do not have permission to upload files",
        ["Aynı dosya Commons'ta zaten var: {0}"] = "The same file is already on Commons: {0}",
        ["Bu adla bir dosya zaten var"] = "A file with this name already exists",
        ["Bu dosya daha önce Commons'tan silinmiş"] = "This file was previously deleted from Commons",
        ["Dosya adı kabul edilmedi"] = "The file name was not accepted",
        ["Önce Commons oturumu açın"] = "Log in to Commons first",
        ["Yükleme başlatılamadı"] = "Could not start the upload",
        ["Uyarı: {0}"] = "Warning: {0}",
        ["Yükleme tamamlanamadı: {0}"] = "Could not complete the upload: {0}",
        ["Kategori oluşturulamadı"] = "Could not create the category",
        ["Görüntü açılamadı: {0}"] = "Could not open the image: {0}",
        ["Model indirilemedi: {0}"] = "Could not download the model: {0}",
        ["Wikidata ögesi bulunamadı: {0}"] = "Wikidata item not found: {0}",
        ["Kullanıcı"] = "User",
        ["Model hazırlanıyor…"] = "Preparing the model…",
        ["Model indiriliyor (yaklaşık 1 GB): {0}"] = "Downloading the model (about 1 GB): {0}",
        ["Model yükleniyor…"] = "Loading the model…",
        ["Yükleme başlıyor…"] = "Starting the upload…",
        ["Maç kategorisi oluşturulamadı: {0}"] = "Could not create the match category: {0}",
        ["Yüklendi, ancak görünen kişiler eklenemedi: {0}"] = "Uploaded, but the depicted people could not be added: {0}",
        ["{0}. takım"] = "Team {0}", ["Analiz bekliyor"] = "Waiting for analysis", ["Numara okunamadı"] = "No number could be read",
        ["Okunan numara kadroyla eşleşmedi: {0}"] = "Numbers read but not matched to the roster: {0}",
        ["{0}: kadro yok. \"Maç\" adımında takımı arayıp seçin ya da \"Kadrolar\" adımında oyuncuları elle ekleyin."] =
            "{0}: no roster. Search for the team and pick it in the \"Match\" step, or add the players by hand in the \"Rosters\" step.",
        ["{0}: forma rengi seçilmedi. \"Maç\" adımında bu maçtaki forma renklerini işaretleyin; renk olmadan numaralar takımla eşleştirilemez."] =
            "{0}: no jersey colour selected. Tick this match's jersey colours in the \"Match\" step; without a colour, numbers cannot be matched to the team.",
        ["siyah"] = "black", ["beyaz"] = "white", ["gri"] = "grey", ["kırmızı"] = "red", ["turuncu"] = "orange",
        ["sarı"] = "yellow", ["yeşil"] = "green", ["turkuaz"] = "turquoise", ["mavi"] = "blue", ["lacivert"] = "navy",
        ["mor"] = "purple", ["pembe"] = "pink", ["bordo"] = "maroon",
    };

    /// <summary>Sınamalar ve elle seçim için; null ise sistemin arayüz dili kullanılır.</summary>
    public static string? Language { get; set; }

    public static bool IsTurkish => (Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "tr";

    public static void Register(IReadOnlyDictionary<string, string> english)
    {
        foreach (var (k, v) in english) English[k] = v;
    }

    public static string T(string turkish, params object?[] args)
    {
        var text = IsTurkish ? turkish : English.GetValueOrDefault(turkish, turkish);
        return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
    }
}
