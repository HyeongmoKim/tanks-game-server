using System;
using System.Linq;
using Tanks.Complete;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PortfolioTanks.Editor
{
    /// <summary>
    /// Keeps the Asset Store demo untouched and creates a safe scene that we can
    /// gradually convert into a networked client.
    /// </summary>
    public static class TanksProjectSetup
    {
        private const string SourceScenePath =
            "Assets/_Tanks/Tutorial_Demo/Demo_Scenes/Demo_Game_Moon.unity";

        private const string RootFolder = "Assets/PortfolioTanks";
        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string TargetScenePath = ScenesFolder + "/TanksClient.unity";

        [MenuItem("Tools/Portfolio Tanks/Prepare Client Baseline")]
        public static void PrepareClientBaseline()
        {
            EnsureFolder("Assets", "PortfolioTanks");
            EnsureFolder(RootFolder, "Scenes");

            SceneAsset sourceScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath);
            if (sourceScene == null)
            {
                throw new InvalidOperationException(
                    $"Tanks source scene was not found at '{SourceScenePath}'.");
            }

            // Copy only once. Re-running this command must never overwrite later work.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
                {
                    throw new InvalidOperationException(
                        $"Failed to copy the Tanks scene to '{TargetScenePath}'.");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            ValidateScene(scene);

            // Scene index 0 is reloaded by the original GameManager after a match.
            // Keeping our client scene as the sole build scene preserves that behavior.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TargetScenePath, true)
            };

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[Portfolio Tanks] Client baseline is ready: {TargetScenePath}");
        }

        // Entry point used by command-line validation.
        public static void PrepareClientBaselineBatch()
        {
            try
            {
                PrepareClientBaseline();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateScene(Scene scene)
        {
            GameManager gameManager = FindInScene<GameManager>(scene);
            CameraControl cameraControl = FindInScene<CameraControl>(scene);
            GameUIHandler gameUi = FindInScene<GameUIHandler>(scene);
            MessageTextReference messageText = FindInScene<MessageTextReference>(scene);

            Require(gameManager != null, "GameManager is missing.");
            Require(cameraControl != null, "CameraControl is missing.");
            Require(gameUi != null, "GameUIHandler is missing.");
            Require(messageText != null, "MessageTextReference is missing.");
            Require(gameManager.m_CameraControl != null,
                "GameManager has no CameraControl reference.");
            Require(gameManager.m_SpawnPoints is { Length: >= 2 },
                "At least two tank spawn points are required.");
            Require(gameManager.m_Tank1Prefab != null
                    && gameManager.m_Tank2Prefab != null
                    && gameManager.m_Tank3Prefab != null
                    && gameManager.m_Tank4Prefab != null,
                "One or more tank prefab references are missing.");
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            return scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"Tanks client scene validation failed: {message}");
            }
        }

        private static void EnsureFolder(string parentFolder, string childFolder)
        {
            string path = $"{parentFolder}/{childFolder}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parentFolder, childFolder);
            }
        }
    }
}
