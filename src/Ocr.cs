using System;

namespace BTOptimizer
{
    /// <summary>
    /// OCR intégré à Windows (Windows.Media.Ocr) : lit le texte d'une image (capture d'écran,
    /// photo de manuel, PDF exporté en image…) pour la base de connaissances. 100 % local,
    /// aucune dépendance externe ni téléchargement — utilise les langues d'affichage de Windows.
    /// Tout est englobé : si l'OCR n'est pas disponible, on renvoie simplement du vide.
    /// </summary>
    internal static class Ocr
    {
        public static string Read(string path)
        {
            try
            {
                var file = Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
                using (var stream = file.OpenAsync(Windows.Storage.FileAccessMode.Read).AsTask().GetAwaiter().GetResult())
                {
                    var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                    using (var bmp = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult())
                    {
                        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                        if (engine == null)
                        {
                            try { engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("fr")); } catch { }
                        }
                        if (engine == null) return "";
                        var res = engine.RecognizeAsync(bmp).AsTask().GetAwaiter().GetResult();
                        return res != null ? (res.Text ?? "") : "";
                    }
                }
            }
            catch { return ""; }
        }
    }
}
