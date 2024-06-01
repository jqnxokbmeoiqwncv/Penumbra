using Penumbra.Interop.PathResolving;
using Penumbra.Meta;
using Penumbra.Meta.Files;
using Penumbra.Meta.Manipulations;
using Penumbra.String.Classes;

namespace Penumbra.Collections.Cache;

public readonly struct ImcCache : IDisposable
{
    private readonly Dictionary<Utf8GamePath, ImcFile> _imcFiles         = [];
    private readonly List<(ImcManipulation, ImcFile)>  _imcManipulations = [];

    public ImcCache()
    { }

    public void SetFiles(ModCollection collection, bool fromFullCompute)
    {
        if (fromFullCompute)
            foreach (var path in _imcFiles.Keys)
                collection._cache!.ForceFileSync(path, PathDataHandler.CreateImc(path.Path, collection));
        else
            foreach (var path in _imcFiles.Keys)
                collection._cache!.ForceFile(path, PathDataHandler.CreateImc(path.Path, collection));
    }

    public void Reset(ModCollection collection)
    {
        foreach (var (path, file) in _imcFiles)
        {
            collection._cache!.RemovePath(path);
            file.Reset();
        }

        _imcManipulations.Clear();
    }

    public bool ApplyMod(MetaFileManager manager, ModCollection collection, ImcManipulation manip)
    {
        if (!manip.Validate(true))
            return false;

        var idx = _imcManipulations.FindIndex(p => p.Item1.Equals(manip));
        if (idx < 0)
        {
            idx = _imcManipulations.Count;
            _imcManipulations.Add((manip, null!));
        }

        var path = manip.GamePath();
        try
        {
            if (!_imcFiles.TryGetValue(path, out var file))
                file = new ImcFile(manager, manip);

            _imcManipulations[idx] = (manip, file);
            if (!manip.Apply(file))
                return false;

            _imcFiles[path] = file;
            var fullPath = PathDataHandler.CreateImc(file.Path.Path, collection);
            collection._cache!.ForceFile(path, fullPath);

            return true;
        }
        catch (ImcException e)
        {
            manager.ValidityChecker.ImcExceptions.Add(e);
            Penumbra.Log.Error(e.ToString());
        }
        catch (Exception e)
        {
            Penumbra.Log.Error($"Could not apply IMC Manipulation {manip}:\n{e}");
        }

        return false;
    }

    public bool RevertMod(MetaFileManager manager, ModCollection collection, ImcManipulation m)
    {
        if (!m.Validate(false))
            return false;

        var idx = _imcManipulations.FindIndex(p => p.Item1.Equals(m));
        if (idx < 0)
            return false;

        var (_, file) = _imcManipulations[idx];
        _imcManipulations.RemoveAt(idx);

        if (_imcManipulations.All(p => !ReferenceEquals(p.Item2, file)))
        {
            _imcFiles.Remove(file.Path);
            collection._cache!.ForceFile(file.Path, FullPath.Empty);
            file.Dispose();
            return true;
        }

        var def   = ImcFile.GetDefault(manager, file.Path, m.EquipSlot, m.Variant.Id, out _);
        var manip = m.Copy(def);
        if (!manip.Apply(file))
            return false;

        var fullPath = PathDataHandler.CreateImc(file.Path.Path, collection);
        collection._cache!.ForceFile(file.Path, fullPath);

        return true;
    }

    public void Dispose()
    {
        foreach (var file in _imcFiles.Values)
            file.Dispose();

        _imcFiles.Clear();
        _imcManipulations.Clear();
    }

    public bool GetImcFile(Utf8GamePath path, [NotNullWhen(true)] out ImcFile? file)
        => _imcFiles.TryGetValue(path, out file);
}
