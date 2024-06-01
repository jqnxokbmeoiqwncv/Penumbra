using ImGuiNET;
using OtterGui.Raii;
using OtterGui;
using OtterGui.Services;
using OtterGui.Widgets;
using Penumbra.Collections;
using Penumbra.UI.Classes;
using Penumbra.Collections.Manager;
using Penumbra.Mods.Manager;
using Penumbra.Services;
using Penumbra.Mods.Settings;
using Penumbra.UI.ModsTab.Groups;

namespace Penumbra.UI.ModsTab;

public class ModPanelSettingsTab(
    CollectionManager collectionManager,
    ModManager modManager,
    ModFileSystemSelector selector,
    TutorialService tutorial,
    CommunicatorService communicator,
    ModGroupDrawer modGroupDrawer)
    : ITab, IUiService
{
    private bool          _inherited;
    private ModSettings   _settings   = null!;
    private ModCollection _collection = null!;
    private int?          _currentPriority;

    public ReadOnlySpan<byte> Label
        => "Settings"u8;

    public void DrawHeader()
        => tutorial.OpenTutorial(BasicTutorialSteps.ModOptions);

    public void Reset()
        => _currentPriority = null;

    public void DrawContent()
    {
        using var child = ImRaii.Child("##settings");
        if (!child)
            return;

        _settings   = selector.SelectedSettings;
        _collection = selector.SelectedSettingCollection;
        _inherited  = _collection != collectionManager.Active.Current;
        DrawInheritedWarning();
        UiHelpers.DefaultLineSpace();
        communicator.PreSettingsPanelDraw.Invoke(selector.Selected!.Identifier);
        DrawEnabledInput();
        tutorial.OpenTutorial(BasicTutorialSteps.EnablingMods);
        ImGui.SameLine();
        DrawPriorityInput();
        tutorial.OpenTutorial(BasicTutorialSteps.Priority);
        DrawRemoveSettings();

        communicator.PostEnabledDraw.Invoke(selector.Selected!.Identifier);

        modGroupDrawer.Draw(selector.Selected!, _settings);
        UiHelpers.DefaultLineSpace();
        communicator.PostSettingsPanelDraw.Invoke(selector.Selected!.Identifier);
    }

    /// <summary> Draw a big red bar if the current setting is inherited. </summary>
    private void DrawInheritedWarning()
    {
        if (!_inherited)
            return;

        using var color = ImRaii.PushColor(ImGuiCol.Button, Colors.PressEnterWarningBg);
        var       width = new Vector2(ImGui.GetContentRegionAvail().X, 0);
        if (ImGui.Button($"These settings are inherited from {_collection.Name}.", width))
            collectionManager.Editor.SetModInheritance(collectionManager.Active.Current, selector.Selected!, false);

        ImGuiUtil.HoverTooltip("You can click this button to copy the current settings to the current selection.\n"
          + "You can also just change any setting, which will copy the settings with the single setting changed to the current selection.");
    }

    /// <summary> Draw a checkbox for the enabled status of the mod. </summary>
    private void DrawEnabledInput()
    {
        var enabled = _settings.Enabled;
        if (!ImGui.Checkbox("Enabled", ref enabled))
            return;

        modManager.SetKnown(selector.Selected!);
        collectionManager.Editor.SetModState(collectionManager.Active.Current, selector.Selected!, enabled);
    }

    /// <summary>
    /// Draw a priority input.
    /// Priority is changed on deactivation of the input box.
    /// </summary>
    private void DrawPriorityInput()
    {
        using var group    = ImRaii.Group();
        var       priority = _currentPriority ?? _settings.Priority.Value;
        ImGui.SetNextItemWidth(50 * UiHelpers.Scale);
        if (ImGui.InputInt("##Priority", ref priority, 0, 0))
            _currentPriority = priority;

        if (ImGui.IsItemDeactivatedAfterEdit() && _currentPriority.HasValue)
        {
            if (_currentPriority != _settings.Priority.Value)
                collectionManager.Editor.SetModPriority(collectionManager.Active.Current, selector.Selected!,
                    new ModPriority(_currentPriority.Value));

            _currentPriority = null;
        }

        ImGuiUtil.LabeledHelpMarker("Priority", "Mods with a higher number here take precedence before Mods with a lower number.\n"
          + "That means, if Mod A should overwrite changes from Mod B, Mod A should have a higher priority number than Mod B.");
    }

    /// <summary>
    /// Draw a button to remove the current settings and inherit them instead
    /// on the top-right corner of the window/tab.
    /// </summary>
    private void DrawRemoveSettings()
    {
        const string text = "Inherit Settings";
        if (_inherited || _settings == ModSettings.Empty)
            return;

        var scroll = ImGui.GetScrollMaxY() > 0 ? ImGui.GetStyle().ScrollbarSize : 0;
        ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X - ImGui.GetStyle().FramePadding.X * 2 - scroll);
        if (ImGui.Button(text))
            collectionManager.Editor.SetModInheritance(collectionManager.Active.Current, selector.Selected!, true);

        ImGuiUtil.HoverTooltip("Remove current settings from this collection so that it can inherit them.\n"
          + "If no inherited collection has settings for this mod, it will be disabled.");
    }
}
