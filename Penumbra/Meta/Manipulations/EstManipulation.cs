using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.Interop.Structs;
using Penumbra.Meta.Files;

namespace Penumbra.Meta.Manipulations;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EstManipulation : IMetaManipulation<EstManipulation>
{
    public static string ToName(EstType type)
        => type switch
        {
            EstType.Hair => "hair",
            EstType.Face => "face",
            EstType.Body => "top",
            EstType.Head => "met",
            _            => "unk",
        };

    public EstIdentifier Identifier { get; private init; }
    public EstEntry      Entry      { get; private init; }

    [JsonConverter(typeof(StringEnumConverter))]
    public Gender Gender
        => Identifier.Gender;

    [JsonConverter(typeof(StringEnumConverter))]
    public ModelRace Race
        => Identifier.Race;

    public PrimaryId SetId 
        => Identifier.SetId;

    [JsonConverter(typeof(StringEnumConverter))]
    public EstType Slot 
        => Identifier.Slot;


    [JsonConstructor]
    public EstManipulation(Gender gender, ModelRace race, EstType slot, PrimaryId setId, EstEntry entry)
    {
        Entry      = entry;
        Identifier = new EstIdentifier(setId, slot, Names.CombinedRace(gender, race));
    }

    public EstManipulation Copy(EstEntry entry)
        => new(Gender, Race, Slot, SetId, entry);


    public override string ToString()
        => $"Est - {SetId} - {Slot} - {Race.ToName()} {Gender.ToName()}";

    public bool Equals(EstManipulation other)
        => Gender == other.Gender
         && Race == other.Race
         && SetId == other.SetId
         && Slot == other.Slot;

    public override bool Equals(object? obj)
        => obj is EstManipulation other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine((int)Gender, (int)Race, SetId, (int)Slot);

    public int CompareTo(EstManipulation other)
    {
        var r = Race.CompareTo(other.Race);
        if (r != 0)
            return r;

        var g = Gender.CompareTo(other.Gender);
        if (g != 0)
            return g;

        var s = Slot.CompareTo(other.Slot);
        return s != 0 ? s : SetId.Id.CompareTo(other.SetId.Id);
    }

    public MetaIndex FileIndex()
        => (MetaIndex)Slot;

    public bool Apply(EstFile file)
    {
        return file.SetEntry(Names.CombinedRace(Gender, Race), SetId.Id, Entry) switch
        {
            EstFile.EstEntryChange.Unchanged => false,
            EstFile.EstEntryChange.Changed   => true,
            EstFile.EstEntryChange.Added     => true,
            EstFile.EstEntryChange.Removed   => true,
            _                                => throw new ArgumentOutOfRangeException(),
        };
    }

    public bool Validate()
    {
        if (!Enum.IsDefined(Slot))
            return false;
        if (Names.CombinedRace(Gender, Race) == GenderRace.Unknown)
            return false;

        // No known check for set id or entry.
        return true;
    }
}


