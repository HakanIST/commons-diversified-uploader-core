using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace UploaderCore;

/// <summary>OAuth 2.0 ile alınan belirteçler. Erişim belirteci kısa ömürlüdür; yenileme belirteci her kullanımda değişir.</summary>
public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool NeedsRefresh => ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(5);
}

public record OAuthRequest(string Url, string Verifier, string State);

/// <summary>Wikimedia hesabıyla oturum: yetkilendirme kodu + PKCE (gizli anahtarı olmayan, herkese açık istemci).</summary>
public static class OAuth
{
    /// <summary>Meta-Wiki'de kayıtlı herkese açık istemci "Commons Diversified Uploader" (gizli değildir).</summary>
    public const string ClientId = "8e53b4967c6c278a2a23ec723e626b2f";
    public const string CallbackScheme = "tr.tall.commonsdiversifieduploader";
    /// <summary>İstemci kaydındaki geri dönüş adresiyle birebir aynı olmalıdır.</summary>
    public const string RedirectUri = CallbackScheme + "://oauth/callback";
    private const string Endpoint = "https://commons.wikimedia.org/w/rest.php/oauth2";

    internal static string Base64Url(ReadOnlySpan<byte> data) => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Tarayıcıda açılacak yetkilendirme adresini ve onunla eşleşen doğrulayıcıyı üretir.</summary>
    public static OAuthRequest MakeRequest(string clientId = ClientId)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var url = Http.Query($"{Endpoint}/authorize", new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = RedirectUri,
            ["code_challenge"] = Challenge(verifier), ["code_challenge_method"] = "S256", ["state"] = state,
        });
        return new OAuthRequest(url, verifier, state);
    }

    /// <summary>Geri dönüş adresinden yetkilendirme kodunu çıkarır; durum değeri eşleşmelidir.</summary>
    public static string Code(string callback, OAuthRequest request)
    {
        var q = callback.IndexOf('?');
        var items = HttpUtility.ParseQueryString(q >= 0 ? callback[(q + 1)..] : "");
        if (items["error"] is { } error) throw new ApiException(L.T("Oturum açılamadı: {0}", items["error_description"] ?? error));
        if (items["state"] != request.State || string.IsNullOrEmpty(items["code"])) throw new ApiException(L.T("Oturum açılamadı: kimlik bilgisi kabul edilmedi"));
        return items["code"]!;
    }

    private static async Task<OAuthTokens> Token(Dictionary<string, string> fields, CancellationToken ct)
    {
        var d = await Http.SendJson(Http.Shared, () => new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/access_token") { Content = new FormUrlEncodedContent(fields) }, ct, retries: 1);
        if (d["access_token"]?.GetValue<string>() is not { } access)
        {
            string? Text(string key) => d[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            throw new ApiException(L.T("Oturum açılamadı: {0}", Text("error_description") ?? Text("message") ?? Text("hint") ?? Text("error") ?? L.T("bilinmeyen hata")));
        }
        var lifetime = d["expires_in"] is JsonValue e && e.TryGetValue<double>(out var seconds) ? seconds : 3600;
        return new OAuthTokens(access, d["refresh_token"]?.GetValue<string>(), DateTimeOffset.UtcNow.AddSeconds(lifetime));
    }

    public static Task<OAuthTokens> Exchange(string code, OAuthRequest request, string clientId = ClientId, CancellationToken ct = default) =>
        Token(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri, ["client_id"] = clientId, ["code_verifier"] = request.Verifier }, ct);

    public static Task<OAuthTokens> Refresh(string refreshToken, string clientId = ClientId, CancellationToken ct = default) =>
        Token(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId }, ct);
}
