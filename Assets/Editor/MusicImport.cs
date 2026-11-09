using UnityEditor;
using UnityEngine;

/// <summary>
/// Music (anything under Resources/Music) is imported streamed and compressed when it is first added, so the music
/// manager can list a folder (Resources.LoadAll) without loading every track into memory. Only on first import:
/// settings changed by hand in the inspector afterwards are kept.
/// </summary>
class MusicImport : AssetPostprocessor {
    void OnPreprocessAudio() {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/Music/")) return;
        AudioImporter importer = (AudioImporter)assetImporter;
        if (!importer.importSettingsMissing) return;
        AudioImporterSampleSettings s = importer.defaultSampleSettings;
        s.loadType = AudioClipLoadType.Streaming;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        importer.defaultSampleSettings = s;
        importer.loadInBackground = true;
    }
}
