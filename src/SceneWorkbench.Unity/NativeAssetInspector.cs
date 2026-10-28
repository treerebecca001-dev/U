using System.Globalization;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed record BinaryRoundTripResult(string SourcePath, string OutputPath, string SourceSha256, string OutputSha256,
    bool SourceUnchanged, bool SameObjectFields, bool ByteIdentical, string Status, NativeAssetSnapshot Before, NativeAssetSnapshot After)
{
    public bool UnityAccepted => false;
    public bool ProvesNewBakeGeneration => false;
}

public static class NativeAssetInspector
{
    public static NativeAssetSnapshot Inspect(string path, string displayPath, string? classDataPath = null)
    {
        SafePath.RejectLinks(path);
        var length = new FileInfo(path).Length;
        using (var stream = File.OpenRead(path))
        {
            Span<byte> prefix = stackalloc byte[16];
            var count = stream.Read(prefix);
            var prefixText = Encoding.UTF8.GetString(prefix[..count]);
            if (prefixText.StartsWith("%YAML", StringComparison.Ordinal) || prefixText.StartsWith("\ufeff%YAML", StringComparison.Ordinal))
                return new NativeAssetSnapshot(displayPath, "unity-yaml", null, null, length, null, "text-not-binary", [], null);
        }
        var manager = new AssetsManager();
        try
        {
            var instance = manager.LoadAssetsFile(path, false);
            var asset = instance.file;
            if (classDataPath is not null)
            {
                SafePath.RejectLinks(classDataPath);
                manager.LoadClassPackage(classDataPath);
                manager.LoadClassDatabaseFromPackage(asset.Metadata.UnityVersion);
            }
            var objects = new List<NativeObjectSnapshot>();
            var failures = new List<string>();
            foreach (var info in asset.AssetInfos)
            {
                var type = Enum.IsDefined(typeof(AssetClassID), info.TypeId) ? ((AssetClassID)info.TypeId).ToString() : info.TypeId.ToString(CultureInfo.InvariantCulture);
                try
                {
                    var root = manager.GetBaseField(instance, info);
                    var checksum = Hashing.Bytes(root.WriteToByteArray());
                    var fields = root.Children.Select(child => child.TemplateField.Name + ": " + child.TemplateField.Type).ToArray();
                    objects.Add(new NativeObjectSnapshot(type, info.PathId.ToString(CultureInfo.InvariantCulture), checksum, fields));
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    objects.Add(new NativeObjectSnapshot(type, info.PathId.ToString(CultureInfo.InvariantCulture), null, []));
                    failures.Add(type + ": " + error.GetType().Name);
                }
            }
            return new NativeAssetSnapshot(displayPath, "unity-serialized", asset.Header.Version, asset.Metadata.UnityVersion,
                length, asset.Metadata.TypeTreeEnabled, failures.Count == 0 ? "fields-read-locally" : "container-read-only",
                objects, failures.Count == 0 ? null : string.Join("; ", failures));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new NativeAssetSnapshot(displayPath, "unrecognized-binary", null, null, length, null, "unreadable", [], error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            manager.UnloadAllAssetsFiles(true);
        }
    }

    public static BinaryRoundTripResult RoundTripContainer(string sourcePath, string outputPath, string? classDataPath = null)
    {
        SafePath.RequireDisjoint(sourcePath, outputPath);
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
            throw new IOException("驗證輸出必須是新檔案。");
        var originalHash = Hashing.File(sourcePath);
        var before = Inspect(sourcePath, sourcePath, classDataPath);
        if (before.Format != "unity-serialized")
            throw new InvalidDataException("不是可讀取的 Unity serialized 資源。");
        var manager = new AssetsManager();
        try
        {
            var instance = manager.LoadAssetsFile(sourcePath, false);
            using var writer = new AssetsFileWriter(File.Open(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None));
            instance.file.Write(writer);
        }
        finally
        {
            manager.UnloadAllAssetsFiles(true);
        }
        var after = Inspect(outputPath, outputPath, classDataPath);
        var sameFields = before.InspectionStatus == "fields-read-locally" && after.InspectionStatus == "fields-read-locally"
            && before.UnityVersion == after.UnityVersion && before.SerializedVersion == after.SerializedVersion
            && before.Objects.Select(item => (item.Type, item.FileId, item.FieldSha256))
                .SequenceEqual(after.Objects.Select(item => (item.Type, item.FileId, item.FieldSha256)));
        var outputHash = Hashing.File(outputPath);
        return new BinaryRoundTripResult(sourcePath, outputPath, originalHash, outputHash, Hashing.File(sourcePath) == originalHash,
            sameFields, originalHash == outputHash, sameFields ? "local-container-roundtrip-only" : "semantic-check-incomplete", before, after);
    }
}
