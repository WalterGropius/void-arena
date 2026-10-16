using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds a standalone macOS app of VOID KNIGHT into Builds/macOS, regenerating the scene first if the committed one
/// predates the current builder. From the menu (Hack &amp; Slash > Build macOS App) or headless:
/// <code>Unity -batchmode -buildTarget OSXUniversal -projectPath . -executeMethod MacBuild.BuildFromCommandLine</code>
/// </summary>
public static class MacBuild
{
    const string ScenePath = "Assets/HackAndSlash/Scenes/VoidArena.unity";
    const string ProductName = "Void Knight";
    const string OutPath = "Builds/macOS/" + ProductName + ".app";

    [MenuItem("Hack & Slash/Build macOS App", priority = 40)]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (Run()) EditorUtility.RevealInFinder(OutPath);
    }

    public static void BuildFromCommandLine()
    {
        bool ok;
        try { ok = Run(); }
        catch (Exception e) { Debug.LogException(e); ok = false; }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Run()
    {
        if (HackSlashBuilder.IsStale())
        {
            Debug.Log("[MacBuild] Scene is stale; rebuilding it before the player build.");
            HackSlashBuilder.Build();
        }

        // the project still carries the template's product name: name the app after the game for this build only
        string oldName = PlayerSettings.productName;
        PlayerSettings.productName = ProductName;
        SetArchitecture();
        try
        {
            if (Directory.Exists(OutPath)) Directory.Delete(OutPath, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log("[MacBuild] " + s.result + ": " + OutPath + " (" + (s.totalSize / (1024 * 1024)) + " MB, " +
                      s.totalErrors + " errors, " + s.totalWarnings + " warnings, " + s.totalTime.TotalSeconds.ToString("F0") + " s)");
            return s.result == BuildResult.Succeeded;
        }
        finally
        {
            PlayerSettings.productName = oldName;
        }
    }

    /// <summary>
    /// Apple silicon only: Unity 6.6 deprecates Intel Macs, and a universal build needs Burst's llvm-lipo, which some
    /// editor installs ship without its execute bit. Reached by reflection so this script compiles without Mac build support.
    /// </summary>
    static void SetArchitecture()
    {
        var type = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
        var prop = type?.GetProperty("architecture");
        if (prop == null || !prop.CanWrite) { Debug.LogWarning("[MacBuild] Can't set the architecture; using the editor default."); return; }
        var names = Enum.GetNames(prop.PropertyType);
        var arm64 = names.FirstOrDefault(n => n.Equals("ARM64", StringComparison.OrdinalIgnoreCase));
        if (arm64 == null) { Debug.LogWarning("[MacBuild] No ARM64 architecture in " + string.Join(", ", names) + "; using the editor default."); return; }
        prop.SetValue(null, Enum.Parse(prop.PropertyType, arm64));
        Debug.Log("[MacBuild] Architecture: " + arm64);
    }
}
