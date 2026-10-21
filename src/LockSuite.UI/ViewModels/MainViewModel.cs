using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using LockSuite.Core;

namespace LockSuite.UI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LockSuiteClient _client = new();
    private readonly DispatcherTimer _refreshTimer;

    private bool _isConnected;
    private bool _isWfpActive;
    private bool _isSimulationMode;
    private bool _failClosed = true;
    private bool _learningMode = true;
    private bool _isPasswordConfigured;
    private int _activeDynamicRulesCount;
    private long _totalQueries;
    private long _allowedQueries;
    private long _blockedQueries;
    private int _whitelistPatternsCount;

    private bool _isAuthenticated;
    private string? _sessionToken;
    private string _statusMessage = "Conectando al servicio LockSuite...";
    private string _activeTab = "Estado";

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public bool IsWfpActive
    {
        get => _isWfpActive;
        set => SetProperty(ref _isWfpActive, value);
    }

    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set => SetProperty(ref _isSimulationMode, value);
    }

    public bool FailClosed
    {
        get => _failClosed;
        set => SetProperty(ref _failClosed, value);
    }

    public bool LearningMode
    {
        get => _learningMode;
        set => SetProperty(ref _learningMode, value);
    }

    public bool IsPasswordConfigured
    {
        get => _isPasswordConfigured;
        set => SetProperty(ref _isPasswordConfigured, value);
    }

    public int ActiveDynamicRulesCount
    {
        get => _activeDynamicRulesCount;
        set => SetProperty(ref _activeDynamicRulesCount, value);
    }

    public long TotalQueries
    {
        get => _totalQueries;
        set => SetProperty(ref _totalQueries, value);
    }

    public long AllowedQueries
    {
        get => _allowedQueries;
        set => SetProperty(ref _allowedQueries, value);
    }

    public long BlockedQueries
    {
        get => _blockedQueries;
        set => SetProperty(ref _blockedQueries, value);
    }

    public int WhitelistPatternsCount
    {
        get => _whitelistPatternsCount;
        set => SetProperty(ref _whitelistPatternsCount, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    public string? SessionToken => _sessionToken;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ActiveTab
    {
        get => _activeTab;
        set => SetProperty(ref _activeTab, value);
    }

    public ObservableCollection<PackViewModel> Packs { get; } = new();
    public ObservableCollection<BlockedDomainEvent> RecentBlocked { get; } = new();

    public MainViewModel()
    {
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();
        _refreshTimer.Start();

        _ = RefreshDataAsync();
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            var status = await _client.GetStatusAsync();
            if (status != null)
            {
                IsConnected = true;
                IsWfpActive = status.IsWfpActive;
                IsSimulationMode = status.IsSimulationMode;
                FailClosed = status.FailClosed;
                LearningMode = status.LearningMode;
                IsPasswordConfigured = status.IsPasswordConfigured;
                ActiveDynamicRulesCount = status.ActiveDynamicRulesCount;
                TotalQueries = status.TotalQueries;
                AllowedQueries = status.AllowedQueries;
                BlockedQueries = status.BlockedQueries;
                WhitelistPatternsCount = status.WhitelistPatternsCount;

                StatusMessage = IsWfpActive 
                    ? (IsSimulationMode ? "Modo Simulación Activo (Sin Elevación)" : "Filtro WFP Activo — Navegación Protegida")
                    : "Servicio conectado (Filtro en espera)";
            }
            else
            {
                IsConnected = false;
                StatusMessage = "Servicio LockSuite detenido o inaccesible";
            }

            // Actualizar recientes
            var blockedList = await _client.GetRecentBlockedAsync();
            RecentBlocked.Clear();
            foreach (var b in blockedList)
            {
                RecentBlocked.Add(b);
            }

            // Cargar configuración si la lista está vacía
            if (Packs.Count == 0 && IsConnected)
            {
                await ReloadConfigAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al sincronizar: {ex.Message}";
        }
    }

    public async Task ReloadConfigAsync()
    {
        var config = await _client.GetConfigAsync();
        if (config != null)
        {
            Packs.Clear();
            foreach (var p in config.Packs)
            {
                Packs.Add(PackViewModel.FromModel(p));
            }
        }
    }

    public async Task<(bool Success, string? Error)> AuthenticateTutorAsync(string password)
    {
        var (success, token, error) = await _client.AuthenticateAsync(password);
        if (success && !string.IsNullOrEmpty(token))
        {
            _sessionToken = token;
            IsAuthenticated = true;
            return (true, null);
        }
        return (false, error);
    }

    public void LockTutor()
    {
        _sessionToken = null;
        IsAuthenticated = false;
    }

    public async Task<bool> TogglePackAsync(PackViewModel pack)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.TogglePackAsync(pack.Name, pack.Enabled, _sessionToken);
        if (ok)
        {
            await RefreshDataAsync();
        }
        return ok;
    }

    public async Task<bool> AddDomainAsync(string domain, string? packName)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.AddDomainAsync(domain, packName, _sessionToken);
        if (ok)
        {
            await ReloadConfigAsync();
            await RefreshDataAsync();
        }
        return ok;
    }

    public async Task<bool> RemoveDomainAsync(string domain)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.RemoveDomainAsync(domain, _sessionToken);
        if (ok)
        {
            await ReloadConfigAsync();
            await RefreshDataAsync();
        }
        return ok;
    }

    public async Task<bool> ClearRecentBlockedAsync()
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.ClearRecentBlockedAsync(_sessionToken);
        if (ok)
        {
            RecentBlocked.Clear();
        }
        return ok;
    }

    public async Task<(bool Success, string? Error)> SetInitialPasswordAsync(string password)
    {
        return await _client.SetInitialPasswordAsync(password);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return (false, "Sesión no autenticada");
        return await _client.ChangePasswordAsync(oldPassword, newPassword, _sessionToken);
    }

    public async Task<string> RunWfpTestAsync()
    {
        if (string.IsNullOrEmpty(_sessionToken)) return "Requiere autenticación de tutor";
        return await _client.TestWfpAsync(_sessionToken);
    }
}
