using System.Text;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class SaveTransactionTests
{
    [Fact]
    public async Task SaveOnlyChangesCopyAndReloadsNativeYaml()
    {
        using var fixture = new TestProject();
        var sourcePath = Path.Combine(fixture.Source, "Assets", "Scene.unity");
        var original = File.ReadAllBytes(sourcePath);
        var copy = await fixture.CopyAsync();
        var document = UnityYamlDocument.Load(copy.Project.Resolve("Assets/Scene.unity"));
        document.SetString(7, "新場景", "GameObject", "m_Name");
        var transaction = new SaveTransaction(copy);
        var result = transaction.Commit([new FileUpdate("Assets/Scene.unity", document.OriginalSha256, document.ToBytes())]);
        Assert.Equal(1, result.ChangedFiles);
        Assert.Equal("新場景", UnityYamlDocument.Load(copy.Project.Resolve("Assets/Scene.unity")).GetScalar(7, "GameObject", "m_Name"));
        Assert.Equal(original, File.ReadAllBytes(sourcePath));
        Assert.Empty(transaction.PendingTransactions());
        var journal = JsonFile.Read<SaveJournal>(copy.Project.Resolve($".scene-workbench/transactions/{result.TransactionId}/journal.json"));
        Assert.Equal(JournalState.Committed, journal.State);
    }

    [Fact]
    public async Task NoOpDoesNotRewriteOrChangeTimestamp()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var path = copy.Project.Resolve("Assets/Scene.unity");
        var before = File.GetLastWriteTimeUtc(path);
        var bytes = File.ReadAllBytes(path);
        var result = new SaveTransaction(copy).Commit([new FileUpdate("Assets/Scene.unity", Hashing.Bytes(bytes), bytes)]);
        Assert.Equal(0, result.ChangedFiles);
        Assert.Null(result.TransactionId);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.False(Directory.Exists(copy.Project.Resolve(".scene-workbench/transactions")));
    }

    [Fact]
    public async Task ExternalChangePreventsAnyOfTheRequestedWrites()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var first = copy.Project.Resolve("Assets/Scene.unity");
        var second = copy.Project.Resolve("Assets/Second.asset");
        var firstHash = Hashing.File(first);
        var secondHash = Hashing.File(second);
        File.WriteAllText(second, "external artist change");
        Assert.Throws<SaveConflictException>(() => new SaveTransaction(copy).Commit([
            new FileUpdate("Assets/Scene.unity", firstHash, Encoding.UTF8.GetBytes("new first")),
            new FileUpdate("Assets/Second.asset", secondHash, Encoding.UTF8.GetBytes("new second"))
        ]));
        Assert.Equal(firstHash, Hashing.File(first));
        Assert.Equal("external artist change", File.ReadAllText(second));
    }

    [Fact]
    public async Task FailureAfterFirstReplacementRollsBackTheWholeTransaction()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var updates = Updates(copy);
        var before = updates.Select(update => update.ExpectedSha256).ToArray();
        var progress = new ImmediateProgress<SaveProgress>(_ => throw new IOException("Simulated interruption."));
        Assert.Throws<IOException>(() => new SaveTransaction(copy).Commit(updates, progress));
        Assert.Equal(before[0], Hashing.File(copy.ResolveEditable(updates[0].RelativePath)));
        Assert.Equal(before[1], Hashing.File(copy.ResolveEditable(updates[1].RelativePath)));
        Assert.Empty(new SaveTransaction(WorkCopy.Open(copy.Project.Root)).PendingTransactions());
    }

    [Fact]
    public async Task RecoveryRefusesToOverwriteAnExternalEditAndPreservesBackups()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var updates = Updates(copy);
        var original = File.ReadAllBytes(copy.ResolveEditable(updates[0].RelativePath));
        var progress = new ImmediateProgress<SaveProgress>(_ =>
        {
            File.WriteAllText(copy.ResolveEditable(updates[0].RelativePath), "external edit during save");
            throw new IOException("Simulated interruption.");
        });
        Assert.Throws<AggregateException>(() => new SaveTransaction(copy).Commit(updates, progress));
        var transaction = new SaveTransaction(WorkCopy.Open(copy.Project.Root));
        Assert.Single(transaction.PendingTransactions());
        Assert.Throws<SaveConflictException>(() => transaction.Recover());
        Assert.Equal("external edit during save", File.ReadAllText(copy.ResolveEditable(updates[0].RelativePath)));
        File.WriteAllBytes(copy.ResolveEditable(updates[0].RelativePath), original);
        Assert.Single(transaction.Recover());
        Assert.Empty(transaction.PendingTransactions());
    }

    [Fact]
    public async Task RestartRecoversAPartiallyCommittedJournal()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var updates = Updates(copy);
        var id = Guid.NewGuid().ToString("N");
        var directory = copy.Project.Resolve(".scene-workbench/transactions/" + id);
        Directory.CreateDirectory(directory);
        var entries = new List<JournalEntry>();
        for (var index = 0; index < updates.Length; index++)
        {
            var update = updates[index];
            var target = copy.ResolveEditable(update.RelativePath);
            var backup = $"{index:D5}.backup";
            var staged = $"{index:D5}.staged";
            File.Copy(target, Path.Combine(directory, backup));
            File.WriteAllBytes(Path.Combine(directory, staged), update.Content);
            entries.Add(new JournalEntry(update.RelativePath, update.ExpectedSha256, Hashing.Bytes(update.Content), File.GetLastWriteTimeUtc(target).Ticks, backup, staged));
        }
        JsonFile.WriteNew(Path.Combine(directory, "journal.json"), new SaveJournal(1, id, JournalState.Committing, DateTimeOffset.UtcNow, entries));
        File.Move(Path.Combine(directory, entries[0].StagedName), copy.ResolveEditable(updates[0].RelativePath), true);
        Assert.NotEqual(updates[0].ExpectedSha256, Hashing.File(copy.ResolveEditable(updates[0].RelativePath)));
        var transaction = new SaveTransaction(WorkCopy.Open(copy.Project.Root));
        Assert.Single(transaction.Recover());
        Assert.Equal(updates[0].ExpectedSha256, Hashing.File(copy.ResolveEditable(updates[0].RelativePath)));
        Assert.Equal(updates[1].ExpectedSha256, Hashing.File(copy.ResolveEditable(updates[1].RelativePath)));
    }

    [Fact]
    public async Task DuplicateWritesAndEscapingJournalPathsAreRejected()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var update = Updates(copy)[0];
        Assert.Throws<InvalidOperationException>(() => new SaveTransaction(copy).Commit([update, update]));
        var id = Guid.NewGuid().ToString("N");
        var directory = copy.Project.Resolve(".scene-workbench/transactions/" + id);
        Directory.CreateDirectory(directory);
        var entry = new JournalEntry("../source/Assets/Scene.unity", update.ExpectedSha256, Hashing.Bytes(update.Content), DateTime.UtcNow.Ticks, "00000.backup", "00000.staged");
        JsonFile.WriteNew(Path.Combine(directory, "journal.json"), new SaveJournal(1, id, JournalState.Committing, DateTimeOffset.UtcNow, [entry]));
        Assert.Throws<InvalidOperationException>(() => new SaveTransaction(copy).Recover());
    }

    private static FileUpdate[] Updates(WorkCopy copy) =>
    [
        new("Assets/Scene.unity", Hashing.File(copy.ResolveEditable("Assets/Scene.unity")), Encoding.UTF8.GetBytes("new first file\n")),
        new("Assets/Second.asset", Hashing.File(copy.ResolveEditable("Assets/Second.asset")), Encoding.UTF8.GetBytes("new second file\n"))
    ];
}
