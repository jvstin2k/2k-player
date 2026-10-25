using System.Runtime.InteropServices;
using System.Text.Json;
using UhfPlayer.Core.Models;
using Windows.Security.Credentials;

namespace UhfPlayer.App;

/// <summary>
/// Persists configured sources in %LOCALAPPDATA%\UhfPlayer\sources.json. Everything that can carry credentials
/// (password, playlist URL with username and password, EPG URL) goes to the Windows Credential Locker instead,
/// one entry per source, visible under "Anmeldeinformationsverwaltung → Webanmeldeinformationen".
/// </summary>
internal static class SourceStore
{
    private const string Resource = "UhfPlayer";
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "sources.json");

    private sealed record Secret(string? M3uLocation, string? Password, string? EpgUrl);

    public static List<SourceConfig> Load()
    {
        if (!File.Exists(FilePath))
            return [];
        var stored = JsonSerializer.Deserialize<List<SourceConfig>>(File.ReadAllText(FilePath)) ?? [];
        if (OpenVault() is not { } vault)
            return stored;
        var sources = stored.ConvertAll(s => Retrieve(vault, s.Id) is { } secret
            ? s with { M3uLocation = secret.M3uLocation, Password = secret.Password, EpgUrl = secret.EpgUrl }
            : s);
        // Files from older versions still hold the secrets in plain text; this moves them into the locker.
        if (stored.Any(s => s.M3uLocation is not null || s.Password is not null || s.EpgUrl is not null))
            Save(sources);
        return sources;
    }

    public static void Save(List<SourceConfig> sources)
    {
        var toFile = sources;
        try
        {
            if (OpenVault() is { } vault)
            {
                foreach (var old in FindAll(vault).Where(c => !sources.Exists(s => s.Id.ToString() == c.UserName)))
                    vault.Remove(old);
                foreach (var s in sources)
                    vault.Add(new PasswordCredential(Resource, s.Id.ToString(), JsonSerializer.Serialize(new Secret(s.M3uLocation, s.Password, s.EpgUrl))));
                toFile = sources.ConvertAll(s => s with { M3uLocation = null, Password = null, EpgUrl = null });
            }
        }
        catch (Exception ex)
        {
            // Without the locker the secrets stay in the file rather than being lost.
            CrashLog.Write(ex);
        }
        File.WriteAllText(FilePath, JsonSerializer.Serialize(toFile, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static PasswordVault? OpenVault()
    {
        try
        {
            return new PasswordVault();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return null;
        }
    }

    private static Secret? Retrieve(PasswordVault vault, Guid id)
    {
        try
        {
            var credential = vault.Retrieve(Resource, id.ToString());
            credential.RetrievePassword();
            return JsonSerializer.Deserialize<Secret>(credential.Password);
        }
        catch (COMException)
        {
            return null; // not in the locker (yet)
        }
    }

    private static IReadOnlyList<PasswordCredential> FindAll(PasswordVault vault)
    {
        try
        {
            return vault.FindAllByResource(Resource);
        }
        catch (COMException)
        {
            return []; // the locker throws instead of returning an empty list
        }
    }
}
