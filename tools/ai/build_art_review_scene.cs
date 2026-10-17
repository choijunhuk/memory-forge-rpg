// Execute this method body through Unity MCP execute_code, outside Play Mode.
if (UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Stop Play Mode before building the review scene.");
for (var loadedIndex = 0; loadedIndex < UnityEngine.SceneManagement.SceneManager.sceneCount; loadedIndex++)
{
    var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneAt(loadedIndex);
    if (loaded.isDirty)
        throw new System.InvalidOperationException("Unsaved loaded scene: " + loaded.name);
}
var scenePath = "Assets/Scenes/MemoryForgeArtReview.unity";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath) != null)
    throw new System.InvalidOperationException("Review scene already exists; do not overwrite it automatically.");
// AssetDatabase misses a not-yet-imported file that Refresh/SaveScene would replace, so the exact disk entry is refused too
// (file, directory or dangling symlink), before any import and again right before saving.
var sceneDisk = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), scenePath);
System.Action refuseDiskScene = () =>
{
    var sceneDirectory = System.IO.Path.GetDirectoryName(sceneDisk);
    var sceneName = System.IO.Path.GetFileName(sceneDisk);
    if (System.IO.File.Exists(sceneDisk) || System.IO.Directory.Exists(sceneDisk)
        || (System.IO.Directory.Exists(sceneDirectory) && System.Array.Exists(System.IO.Directory.GetFileSystemEntries(sceneDirectory),
            existing => string.Equals(System.IO.Path.GetFileName(existing), sceneName, System.StringComparison.OrdinalIgnoreCase))))
        throw new System.InvalidOperationException("Review scene already exists on disk; do not overwrite it automatically: " + sceneDisk);
};
refuseDiskScene();
var font = UnityEngine.Resources.Load<UnityEngine.Font>("Fonts/IBMPlexSansKR-Regular");
if (font == null) throw new System.InvalidOperationException("Existing Korean font not found.");
var folder = "Assets/Art/Candidates/Starter20261017/";
var ids = new[] { "sera", "harin", "nia" };
var titles = new[] { "세라", "하린", "니아" };
var kinds = new[] { "sheet", "cutin", "sd" };
var textures = new System.Collections.Generic.Dictionary<string, UnityEngine.Texture2D>();
UnityEditor.AssetDatabase.Refresh();
// Every image must resolve before the first importer setting changes, so a missing file leaves no partial import edits.
var importers = new System.Collections.Generic.Dictionary<string, UnityEditor.TextureImporter>();
foreach (var id in ids)
foreach (var kind in kinds)
{
    var version = kind == "sd" || (id == "sera" && kind == "sheet") ? "v02" : "v01";
    var path = folder + "char_" + id + "_" + kind + "_default_" + version + ".png";
    var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    if (importer == null) throw new System.InvalidOperationException("Missing image: " + path);
    importers.Add(id + "_" + kind, importer);
}
foreach (var entry in importers)
{
    var importer = entry.Value;
    var path = importer.assetPath;
    var kind = entry.Key.Substring(entry.Key.IndexOf('_') + 1);
    importer.textureType = UnityEditor.TextureImporterType.Default;
    importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
    importer.maxTextureSize = 2048;
    importer.mipmapEnabled = true;
    importer.filterMode = UnityEngine.FilterMode.Trilinear;
    importer.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput;
    importer.alphaIsTransparency = kind == "sd";
    importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    importer.SaveAndReimport();
    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    if (texture == null) throw new System.InvalidOperationException("Import failed: " + path);
    textures.Add(entry.Key, texture);
}
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);
var camera = new UnityEngine.GameObject("ArtReviewCamera", typeof(UnityEngine.Camera))
    .GetComponent<UnityEngine.Camera>();
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(0.025f, 0.04f, 0.065f);
var canvasObject = new UnityEngine.GameObject("ArtReviewCanvas", typeof(UnityEngine.RectTransform),
    typeof(UnityEngine.Canvas), typeof(UnityEngine.UI.CanvasScaler));
canvasObject.GetComponent<UnityEngine.Canvas>().renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = new UnityEngine.Vector2(1920, 1080);
scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
System.Action<UnityEngine.GameObject, float, float, float, float> place = (go, x, y, w, h) =>
{
    go.transform.SetParent(canvasObject.transform, false);
    var rect = go.GetComponent<UnityEngine.RectTransform>();
    rect.anchorMin = rect.anchorMax = rect.pivot = new UnityEngine.Vector2(0, 1);
    rect.anchoredPosition = new UnityEngine.Vector2(x, -y);
    rect.sizeDelta = new UnityEngine.Vector2(w, h);
};
System.Action<string, string, int, float, float, float, float> label = (name, value, size, x, y, w, h) =>
{
    var go = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Text));
    place(go, x, y, w, h);
    var text = go.GetComponent<UnityEngine.UI.Text>();
    text.font = font;
    text.text = value;
    text.fontSize = size;
    text.color = new UnityEngine.Color(0.9f, 0.92f, 0.94f);
    text.raycastTarget = false;
};
label("Title", "기억 공방 · 초기 3인 아트 검수", 40, 32, 24, 1840, 60);
label("Status", "개발 시안 9장 · 시트 / 카드 컷인 / 투명 SD · 최종 승인 전", 22, 36, 90, 1840, 40);
for (var col = 0; col < ids.Length; col++)
{
    var x = 24 + 632 * col;
    var panel = new UnityEngine.GameObject(ids[col] + "_Panel", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Image));
    place(panel, x, 148, 608, 872);
    panel.GetComponent<UnityEngine.UI.Image>().color = new UnityEngine.Color(0.06f, 0.085f, 0.12f);
    panel.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
    label(ids[col] + "_Title", titles[col], 32, x + 20, 164, 568, 48);
    for (var row = 0; row < kinds.Length; row++)
    {
        var key = ids[col] + "_" + kinds[row];
        var go = new UnityEngine.GameObject(key, typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.RawImage));
        if (row == 0) place(go, x + 20, 224, 568, 284);
        else if (row == 1) place(go, x + 20, 564, 264, 352);
        else place(go, x + 308, 584, 280, 280);
        var image = go.GetComponent<UnityEngine.UI.RawImage>();
        image.texture = textures[key];
        image.raycastTarget = false;
    }
    label(ids[col] + "_CardLabel", "카드 컷인", 22, x + 20, 522, 264, 34);
    label(ids[col] + "_SdLabel", "지도 SD · 투명", 22, x + 308, 522, 280, 34);
    label(ids[col] + "_Note", "외형·의상·무기 참조 시안\n최종 리그·애니메이션 별도", 20, x + 20, 938, 568, 64);
}
refuseDiskScene();
if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath))
    throw new System.InvalidOperationException("Could not save the review scene.");
UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
return new { scene = scenePath, textures = textures.Count, newRuntimeScripts = 0 };
