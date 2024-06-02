using Dalamud.Interface;
using Dalamud.Interface.Internal;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Lumina.Data.Files;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using OtterGui;
using OtterGui.Classes;
using OtterGui.Raii;
using Penumbra.Api.Enums;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.Services;
using Penumbra.UI.Classes;
using ApiChangedItemIcon = Penumbra.Api.Enums.ChangedItemIcon;

namespace Penumbra.UI;

public class ChangedItemDrawer : IDisposable
{
    [Flags]
    public enum ChangedItemIcon : uint
    {
        Head          = 0x00_00_01,
        Body          = 0x00_00_02,
        Hands         = 0x00_00_04,
        Legs          = 0x00_00_08,
        Feet          = 0x00_00_10,
        Ears          = 0x00_00_20,
        Neck          = 0x00_00_40,
        Wrists        = 0x00_00_80,
        Finger        = 0x00_01_00,
        Monster       = 0x00_02_00,
        Demihuman     = 0x00_04_00,
        Customization = 0x00_08_00,
        Action        = 0x00_10_00,
        Mainhand      = 0x00_20_00,
        Offhand       = 0x00_40_00,
        Unknown       = 0x00_80_00,
        Emote         = 0x01_00_00,
    }

    private static readonly ChangedItemIcon[] Order =
    [
        ChangedItemIcon.Head,
        ChangedItemIcon.Body,
        ChangedItemIcon.Hands,
        ChangedItemIcon.Legs,
        ChangedItemIcon.Feet,
        ChangedItemIcon.Ears,
        ChangedItemIcon.Neck,
        ChangedItemIcon.Wrists,
        ChangedItemIcon.Finger,
        ChangedItemIcon.Mainhand,
        ChangedItemIcon.Offhand,
        ChangedItemIcon.Customization,
        ChangedItemIcon.Action,
        ChangedItemIcon.Emote,
        ChangedItemIcon.Monster,
        ChangedItemIcon.Demihuman,
        ChangedItemIcon.Unknown,
    ];

    private static readonly string[] LowerNames = Order.Select(f => ToDescription(f).ToLowerInvariant()).ToArray();

    public static bool TryParseIndex(ReadOnlySpan<char> input, out ChangedItemIcon slot)
    {
        // Handle numeric cases before TryParse because numbers
        // are not logical otherwise.
        if (int.TryParse(input, out var idx))
        {
            // We assume users will use 1-based index, but if they enter 0, just use the first.
            if (idx == 0)
            {
                slot = Order[0];
                return true;
            }

            // Use 1-based index.
            --idx;
            if (idx >= 0 && idx < Order.Length)
            {
                slot = Order[idx];
                return true;
            }
        }

        slot = 0;
        return false;
    }

    public static bool TryParsePartial(string lowerInput, out ChangedItemIcon slot)
    {
        if (TryParseIndex(lowerInput, out slot))
            return true;

        slot = 0;
        foreach (var (item, flag) in LowerNames.Zip(Order))
            if (item.Contains(lowerInput, StringComparison.Ordinal))
                slot |= flag;

        return slot != 0;
    }

    public const           ChangedItemIcon AllFlags      = (ChangedItemIcon)0x01FFFF;
    public static readonly int             NumCategories = Order.Length;
    public const           ChangedItemIcon DefaultFlags  = AllFlags & ~ChangedItemIcon.Offhand;

    private readonly Configuration                                    _config;
    private readonly ExcelSheet<Item>                                 _items;
    private readonly CommunicatorService                              _communicator;
    private readonly Dictionary<ChangedItemIcon, IDalamudTextureWrap> _icons = new(16);
    private          float                                            _smallestIconWidth;

    public static Vector2 TypeFilterIconSize
        => new(2 * ImGui.GetTextLineHeight());

    public ChangedItemDrawer(UiBuilder uiBuilder, IDataManager gameData, ITextureProvider textureProvider, CommunicatorService communicator,
        Configuration config)
    {
        _items = gameData.GetExcelSheet<Item>()!;
        uiBuilder.RunWhenUiPrepared(() => CreateEquipSlotIcons(uiBuilder, gameData, textureProvider), true);
        _communicator = communicator;
        _config       = config;
    }

    public void Dispose()
    {
        foreach (var wrap in _icons.Values.Distinct())
            wrap.Dispose();
        _icons.Clear();
    }

    /// <summary> Check if a changed item should be drawn based on its category. </summary>
    public bool FilterChangedItem(string name, object? data, LowerString filter)
        => (_config.Ephemeral.ChangedItemFilter == AllFlags || _config.Ephemeral.ChangedItemFilter.HasFlag(GetCategoryIcon(name, data)))
         && (filter.IsEmpty || filter.IsContained(ChangedItemFilterName(name, data)));

    /// <summary> Draw the icon corresponding to the category of a changed item. </summary>
    public void DrawCategoryIcon(string name, object? data)
        => DrawCategoryIcon(GetCategoryIcon(name, data));

    public void DrawCategoryIcon(ChangedItemIcon iconType)
    {
        var height = ImGui.GetFrameHeight();
        if (!_icons.TryGetValue(iconType, out var icon))
        {
            ImGui.Dummy(new Vector2(height));
            return;
        }

        ImGui.Image(icon.ImGuiHandle, new Vector2(height));
        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            ImGui.Image(icon.ImGuiHandle, new Vector2(_smallestIconWidth));
            ImGui.SameLine();
            ImGuiUtil.DrawTextButton(ToDescription(iconType), new Vector2(0, _smallestIconWidth), 0);
        }
    }

    /// <summary>
    /// Draw a changed item, invoking the Api-Events for clicks and tooltips.
    /// Also draw the item Id in grey if requested.
    /// </summary>
    public void DrawChangedItem(string name, object? data)
    {
        name = ChangedItemName(name, data);
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0,   0.5f))
                   .Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, ImGui.GetStyle().CellPadding.Y * 2)))
        {
            var ret = ImGui.Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetFrameHeight()))
                ? MouseButton.Left
                : MouseButton.None;
            ret = ImGui.IsItemClicked(ImGuiMouseButton.Right) ? MouseButton.Right : ret;
            ret = ImGui.IsItemClicked(ImGuiMouseButton.Middle) ? MouseButton.Middle : ret;
            if (ret != MouseButton.None)
                _communicator.ChangedItemClick.Invoke(ret, Convert(data));
        }

        if (_communicator.ChangedItemHover.HasTooltip && ImGui.IsItemHovered())
        {
            // We can not be sure that any subscriber actually prints something in any case.
            // Circumvent ugly blank tooltip with less-ugly useless tooltip.
            using var tt    = ImRaii.Tooltip();
            using var group = ImRaii.Group();
            _communicator.ChangedItemHover.Invoke(Convert(data));
            group.Dispose();
            if (ImGui.GetItemRectSize() == Vector2.Zero)
                ImGui.TextUnformatted("No actions available.");
        }
    }

    /// <summary> Draw the model information, right-justified. </summary>
    public void DrawModelData(object? data)
    {
        if (!GetChangedItemObject(data, out var text))
            return;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X);
        ImGui.AlignTextToFramePadding();
        ImGuiUtil.RightJustify(text, ColorId.ItemId.Value());
    }

    /// <summary> Draw a header line with the different icon types to filter them. </summary>
    public void DrawTypeFilter()
    {
        if (_config.HideChangedItemFilters)
            return;

        var typeFilter = _config.Ephemeral.ChangedItemFilter;
        if (DrawTypeFilter(ref typeFilter))
        {
            _config.Ephemeral.ChangedItemFilter = typeFilter;
            _config.Ephemeral.Save();
        }
    }

    /// <summary> Draw a header line with the different icon types to filter them. </summary>
    public bool DrawTypeFilter(ref ChangedItemIcon typeFilter)
    {
        var       ret   = false;
        using var _     = ImRaii.PushId("ChangedItemIconFilter");
        var       size  = TypeFilterIconSize;
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);


        bool DrawIcon(ChangedItemIcon type, ref ChangedItemIcon typeFilter)
        {
            var ret  = false;
            var icon = _icons[type];
            var flag = typeFilter.HasFlag(type);
            ImGui.Image(icon.ImGuiHandle, size, Vector2.Zero, Vector2.One, flag ? Vector4.One : new Vector4(0.6f, 0.3f, 0.3f, 1f));
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                typeFilter = flag ? typeFilter & ~type : typeFilter | type;
                ret        = true;
            }

            using var popup = ImRaii.ContextPopupItem(type.ToString());
            if (popup)
                if (ImGui.MenuItem("Enable Only This"))
                {
                    typeFilter = type;
                    ret        = true;
                    ImGui.CloseCurrentPopup();
                }

            if (ImGui.IsItemHovered())
            {
                using var tt = ImRaii.Tooltip();
                ImGui.Image(icon.ImGuiHandle, new Vector2(_smallestIconWidth));
                ImGui.SameLine();
                ImGuiUtil.DrawTextButton(ToDescription(type), new Vector2(0, _smallestIconWidth), 0);
            }

            return ret;
        }

        foreach (var iconType in Order)
        {
            ret |= DrawIcon(iconType, ref typeFilter);
            ImGui.SameLine();
        }

        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - size.X);
        ImGui.Image(_icons[AllFlags].ImGuiHandle, size, Vector2.Zero, Vector2.One,
            typeFilter == 0        ? new Vector4(0.6f,  0.3f,  0.3f,  1f) :
            typeFilter == AllFlags ? new Vector4(0.75f, 0.75f, 0.75f, 1f) : new Vector4(0.5f, 0.5f, 1f, 1f));
        if (ImGui.IsItemClicked())
        {
            typeFilter = typeFilter == AllFlags ? 0 : AllFlags;
            ret        = true;
        }

        return ret;
    }

    /// <summary> Obtain the icon category corresponding to a changed item. </summary>
    internal static ChangedItemIcon GetCategoryIcon(string name, object? obj)
    {
        var iconType = ChangedItemIcon.Unknown;
        switch (obj)
        {
            case EquipItem it:
                iconType = GetCategoryIcon(it.Type.ToSlot());
                break;
            case ModelChara m:
                iconType = (CharacterBase.ModelType)m.Type switch
                {
                    CharacterBase.ModelType.DemiHuman => ChangedItemIcon.Demihuman,
                    CharacterBase.ModelType.Monster   => ChangedItemIcon.Monster,
                    _                                 => ChangedItemIcon.Unknown,
                };
                break;
            default:
            {
                if (name.StartsWith("Action: "))
                    iconType = ChangedItemIcon.Action;
                else if (name.StartsWith("Emote: "))
                    iconType = ChangedItemIcon.Emote;
                else if (name.StartsWith("Customization: "))
                    iconType = ChangedItemIcon.Customization;
                break;
            }
        }

        return iconType;
    }

    internal static ChangedItemIcon GetCategoryIcon(EquipSlot slot)
        => slot switch
        {
            EquipSlot.MainHand => ChangedItemIcon.Mainhand,
            EquipSlot.OffHand  => ChangedItemIcon.Offhand,
            EquipSlot.Head     => ChangedItemIcon.Head,
            EquipSlot.Body     => ChangedItemIcon.Body,
            EquipSlot.Hands    => ChangedItemIcon.Hands,
            EquipSlot.Legs     => ChangedItemIcon.Legs,
            EquipSlot.Feet     => ChangedItemIcon.Feet,
            EquipSlot.Ears     => ChangedItemIcon.Ears,
            EquipSlot.Neck     => ChangedItemIcon.Neck,
            EquipSlot.Wrists   => ChangedItemIcon.Wrists,
            EquipSlot.RFinger  => ChangedItemIcon.Finger,
            _                  => ChangedItemIcon.Unknown,
        };

    /// <summary> Return more detailed object information in text, if it exists. </summary>
    private static bool GetChangedItemObject(object? obj, out string text)
    {
        switch (obj)
        {
            case EquipItem it:
                text = it.ModelString;
                return true;
            case ModelChara m:
                text = $"({((CharacterBase.ModelType)m.Type).ToName()} {m.Model}-{m.Base}-{m.Variant})";
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    /// <summary> We need to transform the internal EquipItem type to the Lumina Item type for API-events. </summary>
    private object? Convert(object? data)
    {
        if (data is EquipItem it)
            return (_items.GetRow(it.ItemId.Id), it.Type);

        return data;
    }

    private static string ToDescription(ChangedItemIcon icon)
        => icon switch
        {
            ChangedItemIcon.Head          => EquipSlot.Head.ToName(),
            ChangedItemIcon.Body          => EquipSlot.Body.ToName(),
            ChangedItemIcon.Hands         => EquipSlot.Hands.ToName(),
            ChangedItemIcon.Legs          => EquipSlot.Legs.ToName(),
            ChangedItemIcon.Feet          => EquipSlot.Feet.ToName(),
            ChangedItemIcon.Ears          => EquipSlot.Ears.ToName(),
            ChangedItemIcon.Neck          => EquipSlot.Neck.ToName(),
            ChangedItemIcon.Wrists        => EquipSlot.Wrists.ToName(),
            ChangedItemIcon.Finger        => "Ring",
            ChangedItemIcon.Monster       => "Monster",
            ChangedItemIcon.Demihuman     => "Demi-Human",
            ChangedItemIcon.Customization => "Customization",
            ChangedItemIcon.Action        => "Action",
            ChangedItemIcon.Emote         => "Emote",
            ChangedItemIcon.Mainhand      => "Weapon (Mainhand)",
            ChangedItemIcon.Offhand       => "Weapon (Offhand)",
            _                             => "Other",
        };

    internal static ApiChangedItemIcon ToApiIcon(ChangedItemIcon icon)
        => icon switch
        {
            ChangedItemIcon.Head          => ApiChangedItemIcon.Head,
            ChangedItemIcon.Body          => ApiChangedItemIcon.Body,
            ChangedItemIcon.Hands         => ApiChangedItemIcon.Hands,
            ChangedItemIcon.Legs          => ApiChangedItemIcon.Legs,
            ChangedItemIcon.Feet          => ApiChangedItemIcon.Feet,
            ChangedItemIcon.Ears          => ApiChangedItemIcon.Ears,
            ChangedItemIcon.Neck          => ApiChangedItemIcon.Neck,
            ChangedItemIcon.Wrists        => ApiChangedItemIcon.Wrists,
            ChangedItemIcon.Finger        => ApiChangedItemIcon.Finger,
            ChangedItemIcon.Monster       => ApiChangedItemIcon.Monster,
            ChangedItemIcon.Demihuman     => ApiChangedItemIcon.Demihuman,
            ChangedItemIcon.Customization => ApiChangedItemIcon.Customization,
            ChangedItemIcon.Action        => ApiChangedItemIcon.Action,
            ChangedItemIcon.Emote         => ApiChangedItemIcon.Emote,
            ChangedItemIcon.Mainhand      => ApiChangedItemIcon.Mainhand,
            ChangedItemIcon.Offhand       => ApiChangedItemIcon.Offhand,
            ChangedItemIcon.Unknown       => ApiChangedItemIcon.Unknown,
            _                             => ApiChangedItemIcon.None,
        };

    /// <summary> Apply Changed Item Counters to the Name if necessary. </summary>
    private static string ChangedItemName(string name, object? data)
        => data is int counter ? $"{counter} Files Manipulating {name}s" : name;

    /// <summary> Add filterable information to the string. </summary>
    private static string ChangedItemFilterName(string name, object? data)
        => data switch
        {
            int counter  => $"{counter} Files Manipulating {name}s",
            EquipItem it => $"{name}\0{(GetChangedItemObject(it, out var t) ? t : string.Empty)}",
            ModelChara m => $"{name}\0{(GetChangedItemObject(m,  out var t) ? t : string.Empty)}",
            _            => name,
        };

    /// <summary> Initialize the icons. </summary>
    private bool CreateEquipSlotIcons(UiBuilder uiBuilder, IDataManager gameData, ITextureProvider textureProvider)
    {
        using var equipTypeIcons = uiBuilder.LoadUld("ui/uld/ArmouryBoard.uld");

        if (!equipTypeIcons.Valid)
            return false;

        void Add(ChangedItemIcon icon, IDalamudTextureWrap? tex)
        {
            if (tex != null)
                _icons.Add(icon, tex);
        }

        Add(ChangedItemIcon.Mainhand,      equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 0));
        Add(ChangedItemIcon.Head,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 1));
        Add(ChangedItemIcon.Body,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 2));
        Add(ChangedItemIcon.Hands,         equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 3));
        Add(ChangedItemIcon.Legs,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 5));
        Add(ChangedItemIcon.Feet,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 6));
        Add(ChangedItemIcon.Offhand,       equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 7));
        Add(ChangedItemIcon.Ears,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 8));
        Add(ChangedItemIcon.Neck,          equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 9));
        Add(ChangedItemIcon.Wrists,        equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 10));
        Add(ChangedItemIcon.Finger,        equipTypeIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 11));
        Add(ChangedItemIcon.Monster,       textureProvider.GetTextureFromGame("ui/icon/062000/062042_hr1.tex", true));
        Add(ChangedItemIcon.Demihuman,     textureProvider.GetTextureFromGame("ui/icon/062000/062041_hr1.tex", true));
        Add(ChangedItemIcon.Customization, textureProvider.GetTextureFromGame("ui/icon/062000/062043_hr1.tex", true));
        Add(ChangedItemIcon.Action,        textureProvider.GetTextureFromGame("ui/icon/062000/062001_hr1.tex", true));
        Add(ChangedItemIcon.Emote,         LoadEmoteTexture(gameData, uiBuilder));
        Add(ChangedItemIcon.Unknown,       LoadUnknownTexture(gameData, uiBuilder));
        Add(AllFlags,                      textureProvider.GetTextureFromGame("ui/icon/114000/114052_hr1.tex", true));

        _smallestIconWidth = _icons.Values.Min(i => i.Width);

        return true;
    }

    private static unsafe IDalamudTextureWrap? LoadUnknownTexture(IDataManager gameData, UiBuilder uiBuilder)
    {
        var unk = gameData.GetFile<TexFile>("ui/uld/levelup2_hr1.tex");
        if (unk == null)
            return null;

        var image = unk.GetRgbaImageData();
        var bytes = new byte[unk.Header.Height * unk.Header.Height * 4];
        var diff  = 2 * (unk.Header.Height - unk.Header.Width);
        for (var y = 0; y < unk.Header.Height; ++y)
            image.AsSpan(4 * y * unk.Header.Width, 4 * unk.Header.Width).CopyTo(bytes.AsSpan(4 * y * unk.Header.Height + diff));

        return uiBuilder.LoadImageRaw(bytes, unk.Header.Height, unk.Header.Height, 4);
    }

    private static unsafe IDalamudTextureWrap? LoadEmoteTexture(IDataManager gameData, UiBuilder uiBuilder)
    {
        var emote = gameData.GetFile<TexFile>("ui/icon/000000/000019_hr1.tex");
        if (emote == null)
            return null;

        var image2 = emote.GetRgbaImageData();
        fixed (byte* ptr = image2)
        {
            var color = (uint*)ptr;
            for (var i = 0; i < image2.Length / 4; ++i)
            {
                if (color[i] == 0xFF000000)
                    image2[i * 4 + 3] = 0;
            }
        }

        return uiBuilder.LoadImageRaw(image2, emote.Header.Width, emote.Header.Height, 4);
    }
}
