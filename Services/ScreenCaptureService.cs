using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tesseract;

namespace OverlayApp.Services
{
    public class ScreenCaptureService
    {
        // Importer les fonctions Windows API pour la capture d'écran
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, CopyPixelOperation rop);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // Capture une région spécifique de l'écran
        public Bitmap? CaptureScreenRegion(int x, int y, int width, int height)
        {
            try
            {
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr desktopDC = GetWindowDC(GetDesktopWindow());
                    IntPtr hdcDest = graphics.GetHdc();

                    BitBlt(hdcDest, 0, 0, width, height, desktopDC, x, y, CopyPixelOperation.SourceCopy);

                    graphics.ReleaseHdc(hdcDest);
                    ReleaseDC(GetDesktopWindow(), desktopDC);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur capture d'écran: {ex.Message}");
                return null;
            }
        }

        // Trouver la fenêtre du client League of Legends
        public Rectangle? GetLeagueClientWindow()
        {
            try
            {
                // Chercher la fenêtre du client LoL (plusieurs noms possibles)
                string[] possibleWindowNames = {
                    "League of Legends",
                    "League of Legends (TM) Client"
                };

                foreach (var windowName in possibleWindowNames)
                {
                    IntPtr hWnd = FindWindow(null, windowName);
                    if (hWnd != IntPtr.Zero)
                    {
                        GetWindowRect(hWnd, out RECT rect);
                        return new Rectangle(
                            rect.Left,
                            rect.Top,
                            rect.Right - rect.Left,
                            rect.Bottom - rect.Top
                        );
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur recherche fenêtre LoL: {ex.Message}");
                return null;
            }
        }
    }

    public class OcrService
    {
        private readonly string _tessDataPath;
        private TesseractEngine? _engine;

        public OcrService(string tessDataPath = @".\tessdata")
        {
            _tessDataPath = tessDataPath;
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                // Initialiser Tesseract avec la langue anglaise
                _engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                _engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 #");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur initialisation Tesseract: {ex.Message}");
                Console.WriteLine("Assurez-vous que les données Tesseract sont installées dans ./tessdata");
            }
        }

        // Extraire du texte depuis une image
        public string? ExtractText(Bitmap image)
        {
            if (_engine == null)
                return null;

            try
            {
                // Convertir Bitmap en Pix pour Tesseract
                using (var ms = new System.IO.MemoryStream())
                {
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    using (var pix = Pix.LoadFromMemory(ms.ToArray()))
                    using (var page = _engine.Process(pix))
                    {
                        return page.GetText();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur OCR: {ex.Message}");
                return null;
            }
        }

        // Extraire les noms de joueurs depuis l'écran de draft
        public List<string> ExtractPlayerNames(Bitmap draftScreenshot)
        {
            var playerNames = new List<string>();
            
            if (_engine == null)
                return playerNames;

            try
            {
                // TODO: Adapter les coordonnées selon la position réelle des noms dans l'interface
                // Ces valeurs sont des exemples et devront être ajustées
                var nameRegions = new[]
                {
                    new Rectangle(100, 100, 200, 30),  // Joueur 1
                    new Rectangle(100, 140, 200, 30),  // Joueur 2
                    new Rectangle(100, 180, 200, 30),  // Joueur 3
                    new Rectangle(100, 220, 200, 30),  // Joueur 4
                    new Rectangle(100, 260, 200, 30),  // Joueur 5
                };

                foreach (var region in nameRegions)
                {
                    using (Bitmap nameRegion = draftScreenshot.Clone(region, draftScreenshot.PixelFormat))
                    {
                        var text = ExtractText(nameRegion);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            playerNames.Add(text.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur extraction noms: {ex.Message}");
            }

            return playerNames;
        }

        public void Dispose()
        {
            _engine?.Dispose();
        }
    }
}
