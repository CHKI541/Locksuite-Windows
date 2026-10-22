using System.Collections.ObjectModel;
using LockSuite.Core;

namespace LockSuite.UI.ViewModels;

public sealed class PackViewModel : ViewModelBase
{
    private string _name = "";
    private bool _enabled;
    private bool _builtIn;
    private bool _blocks;
    private string? _id;

    /// <summary>Id del preset de fabrica (null en paquetes creados por el tutor).</summary>
    public string? Id
    {
        get => _id;
        set
        {
            if (SetProperty(ref _id, value))
            {
                OnPropertyChanged(nameof(Preset));
                OnPropertyChanged(nameof(IsService));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Notice));
                OnPropertyChanged(nameof(HasNotice));
                OnPropertyChanged(nameof(OpenUrl));
                OnPropertyChanged(nameof(CanOpen));
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(ToggleText));
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public bool BuiltIn
    {
        get => _builtIn;
        set => SetProperty(ref _builtIn, value);
    }

    /// <summary>True: paquete de bloqueo (sus dominios se bloquean aunque otro paquete los permita).</summary>
    public bool Blocks
    {
        get => _blocks;
        set
        {
            if (SetProperty(ref _blocks, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(DomainSummary));
                OnPropertyChanged(nameof(EnabledLabel));
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    public ObservableCollection<string> Domains { get; } = new();

    /// <summary>Dominios del preset que el tutor quito a mano (solo paquetes de fabrica).</summary>
    public ObservableCollection<string> Excluded { get; } = new();

    // ----- Datos de la tarjeta (vienen del catalogo de presets) -----

    public PresetDefinition? Preset => Presets.Find(Id);

    /// <summary>True si se muestra como tarjeta "Permitir X" (Gmail, Drive, ...).</summary>
    public bool IsService => Preset?.IsService == true;

    public string Title => string.IsNullOrEmpty(Preset?.Title) ? Name : Preset!.Title;

    public string Icon => string.IsNullOrEmpty(Preset?.Icon) ? (Blocks ? "🚫" : "📦") : Preset!.Icon;

    public string Description => Preset?.Description ?? "";

    public string Notice => Preset?.Notice ?? "";

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public string OpenUrl => Preset?.OpenUrl ?? "";

    /// <summary>El boton "Abrir" solo tiene sentido si el servicio esta permitido.</summary>
    public bool CanOpen => Enabled && !string.IsNullOrEmpty(OpenUrl);

    public string StateText => Blocks
        ? (Enabled ? "Bloqueo activo" : "Bloqueo desactivado")
        : (Enabled ? "✓ Permitido" : "Bloqueado");

    /// <summary>Texto del boton de la tarjeta: "Permitir Gmail" / "Bloquear Gmail".</summary>
    public string ToggleText => Enabled ? $"Bloquear {Title}" : $"Permitir {Title}";

    public string EnabledLabel => Blocks ? "Activo" : "Habilitado";

    public string DomainSummary => Blocks
        ? $"{Domains.Count} {(Domains.Count == 1 ? "dominio siempre bloqueado" : "dominios siempre bloqueados")}"
        : $"{Domains.Count} {(Domains.Count == 1 ? "dominio" : "dominios")}";

    public DomainPack ToModel()
    {
        var pack = new DomainPack
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            BuiltIn = BuiltIn,
            Blocks = Blocks
        };
        pack.Domains.AddRange(Domains);
        pack.Excluded.AddRange(Excluded);
        return pack;
    }

    public static PackViewModel FromModel(DomainPack pack)
    {
        var vm = new PackViewModel
        {
            Id = pack.Id,
            Name = pack.Name,
            Enabled = pack.Enabled,
            BuiltIn = pack.BuiltIn,
            Blocks = pack.Blocks
        };
        foreach (var d in pack.Domains ?? new System.Collections.Generic.List<string>())
        {
            vm.Domains.Add(d);
        }
        foreach (var d in pack.Excluded ?? new System.Collections.Generic.List<string>())
        {
            vm.Excluded.Add(d);
        }
        return vm;
    }
}
