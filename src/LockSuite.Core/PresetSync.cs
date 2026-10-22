using System;
using System.Collections.Generic;
using System.Linq;

namespace LockSuite.Core;

/// <summary>
/// Mantiene los paquetes de fabrica de una configuracion al dia con el catalogo de
/// <see cref="Presets"/>. Se ejecuta al cargar la config (y al guardarla por IPC), asi una
/// actualizacion de LockSuite mejora los paquetes sin pisar lo que hizo el tutor:
///
///   - Migra los paquetes de versiones anteriores (sin id):
///       "Google (Gmail, Chat, Drive, Fotos)" -> Gmail + Google Chat + Google Drive + Google Fotos
///       "Sitios de la comunidad"             -> prog.co.il + mitmachim.top
///     Los nuevos heredan el estado del viejo. Los dominios amplios del paquete viejo
///     (google.com, googleapis.com, google.com.ar, ...) se descartan a proposito.
///   - Los dominios que el tutor habia agregado a un paquete de fabrica se mueven a
///     "Personalizados" (con el mismo estado), para que no se pierdan.
///   - Agrega los presets nuevos (p. ej. el "Bloqueo fijo") y actualiza la lista de dominios
///     de cada preset, respetando los que el tutor quito a mano (lista "excluded").
///   - Es idempotente: si la config ya esta al dia, no cambia nada y devuelve false.
/// </summary>
public static class PresetSync
{
    /// <summary>Sincroniza la config contra el catalogo. Devuelve true si hubo cambios (hay que guardar).</summary>
    public static bool Apply(WhitelistConfig cfg)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        bool changed = false;

        // 0) Datos incompletos (JSON editado a mano o de otra version)
        if (cfg.Packs is null) { cfg.Packs = new List<DomainPack>(); changed = true; }
        if (cfg.Packs.RemoveAll(p => p is null) > 0) changed = true;
        foreach (var p in cfg.Packs)
        {
            if (p.Domains is null) { p.Domains = new List<string>(); changed = true; }
            if (p.Excluded is null) { p.Excluded = new List<string>(); changed = true; }
            if (p.Name is null) { p.Name = ""; changed = true; }
        }

        // 1) Paquetes viejos que se reparten en varios nuevos
        foreach (var split in Presets.LegacySplits)
            changed |= ApplySplit(cfg, split);

        // 2) Paquetes viejos que solo necesitan su id
        foreach (var def in Presets.Catalog)
        {
            if (def.LegacyNames.Count == 0 || FindById(cfg, def.Id) is not null) continue;
            var legacy = cfg.Packs.FirstOrDefault(p => p.Id is null && p.BuiltIn && def.LegacyNames.Any(n => SameName(p.Name, n)));
            if (legacy is null) continue;
            legacy.Id = def.Id;
            changed = true;
        }

        // 3) Ids repetidos (no deberia pasar): queda el primero
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cfg.Packs.Count; i++)
        {
            var id = cfg.Packs[i].Id;
            if (id is null) continue;
            if (!seenIds.Add(id)) { cfg.Packs.RemoveAt(i); i--; changed = true; }
        }

        // 4) Cada preset del catalogo: crearlo si falta y sincronizar sus dominios
        foreach (var def in Presets.Catalog)
        {
            var pack = FindById(cfg, def.Id);
            if (pack is null)
            {
                InsertInCatalogOrder(cfg, Presets.ToPack(def, def.EnabledOnUpgrade));
                changed = true;
                continue;
            }
            changed |= SyncPack(cfg, pack, def);
        }

        // 5) Paquetes del tutor con el mismo nombre que un preset: se renombran
        //    (los comandos de la interfaz identifican los paquetes por nombre).
        foreach (var def in Presets.Catalog)
        {
            foreach (var p in cfg.Packs.Where(p => p.Id is null && SameName(p.Name, def.Name)).ToList())
            {
                p.Name = UniqueName(cfg, def.Name + " (personalizado)");
                changed = true;
            }
        }

        if (cfg.Version != WhitelistConfig.CurrentVersion)
        {
            cfg.Version = WhitelistConfig.CurrentVersion;
            changed = true;
        }

        return changed;
    }

    private static bool ApplySplit(WhitelistConfig cfg, LegacySplit split)
    {
        int idx = cfg.Packs.FindIndex(p => p.Id is null && p.BuiltIn && SameName(p.Name, split.LegacyName));
        if (idx < 0) return false;

        var legacy = cfg.Packs[idx];
        cfg.Packs.RemoveAt(idx);

        var targets = split.TargetIds
            .Select(Presets.Find)
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        // Lo que ya cubren los paquetes nuevos o se descarta a proposito no es "del tutor".
        var known = new HashSet<string>(split.LegacyDomains.Select(Norm), StringComparer.Ordinal);
        foreach (var t in targets)
            foreach (var d in t.Domains)
                known.Add(Norm(d));

        var extras = legacy.Domains
            .Select(Norm)
            .Where(d => d.Length > 0 && !known.Contains(d))
            .Distinct()
            .ToList();

        int insertAt = Math.Min(idx, cfg.Packs.Count);
        foreach (var def in targets)
        {
            if (FindById(cfg, def.Id) is not null) continue;   // ya existe: no duplicar
            cfg.Packs.Insert(insertAt++, Presets.ToPack(def, legacy.Enabled));
        }

        MoveExtras(cfg, extras, legacy.Enabled, legacy.Name);
        return true;
    }

    private static bool SyncPack(WhitelistConfig cfg, DomainPack pack, PresetDefinition def)
    {
        bool changed = false;
        if (!pack.BuiltIn) { pack.BuiltIn = true; changed = true; }
        if (pack.Blocks != def.Blocks) { pack.Blocks = def.Blocks; changed = true; }
        if (!string.Equals(pack.Name, def.Name, StringComparison.Ordinal)) { pack.Name = def.Name; changed = true; }

        var catalog = def.Domains.Select(Norm).Where(d => d.Length > 0).Distinct().ToList();
        var catalogSet = new HashSet<string>(catalog, StringComparer.Ordinal);
        var legacySet = new HashSet<string>(def.LegacyDomains.Select(Norm), StringComparer.Ordinal);

        // Excluidos por el tutor: solo tienen sentido mientras sigan en el catalogo.
        var excluded = pack.Excluded.Select(Norm).Where(catalogSet.Contains).Distinct().ToList();
        if (!excluded.SequenceEqual(pack.Excluded)) { pack.Excluded = excluded; changed = true; }
        var excludedSet = new HashSet<string>(excluded, StringComparer.Ordinal);

        // Ni del catalogo ni de versiones viejas: lo agrego el tutor.
        var extras = pack.Domains
            .Select(Norm)
            .Where(d => d.Length > 0 && !catalogSet.Contains(d) && !legacySet.Contains(d))
            .Distinct()
            .ToList();

        var desired = catalog.Where(d => !excludedSet.Contains(d)).ToList();
        if (def.Blocks)
        {
            // En un paquete de bloqueo lo agregado sigue bloqueando: moverlo a "Personalizados"
            // lo convertiria en un permiso.
            desired.AddRange(extras);
            extras.Clear();
        }

        if (!desired.SequenceEqual(pack.Domains)) { pack.Domains = desired; changed = true; }

        if (extras.Count > 0)
        {
            MoveExtras(cfg, extras, pack.Enabled, pack.Name);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Mueve dominios agregados por el tutor a un paquete propio, conservando si estaban
    /// permitidos o no: habilitados -> "Personalizados"; deshabilitados -> "X (dominios agregados)" deshabilitado.
    /// </summary>
    private static void MoveExtras(WhitelistConfig cfg, List<string> extras, bool enabled, string sourceName)
    {
        if (extras.Count == 0) return;

        DomainPack? target;
        if (enabled)
        {
            target = cfg.Packs.FirstOrDefault(p => p.Id is null && !p.BuiltIn && !p.Blocks && p.Enabled && SameName(p.Name, Presets.CustomPackName));
            if (target is null)
            {
                bool taken = cfg.Packs.Any(p => SameName(p.Name, Presets.CustomPackName));
                string name = taken ? Presets.CustomPackName + " (migrados)" : Presets.CustomPackName;
                target = cfg.Packs.FirstOrDefault(p => p.Id is null && !p.BuiltIn && !p.Blocks && p.Enabled && SameName(p.Name, name));
                if (target is null)
                {
                    target = new DomainPack { Name = UniqueName(cfg, name), Enabled = true };
                    cfg.Packs.Add(target);
                }
            }
        }
        else
        {
            string name = sourceName + " (dominios agregados)";
            target = cfg.Packs.FirstOrDefault(p => p.Id is null && !p.BuiltIn && !p.Blocks && !p.Enabled && SameName(p.Name, name));
            if (target is null)
            {
                target = new DomainPack { Name = UniqueName(cfg, name), Enabled = false };
                cfg.Packs.Add(target);
            }
        }

        foreach (var d in extras)
            if (!target.Domains.Any(x => Norm(x) == d))
                target.Domains.Add(d);
    }

    private static void InsertInCatalogOrder(WhitelistConfig cfg, DomainPack pack)
    {
        int catIdx = CatalogIndex(pack.Id);
        int insertAt = 0;
        for (int i = 0; i < cfg.Packs.Count; i++)
        {
            int ci = CatalogIndex(cfg.Packs[i].Id);
            if (ci >= 0 && ci < catIdx) insertAt = i + 1;
        }
        cfg.Packs.Insert(insertAt, pack);
    }

    private static int CatalogIndex(string? id)
    {
        if (id is null) return -1;
        for (int i = 0; i < Presets.Catalog.Count; i++)
            if (string.Equals(Presets.Catalog[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static DomainPack? FindById(WhitelistConfig cfg, string id)
        => cfg.Packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string UniqueName(WhitelistConfig cfg, string baseName)
    {
        string name = baseName;
        int n = 2;
        while (cfg.Packs.Any(p => SameName(p.Name, name)))
            name = $"{baseName} {n++}";
        return name;
    }

    private static bool SameName(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? d) => DomainMatcher.Normalize(d);
}
