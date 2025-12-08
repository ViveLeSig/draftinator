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
                OverlayApp.OverlayForm.LogStatic($"Erreur initialisation Tesseract: {ex.Message}");
            }
        }

        public List<(Rectangle playerRegion, Rectangle roleRegion, string playerName, string role)> AutoDetectPlayers(
            int searchX, int searchY, int searchWidth, int searchHeight)
        {
            OverlayApp.OverlayForm.LogStatic($"🔬 DÉBUT AutoDetectPlayers: X={searchX}, Y={searchY}, W={searchWidth}, H={searchHeight}");
            OverlayApp.OverlayForm.LogStatic($"📂 Working Directory: {Directory.GetCurrentDirectory()}");
            
            if (_tesseractEngine == null)
            {
                OverlayApp.OverlayForm.LogStatic("❌ Tesseract non initialisé");
                return new List<(Rectangle, Rectangle, string, string)>();
            }
            
            OverlayApp.OverlayForm.LogStatic("✅ Tesseract initialisé");

            var results = new List<(Rectangle playerRegion, Rectangle roleRegion, string playerName, string role)>();

            try
            {
                OverlayApp.OverlayForm.LogStatic("📸 Capture de l'écran...");
                // Capturer la région de recherche
                var searchBitmap = _screenCapture.CaptureScreenRegion(searchX, searchY, searchWidth, searchHeight);
                if (searchBitmap == null)
                {
                    OverlayApp.OverlayForm.LogStatic("❌ Erreur capture bitmap");
                    return results;
                }
                
                // Sauvegarder pour debug
                searchBitmap.Save("debug_capture_original.png");
                OverlayApp.OverlayForm.LogStatic("📸 Capture originale sauvegardée dans debug_capture_original.png");
                
                // PRÉTRAITEMENT POUR AMÉLIORER LA DÉTECTION
                var processedBitmap = PreprocessImageForOCR(searchBitmap);
                processedBitmap.Save("debug_capture_processed.png");
                OverlayApp.OverlayForm.LogStatic("🎨 Image traitée sauvegardée dans debug_capture_processed.png");
                
                // Convertir en format Tesseract
                var pixData = BitmapToByteArray(processedBitmap);
                using var pix = Pix.LoadFromMemory(pixData);
                using var page = _tesseractEngine.Process(pix);
                
                processedBitmap.Dispose();

                // Extraire tout le texte détecté
                var fullText = page.GetText();
                OverlayApp.OverlayForm.LogStatic($"=== Texte OCR détecté ===");
                OverlayApp.OverlayForm.LogStatic(fullText);
                OverlayApp.OverlayForm.LogStatic($"========================");

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
                                OverlayApp.OverlayForm.LogStatic($"Mot: '{word.Trim()}' à Y={adjustedBounds.Y} (conf: {confidence:F1}%)");
                            }
                        }
                    } while (iter.Next(PageIteratorLevel.Word));
                }

                OverlayApp.OverlayForm.LogStatic($"\n=== {wordData.Count} mots détectés ===");
                
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
                        OverlayApp.OverlayForm.LogStatic($"Rôle trouvé: {role} à Y={bounds.Y}");
                    }
                }

                // Trier les rôles par position Y
                rolePositions = rolePositions.OrderBy(r => r.y).ToList();
                OverlayApp.OverlayForm.LogStatic($"\n{rolePositions.Count} rôles détectés");

                // Pour chaque rôle, chercher le pseudo juste en dessous (dans les 60px)
                var detectedPlayerYPositions = new List<int>();
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
                        // Prendre le mot le plus long et avec la meilleure confiance (généralement le pseudo)
                        // car les noms de champions sont souvent plus courts ou mal détectés
                        var playerWord = candidateWords
                            .Where(w => w.confidence > 40)
                            .OrderByDescending(w => w.text.Length)
                            .ThenByDescending(w => w.confidence)
                            .FirstOrDefault();
                        
                        if (playerWord.text == null)
                            playerWord = candidateWords.First();
                        
                        var playerName = playerWord.text;
                        var playerBounds = playerWord.bounds;
                        
                        // Élargir les bounds pour capturer tout le pseudo
                        playerBounds.Width = Math.Max(150, playerBounds.Width);
                        playerBounds.Height = Math.Max(30, playerBounds.Height);
                        
                        var roleRegion = roleBounds;
                        roleRegion.Width = Math.Max(100, roleRegion.Width);
                        roleRegion.Height = Math.Max(25, roleRegion.Height);

                        results.Add((playerBounds, roleRegion, playerName, role));
                        detectedPlayerYPositions.Add(playerBounds.Y);
                        OverlayApp.OverlayForm.LogStatic($"✓ Joueur détecté: {playerName} ({role}) à Y={playerBounds.Y}");
                    }
                }

                // Si on a moins de 5 joueurs, chercher les pseudos sans rôle détecté
                if (results.Count < 5)
                {
                    OverlayApp.OverlayForm.LogStatic($"\n🔍 Recherche des joueurs sans rôle détecté ({results.Count}/5)...");
                    
                    var allRoles = new HashSet<string> { "TOP", "JUNGLE", "MID", "BOTTOM", "SUPPORT" };
                    var foundRoles = new HashSet<string>(results.Select(r => r.role));
                    var missingRoles = allRoles.Except(foundRoles).ToList();
                    
                    OverlayApp.OverlayForm.LogStatic($"Rôles manquants: {string.Join(", ", missingRoles)}");
                    
                    // Chercher les noms de joueurs qui n'ont pas encore été détectés
                    var undetectedPlayers = wordData
                        .Where(w => w.text.Length >= 5) // Pseudo probable
                        .Where(w => !detectedPlayerYPositions.Any(y => Math.Abs(w.bounds.Y - y) < 50)) // Pas déjà détecté
                        .Where(w => !_roleKeywords.Any(r => w.text.ToUpper().Contains(r))) // Pas un rôle
                        .Where(w => !w.text.ToUpper().Contains("COURS")) // Pas "En cours"
                        .OrderBy(w => w.bounds.Y)
                        .ToList();
                    
                    foreach (var player in undetectedPlayers)
                    {
                        if (results.Count >= 5 || missingRoles.Count == 0) break;
                        
                        var playerBounds = player.bounds;
                        playerBounds.Width = Math.Max(150, playerBounds.Width);
                        playerBounds.Height = Math.Max(30, playerBounds.Height);
                        
                        // Chercher le rôle au-dessus (dans les 60px)
                        var roleRegion = new Rectangle(playerBounds.X, Math.Max(0, playerBounds.Y - 50), 100, 25);
                        
                        // Assigner le premier rôle manquant (généralement TOP)
                        var assignedRole = missingRoles.First();
                        missingRoles.RemoveAt(0);
                        
                        results.Add((playerBounds, roleRegion, player.text, assignedRole));
                        OverlayApp.OverlayForm.LogStatic($"✓ Joueur détecté (sans rôle OCR): {player.text} ({assignedRole}) à Y={playerBounds.Y}");
                    }
                }

                searchBitmap.Dispose();
            }
            catch (Exception ex)
            {
                OverlayApp.OverlayForm.LogStatic($"Erreur auto-détection: {ex.Message}");
                OverlayApp.OverlayForm.LogStatic($"Stack: {ex.StackTrace}");
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
            
            if (upperText.Contains("TOP") || upperText.Contains("HAUT"))
                return "TOP";
            if (upperText.Contains("JUNGLE") || upperText.Contains("JGL") || upperText.Contains("JUNGL"))
                return "JUNGLE";
            if (upperText.Contains("MID") || upperText.Contains("MIDDLE") || upperText.Contains("MILIEU"))
                return "MID";
            if (upperText.Contains("BOTTOM") || upperText.Contains("BOT") || upperText.Contains("ADC") || upperText.Contains("BAS"))
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

        private Bitmap PreprocessImageForOCR(Bitmap original)
        {
            // Créer une nouvelle image avec taille augmentée (x3 pour meilleure résolution OCR)
            int newWidth = original.Width * 3;
            int newHeight = original.Height * 3;
            var enlarged = new Bitmap(newWidth, newHeight);

            using (var g = Graphics.FromImage(enlarged))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, newWidth, newHeight);
            }

            OverlayApp.OverlayForm.LogStatic($"🔍 Image agrandie: {original.Width}x{original.Height} → {newWidth}x{newHeight}");

            // Convertir en niveaux de gris et augmenter le contraste
            var processed = new Bitmap(newWidth, newHeight);
            
            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    var pixel = enlarged.GetPixel(x, y);
                    
                    // Convertir en niveau de gris
                    int gray = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
                    
                    // Seuillage adaptatif : texte blanc sur fond sombre
                    // Si le pixel est assez clair, le rendre blanc, sinon noir
                    int threshold = 100; // Ajuster selon luminosité du texte
                    int newValue = gray > threshold ? 255 : 0;
                    
                    var newColor = Color.FromArgb(newValue, newValue, newValue);
                    processed.SetPixel(x, y, newColor);
                }
            }

            enlarged.Dispose();
            OverlayApp.OverlayForm.LogStatic("✅ Prétraitement terminé: contraste élevé, noir et blanc");
            
            return processed;
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

