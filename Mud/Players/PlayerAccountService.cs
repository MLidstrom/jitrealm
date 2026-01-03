using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JitRealm.Mud.Configuration;

namespace JitRealm.Mud.Players;

/// <summary>
/// Service for managing player accounts - creation, authentication, and persistence.
/// Player files are stored at players/{first_letter}/{name}.json.
/// </summary>
public sealed class PlayerAccountService
{
    private readonly string _playersDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Fully-qualified path to the players directory used by this instance.
    /// </summary>
    public string PlayersDirectory => _playersDirectory;

    /// <summary>
    /// Minimum player name length.
    /// </summary>
    public const int MinNameLength = 3;

    /// <summary>
    /// Maximum player name length.
    /// </summary>
    public const int MaxNameLength = 20;

    /// <summary>
    /// Minimum password length.
    /// </summary>
    public const int MinPasswordLength = 4;

    public PlayerAccountService(DriverSettings settings)
    {
        // IMPORTANT:
        // Use the current working directory as the base for relative paths so running from the repo root
        // finds existing player files under ./players. AppContext.BaseDirectory points at bin/{config}/net8.0/,
        // which would create a separate players directory in the output folder and make logins "not found".
        _playersDirectory = Path.IsPathRooted(settings.Paths.PlayersDirectory)
            ? settings.Paths.PlayersDirectory
            : Path.GetFullPath(settings.Paths.PlayersDirectory, Directory.GetCurrentDirectory());
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure players directory exists
        Directory.CreateDirectory(_playersDirectory);
    }

    /// <summary>
    /// Check if a player account exists.
    /// </summary>
    public Task<bool> PlayerExistsAsync(string name)
    {
        var filePath = GetPlayerFilePath(name);
        return Task.FromResult(File.Exists(filePath));
    }

    /// <summary>
    /// Returns true if any player account files exist on disk.
    /// Useful for first-run UX (prompt users to create a player).
    /// </summary>
    public bool AnyPlayersExist()
    {
        try
        {
            return Directory.EnumerateFiles(_playersDirectory, "*.json", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validate a player name.
    /// </summary>
    /// <returns>Error message or null if valid.</returns>
    public static string? ValidatePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name cannot be empty.";

        if (name.Length < MinNameLength)
            return $"Name must be at least {MinNameLength} characters.";

        if (name.Length > MaxNameLength)
            return $"Name cannot be longer than {MaxNameLength} characters.";

        if (!Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9]*$"))
            return "Name must start with a letter and contain only letters and numbers.";

        return null;
    }

    /// <summary>
    /// Validate a password.
    /// </summary>
    /// <returns>Error message or null if valid.</returns>
    public static string? ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Password cannot be empty.";

        if (password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";

        return null;
    }

    /// <summary>
    /// Create a new player account.
    /// </summary>
    public async Task<PlayerAccountData> CreateAccountAsync(string name, string password)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);

        var account = new PlayerAccountData
        {
            Name = name,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
            IsWizard = false
        };

        await SavePlayerDataAsync(name, account);
        return account;
    }

    /// <summary>
    /// Validate player credentials.
    /// </summary>
    /// <returns>True if credentials are valid.</returns>
    public async Task<bool> ValidateCredentialsAsync(string name, string password)
    {
        var account = await LoadPlayerDataAsync(name);
        if (account is null)
            return false;

        var hash = HashPassword(password, account.PasswordSalt);
        return hash == account.PasswordHash;
    }

    /// <summary>
    /// Load player data from file.
    /// </summary>
    public async Task<PlayerAccountData?> LoadPlayerDataAsync(string name)
    {
        var filePath = GetPlayerFilePath(name);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<PlayerAccountData>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerAccountService] Error loading player {name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save player data to file.
    /// </summary>
    public async Task SavePlayerDataAsync(string name, PlayerAccountData data)
    {
        var filePath = GetPlayerFilePath(name);
        var directory = Path.GetDirectoryName(filePath);

        if (directory is not null)
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Update last login timestamp.
    /// </summary>
    public async Task UpdateLastLoginAsync(string name)
    {
        var account = await LoadPlayerDataAsync(name);
        if (account is not null)
        {
            account.LastLogin = DateTime.UtcNow;
            await SavePlayerDataAsync(name, account);
        }
    }

    /// <summary>
    /// Get the directory path for a player's data.
    /// Format: players/{first_letter}/{name}/
    /// This directory contains the player's save file and any wizard files (home.cs, etc).
    /// </summary>
    public string GetPlayerDirectory(string name)
    {
        var normalizedName = name.ToLowerInvariant();
        var firstLetter = normalizedName[0].ToString();
        return Path.Combine(_playersDirectory, firstLetter, normalizedName);
    }

    /// <summary>
    /// Get the file path for a player's save file.
    /// Format: players/{first_letter}/{name}/{name}.json
    /// </summary>
    public string GetPlayerFilePath(string name)
    {
        var normalizedName = name.ToLowerInvariant();
        return Path.Combine(GetPlayerDirectory(name), $"{normalizedName}.json");
    }

    /// <summary>
    /// Generate a random 16-byte salt as base64.
    /// </summary>
    private static string GenerateSalt()
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        return Convert.ToBase64String(salt);
    }

    /// <summary>
    /// Hash a password with salt using SHA256.
    /// </summary>
    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var combined = new byte[saltBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(saltBytes, 0, combined, 0, saltBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, combined, saltBytes.Length, passwordBytes.Length);

        var hash = SHA256.HashData(combined);
        return Convert.ToBase64String(hash);
    }
}
