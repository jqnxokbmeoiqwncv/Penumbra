using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using OtterGui.Services;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.Interop.PathResolving;

namespace Penumbra.Interop.Hooks.Meta;

public unsafe class EqdpEquipHook : FastHook<EqdpEquipHook.Delegate>
{
    public delegate void Delegate(CharacterUtility* utility, EqdpEntry* entry, uint id, uint raceCode);

    private readonly MetaState _metaState;

    public EqdpEquipHook(HookManager hooks, MetaState metaState)
    {
        _metaState = metaState;
        Task       = hooks.CreateHook<Delegate>("GetEqdpEquipEntry", "E8 ?? ?? ?? ?? 85 DB 75 ?? F6 45", Detour, true);
    }

    private void Detour(CharacterUtility* utility, EqdpEntry* entry, uint setId, uint raceCode)
    {
        Task.Result.Original(utility, entry, setId, raceCode);
        if (_metaState.EqdpCollection.TryPeek(out var collection)
         && collection is { Valid: true, ModCollection.MetaCache: { } cache })
            *entry = cache.Eqdp.ApplyFullEntry(new PrimaryId((ushort)setId), (GenderRace)raceCode, false, *entry);
        Penumbra.Log.Excessive(
            $"[GetEqdpEquipEntry] Invoked on 0x{(ulong)utility:X} with {setId}, {(GenderRace)raceCode}, returned {(ushort)*entry:B10}.");
    }
}
