using UnityEditor;
using UnityEngine;

// Import settings for the art Unity loads from Resources/Art (byte copies of the originals in art/, OWNERSHIP.md).
// Characters are drawn at about half their source height at rest and up to ~2.6x in close shots. Without mipmaps the
// minified fine line art skipped pixels and looked broken at the edges; normal-quality block compression also damaged
// the lines. So: mipmaps with a sharpening (Kaiser) filter and a small negative bias (plain mipmaps looked soft),
// trilinear filtering, high-quality compression, no resize.
// This changes only how Unity samples the picture, never the picture itself.
public sealed class MemoryForgeArtImport : AssetPostprocessor
{
    const string ArtRoot = "Assets/Resources/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.mipMapBias = -0.5f; // lean to the sharper mip so cel lines stay crisp instead of soft
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        // Codex's battle UI kit (art/ui/battle-kit/README.md): its 9-sliced pieces are drawn at about source scale and must keep their
        // caps exact, so no mipmaps and bilinear. Its small icons are drawn at a third of their size and keep the mipmaps above.
        if (assetPath.StartsWith(ArtRoot + "UI/battle/") && System.Array.Exists(SlicedKit, prefix => System.IO.Path.GetFileName(assetPath).StartsWith(prefix)))
        {
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
        }
        // Android: the automatic format needs sizes in multiples of 4, and many originals are 1254×1254, so they fell back to
        // uncompressed RGBA32 with mipmaps (about 8 MB each; the dev APK reached 620 MB). ASTC takes any size: 4×4 blocks for the UI
        // kit's crisp edges and small icons, 6×6 for the paintings.
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true; android.maxTextureSize = 2048; android.compressionQuality = 100;
        android.format = assetPath.StartsWith(ArtRoot + "UI/") ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);
        // Android still shipped RGBA32: Unity skips every block format (ASTC and ETC2 alike) for a mipmapped texture whose sides are
        // not powers of two (measured 2026-10-20: 1254² with mipmaps → RGBA32, without → ASTC 6×6; 1254² scaled to 1024² with mipmaps →
        // ASTC 6×6). So on Android only: an NPOT square, which includes the 1254² portraits drawn at 64 px, is imported at the nearest
        // power of two and keeps its mipmaps (a Sprite import ignores that scaling, so it comes in as a plain texture, which is how the
        // client loads every art file anyway); any other NPOT shape is drawn near its own size (backgrounds about 1:1, moment figures
        // about 0.6×) and drops its mipmaps. Texture imports are kept per platform, so PC keeps the full-size mipmapped picture.
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android && importer.mipmapEnabled)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (!Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height))
            {
                if (width == height) { importer.textureType = TextureImporterType.Default; importer.npotScale = TextureImporterNPOTScale.ToNearest; }
                else importer.mipmapEnabled = false;
            }
        }
    }

    // Raised when the settings above change, so Unity imports every art texture again with them.
    public override uint GetVersion() => 3;

    static readonly string[] SlicedKit = { "ui_rail_", "ui_hp_frame_", "ui_card_border_", "ui_button_pill_", "ui_panel_frame", "ui_card_face_plate", "ui_ribbon_title" };
}
