using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Tesseract;

namespace OverlayApp.Services
{
    public class AutoDetectionService : IDisposable
    {
        private readonly ScreenCaptureService _screenCapture;
        private TesseractEngine? _tesseractEngine;
        private readonly string[] _roleKeywords = { "TOP", "JUNGLE", "MID", "MIDDLE", "BOTTOM", "BOT", "SUPPORT", "SUP" };

        public AutoDetectionService(ScreenCaptureService screenCapture)
        {
            _screenCapture = screenCapture;
            InitializeTesseract();
        }

        private void InitializeTesseract()
        {
            try
            {
                var tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                _tesseractEngine = new TesseractEngine(tessdataPath, "eng");
                _tesseractEngine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur initialisation Tesseract: {ex.Message}");
            }
        }

        public List<(Rectangle playerRegion, Rectangle roleRegion, string playerName, string role)> AutoDetectPlayers(
            int searchX, int searchY, int searchWidth, int searchHeight)
        {
            if (_tesseractEngine == null)
            {
                Console.WriteLine("Tesseract non initialisé");
                return new List<(Rectangle, Rectangle, string, string)>();
            }

            var results = new List<(Rectangle playerRegion, Rectangle roleRegion, string playerName, string role)>();

            try
            {
                // Capturer la région de recherche
                var searchBitmap = _screenCapture.CaptureScreenRegion(searchX, searchY, searchWidth, searchHeight);
                if (searchBitmap == null)
                {
                    Console.WriteLine("Erreur capture bitmap");
                    return results;
                }
                
                // Sauvegarder pour debug
                searchBitmap.Save("debug_capture.png");
                Console.WriteLine("Capture sauvegardée dans debug_capture.png");
                
                // Convertir en format Tesseract
                var pixData = BitmapToByteArray(searchBitmap);
                using var pix = Pix.LoadFromMemory(pixData);
                using var page = _tesseractEngine.Process(pix);

                // Extraire tout le texte détecté
                var fullText = page.GetText();
                Console.WriteLine($"=== Texte OCR détecté ===");
                Console.WriteLine(fullText);
                Console.WriteLine($"========================");

                // Extraire tous les mots avec leurs positions
                var wordData = new List<(string text, Rectangle bounds, float confidence)>();
                using (var iter = page.GetIterator())
                {
                    iter.Begin();
                    do
                    {
                        if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                        {
                            var word = iter.GetText(PageIteratorLevel.Word);
                            var confidence = iter.GetConfidence(PageIteratorLevel.Word);
                            
                            if (!string.IsNullOrWhiteSpace(word) && confidence > 30)
                            {
                                // Ajuster les coordonnées à l'écran
                                var adjustedBounds = new Rectangle(
                                    searchX + bounds.X1,
                                    searchY + bounds.Y1,
                                    bounds.Width,
                                    bounds.Height
                                );
                                wordData.Add((word.Trim(), adjustedBounds, confidence));
                                Console.WriteLine($"Mot: '{word.Trim()}' à Y={adjustedBounds.Y} (conf: {confidence:F1}%)");
                            }
                        }
                    } while (iter.Next(PageIteratorLevel.Word));
                }

                Console.WriteLine($"\n=== {wordData.Count} mots détectés ===");
                
                // Stratégie basée sur le screenshot : chercher les marqueurs "Banning..." ou les rôles
                // Les joueurs sont dans la partie gauche, avec rôle au-dessus du pseudo
                
                // Trouver les positions Y des rôles (TOP, SUPPORT, BOTTOM, MID, JUNGLE)
                var rolePositions = new List<(string role, int y, Rectangle bounds)>();
                foreach (var (text, bounds, conf) in wordData)
                {
                    var role = ExtractRole(text);
                    if (role != "INCONNU")
                    {
                        rolePositions.Add((role, bounds.Y, bounds));
                        Console.WriteLine($"Rôle trouvé: {role} à Y={bounds.Y}");
                    }
                }

                // Trier les rôles par position Y
                rolePositions = rolePositions.OrderBy(r => r.y).ToList();
                Console.WriteLine($"\n{rolePositions.Count} rôles détectés");

                // Pour chaque rôle, chercher le pseudo juste en dessous (dans les 60px)
                foreach (var (role, roleY, roleBounds) in rolePositions)
                {
                    // Chercher des mots dans une zone en dessous du rôle
                    var candidateWords = wordData
                        .Where(w => w.bounds.Y > roleY && w.bounds.Y < roleY + 60)
                        .Where(w => Math.Abs(w.bounds.X - roleBounds.X) < 100) // Même colonne X
                        .Where(w => !_roleKeywords.Any(r => w.text.ToUpper().Contains(r)))
                        .Where(w => w.text.Length >= 3)
                        .OrderBy(w => w.bounds.Y)
                        .ToList();

                    if (candidateWords.Any())
                    {
                        var playerWord = candidateWords.First();
                        var playerName = playerWord.text;
                        var playerBounds = playerWord.bounds;
                        
                        // Élargir les bounds pour capturer tout le pseudo
                        playerBounds.Width = Math.Max(150, playerBounds.Width);
                        playerBounds.Height = Math.Max(30, playerBounds.Height);
                        
                        var roleRegion = roleBounds;
                        roleRegion.Width = Math.Max(100, roleRegion.Width);
                        roleRegion.Height = Math.Max(25, roleRegion.Height);

                        results.Add((playerBounds, roleRegion, playerName, role));
                        Console.WriteLine($"✓ Joueur détecté: {playerName} ({role}) à Y={playerBounds.Y}");
                        
                        if (results.Count >= 5) break;
                    }
                }

                searchBitmap.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur auto-détection: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }

            return results;
        }

        private List<List<(string text, Rectangle bounds, float confidence)>> GroupByLines(
            List<(string text, Rectangle bounds, float confidence)> wordData)
        {
            var lines = new List<List<(string text, Rectangle bounds, float confidence)>>();
            var sortedWords = wordData.OrderBy(w => w.bounds.Y).ThenBy(w => w.bounds.X).ToList();

            foreach (var word in sortedWords)
            {
                bool addedToLine = false;
                
                foreach (var line in lines)
                {
                    var avgY = line.Average(w => w.bounds.Y);
                    if (Math.Abs(word.bounds.Y - avgY) < 15) // Tolérance de 15px
                    {
                        line.Add(word);
                        addedToLine = true;
                        break;
                    }
                }

                if (!addedToLine)
                {
                    lines.Add(new List<(string, Rectangle, float)> { word });
                }
            }

            return lines;
        }

        private bool LooksLikePlayerName(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
                return false;

            // Ne doit pas être un mot-clé de rôle
            var upperText = text.ToUpper();
            if (_roleKeywords.Any(r => upperText.Contains(r)))
                return false;

            // Doit contenir au moins une lettre
            return text.Any(char.IsLetter);
        }

        private string ExtractRole(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "INCONNU";
                
            var upperText = text.ToUpper().Replace(" ", "").Replace(".", "").Replace(",", "");
            
            if (upperText.Contains("TOP"))
                return "TOP";
            if (upperText.Contains("JUNGLE") || upperText.Contains("JGL") || upperText.Contains("JUNGL"))
                return "JUNGLE";
            if (upperText.Contains("MID") || upperText.Contains("MIDDLE"))
                return "MID";
            if (upperText.Contains("BOTTOM") || upperText.Contains("BOT") || upperText.Contains("ADC"))
                return "BOTTOM";
            if (upperText.Contains("SUPPORT") || upperText.Contains("SUP") || upperText.Contains("SUPP"))
                return "SUPPORT";

            return "INCONNU";
        }

        private Rectangle GetLineBounds(List<(string text, Rectangle bounds, float confidence)> line)
        {
            if (line.Count == 0)
                return Rectangle.Empty;

            var minX = line.Min(w => w.bounds.X);
            var minY = line.Min(w => w.bounds.Y);
            var maxX = line.Max(w => w.bounds.Right);
            var maxY = line.Max(w => w.bounds.Bottom);

            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        private byte[] BitmapToByteArray(Bitmap bitmap)
        {
            using var ms = new System.IO.MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        public void Dispose()
        {
            _tesseractEngine?.Dispose();
        }
    }
}
