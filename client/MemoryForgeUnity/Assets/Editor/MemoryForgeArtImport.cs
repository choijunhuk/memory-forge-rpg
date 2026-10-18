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
    }

    static readonly string[] SlicedKit = { "ui_rail_", "ui_hp_frame_", "ui_card_border_", "ui_button_pill_", "ui_panel_frame", "ui_card_face_plate", "ui_ribbon_title" };
}
