using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PulyaDura.EditorTools
{
    /// <summary>
    /// Собирает проект с нуля: слои, базовые материалы, GameConfig и сцену Main.
    /// Меню: Пуля — дура → Пересобрать сцену. Из командной строки:
    /// Unity -batchmode -projectPath . -executeMethod PulyaDura.EditorTools.ProjectBuilder.BuildAll -quit
    /// Существующий GameConfig не перезаписывается, правки в инспекторе сохраняются.
    /// </summary>
    public static class ProjectBuilder
    {
        const string Root = "Assets/_Game";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const string ConfigPath = Root + "/Configs/GameConfig.asset";
        const string RoomsPath = Root + "/Data/rooms.txt";

        [MenuItem("Пуля — дура/Пересобрать сцену")]
        public static void BuildAll()
        {
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Configs");
            EnsureFolder(Root + "/Materials");

            EnsureLayers();
            var lit = EnsureMaterial("Lit", "Universal Render Pipeline/Lit", m =>
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            var unlit = EnsureMaterial("Unlit", "Universal Render Pipeline/Unlit", m => { });
            var fx = EnsureMaterial("FxAdditive", "Universal Render Pipeline/Particles/Unlit", SetupAdditive);

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.litMaterial = lit;
            config.unlitMaterial = unlit;
            config.fxMaterial = fx;
            if (config.rooms == null) config.rooms = AssetDatabase.LoadAssetAtPath<TextAsset>(RoomsPath);
            EditorUtility.SetDirty(config);

            BuildScene(config);
            CleanupTemplate();

            PlayerSettings.productName = "Пуля — дура";
            PlayerSettings.companyName = "TPU Jam Team";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] Готово: " + ScenePath);
        }

        /// <summary>Сборка Windows-билда в Builds/Win (папка в .gitignore).</summary>
        [MenuItem("Пуля — дура/Собрать билд Windows")]
        public static void BuildWindows()
        {
            var report = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Win/PulyaDura.exe",
                target = BuildTarget.StandaloneWindows64,
                options = UnityEditor.BuildOptions.Development
            });
            Debug.Log($"[ProjectBuilder] Билд: {report.summary.result}, ошибок {report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static void BuildScene(GameConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 34f;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 20f, -12f), Quaternion.Euler(58f, 0f, 0f));

            var game = new GameObject("Game");
            game.AddComponent<Sfx>();
            game.AddComponent<GameManager>().config = config;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static Material EnsureMaterial(string name, string shader, System.Action<Material> setup)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader)) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Аддитивное смешивание для URP Particles/Unlit: свечение складывается, чёрное прозрачно.</summary>
        static void SetupAdditive(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(2.2f, 2.2f, 2.2f, 1f));
        }

        static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(Layers.Wall).stringValue = "Wall";
            layers.GetArrayElementAtIndex(Layers.Tank).stringValue = "Tank";
            layers.GetArrayElementAtIndex(Layers.Debris).stringValue = "Debris";
            tagManager.ApplyModifiedProperties();
        }

        static void CleanupTemplate()
        {
            foreach (var path in new[] { "Assets/TutorialInfo", "Assets/Readme.asset", "Assets/Scenes" })
                if (AssetDatabase.IsValidFolder(path) || File.Exists(path))
                    AssetDatabase.DeleteAsset(path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Кнопка Play всегда запускает сцену Main, какая бы сцена ни была открыта.
    /// Без этого склонированный проект открывается с пустой сценой Untitled.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMain
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";

        static PlayFromMain()
        {
            EditorApplication.update += WaitForEditor;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Apply();
            };
        }

        static void WaitForEditor()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitForEditor;
            Apply();
            // Свежий клон открывается с пустой сценой: сразу открываем Main, чтобы не путать судей
            var active = EditorSceneManager.GetActiveScene();
            if (!EditorApplication.isPlayingOrWillChangePlaymode && string.IsNullOrEmpty(active.path) && !active.isDirty
                && AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScene) != null)
                EditorSceneManager.OpenScene(MainScene);
        }

        static void Apply()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScene);
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
        }
    }
}
