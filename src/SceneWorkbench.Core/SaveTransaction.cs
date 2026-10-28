namespace SceneWorkbench.Core;

public sealed record FileUpdate(string RelativePath, string ExpectedSha256, byte[] Content);
public sealed record SaveProgress(int FilesWritten, int TotalFiles, string RelativePath);
public sealed record SaveResult(string? TransactionId, int ChangedFiles);
public enum JournalState { Prepared, Committing, Committed, RolledBack, RecoveryRequired }
public sealed record JournalEntry(string RelativePath, string OldSha256, string NewSha256, long OriginalWriteUtcTicks, string BackupName, string StagedName);
public sealed record SaveJournal(int SchemaVersion, string Id, JournalState State, DateTimeOffset CreatedUtc, IReadOnlyList<JournalEntry> Entries);

public sealed class SaveConflictException(string message) : IOException(message);

public sealed class SaveTransaction(IProjectWriteAccess workCopy)
{
    private const string TransactionRoot = WorkCopy.MetadataDirectory + "/transactions";

    public SaveResult Commit(IEnumerable<FileUpdate> requested, IProgress<SaveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var lease = AcquireLease();
        if (ReadPending().Count != 0)
            throw new InvalidOperationException("有未完成的存檔，請先執行 recover 復原。");
        var updates = requested.ToArray();
        var unique = new HashSet<string>(SafePath.Comparer);
        var changed = new List<FileUpdate>();
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = workCopy.ResolveEditable(update.RelativePath);
            if (!unique.Add(path))
                throw new InvalidOperationException("同一次存檔不可重複修改相同檔案。");
            EnsureHash(path, update.ExpectedSha256);
            if (!Hashing.Bytes(update.Content).Equals(update.ExpectedSha256, StringComparison.Ordinal))
                changed.Add(update);
        }
        if (changed.Count == 0)
            return new SaveResult(null, 0);

        var id = Guid.NewGuid().ToString("N");
        var directory = workCopy.Project.Resolve(TransactionRoot + "/" + id);
        Directory.CreateDirectory(directory);
        var entries = new List<JournalEntry>();
        foreach (var update in changed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = workCopy.ResolveEditable(update.RelativePath);
            var originalTime = System.IO.File.GetLastWriteTimeUtc(source).Ticks;
            var originalBytes = System.IO.File.ReadAllBytes(source);
            if (!Hashing.Bytes(originalBytes).Equals(update.ExpectedSha256, StringComparison.Ordinal))
                throw new SaveConflictException($"檔案已被外部修改，尚未覆寫：{update.RelativePath}");
            var index = entries.Count;
            var backupName = $"{index:D5}.backup";
            var stagedName = $"{index:D5}.staged";
            WriteBytesNew(SafePath.Resolve(directory, backupName), originalBytes);
            WriteBytesNew(SafePath.Resolve(directory, stagedName), update.Content);
            entries.Add(new JournalEntry(update.RelativePath, update.ExpectedSha256, Hashing.Bytes(update.Content), originalTime, backupName, stagedName));
        }
        var journal = new SaveJournal(1, id, JournalState.Prepared, DateTimeOffset.UtcNow, entries);
        var journalPath = SafePath.Resolve(directory, "journal.json");
        JsonFile.WriteNew(journalPath, journal);
        try
        {
            journal = journal with { State = JournalState.Committing };
            JsonFile.WriteAtomic(journalPath, journal);
            var written = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = workCopy.ResolveEditable(entry.RelativePath);
                EnsureHash(target, entry.OldSha256);
                var staged = SafePath.Resolve(directory, entry.StagedName);
                EnsureHash(staged, entry.NewSha256);
                System.IO.File.Move(staged, target, true);
                written++;
                progress?.Report(new SaveProgress(written, entries.Count, entry.RelativePath));
            }
            JsonFile.WriteAtomic(journalPath, journal with { State = JournalState.Committed });
            return new SaveResult(id, entries.Count);
        }
        catch (Exception failure)
        {
            try
            {
                RollBack(directory, journal);
            }
            catch (Exception recoveryFailure)
            {
                JsonFile.WriteAtomic(journalPath, journal with { State = JournalState.RecoveryRequired });
                throw new AggregateException("存檔中斷且自動復原遇到衝突；備份已保留。", failure, recoveryFailure);
            }
            throw;
        }
    }

    public IReadOnlyList<string> PendingTransactions() => ReadPending().Select(item => item.Journal.Id).ToArray();

    public IReadOnlyList<string> Recover()
    {
        using var lease = AcquireLease();
        var recovered = new List<string>();
        foreach (var (directory, journal) in ReadPending())
        {
            RollBack(directory, journal);
            recovered.Add(journal.Id);
        }
        return recovered;
    }

    private List<(string Directory, SaveJournal Journal)> ReadPending()
    {
        var root = workCopy.Project.Resolve(TransactionRoot);
        var result = new List<(string, SaveJournal)>();
        if (!System.IO.Directory.Exists(root))
            return result;
        foreach (var directory in System.IO.Directory.EnumerateDirectories(root).OrderBy(path => path, SafePath.Comparer))
        {
            SafePath.RejectLinks(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
                throw new InvalidDataException("存檔紀錄目錄名稱無效。");
            var path = SafePath.Resolve(directory, "journal.json");
            if (!System.IO.File.Exists(path))
                continue; // Preparation has not reached the point where project files can change.
            var journal = JsonFile.Read<SaveJournal>(path);
            if (journal.SchemaVersion != 1 || journal.Id != Path.GetFileName(directory) || !Enum.IsDefined(journal.State))
                throw new InvalidDataException("存檔紀錄版本、狀態或識別碼無效。");
            ValidateEntries(journal);
            if (journal.State is not JournalState.Committed and not JournalState.RolledBack)
                result.Add((directory, journal));
        }
        return result;
    }

    private void ValidateEntries(SaveJournal journal)
    {
        var unique = new HashSet<string>(SafePath.Comparer);
        for (var index = 0; index < journal.Entries.Count; index++)
        {
            var entry = journal.Entries[index];
            if (!unique.Add(workCopy.ResolveEditable(entry.RelativePath))
                || entry.BackupName != $"{index:D5}.backup" || entry.StagedName != $"{index:D5}.staged"
                || !IsHash(entry.OldSha256) || !IsHash(entry.NewSha256)
                || entry.OriginalWriteUtcTicks < DateTime.MinValue.Ticks || entry.OriginalWriteUtcTicks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("存檔紀錄含不安全的路徑或資料。");
        }
    }

    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private void RollBack(string directory, SaveJournal journal)
    {
        ValidateEntries(journal);
        // Preflight every entry so one external change cannot cause a partial restoration.
        foreach (var entry in journal.Entries)
        {
            var target = workCopy.ResolveEditable(entry.RelativePath);
            if (!System.IO.File.Exists(target))
                throw new SaveConflictException($"復原目標已被移除：{entry.RelativePath}");
            var current = Hashing.File(target);
            if (current != entry.OldSha256 && current != entry.NewSha256)
                throw new SaveConflictException($"復原目標已有其他修改，保留現況與備份：{entry.RelativePath}");
            EnsureHash(SafePath.Resolve(directory, entry.BackupName), entry.OldSha256);
        }
        foreach (var entry in journal.Entries.Reverse())
        {
            var target = workCopy.ResolveEditable(entry.RelativePath);
            var current = Hashing.File(target);
            if (current == entry.OldSha256)
                continue;
            if (current != entry.NewSha256)
                throw new SaveConflictException($"復原時檔案已改動：{entry.RelativePath}");
            var restore = SafePath.Resolve(directory, Guid.NewGuid().ToString("N") + ".restore");
            System.IO.File.Copy(SafePath.Resolve(directory, entry.BackupName), restore, false);
            System.IO.File.Move(restore, target, true);
            System.IO.File.SetLastWriteTimeUtc(target, new DateTime(entry.OriginalWriteUtcTicks, DateTimeKind.Utc));
        }
        JsonFile.WriteAtomic(SafePath.Resolve(directory, "journal.json"), journal with { State = JournalState.RolledBack });
    }

    private FileStream AcquireLease()
    {
        // Direct mode creates only the transaction store, lazily when a save/recovery is requested.
        Directory.CreateDirectory(workCopy.Project.Resolve(WorkCopy.MetadataDirectory));
        return new FileStream(workCopy.Project.Resolve(WorkCopy.MetadataDirectory + "/write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void EnsureHash(string path, string expected)
    {
        if (!IsHash(expected))
            throw new ArgumentException("預期雜湊無效。", nameof(expected));
        if (!System.IO.File.Exists(path) || Hashing.File(path) != expected)
            throw new SaveConflictException($"檔案已被外部修改或移除，尚未覆寫：{path}");
    }

    private static void WriteBytesNew(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(true);
    }
}
