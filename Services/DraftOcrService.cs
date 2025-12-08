using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tesseract;

namespace OverlayApp.Services
{
    public class DraftOcrService
    {
        private readonly ScreenCaptureService _screenCapture;
        private TesseractEngine? _ocrEngine;
        private List<OcrRegion> _playerRegions;
        private readonly string _regionsFile = "ocr_regions.json";

        public DraftOcrService(ScreenCaptureService screenCapture)
        {
            _screenCapture = screenCapture;
            _playerRegions = LoadRegions();
            InitializeOcr();
        }

        private void InitializeOcr()
        {
            try
            {
                // Initialiser Tesseract avec les données d'entraînement
                _ocrEngine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                Console.WriteLine("OCR initialisé avec succès");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur initialisation OCR: {ex.Message}");
            }
        }

        public List<PlayerDraftInfo> DetectPlayersFromDraft()
        {
            var players = new List<PlayerDraftInfo>();

            if (_ocrEngine == null)
            {
                Console.WriteLine("❌ OCR non initialisé");
                return players;
            }

            Console.WriteLine($"🔍 Détection OCR: {_playerRegions.Count} régions configurées");

            // Détecter les 5 joueurs
            for (int i = 0; i < _playerRegions.Count && i < 5; i++)
            {
                var region = _playerRegions[i];
                Console.WriteLine($"\n📍 Joueur {i + 1} - Zone: X={region.PlayerX}, Y={region.PlayerY}, W={region.PlayerWidth}, H={region.PlayerHeight}");
                
                try
                {
                    // Détecter le pseudo du joueur
                    var screenshot = _screenCapture.CaptureScreenRegion(
                        region.PlayerX, region.PlayerY, region.PlayerWidth, region.PlayerHeight);

                    string playerName = string.Empty;
                    if (screenshot != null)
                    {
                        var processedImage = PreprocessImage(screenshot);
                        using (var pix = Pix.LoadFromMemory(BitmapToByteArray(processedImage)))
                        using (var page = _ocrEngine.Process(pix))
                        {
                            var text = page.GetText().Trim();
                            playerName = ExtractPlayerName(text);
                            Console.WriteLine($"   OCR brut: '{text}'");
                            Console.WriteLine($"   Extrait: '{playerName}' (Confiance: {page.GetMeanConfidence():F2})");
                        }
                        processedImage.Dispose();
                        screenshot.Dispose();
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ Capture échouée");
                    }

                    // Détecter le rôle au-dessus du pseudo
                    string role = "UNKNOWN";
                    if (region.RoleX > 0 && region.RoleY > 0)
                    {
                        Console.WriteLine($"   Rôle - Zone: X={region.RoleX}, Y={region.RoleY}, W={region.RoleWidth}, H={region.RoleHeight}");
                        var roleScreenshot = _screenCapture.CaptureScreenRegion(
                            region.RoleX, region.RoleY, region.RoleWidth, region.RoleHeight);

                        if (roleScreenshot != null)
                        {
                            var processedRoleImage = PreprocessImage(roleScreenshot);
                            using (var pix = Pix.LoadFromMemory(BitmapToByteArray(processedRoleImage)))
                            using (var page = _ocrEngine.Process(pix))
                            {
                                var roleText = page.GetText().Trim().ToUpper();
                                role = ExtractRole(roleText);
                                Console.WriteLine($"   Rôle OCR: '{roleText}' → '{role}'");
                            }
                            processedRoleImage.Dispose();
                            roleScreenshot.Dispose();
                        }
                    }

                    if (!string.IsNullOrEmpty(playerName))
                    {
                        Console.WriteLine($"   ✅ Joueur ajouté: {playerName} ({role})");
                        players.Add(new PlayerDraftInfo
                        {
                            PlayerName = playerName,
                            Role = role,
                            Confidence = 1.0f,
                            PlayerIndex = i + 1
                        });
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ Pseudo vide, joueur ignoré");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Erreur OCR: {ex.Message}");
                }
            }

            // Déduire les rôles manquants pour les joueurs avec UNKNOWN
            DeduceMissingRoles(players);

            Console.WriteLine($"\n📊 Total détecté: {players.Count} joueur(s)");
            return players;
        }

        private Bitmap PreprocessImage(Bitmap original)
        {
            // Convertir en niveaux de gris et augmenter le contraste pour améliorer l'OCR
            var processed = new Bitmap(original.Width, original.Height);
            
            using (var g = Graphics.FromImage(processed))
            {
                // Matrice de contraste augmenté
                var attributes = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] {2.0f, 0, 0, 0, 0},
                    new float[] {0, 2.0f, 0, 0, 0},
                    new float[] {0, 0, 2.0f, 0, 0},
                    new float[] {0, 0, 0, 1.0f, 0},
                    new float[] {-0.5f, -0.5f, -0.5f, 0, 1.0f}
                });

                var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
                imageAttributes.SetColorMatrix(attributes);

                g.DrawImage(original,
                    new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height,
                    GraphicsUnit.Pixel, imageAttributes);
            }

            return processed;
        }

        private string ExtractPlayerName(string ocrText)
        {
            if (string.IsNullOrWhiteSpace(ocrText))
                return string.Empty;

            // Nettoyer le texte OCR (enlever les caractères parasites)
            ocrText = ocrText.Trim();
            
            // Pattern pour détecter "Pseudo#TAG" ou "Pseudo"
            // Le # peut être mal lu comme d'autres caractères
            var patterns = new[]
            {
                @"([A-Za-z0-9\s]+)[#@]([A-Za-z0-9]+)",  // Pseudo#TAG
                @"([A-Za-z0-9\s]+)"  // Juste Pseudo si pas de TAG détecté
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(ocrText, pattern);
                if (match.Success)
                {
                    if (match.Groups.Count >= 3 && !string.IsNullOrEmpty(match.Groups[2].Value))
                    {
                        // Format: Pseudo#TAG
                        return $"{match.Groups[1].Value.Trim()}#{match.Groups[2].Value.Trim()}";
                    }
                    else if (match.Groups.Count >= 2)
                    {
                        // Juste le pseudo
                        return match.Groups[1].Value.Trim();
                    }
                }
            }

            // Si aucun pattern ne matche, retourner le texte nettoyé
            return Regex.Replace(ocrText, @"[^A-Za-z0-9#@\s]", "").Trim();
        }

        private string ExtractRole(string ocrText)
        {
            if (string.IsNullOrWhiteSpace(ocrText))
                return "UNKNOWN";

            // Nettoyer et normaliser
            ocrText = ocrText.ToUpper().Trim();

            // Détecter les rôles connus
            if (ocrText.Contains("TOP")) return "TOP";
            if (ocrText.Contains("JUNGLE") || ocrText.Contains("JG") || ocrText.Contains("JUNG")) return "JUNGLE";
            if (ocrText.Contains("MID") || ocrText.Contains("MIDDLE")) return "MID";
            if (ocrText.Contains("BOT") || ocrText.Contains("BOTTOM") || ocrText.Contains("ADC")) return "BOTTOM";
            if (ocrText.Contains("SUP") || ocrText.Contains("SUPPORT")) return "SUPPORT";

            return "UNKNOWN";
        }

        /// <summary>
        /// Déduit les rôles manquants pour les joueurs avec UNKNOWN
        /// en trouvant les rôles qui n'ont pas encore été assignés
        /// </summary>
        private void DeduceMissingRoles(List<PlayerDraftInfo> players)
        {
            var allRoles = new HashSet<string> { "TOP", "JUNGLE", "MID", "BOTTOM", "SUPPORT" };
            var assignedRoles = new HashSet<string>(
                players.Where(p => p.Role != "UNKNOWN").Select(p => p.Role)
            );
            var missingRoles = allRoles.Except(assignedRoles).ToList();
            
            if (missingRoles.Count > 0)
            {
                Console.WriteLine($"\n🔍 Rôles manquants détectés: {string.Join(", ", missingRoles)}");
                
                var unknownPlayers = players.Where(p => p.Role == "UNKNOWN").ToList();
                for (int i = 0; i < unknownPlayers.Count && i < missingRoles.Count; i++)
                {
                    unknownPlayers[i].Role = missingRoles[i];
                    Console.WriteLine($"   ✓ Rôle assigné: {unknownPlayers[i].PlayerName} → {missingRoles[i]}");
                }
            }
        }

        private byte[] BitmapToByteArray(Bitmap bitmap)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public void UpdateRegions(List<(string playerLabel, Rectangle playerRegion, Rectangle roleRegion)> calibratedRegions)
        {
            _playerRegions = calibratedRegions
                .Select((r, index) => new OcrRegion 
                { 
                    PlayerLabel = r.playerLabel,
                    PlayerX = r.playerRegion.X, 
                    PlayerY = r.playerRegion.Y, 
                    PlayerWidth = r.playerRegion.Width, 
                    PlayerHeight = r.playerRegion.Height,
                    RoleX = r.roleRegion.X,
                    RoleY = r.roleRegion.Y,
                    RoleWidth = r.roleRegion.Width,
                    RoleHeight = r.roleRegion.Height
                })
                .ToList();
            
            SaveRegions();
        }

        private List<OcrRegion> LoadRegions()
        {
            try
            {
                if (File.Exists(_regionsFile))
                {
                    var json = File.ReadAllText(_regionsFile);
                    var loaded = JsonSerializer.Deserialize<List<OcrRegion>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        Console.WriteLine($"Régions OCR chargées: {loaded.Count}");
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur chargement régions OCR: {ex.Message}");
            }

            // Régions par défaut (1920x1080) - Joueur 1 à 5
            Console.WriteLine("Utilisation des régions par défaut");
            return new List<OcrRegion>
            {
                new OcrRegion { PlayerLabel = "Joueur 1", PlayerX = 50, PlayerY = 200, PlayerWidth = 300, PlayerHeight = 40, RoleX = 50, RoleY = 160, RoleWidth = 100, RoleHeight = 30 },
                new OcrRegion { PlayerLabel = "Joueur 2", PlayerX = 50, PlayerY = 280, PlayerWidth = 300, PlayerHeight = 40, RoleX = 50, RoleY = 240, RoleWidth = 100, RoleHeight = 30 },
                new OcrRegion { PlayerLabel = "Joueur 3", PlayerX = 50, PlayerY = 360, PlayerWidth = 300, PlayerHeight = 40, RoleX = 50, RoleY = 320, RoleWidth = 100, RoleHeight = 30 },
                new OcrRegion { PlayerLabel = "Joueur 4", PlayerX = 50, PlayerY = 440, PlayerWidth = 300, PlayerHeight = 40, RoleX = 50, RoleY = 400, RoleWidth = 100, RoleHeight = 30 },
                new OcrRegion { PlayerLabel = "Joueur 5", PlayerX = 50, PlayerY = 520, PlayerWidth = 300, PlayerHeight = 40, RoleX = 50, RoleY = 480, RoleWidth = 100, RoleHeight = 30 }
            };
        }

        private void SaveRegions()
        {
            try
            {
                var json = JsonSerializer.Serialize(_playerRegions, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_regionsFile, json);
                Console.WriteLine($"Régions OCR sauvegardées: {_playerRegions.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur sauvegarde régions OCR: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _ocrEngine?.Dispose();
        }
    }

    public class OcrRegion
    {
        public string PlayerLabel { get; set; } = string.Empty;
        public int PlayerX { get; set; }
        public int PlayerY { get; set; }
        public int PlayerWidth { get; set; }
        public int PlayerHeight { get; set; }
        public int RoleX { get; set; }
        public int RoleY { get; set; }
        public int RoleWidth { get; set; }
        public int RoleHeight { get; set; }
        
        // Compatibilité (deprecated)
        public string Role { get; set; } = string.Empty;
        public int X { get => PlayerX; set => PlayerX = value; }
        public int Y { get => PlayerY; set => PlayerY = value; }
        public int Width { get => PlayerWidth; set => PlayerWidth = value; }
        public int Height { get => PlayerHeight; set => PlayerHeight = value; }
    }

    public class PlayerDraftInfo
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public int PlayerIndex { get; set; } // 1 à 5
    }
}
