using UnityEditor;
using UnityEngine;

namespace PulyaDura.EditorTools
{
    /// <summary>
    /// Музыка из Resources/Music импортируется потоково (Streaming, Vorbis):
    /// 6-минутный трек гаража не занимает память целиком. Короткие звуки остаются как есть.
    /// </summary>
    public class MusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
        }
    }
}
