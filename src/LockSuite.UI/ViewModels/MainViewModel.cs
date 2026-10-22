using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private bool _isDnsListening;
    private bool _failClosed = true;
    private bool _learningMode = true;
    private bool _allowLocalNetwork;
    private bool _captivePortalActive;
    private string _captivePortalBannerText = "";
    private bool _isPasswordConfigured;
    private int _activeDynamicRulesCount;
    private long _totalQueries;
    private long _allowedQueries;
    private long _blockedQueries;
    private int _whitelistPatternsCount;

    // Modo de prueba
    private bool _isTrialActive;
    private bool _isTrialAwaiting;
    private bool _trialRequireTutorToRemove;
    private string _trialBannerText = "";
    private bool _trialPromptShown;

    private bool _isAuthenticated;
    private string? _sessionToken;
    private string _statusMessage = "Conectando al servicio LockSuite...";
    private string _activeTab = "Estado";

    /// <summary>Se dispara (una vez) cuando la prueba venció y espera la decisión del usuario.</summary>
    public event Action? TrialAwaitingDecision;

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
                OnPropertyChanged(nameof(IsDnsDown));
        }
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

    public bool IsDnsListening
    {
        get => _isDnsListening;
        set
        {
            if (SetProperty(ref _isDnsListening, value))
                OnPropertyChanged(nameof(IsDnsDown));
        }
    }

    /// <summary>
    /// True cuando el servicio está conectado pero el resolutor DNS local NO está escuchando
    /// (p. ej. el puerto 53 lo ocupa Docker/WSL/VPN). En ese estado el filtrado por dominio no
    /// funciona y conviene avisarlo con un cartel, no solo con un renglón de detalle (caso 3.3).
    /// </summary>
    public bool IsDnsDown => _isConnected && !_isDnsListening;

    public bool AllowLocalNetwork
    {
        get => _allowLocalNetwork;
        set => SetProperty(ref _allowLocalNetwork, value);
    }

    public bool CaptivePortalActive
    {
        get => _captivePortalActive;
        set => SetProperty(ref _captivePortalActive, value);
    }

    public string CaptivePortalBannerText
    {
        get => _captivePortalBannerText;
        set => SetProperty(ref _captivePortalBannerText, value);
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

    public bool IsTrialActive
    {
        get => _isTrialActive;
        set => SetProperty(ref _isTrialActive, value);
    }

    public bool IsTrialAwaiting
    {
        get => _isTrialAwaiting;
        set => SetProperty(ref _isTrialAwaiting, value);
    }

    public bool TrialRequireTutorToRemove
    {
        get => _trialRequireTutorToRemove;
        set => SetProperty(ref _trialRequireTutorToRemove, value);
    }

    public string TrialBannerText
    {
        get => _trialBannerText;
        set => SetProperty(ref _trialBannerText, value);
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

    /// <summary>
    /// Paquetes que se muestran como tarjeta "Permitir X" (Gmail, Chat, Drive, Fotos, prog.co.il,
    /// mitmachim.top), en el orden del catalogo. Son las mismas instancias que en <see cref="Packs"/>.
    /// </summary>
    public ObservableCollection<PackViewModel> ServicePacks { get; } = new();

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

    // Evita refrescos superpuestos: el temporizador dispara cada 3 s y, si el servicio tarda en
    // responder, antes se acumulaban consultas (y ventanas "colgadas") una encima de otra.
    private int _refreshing;

    public async Task RefreshDataAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var status = await _client.GetStatusAsync();
            if (status != null)
            {
                IsConnected = true;
                IsWfpActive = status.IsWfpActive;
                IsSimulationMode = status.IsSimulationMode;
                IsDnsListening = status.IsDnsListening;
                FailClosed = status.FailClosed;
                LearningMode = status.LearningMode;
                IsPasswordConfigured = status.IsPasswordConfigured;
                ActiveDynamicRulesCount = status.ActiveDynamicRulesCount;
                TotalQueries = status.TotalQueries;
                AllowedQueries = status.AllowedQueries;
                BlockedQueries = status.BlockedQueries;
                WhitelistPatternsCount = status.WhitelistPatternsCount;
                AllowLocalNetwork = status.AllowLocalNetwork;
                CaptivePortalActive = status.CaptivePortalActive;
                if (status.CaptivePortalActive)
                {
                    int s = status.CaptivePortalRemainingSeconds;
                    CaptivePortalBannerText = $"Modo portal cautivo activo: quedan {s / 60} min {s % 60} s. " +
                        "Podés abrir la página de la red Wi-Fi. Al terminar, el filtro vuelve solo.";
                }

                StatusMessage = IsWfpActive
                    ? (IsSimulationMode ? "Modo Simulación Activo (Sin Elevación)" : "Filtro WFP Activo — Navegación Protegida")
                    : "Servicio conectado (Filtro en espera)";
            }
            else
            {
                // Sin servicio: la ventana se muestra igual al instante. No se encadenan mas consultas
                // (cada una esperaria otro timeout); se reintenta en el proximo ciclo del temporizador.
                IsConnected = false;
                StatusMessage = "Servicio LockSuite detenido o inaccesible";
                return;
            }

            // Actualizar recientes
            var blockedList = await _client.GetRecentBlockedAsync();
            RecentBlocked.Clear();
            foreach (var b in blockedList)
            {
                RecentBlocked.Add(b);
            }

            // Estado del modo de prueba
            await RefreshTrialAsync();

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
        finally
        {
            System.Threading.Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private async Task RefreshTrialAsync()
    {
        var trial = await _client.GetTrialStatusAsync();
        if (trial == null || !trial.Enabled)
        {
            IsTrialActive = false;
            IsTrialAwaiting = false;
            _trialPromptShown = false;
            return;
        }

        TrialRequireTutorToRemove = trial.RequireTutorToRemove;

        if (trial.State == "Active")
        {
            IsTrialActive = true;
            IsTrialAwaiting = false;
            _trialPromptShown = false;
            int total = trial.RemainingSeconds;
            int h = total / 3600;
            int m = (total % 3600) / 60;
            TrialBannerText = $"Modo de prueba: quedan {h} h {m} min. Al terminar se te preguntará si conservar LockSuite o quitarlo.";
        }
        else if (trial.State == "AwaitingDecision")
        {
            IsTrialActive = false;
            IsTrialAwaiting = true;
            TrialBannerText = "El período de prueba terminó. Elegí si conservar LockSuite de forma permanente o quitarlo.";
            if (!_trialPromptShown)
            {
                _trialPromptShown = true;
                TrialAwaitingDecision?.Invoke();
            }
        }
        else
        {
            // None / Kept / Removing
            IsTrialActive = false;
            IsTrialAwaiting = false;
        }
    }

    public async Task ReloadConfigAsync()
    {
        var config = await _client.GetConfigAsync();
        if (config != null)
        {
            Packs.Clear();
            ServicePacks.Clear();
            foreach (var p in config.Packs ?? new List<DomainPack>())
            {
                if (p is null) continue;
                Packs.Add(PackViewModel.FromModel(p));
            }

            // Tarjetas de servicios: en el orden del catalogo de presets.
            foreach (var def in Presets.Catalog.Where(d => d.IsService))
            {
                var vm = Packs.FirstOrDefault(p => string.Equals(p.Id, def.Id, StringComparison.OrdinalIgnoreCase));
                if (vm != null) ServicePacks.Add(vm);
            }
        }
    }

    /// <summary>
    /// True si el host esta en un paquete de bloqueo activo (p. ej. el buscador de Google o Gemini):
    /// agregarlo a la lista blanca no lo habilitaria.
    /// </summary>
    public bool IsHardBlocked(string host)
    {
        var blockPatterns = Packs.Where(p => p.Blocks && p.Enabled).SelectMany(p => p.Domains);
        return DomainMatcher.MatchesAny(blockPatterns, host);
    }

    /// <summary>Paquetes del tutor (no de fabrica ni de bloqueo) para el dialogo "Agregar dominio".</summary>
    public List<string> CustomPackNames()
    {
        var names = Packs.Where(p => !p.BuiltIn && !p.Blocks).Select(p => p.Name).ToList();
        if (!names.Any(n => string.Equals(n, Presets.CustomPackName, StringComparison.OrdinalIgnoreCase)))
        {
            names.Insert(0, Presets.CustomPackName);
        }
        return names;
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

    public async Task<bool> SetLearningModeAsync(bool enabled)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.SetLearningModeAsync(enabled, _sessionToken);
        if (ok)
        {
            await RefreshDataAsync();
        }
        return ok;
    }

    public async Task<bool> SetAllowLocalNetworkAsync(bool enabled)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.SetAllowLocalNetworkAsync(enabled, _sessionToken);
        if (ok) await RefreshDataAsync();
        return ok;
    }

    public async Task<(bool Success, string? Error)> StartCaptivePortalAsync(int minutes)
    {
        if (string.IsNullOrEmpty(_sessionToken)) return (false, "Sesión no autenticada");
        var r = await _client.StartCaptivePortalAsync(minutes, _sessionToken);
        if (r.Success) await RefreshDataAsync();
        return r;
    }

    public async Task<bool> EndCaptivePortalAsync()
    {
        if (string.IsNullOrEmpty(_sessionToken)) return false;
        bool ok = await _client.EndCaptivePortalAsync(_sessionToken);
        if (ok) await RefreshDataAsync();
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

    /// <summary>Decisión del usuario al terminar la prueba. Para "quitar" con tutorlock, pasar el token.</summary>
    public async Task<(bool Success, string? Error)> DecideTrialAsync(bool keep, string? token)
    {
        var result = await _client.DecideTrialAsync(keep, token);
        if (result.Success)
        {
            await RefreshTrialAsync();
        }
        return result;
    }
}
