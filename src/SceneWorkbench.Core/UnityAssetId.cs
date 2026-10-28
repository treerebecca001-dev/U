using System.Globalization;
using System.Text.RegularExpressions;

namespace SceneWorkbench.Core;

public readonly record struct UnityAssetId
{
    private static readonly Regex GuidPattern = new("\\A[0-9a-fA-F]{32}\\z", RegexOptions.CultureInvariant);

    public string Guid { get; }
    public long FileId { get; }

    public UnityAssetId(string guid, long fileId)
    {
        ArgumentNullException.ThrowIfNull(guid);
        if (!GuidPattern.IsMatch(guid))
            throw new ArgumentException("Unity GUID 必須是 32 位十六進位字串。", nameof(guid));
        Guid = guid.ToLowerInvariant();
        FileId = fileId;
    }

    public override string ToString() => $"{Guid}:{FileId.ToString(CultureInfo.InvariantCulture)}";
}
