using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Server.Auth;

public sealed record TokenClaims(string UserId, string Name, bool Guest, long Expires);

/// <summary>Compact HMAC-signed bearer tokens: base64url(payload).base64url(signature).</summary>
public sealed class TokenService
{
    private readonly byte[] _key;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(60);

    public TokenService(string secret)
    {
        if (secret.Length < 16) throw new ArgumentException("Token secret must be at least 16 characters");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Issue(string userId, string name, bool guest)
    {
        var claims = new TokenClaims(userId, name, guest, DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds());
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Base64Url(Sign(payload));
    }

    public TokenClaims? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        int dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;
        string payload = token[..dot];
        byte[] given;
        try
        {
            given = FromBase64Url(token[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(given, Sign(payload))) return null;
        try
        {
            var claims = JsonSerializer.Deserialize<TokenClaims>(FromBase64Url(payload));
            if (claims == null || claims.Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return claims;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return null;
        }
    }

    private byte[] Sign(string payload) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}

public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out int iterations)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
