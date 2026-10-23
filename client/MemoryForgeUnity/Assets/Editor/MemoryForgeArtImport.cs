using UnityEditor;
using UnityEngine;

// Import settings for the art Unity loads from Resources/Art (byte copies of the originals in art/, OWNERSHIP.md).
// Characters are drawn at about half their source height at rest and up to ~2.6x in close shots. Without mipmaps the
// minified fine line art skipped pixels and looked broken at the edges; normal-quality block compression also damaged
// the lines. So: mipmaps with a sharpening (Kaiser) filter and a small negative bias (plain mipmaps looked soft),
// trilinear filtering, high-quality compression, and no resize except the power-of-two squares below.
// This changes only how Unity samples the picture, never the picture itself.
public sealed class MemoryForgeArtImport : AssetPostprocessor
{
    const string ArtRoot = "Assets/Resources/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (TextureImporter)assetImporter;
        // Codex's battle UI kit (art/ui/battle-kit/README.md): its 9-sliced pieces are drawn at about source scale and must keep their
        // caps exact, so no mipmaps and bilinear. Its small icons are drawn at a third of their size and keep the mipmaps.
        bool sliced = assetPath.StartsWith(ArtRoot + "UI/battle/") && System.Array.Exists(SlicedKit, prefix => System.IO.Path.GetFileName(assetPath).StartsWith(prefix));
        // Unity skips every block format (ASTC and ETC2 alike) for a mipmapped texture whose sides are not powers of two and ships it
        // as RGBA32 (measured 2026-10-20 on Android: 1254² with mipmaps → RGBA32, without → ASTC 6×6; 1254² scaled to 1024² with
        // mipmaps → ASTC 6×6; the dev APK had reached 628 MB). So an NPOT square, which includes the 1254² portraits drawn at 64 px, is
        // imported at the nearest power of two and keeps its mipmaps (a Sprite import ignores that scaling, so it comes in as a plain
        // texture, which is how the client loads every art file anyway); any other NPOT shape is drawn near its own size (backgrounds
        // about 1:1, moment figures about 0.6×) and drops its mipmaps. Unity writes these settings back to the .meta, so they are the
        // same on every platform instead of flipping with the build target. The type is set first: setting it resets other options.
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        bool npot = !Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height), squared = !sliced && npot && width == height;
        importer.textureType = squared ? TextureImporterType.Default : TextureImporterType.Sprite;
        if (!squared) importer.spriteImportMode = SpriteImportMode.Single;
        importer.npotScale = squared ? TextureImporterNPOTScale.ToNearest : TextureImporterNPOTScale.None;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = !sliced && (!npot || squared);
        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        importer.filterMode = sliced ? FilterMode.Bilinear : FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.mipMapBias = -0.5f; // lean to the sharper mip so cel lines stay crisp instead of soft
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.wrapMode = TextureWrapMode.Clamp;
        // Android: the automatic format (ETC2) needs sizes in multiples of 4, and many originals are 1254×1254. ASTC takes any size:
        // 4×4 blocks for the UI kit's crisp edges and small icons, 6×6 for the paintings.
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true; android.maxTextureSize = 2048; android.compressionQuality = 100;
        android.format = assetPath.StartsWith(ArtRoot + "UI/") ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);
    }

    // Raised when the settings above change, so Unity imports every art texture again with them.
    public override uint GetVersion() => 4;

    static readonly string[] SlicedKit = { "ui_rail_", "ui_hp_frame_", "ui_card_border_", "ui_button_pill_", "ui_panel_frame", "ui_card_face_plate", "ui_ribbon_title" };
}
