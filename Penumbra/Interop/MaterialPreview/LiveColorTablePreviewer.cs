using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using Penumbra.GameData.Interop;
using Penumbra.Interop.SafeHandles;

namespace Penumbra.Interop.MaterialPreview;

public sealed unsafe class LiveColorTablePreviewer : LiveMaterialPreviewerBase
{
    public const int TextureWidth  = 4;
    public const int TextureHeight = GameData.Files.MaterialStructs.LegacyColorTable.NumUsedRows;
    public const int TextureLength = TextureWidth * TextureHeight * 4;

    private readonly IFramework _framework;

    private readonly Texture**         _colorTableTexture;
    private readonly SafeTextureHandle _originalColorTableTexture;

    private bool _updatePending;

    public Half[] ColorTable { get; }

    public LiveColorTablePreviewer(ObjectManager objects, IFramework framework, MaterialInfo materialInfo)
        : base(objects, materialInfo)
    {
        _framework = framework;

        var mtrlHandle = Material->MaterialResourceHandle;
        if (mtrlHandle == null)
            throw new InvalidOperationException("Material doesn't have a resource handle");

        var colorSetTextures = DrawObject->ColorTableTextures;
        if (colorSetTextures == null)
            throw new InvalidOperationException("Draw object doesn't have color table textures");

        _colorTableTexture = colorSetTextures + (MaterialInfo.ModelSlot * 4 + MaterialInfo.MaterialSlot);

        _originalColorTableTexture = new SafeTextureHandle(*_colorTableTexture, true);
        if (_originalColorTableTexture == null)
            throw new InvalidOperationException("Material doesn't have a color table");

        ColorTable     = new Half[TextureLength];
        _updatePending = true;

        framework.Update += OnFrameworkUpdate;
    }

    protected override void Clear(bool disposing, bool reset)
    {
        _framework.Update -= OnFrameworkUpdate;

        base.Clear(disposing, reset);

        if (reset)
            _originalColorTableTexture.Exchange(ref *(nint*)_colorTableTexture);

        _originalColorTableTexture.Dispose();
    }

    public void ScheduleUpdate()
    {
        _updatePending = true;
    }

    [SkipLocalsInit]
    private void OnFrameworkUpdate(IFramework _)
    {
        if (!_updatePending)
            return;

        _updatePending = false;

        if (!CheckValidity())
            return;

        var textureSize = stackalloc int[2];
        textureSize[0] = TextureWidth;
        textureSize[1] = TextureHeight;

        using var texture =
            new SafeTextureHandle(Device.Instance()->CreateTexture2D(textureSize, 1, 0x2460, 0x80000804, 7), false);
        if (texture.IsInvalid)
            return;

        bool success;
        lock (ColorTable)
        {
            fixed (Half* colorTable = ColorTable)
            {
                success = texture.Texture->InitializeContents(colorTable);
            }
        }

        if (success)
            texture.Exchange(ref *(nint*)_colorTableTexture);
    }

    protected override bool IsStillValid()
    {
        if (!base.IsStillValid())
            return false;

        var colorSetTextures = DrawObject->ColorTableTextures;
        if (colorSetTextures == null)
            return false;

        return _colorTableTexture == colorSetTextures + (MaterialInfo.ModelSlot * 4 + MaterialInfo.MaterialSlot);
    }
}
