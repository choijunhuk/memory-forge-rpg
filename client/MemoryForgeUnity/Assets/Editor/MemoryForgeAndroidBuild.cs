using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        // Gradle can retain old split assets with incompatible object IDs. Removing only previous APK outputs forces fresh packaging.
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
        if (s.result == BuildResult.Succeeded) ValidateApk(output);
        Debug.Log($"MF_ANDROID_BUILD result={s.result} backend={backend} arch={PlayerSettings.Android.targetArchitectures} " +
                  $"bytes={s.totalSize} seconds={s.totalTime.TotalSeconds:F0} errors={s.totalErrors} warnings={s.totalWarnings} path={output}");
        if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void ValidateApk(string path)
    {
        using (var apk = ZipFile.OpenRead(path))
        {
            var names = new HashSet<string>(apk.Entries.Select(entry => entry.FullName), StringComparer.Ordinal);
            var splits = apk.Entries.Select(entry => Regex.Match(entry.FullName, @"\A(assets/bin/Data/.+)\.split([0-9]+)\z"))
                .Where(match => match.Success).GroupBy(match => match.Groups[1].Value);
            foreach (var file in splits)
            {
                if (names.Contains(file.Key))
                    throw new InvalidDataException($"APK contains both whole and split Unity data: {file.Key}");
                var count = file.Count();
                for (int index = 0; index < count; index++)
                    if (!names.Contains(file.Key + ".split" + index))
                        throw new InvalidDataException($"APK split sequence must start at zero without gaps: {file.Key}");
            }
        }
    }
}
