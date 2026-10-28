using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SceneWorkbench.Validation
{
    // Development acceptance only. Never used by the standalone editor at runtime.
    public static class BaselineProbe
    {
        private const string Fixture = "Assets/SceneWorkbenchValidation/Fixtures/RoundTrip.unity";
        [Serializable] private sealed class Catalog { public ShaderRow[] shaders; public SceneRow[] scenes; }
        [Serializable] public sealed class ShaderRow { public string path; public string name; public string[] errors; }
        [Serializable] public sealed class SceneRow { public string path; public int rootObjects; public int missingScripts; }
        [Serializable] public sealed class PackageRow { public string name; public string version; public string source; public string resolvedPath; }
        [Serializable] public sealed class Report
        {
            public string schema = "scene-workbench-unity-baseline-v1";
            public string generatedUtc;
            public string mode;
            public string editorVersion;
            public string projectRoot;
            public string colorSpace;
            public bool developmentAcceptanceOnly = true;
            public bool provesIndependentGeneration = false;
            public bool success;
            public string failure;
            public PackageRow[] packages;
            public ShaderRow[] shaders;
            public SceneRow[] scenes;
            public string fixtureScene;
            public string fixtureGuid;
            public string fixtureTransformFileId;
            public Vector3 position;
            public string unityReturnedScene;
        }

        public static void Run()
        {
            var report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), mode = Argument("-sceneWorkbenchMode"),
                editorVersion = Application.unityVersion,
                projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                colorSpace = PlayerSettings.colorSpace.ToString(), fixtureScene = Fixture
            };
            var output = Argument("-sceneWorkbenchReport");
            try
            {
                Directory.SetCurrentDirectory(report.projectRoot);
                if (report.editorVersion != "2022.3.13f1") throw new InvalidOperationException("Unexpected Unity Editor version.");
                if (report.colorSpace != "Gamma") throw new InvalidOperationException("Reference Gamma setting changed.");
                report.packages = PackageInfo.GetAllRegisteredPackages().OrderBy(item => item.name, StringComparer.Ordinal)
                    .Select(item => new PackageRow { name = item.name, version = item.version, source = item.source.ToString(), resolvedPath = item.resolvedPath }).ToArray();
                RequirePackage(report.packages, "com.unity.render-pipelines.universal", "14.0.9");
                RequirePackage(report.packages, "com.unity.render-pipelines.core", "14.0.9");
                RequirePackage(report.packages, "com.unity.shadergraph", "14.0.9");
                RequirePackage(report.packages, "com.tuyoogame.yooasset", "1.4.5-preview");
                RequirePackage(report.packages, "com.unity.probuilder", "5.2.2");
                RequirePackage(report.packages, "com.unity.polybrush", "1.1.5");
                if (report.mode == "baseline")
                {
                    var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Argument("-sceneWorkbenchCatalog")));
                    var shaders = new List<ShaderRow>();
                    foreach (var source in catalog.shaders)
                    {
                        var shader = AssetDatabase.LoadAssetAtPath<Shader>(source.path);
                        if (shader == null) throw new InvalidOperationException("Shader cannot load: " + source.path);
                        var errors = ShaderUtil.GetShaderMessages(shader).Where(item => item.severity.ToString() == "Error")
                            .Select(item => item.message).ToArray();
                        shaders.Add(new ShaderRow { path = source.path, name = shader.name, errors = errors });
                    }
                    report.shaders = shaders.ToArray();
                    if (report.shaders.Length != 14 || report.shaders.Any(item => item.errors.Length != 0))
                        throw new InvalidOperationException("The fourteen unchanged ASE shaders require successful Unity import.");
                    var scenes = new List<SceneRow>();
                    foreach (var source in catalog.scenes)
                    {
                        var scene = EditorSceneManager.OpenScene(source.path, OpenSceneMode.Single);
                        var roots = scene.GetRootGameObjects();
                        var missing = roots.SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                            .Sum(item => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject));
                        scenes.Add(new SceneRow { path = source.path, rootObjects = roots.Length, missingScripts = missing });
                    }
                    report.scenes = scenes.ToArray();
                    if (File.Exists(Fixture)) throw new IOException("Fixture already exists; never overwrite acceptance data.");
                    Directory.CreateDirectory(Path.GetDirectoryName(Fixture));
                    AssetDatabase.Refresh();
                    var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var target = new GameObject("SceneWorkbenchRoundTrip");
                    target.transform.localPosition = new Vector3(1.25f, -2.5f, 0);
                    target.transform.localScale = new Vector3(1.2f, 1.3f, 1.4f);
                    target.layer = 31;
                    target.isStatic = true;
                    target.AddComponent<BoxCollider>().center = new Vector3(0.1f, 0.2f, 0.3f);
                    var untouched = new GameObject("UntouchedLight");
                    untouched.AddComponent<Light>().intensity = 2.25f;
                    if (!EditorSceneManager.SaveScene(fixture, Fixture)) throw new IOException("Unity fixture scene save failed.");
                    report.fixtureTransformFileId = unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(target.transform).targetObjectId).ToString(CultureInfo.InvariantCulture);
                    report.fixtureGuid = AssetDatabase.AssetPathToGUID(Fixture);
                    report.position = target.transform.localPosition;
                }
                else if (report.mode == "roundtrip")
                {
                    var scene = EditorSceneManager.OpenScene(Fixture, OpenSceneMode.Single);
                    var target = scene.GetRootGameObjects().Single(item => item.name == "SceneWorkbenchRoundTrip");
                    if ((target.transform.localPosition - new Vector3(7.125f, -8.25f, 9.5f)).sqrMagnitude > 0.000001f)
                        throw new InvalidDataException("Standalone edit was not loaded by Unity.");
                    if (target.layer != 31 || !target.isStatic || target.GetComponent<BoxCollider>() == null)
                        throw new InvalidDataException("Unedited scene state changed.");
                    if (scene.GetRootGameObjects().Single(item => item.name == "UntouchedLight").GetComponent<Light>().intensity != 2.25f)
                        throw new InvalidDataException("Unedited component state changed.");
                    report.position = target.transform.localPosition;
                    report.fixtureTransformFileId = unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(target.transform).targetObjectId).ToString(CultureInfo.InvariantCulture);
                    report.fixtureGuid = AssetDatabase.AssetPathToGUID(Fixture);
                    report.unityReturnedScene = "Assets/SceneWorkbenchValidation/Fixtures/UnityReturned.unity";
                    if (File.Exists(report.unityReturnedScene) || !EditorSceneManager.SaveScene(scene, report.unityReturnedScene))
                        throw new IOException("Unity return scene save failed or destination already exists.");
                }
                else throw new ArgumentException("Unknown acceptance mode.");
                report.success = true;
            }
            catch (Exception error)
            {
                report.failure = error.ToString();
                Debug.LogError(error);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.success ? 0 : 1);
        }

        private static string Argument(string name)
        {
            var values = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(values, name);
            if (index < 0 || index + 1 >= values.Length) throw new ArgumentException("Missing " + name);
            return values[index + 1];
        }

        private static void RequirePackage(PackageRow[] packages, string name, string version)
        {
            if (!packages.Any(item => item.name == name && item.version == version))
                throw new InvalidDataException("Expected locked package not registered: " + name + " " + version);
        }
    }
}
