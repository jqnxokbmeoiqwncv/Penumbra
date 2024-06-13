using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.Interop.Structs;

namespace Penumbra.Meta.Manipulations;

public enum EstType : byte
{
    Hair = MetaIndex.HairEst,
    Face = MetaIndex.FaceEst,
    Body = MetaIndex.BodyEst,
    Head = MetaIndex.HeadEst,
}

public readonly record struct EstIdentifier(PrimaryId SetId, EstType Slot, GenderRace GenderRace)
    : IMetaIdentifier, IComparable<EstIdentifier>
{
    public ModelRace Race
        => GenderRace.Split().Item2;

    public Gender Gender
        => GenderRace.Split().Item1;

    public void AddChangedItems(ObjectIdentification identifier, IDictionary<string, object?> changedItems)
    {
        switch (Slot)
        {
            case EstType.Hair:
                changedItems.TryAdd(
                    $"Customization: {GenderRace.Split().Item2.ToName()} {GenderRace.Split().Item1.ToName()} Hair (Hair) {SetId}", null);
                break;
            case EstType.Face:
                changedItems.TryAdd(
                    $"Customization: {GenderRace.Split().Item2.ToName()} {GenderRace.Split().Item1.ToName()} Face (Face) {SetId}", null);
                break;
            case EstType.Body:
                identifier.Identify(changedItems, GamePaths.Equipment.Mdl.Path(SetId, GenderRace, EquipSlot.Body));
                break;
            case EstType.Head:
                identifier.Identify(changedItems, GamePaths.Equipment.Mdl.Path(SetId, GenderRace, EquipSlot.Head));
                break;
        }
    }

    public MetaIndex FileIndex()
        => (MetaIndex)Slot;

    public override string ToString()
        => $"Est - {SetId} - {Slot} - {GenderRace.ToName()}";

    public bool Validate()
    {
        if (!Enum.IsDefined(Slot))
            return false;
        if (GenderRace is GenderRace.Unknown || !Enum.IsDefined(GenderRace))
            return false;

        // No known check for set id.
        return true;
    }

    public int CompareTo(EstIdentifier other)
    {
        var gr = GenderRace.CompareTo(other.GenderRace);
        if (gr != 0)
            return gr;

        var id = SetId.Id.CompareTo(other.SetId.Id);
        return id != 0 ? id : Slot.CompareTo(other.Slot);
    }

    public static EstIdentifier? FromJson(JObject jObj)
    {
        var gender = jObj["Gender"]?.ToObject<Gender>() ?? Gender.Unknown;
        var race   = jObj["Race"]?.ToObject<ModelRace>() ?? ModelRace.Unknown;
        var setId  = new PrimaryId(jObj["SetId"]?.ToObject<ushort>() ?? 0);
        var slot   = jObj["Slot"]?.ToObject<EstType>() ?? 0;
        var ret    = new EstIdentifier(setId, slot, Names.CombinedRace(gender, race));
        return ret.Validate() ? ret : null;
    }

    public JObject AddToJson(JObject jObj)
    {
        var (gender, race) = GenderRace.Split();
        jObj["Gender"]     = gender.ToString();
        jObj["Race"]       = race.ToString();
        jObj["SetId"]      = SetId.Id.ToString();
        jObj["Slot"]       = Slot.ToString();
        return jObj;
    }
}

[JsonConverter(typeof(Converter))]
public readonly record struct EstEntry(ushort Value)
{
    public static readonly EstEntry Zero = new(0);

    public PrimaryId AsId
        => new(Value);

    private class Converter : JsonConverter<EstEntry>
    {
        public override void WriteJson(JsonWriter writer, EstEntry value, JsonSerializer serializer)
            => serializer.Serialize(writer, value.Value);

        public override EstEntry ReadJson(JsonReader reader, Type objectType, EstEntry existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new(serializer.Deserialize<ushort>(reader));
    }
}
