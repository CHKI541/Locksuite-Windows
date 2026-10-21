using System.Collections.ObjectModel;
using LockSuite.Core;

namespace LockSuite.UI.ViewModels;

public sealed class PackViewModel : ViewModelBase
{
    private string _name = "";
    private bool _enabled;
    private bool _builtIn;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public bool BuiltIn
    {
        get => _builtIn;
        set => SetProperty(ref _builtIn, value);
    }

    public ObservableCollection<string> Domains { get; } = new();

    public string DomainSummary => $"{Domains.Count} {(Domains.Count == 1 ? "dominio" : "dominios")}";

    public DomainPack ToModel()
    {
        var pack = new DomainPack
        {
            Name = Name,
            Enabled = Enabled,
            BuiltIn = BuiltIn
        };
        pack.Domains.AddRange(Domains);
        return pack;
    }

    public static PackViewModel FromModel(DomainPack pack)
    {
        var vm = new PackViewModel
        {
            Name = pack.Name,
            Enabled = pack.Enabled,
            BuiltIn = pack.BuiltIn
        };
        foreach (var d in pack.Domains)
        {
            vm.Domains.Add(d);
        }
        return vm;
    }
}
