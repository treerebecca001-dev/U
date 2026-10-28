using System.Numerics;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class DirectProjectAccessTests
{
    [Fact]
    public void OpenAndNoOpSaveCreateNoMetadataOrCopy()
    {
        using var fixture = new TestProject(); var original = File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"));
        var access = DirectProjectAccess.Open(fixture.Source);
        var scene = new SceneEditSession(access.Project, "Assets/Scene.unity", access);
        Assert.Equal(0, scene.Save().ChangedFiles);
        Assert.False(Directory.Exists(access.Project.Resolve(WorkCopy.MetadataDirectory)));
        Assert.False(Directory.Exists(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(access.Project.Resolve("Assets/Scene.unity")));
    }
    [Fact]
    public void DirectSceneSaveFreshReadAndUndoBackUpOnlyTheChangedFile()
    {
        using var fixture = new TestProject(); var access = DirectProjectAccess.Open(fixture.Source);
        var relative = "Assets/Scene.unity"; var original = File.ReadAllBytes(access.Project.Resolve(relative));
        var meta = Hashing.File(access.Project.Resolve(relative + ".meta"));
        var scene = new SceneEditSession(access.Project, relative, access); var item = scene.Items.Single();
        scene.SetPosition(item.Id, new Vector3(7, 8, 9)); var result = scene.Save();
        var journalRoot = access.Project.Resolve(WorkCopy.MetadataDirectory + "/transactions/" + result.TransactionId);
        var journal = JsonFile.Read<SaveJournal>(Path.Combine(journalRoot, "journal.json"));
        Assert.Equal(relative, Assert.Single(journal.Entries).RelativePath); Assert.Equal(JournalState.Committed, journal.State);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(journalRoot, journal.Entries[0].BackupName)));
        Assert.Equal(new Vector3(7, 8, 9), new SceneEditSession(ProjectSession.Open(fixture.Source), relative, DirectProjectAccess.Open(fixture.Source)).GetItem(item.Id).Position);
        scene.Undo(); scene.Save(); Assert.Equal(original, File.ReadAllBytes(access.Project.Resolve(relative)));
        Assert.Equal(meta, Hashing.File(access.Project.Resolve(relative + ".meta")));
        Assert.False(File.Exists(access.Project.Resolve(WorkCopy.InfoPath))); Assert.False(Directory.Exists(fixture.Destination));
    }
    [Theory]
    [InlineData(16777217)]
    [InlineData(-16777217)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void DirectMaterialUsesExactIntegerAndPreservesShaderAndUnknownBytes(int value)
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddInteger(fixture);
        var access = DirectProjectAccess.Open(fixture.Source); var path = access.Project.Resolve("Assets/Test.mat");
        var original = File.ReadAllBytes(path); var shader = Hashing.File(access.Project.Resolve("Assets/Test.shader"));
        string? Resolve(string guid) => guid == MaterialEditSessionTests.ShaderGuid ? "Assets/Test.shader" : guid == MaterialEditSessionTests.TextureGuid ? "Assets/Texture.png" : null;
        var material = new MaterialEditSession(access.Project, "Assets/Test.mat", Resolve, writeAccess: access);
        material.SetInteger("_Count", value); var result = material.Save();
        var fresh = new MaterialEditSession(ProjectSession.Open(fixture.Source), "Assets/Test.mat", Resolve, writeAccess: DirectProjectAccess.Open(fixture.Source));
        Assert.Equal(value, Assert.IsType<int>(fresh.Snapshot(true).Properties.Single(p => p.Name == "_Count").Value[0]));
        var directory = access.Project.Resolve(WorkCopy.MetadataDirectory + "/transactions/" + result.TransactionId);
        var journal = JsonFile.Read<SaveJournal>(Path.Combine(directory, "journal.json"));
        Assert.Equal("Assets/Test.mat", Assert.Single(journal.Entries).RelativePath);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(directory, journal.Entries[0].BackupName)));
        material.Undo(); material.Save(); Assert.Equal(original, File.ReadAllBytes(path)); Assert.Equal(shader, Hashing.File(access.Project.Resolve("Assets/Test.shader")));
        Assert.False(File.Exists(access.Project.Resolve(WorkCopy.InfoPath)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSceneExternalContentOrMetaCannotBeOverwritten(bool meta)
    {
        using var fixture = new TestProject(); var access = DirectProjectAccess.Open(fixture.Source);
        var scene = new SceneEditSession(access.Project, "Assets/Scene.unity", access); scene.SetPosition(scene.Items.Single().Id, Vector3.One);
        var changed = access.Project.Resolve(meta ? "Assets/Scene.unity.meta" : "Assets/Scene.unity");
        File.AppendAllText(changed, "\n# external owner edit\n"); var external = File.ReadAllBytes(changed);
        Assert.Throws<SaveConflictException>(() => scene.Save()); Assert.True(scene.IsDirty); Assert.Equal(external, File.ReadAllBytes(changed));
    }
    [Fact]
    public void DirectTransactionFailureRollsBackBothFilesAndKeepsBackups()
    {
        using var fixture = new TestProject(); var access = DirectProjectAccess.Open(fixture.Source);
        var first = "Assets/Scene.unity"; var second = "Assets/Second.asset";
        var a = File.ReadAllBytes(access.Project.Resolve(first)); var b = File.ReadAllBytes(access.Project.Resolve(second));
        var tx = new SaveTransaction(access);
        Assert.Throws<InvalidOperationException>(() => tx.Commit([new FileUpdate(first, Hashing.Bytes(a), [1, 2]), new FileUpdate(second, Hashing.Bytes(b), [3, 4])],
            new ImmediateProgress<SaveProgress>(_ => throw new InvalidOperationException("simulated write interruption"))));
        Assert.Equal(a, File.ReadAllBytes(access.Project.Resolve(first))); Assert.Equal(b, File.ReadAllBytes(access.Project.Resolve(second)));
        var directory = Directory.GetDirectories(access.Project.Resolve(WorkCopy.MetadataDirectory + "/transactions")).Single();
        var journal = JsonFile.Read<SaveJournal>(Path.Combine(directory, "journal.json")); Assert.Equal(JournalState.RolledBack, journal.State); Assert.Equal(2, journal.Entries.Count);
        Assert.Empty(tx.PendingTransactions()); Assert.False(File.Exists(access.Project.Resolve(WorkCopy.InfoPath)));
    }
    [Fact]
    public void DirectWriteLockAndPathBoundariesDoNotAllowAnotherWrite()
    {
        using var fixture = new TestProject(); var access = DirectProjectAccess.Open(fixture.Source);
        Assert.Throws<InvalidOperationException>(() => access.ResolveEditable("../source/Assets/Scene.unity"));
        Assert.Throws<InvalidOperationException>(() => access.ResolveEditable("Assets/../../escape.asset"));
        Directory.CreateDirectory(access.Project.Resolve(WorkCopy.MetadataDirectory));
        using var lease = new FileStream(access.Project.Resolve(WorkCopy.MetadataDirectory + "/write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var scene = new SceneEditSession(access.Project, "Assets/Scene.unity", access); var original = File.ReadAllBytes(access.Project.Resolve("Assets/Scene.unity"));
        scene.SetPosition(scene.Items.Single().Id, Vector3.One); Assert.Throws<IOException>(() => scene.Save());
        Assert.Equal(original, File.ReadAllBytes(access.Project.Resolve("Assets/Scene.unity"))); Assert.True(scene.IsDirty);
    }
    [Fact]
    public async Task WriteScopeCannotPointAtAnotherOtherwiseIdenticalProject()
    {
        using var fixture = new TestProject(); await fixture.CopyAsync();
        Assert.Throws<ArgumentException>(() => new SceneEditSession(ProjectSession.Open(fixture.Source), "Assets/Scene.unity", DirectProjectAccess.Open(fixture.Destination)));
    }
}
