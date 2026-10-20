using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Development-only Android APK for device smoke tests (not a release build). The dev package id, backend and
// architecture are set here in memory for this batchmode run only; project-wide Android settings stay a root decision.
// Run: Unity -batchmode -quit -nographics -buildTarget Android -projectPath <project>
//        -executeMethod MemoryForgeAndroidBuild.BuildDevApk [MF_APK_OUT=<path>] [MF_ANDROID_BACKEND=mono]
public static class MemoryForgeAndroidBuild
{
    const string DevPackage = "com.memoryforge.rpg.dev";
    // The lobby opens first and loads the expedition by name, so both scenes ship.
    static readonly string[] Scenes = { "Assets/Scenes/MemoryForgeLobby.unity", "Assets/Scenes/MemoryForgeExpedition.unity" };

    public static void BuildDevApk()
    {
        var output = Environment.GetEnvironmentVariable("MF_APK_OUT");
        if (string.IsNullOrEmpty(output)) output = "Builds/Android/MemoryForge-dev.apk";
        // IL2CPP + ARM64 by default; Mono only supports ARMv7 on Android, so the fallback switches architecture with it.
        var mono = Environment.GetEnvironmentVariable("MF_ANDROID_BACKEND") == "mono";
        var backend = mono ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, DevPackage);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend);
        PlayerSettings.Android.targetArchitectures = mono ? AndroidArchitecture.ARMv7 : AndroidArchitecture.ARM64;
        // The client lays out one fixed 1600x900 frame (FitView); the project still allows portrait, which shrinks it.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        EditorUserBuildSettings.buildAppBundle = false;
        // Gradle packages the APK in place, so entries a smaller build no longer has stay behind as dead bytes (a 149 MB build
        // came out as a 628 MB file after a big one). Removing its previous output makes it write a fresh file; nothing else is reused from it.
        var gradleApk = Path.Combine("Library/Bee/Android/Prj", mono ? "Mono2x" : "IL2CPP", "Gradle/launcher/build/outputs/apk");
        if (Directory.Exists(gradleApk)) Directory.Delete(gradleApk, true);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.Development,
        });
        var s = report.summary;
        Debug.Log($"MF_ANDROID_BUILD result={s.result} backend={backend} arch={PlayerSettings.Android.targetArchitectures} " +
                  $"bytes={s.totalSize} seconds={s.totalTime.TotalSeconds:F0} errors={s.totalErrors} warnings={s.totalWarnings} path={output}");
        if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
