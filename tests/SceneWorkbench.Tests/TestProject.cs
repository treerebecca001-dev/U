using System.Diagnostics;
using System.Text;
using SceneWorkbench.Core;

namespace SceneWorkbench.Tests;

internal sealed class TestProject : IDisposable
{
    public string Root { get; }
    public string Source => Path.Combine(Root, "source");
    public string Destination => Path.Combine(Root, "working");
    public string Base { get; }

    public TestProject()
    {
        var workspace = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(workspace, "SceneWorkbench.slnx")))
            workspace = Path.GetDirectoryName(workspace) ?? throw new InvalidOperationException("找不到測試工作區。");
        Base = Path.Combine(workspace, "artifacts", "test-runs");
        Root = Path.Combine(Base, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        foreach (var name in ProjectSession.RequiredRoots)
            Directory.CreateDirectory(Path.Combine(Source, name));
        Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2021.3.15f1\n");
        ConfigureUrp("12.1.15", "2021.3");
        Write("Assets/Scene.unity", SceneText());
        Write("Assets/Scene.unity.meta", "fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        Write("Assets/Second.asset", "original second asset\n");
        Directory.CreateDirectory(Path.Combine(Source, "Assets", "EmptyFolder"));
    }

    public void ConfigureUrp(string version, string minimum)
    {
        Write("Packages/manifest.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"" + version + "\"}}");
        Write("Packages/packages-lock.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":{\"version\":\"" + version + "\",\"source\":\"builtin\",\"depth\":0,\"dependencies\":{}}}}");
        Write("Library/PackageCache/com.unity.render-pipelines.universal@" + version + "/package.json",
            "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"" + version + "\",\"unity\":\"" + minimum + "\"}");
    }

    public string Write(string relative, string content)
    {
        var path = SafePath.Resolve(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public Task<WorkCopy> CopyAsync(bool cache = true) => WorkCopy.CreateAsync(Source, Destination, cache);

    public static string SceneText(string newline = "\n") => """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!1 &7
        GameObject:
          m_Name: "美術場景 🌲: 原始"
          m_Layer: 31
          m_Component:
          - component: {fileID: -2745811161023527127}
        --- !u!4 &-2745811161023527127
        Transform:
          m_GameObject: {fileID: 7}
          m_LocalPosition: {x: 1.0000, y: -2.5, z: 0}
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_UnknownSetting: [4, 8, 15, 16, 23, 42] # preserve this exact text
        --- !u!114 &99
        MonoBehaviour:
          m_Script: {fileID: 11500000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}
          m_UnknownManagedData:
            description: |
              保留未知腳本、縮排與資料。
              This object must remain byte-identical.
            pointer: {fileID: -9223372036854775808, guid: cccccccccccccccccccccccccccccccc, type: 3}
        """.ReplaceLineEndings(newline) + newline;

    public static void MakeDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot create the test junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }

    public void Dispose()
    {
        var resolved = SafePath.Canonical(Root);
        if (!SafePath.IsWithin(Base, resolved) || resolved.Equals(SafePath.Canonical(Base), SafePath.Comparison))
            throw new InvalidOperationException("測試清理路徑超出受控資料夾。");
        // Retain junction fixtures as evidence. Never traverse or recursively remove a link during cleanup.
        if (!Directory.Exists(resolved)) return;
        var directories = new Stack<string>();
        directories.Push(resolved);
        while (directories.Count != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
            }
        }
        Directory.Delete(resolved, true);
    }
}

internal sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
