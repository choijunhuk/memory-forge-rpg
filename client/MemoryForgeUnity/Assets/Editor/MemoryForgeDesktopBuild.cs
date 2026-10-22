using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// The development account screen is enabled only in Editor/Development builds.
public static class MemoryForgeDesktopBuild
{
    public static void BuildDev()
    {
        string output = Environment.GetEnvironmentVariable("MF_PC_OUT");
        if (string.IsNullOrEmpty(output)) output = "Builds/Memory Forge.app";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        // PC review often opens the player behind Codex; keep its first frame and async receipts running without focus.
        PlayerSettings.runInBackground = true;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MemoryForgeLobby.unity", "Assets/Scenes/MemoryForgeExpedition.unity", "Assets/Scenes/MemoryForgeServerPractice.unity", "Assets/Scenes/MemoryForgeGrowth.unity" },
            locationPathName = output, target = BuildTarget.StandaloneOSX, options = BuildOptions.Development
        });
        var s = report.summary;
        Debug.Log($"MF_DESKTOP_BUILD result={s.result} bytes={s.totalSize} seconds={s.totalTime.TotalSeconds:F1} errors={s.totalErrors} warnings={s.totalWarnings}");
        if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
