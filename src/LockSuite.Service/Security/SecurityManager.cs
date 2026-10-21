using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Security;

public enum AuthResult
{
    Success,
    InvalidPassword,
    LockedOut,
    NotInitialized
}

/// <summary>
/// Gestiona la contraseña maestra (hash con sal, PBKDF2 + SHA256),
/// almacenamiento protegido con DPAPI (LocalMachine) y protección anti-fuerza bruta.
/// </summary>
public sealed class SecurityManager
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly byte[] Entropy = "LockSuite-Master-Key-Entropy-v1"u8.ToArray();

    private readonly string _authFilePath;
    private readonly ILogger<SecurityManager> _logger;
    private readonly object _lock = new();

    // Anti-fuerza bruta
    private int _consecutiveFailedAttempts;
    private DateTimeOffset _lockoutUntil = DateTimeOffset.MinValue;

    // Tokens de sesión autenticada (Token -> Expiration)
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeSessions = new();
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(15);

    public SecurityManager(ILogger<SecurityManager> logger, string? customAuthPath = null)
    {
        _logger = logger;
        if (!string.IsNullOrEmpty(customAuthPath))
        {
            _authFilePath = customAuthPath;
        }
        else
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            _authFilePath = Path.Combine(programData, "LockSuite", "config", "auth.dat");
        }
    }

    public bool IsPasswordSet()
    {
        lock (_lock)
        {
            return File.Exists(_authFilePath);
        }
    }

    public bool SetInitialPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;

        lock (_lock)
        {
            if (File.Exists(_authFilePath))
            {
                _logger.LogWarning("Intento de establecer contraseña inicial cuando ya existe una configurada");
                return false;
            }

            SavePasswordInternal(password);
            _logger.LogInformation("Contraseña maestra inicial configurada exitosamente");
            return true;
        }
    }

    public bool ChangePassword(string oldPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) return false;

        var authResult = Authenticate(oldPassword, out _);
        if (authResult != AuthResult.Success)
        {
            _logger.LogWarning("Fallo al cambiar contraseña: autenticación de contraseña anterior denegada ({Result})", authResult);
            return false;
        }

        lock (_lock)
        {
            SavePasswordInternal(newPassword);
            _activeSessions.Clear(); // Invalida sesiones anteriores
            _logger.LogInformation("Contraseña maestra actualizada exitosamente");
            return true;
        }
    }

    public AuthResult Authenticate(string password, out string? sessionToken)
    {
        sessionToken = null;

        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _lockoutUntil)
            {
                var remaining = _lockoutUntil - now;
                _logger.LogWarning("Intento de login bloqueado por límite de intentos. Restante: {Seconds}s", 
                    (int)remaining.TotalSeconds);
                return AuthResult.LockedOut;
            }

            // El bloqueo expiro: reiniciar el contador para no re-bloquear al primer error posterior.
            if (_lockoutUntil != DateTimeOffset.MinValue)
            {
                _consecutiveFailedAttempts = 0;
                _lockoutUntil = DateTimeOffset.MinValue;
            }

            if (!File.Exists(_authFilePath))
            {
                _logger.LogWarning("Autenticación solicitada pero la contraseña maestra no fue inicializada");
                return AuthResult.NotInitialized;
            }

            if (!VerifyPasswordInternal(password))
            {
                _consecutiveFailedAttempts++;
                if (_consecutiveFailedAttempts >= 10)
                {
                    _lockoutUntil = now.AddMinutes(5);
                    _logger.LogWarning("Límite crítico de fallos alcanzado ({Attempts}). Bloqueo por 5 minutos.", _consecutiveFailedAttempts);
                }
                else if (_consecutiveFailedAttempts >= 5)
                {
                    _lockoutUntil = now.AddSeconds(60);
                    _logger.LogWarning("Límite de fallos alcanzado ({Attempts}). Bloqueo por 60 segundos.", _consecutiveFailedAttempts);
                }

                return AuthResult.InvalidPassword;
            }

            // Exito: reiniciar contador de fallos
            _consecutiveFailedAttempts = 0;
            _lockoutUntil = DateTimeOffset.MinValue;

            // Generar token de sesion
            sessionToken = Guid.NewGuid().ToString("N");
            _activeSessions[sessionToken] = now.Add(SessionDuration);
            _logger.LogInformation("Autenticación exitosa. Sesión emitida válida por {Minutes} minutos", SessionDuration.TotalMinutes);
            return AuthResult.Success;
        }
    }

    public bool ValidateSession(string? sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return false;

        if (_activeSessions.TryGetValue(sessionToken, out var expiration))
        {
            if (DateTimeOffset.UtcNow < expiration)
            {
                // Renueva la ventana de sesión en actividad
                _activeSessions[sessionToken] = DateTimeOffset.UtcNow.Add(SessionDuration);
                return true;
            }
            _activeSessions.TryRemove(sessionToken, out _);
        }

        return false;
    }

    public void RevokeSession(string sessionToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            _activeSessions.TryRemove(sessionToken, out _);
        }
    }

    private void SavePasswordInternal(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        byte[] payload = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, payload, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, payload, SaltSize, HashSize);

        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(payload, Entropy, DataProtectionScope.LocalMachine);
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback si no está en Windows
            encrypted = payload;
        }

        string? dir = Path.GetDirectoryName(_authFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string temp = _authFilePath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, _authFilePath, overwrite: true);
    }

    private bool VerifyPasswordInternal(string password)
    {
        try
        {
            byte[] encrypted = File.ReadAllBytes(_authFilePath);
            byte[] payload;
            try
            {
                payload = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.LocalMachine);
            }
            catch (PlatformNotSupportedException)
            {
                payload = encrypted;
            }

            if (payload.Length != SaltSize + HashSize) return false;

            byte[] salt = new byte[SaltSize];
            byte[] storedHash = new byte[HashSize];
            Buffer.BlockCopy(payload, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(payload, SaltSize, storedHash, 0, HashSize);

            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al desencriptar o verificar contraseña maestra");
            return false;
        }
    }
}
