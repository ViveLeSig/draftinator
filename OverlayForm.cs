using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using OverlayApp.Services;
using OverlayApp.UI;

namespace OverlayApp
{
    public class OverlayForm : Form
    {
        private static StreamWriter? _logWriter;
        
        public static void LogStatic(string message)
        {
            try
            {
                if (_logWriter == null)
                {
                    _logWriter = new StreamWriter("debug.log", append: true);
                    _logWriter.AutoFlush = true;
                }
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _logWriter.WriteLine($"[{timestamp}] {message}");
                Console.WriteLine(message);
            }
            catch { }
        }
        
        private void Log(string message)
        {
            LogStatic(message);
        }

        private RiotApiService? _riotApiService;
        private ScreenCaptureService? _screenCaptureService;
        private DraftOcrService? _draftOcrService;
        private PlayerResolver? _playerResolver;
        private ChampionIconService _championIconService;
        private FlowLayoutPanel playerStatsContainer;
        private Button refreshButton;
        private bool _useOcr = true;
        private System.Windows.Forms.Timer? _topMostTimer;
        private System.Windows.Forms.Timer? _draftDetectionTimer;
        private Label? _statusLabel;
        private bool _draftDetected = false;
        private bool _autoCalibrationDone = false;

        public OverlayForm()
        {
            _championIconService = new ChampionIconService();
            _useOcr = true; // Mode OCR par défaut
            InitializeOverlay();
            InitializeServices();
            StartDraftDetection();
            
            // Utiliser KeyPreview au lieu du hook global pour éviter de bloquer les autres applications
            this.KeyPreview = true;
            this.KeyDown += OverlayForm_KeyDown;
        }

        private void OverlayForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Application.Exit();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _topMostTimer?.Stop();
            _topMostTimer?.Dispose();
            _draftDetectionTimer?.Stop();
            _draftDetectionTimer?.Dispose();
            _draftOcrService?.Dispose();
            base.OnFormClosed(e);
        }

        private void InitializeOverlay()
        {
            // Configuration de la fenêtre overlay
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            
            // Définir la taille et la position (plein écran)
            var screen = Screen.PrimaryScreen.Bounds;
            this.Bounds = screen;
            
            // Rendre la fenêtre transparente
            this.BackColor = Color.LimeGreen;
            this.TransparencyKey = Color.LimeGreen;
            this.Opacity = 1.0;
            
            // Timer pour maintenir la fenêtre au premier plan
            _topMostTimer = new System.Windows.Forms.Timer();
            _topMostTimer.Interval = 500; // Toutes les 500ms
            _topMostTimer.Tick += (s, e) => 
            {
                if (!this.TopMost)
                {
                    this.TopMost = true;
                }
            };
            _topMostTimer.Start();
            
            // Ajouter l'interface utilisateur
            AddOverlayUI();
        }

        private void StartDraftDetection()
        {
            // Créer un label de statut centré
            _statusLabel = new Label
            {
                Text = "🔍 En attente du draft...",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(180, 0, 0, 0),
                AutoSize = false,
                Size = new Size(400, 60),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point((this.Width - 400) / 2, (this.Height - 60) / 2)
            };
            this.Controls.Add(_statusLabel);
            _statusLabel.BringToFront();

            // Démarrer le timer de détection (vérifie toutes les 2 secondes)
            _draftDetectionTimer = new System.Windows.Forms.Timer();
            _draftDetectionTimer.Interval = 2000;
            _draftDetectionTimer.Tick += DraftDetectionTimer_Tick;
            _draftDetectionTimer.Start();

            Console.WriteLine("⚡ Détection automatique du draft activée");
        }

        private void DraftDetectionTimer_Tick(object? sender, EventArgs e)
        {
            if (_draftDetected || _screenCaptureService == null)
                return;

            try
            {
                // Stratégie 1: Détecter la zone sombre sur le côté gauche caractéristique du draft
                // Cette zone contient les joueurs et est très sombre (presque noire)
                var leftBitmap = _screenCaptureService.CaptureScreenRegion(10, 100, 250, 500);
                
                if (leftBitmap != null)
                {
                    bool hasLeftDarkPanel = AnalyzeDarkPanel(leftBitmap);
                    leftBitmap.Dispose();

                    if (hasLeftDarkPanel)
                    {
                        // Stratégie 2: Vérifier la présence de la grille de champions au centre
                        // La grille a des icônes colorées espacées régulièrement
                        int centerX = Screen.PrimaryScreen.Bounds.Width / 2;
                        int centerY = Screen.PrimaryScreen.Bounds.Height / 2;
                        var centerBitmap = _screenCaptureService.CaptureScreenRegion(
                            centerX - 300, centerY - 200, 600, 400);

                        if (centerBitmap != null)
                        {
                            bool hasChampionGrid = AnalyzeChampionGrid(centerBitmap);
                            centerBitmap.Dispose();

                            if (hasChampionGrid)
                            {
                                OnDraftDetected();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur détection draft: {ex.Message}");
            }
        }

        private bool AnalyzeDarkPanel(Bitmap bitmap)
        {
            // Analyser si l'image contient une zone très sombre (panneau des joueurs)
            int darkPixelCount = 0;
            int totalPixels = 0;
            int sampleStep = 5; // Échantillonner tous les 5 pixels pour performance

            for (int y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    totalPixels++;

                    // Pixel très sombre (proche du noir)
                    if (pixel.R < 40 && pixel.G < 40 && pixel.B < 40)
                    {
                        darkPixelCount++;
                    }
                }
            }

            // Si plus de 60% de pixels sombres, on considère que c'est le panneau du draft
            float darkRatio = (float)darkPixelCount / totalPixels;
            return darkRatio > 0.6f;
        }

        private bool AnalyzeChampionGrid(Bitmap bitmap)
        {
            // Analyser si l'image contient une grille colorée (icônes de champions)
            // On cherche des variations de couleur régulières (icônes espacées)
            
            int colorfulPixelCount = 0;
            int totalPixels = 0;
            int sampleStep = 8;

            for (int y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    totalPixels++;

                    // Pixel coloré (pas gris, pas noir, pas blanc)
                    int maxChannel = Math.Max(Math.Max(pixel.R, pixel.G), pixel.B);
                    int minChannel = Math.Min(Math.Min(pixel.R, pixel.G), pixel.B);
                    int channelDiff = maxChannel - minChannel;

                    // Si différence > 30 et luminosité moyenne, c'est un pixel coloré
                    if (channelDiff > 30 && maxChannel > 50 && maxChannel < 250)
                    {
                        colorfulPixelCount++;
                    }
                }
            }

            // Si plus de 20% de pixels colorés, c'est probablement la grille de champions
            float colorfulRatio = (float)colorfulPixelCount / totalPixels;
            Console.WriteLine($"Ratio pixels colorés: {colorfulRatio:F2}");
            return colorfulRatio > 0.20f;
        }

        private void OnDraftDetected()
        {
            _draftDetected = true;
            _draftDetectionTimer?.Stop();
            
            Console.WriteLine("✅ Draft détecté !");
            
            if (_statusLabel != null)
            {
                _statusLabel.Text = "✅ Draft détecté !";
                _statusLabel.BackColor = Color.FromArgb(180, 0, 100, 0);
            }

            // Attendre 1 seconde puis lancer l'auto-calibration si nécessaire
            var delayTimer = new System.Windows.Forms.Timer();
            delayTimer.Interval = 1000;
            delayTimer.Tick += (s, e) =>
            {
                delayTimer.Stop();
                delayTimer.Dispose();
                
                // Masquer le label de statut
                if (_statusLabel != null)
                {
                    _statusLabel.Visible = false;
                }

                // Si pas de calibration, la lancer automatiquement
                if (!File.Exists("ocr_regions.json") && !_autoCalibrationDone)
                {
                    _autoCalibrationDone = true;
                    AutoCalibrateOnDraftDetection();
                }
            };
            delayTimer.Start();
        }

        private void AutoCalibrateOnDraftDetection()
        {
            var result = MessageBox.Show(
                "✨ Draft détecté !\n\n" +
                "Calibration automatique des zones de joueurs...\n\n" +
                "Sélectionnez la zone de gauche contenant les 5 joueurs et leurs rôles.",
                "Calibration automatique",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                TryAutoDetect(showIntroMessage: false);
            }
        }

        private byte[] BitmapToByteArray(Bitmap bitmap)
        {
            using var ms = new System.IO.MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private void InitializeServices()
        {
            _screenCaptureService = new ScreenCaptureService();
            _draftOcrService = new DraftOcrService(_screenCaptureService);
            
            // Charger la clé API depuis le fichier
            try
            {
                if (System.IO.File.Exists("riot_api_key.txt"))
                {
                    var apiKey = System.IO.File.ReadAllText("riot_api_key.txt").Trim();
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        _riotApiService = new RiotApiService(apiKey, "euw1", "europe");
                        _playerResolver = new PlayerResolver(_riotApiService);
                        Log("Clé API chargée depuis le fichier");
                        
                        // Validation asynchrone de la clé API
                        Task.Run(async () =>
                        {
                            var (isValid, message) = await _riotApiService.ValidateApiKeyAsync();
                            Log($"Validation clé API: {message}");
                            
                            if (!isValid)
                            {
                                // Afficher un message à l'utilisateur
                                this.Invoke((Action)(() =>
                                {
                                    MessageBox.Show(
                                        $"{message}\n\n" +
                                        "Veuillez mettre à jour votre clé API dans le fichier riot_api_key.txt\n\n" +
                                        "1. Aller sur https://developer.riotgames.com/\n" +
                                        "2. Se connecter\n" +
                                        "3. Régénérer une clé (valide 24h)\n" +
                                        "4. Remplacer le contenu de riot_api_key.txt",
                                        "Clé API invalide",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Warning);
                                }));
                            }
                        });
                    }
                }
                else
                {
                    Log("ATTENTION: Fichier riot_api_key.txt introuvable!");
                }
            }
            catch (Exception ex)
            {
                Log($"Erreur chargement clé API: {ex.Message}");
            }
        }

        private void AddOverlayUI()
        {
            // Bouton de fermeture
            // Bouton pour basculer entre mode test et OCR
            var ocrToggleButton = new Button
            {
                Text = "Mode: OCR",
                Location = new Point(this.Width - 670, 20),
                Size = new Size(130, 35),
                BackColor = Color.FromArgb(220, 0, 150, 100),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            ocrToggleButton.Click += (s, e) => 
            {
                _useOcr = !_useOcr;
                ocrToggleButton.Text = _useOcr ? "Mode: OCR" : "Mode: Test";
                ocrToggleButton.BackColor = _useOcr ? Color.FromArgb(220, 0, 150, 100) : Color.FromArgb(220, 100, 100, 100);
            };
            
            // Bouton d'auto-détection
            var autoDetectButton = new Button
            {
                Text = "🔍 Auto-Détecter",
                Location = new Point(this.Width - 530, 20),
                Size = new Size(130, 35),
                BackColor = Color.FromArgb(220, 0, 150, 150),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            autoDetectButton.Click += AutoDetectButton_Click;
            
            // Bouton de calibration OCR
            var calibrateButton = new Button
            {
                Text = "⚙ Calibrer",
                Location = new Point(this.Width - 390, 20),
                Size = new Size(130, 35),
                BackColor = Color.FromArgb(220, 150, 100, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            calibrateButton.Click += CalibrateButton_Click;
            
            // Bouton de rafraîchissement
            refreshButton = new Button
            {
                Text = "Analyser Draft",
                Location = new Point(this.Width - 250, 20),
                Size = new Size(140, 35),
                BackColor = Color.FromArgb(220, 0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            refreshButton.Click += RefreshButton_Click;
            
            var closeButton = new Button
            {
                Text = "✕ Fermer",
                Location = new Point(this.Width - 100, 20),
                Size = new Size(90, 35),
                BackColor = Color.FromArgb(220, 180, 0, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            closeButton.Click += (s, e) => Application.Exit();

            // Container pour les stats des joueurs - en colonne sans overflow
            playerStatsContainer = new FlowLayoutPanel
            {
                Location = new Point(20, 70),
                Size = new Size(this.Width - 40, this.Height - 100),
                AutoScroll = false,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            this.Controls.AddRange(new Control[] {
                closeButton,
                refreshButton,
                calibrateButton,
                autoDetectButton,
                ocrToggleButton,
                playerStatsContainer
            });
        }

        private bool TryAutoDetect(bool showIntroMessage = true)
        {
            if (showIntroMessage)
            {
                var result = MessageBox.Show(
                    "Auto-détection des zones de joueurs\n\n" +
                    "Cliquez sur une zone de l'écran où se trouvent les 5 joueurs.\n" +
                    "L'algorithme va automatiquement détecter les pseudos et rôles.\n\n" +
                    "Prêt ?",
                    "Auto-Détection",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);

                if (result != DialogResult.OK)
                    return false;
            }

            // Demander à l'utilisateur de sélectionner une grande zone
            var selectionForm = new CalibrationForm("Zone de recherche (tous les joueurs)");
            if (selectionForm.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            var searchRegion = selectionForm.SelectedRegion;
            Console.WriteLine($"Zone de recherche: X={searchRegion.X}, Y={searchRegion.Y}, W={searchRegion.Width}, H={searchRegion.Height}");

            // Lancer l'auto-détection
            var autoDetect = new AutoDetectionService(_screenCaptureService!);
            var detectedPlayers = autoDetect.AutoDetectPlayers(
                searchRegion.X, searchRegion.Y, searchRegion.Width, searchRegion.Height);

            if (detectedPlayers.Count == 0)
            {
                autoDetect.Dispose();
                MessageBox.Show(
                    "Aucun joueur détecté automatiquement.\n\n" +
                    "Assurez-vous d'être dans un écran de draft et de sélectionner une zone contenant les pseudos.",
                    "Aucun joueur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            // Convertir et sauvegarder
            var calibratedRegions = detectedPlayers
                .Take(5)
                .Select((p, index) => ($"Joueur {index + 1}", p.playerRegion, p.roleRegion))
                .ToList();

            if (_draftOcrService != null)
            {
                _draftOcrService.UpdateRegions(calibratedRegions);
                
                var detectedInfo = string.Join("\n", detectedPlayers.Take(5).Select((p, i) =>
                    $"Joueur {i + 1}: {p.playerName} ({p.role})"));
                
                MessageBox.Show(
                    $"Auto-détection réussie!\n\n{detectedPlayers.Count} joueurs trouvés:\n\n{detectedInfo}",
                    "Succès",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            autoDetect.Dispose();
            return true;
        }

        private void AutoDetectButton_Click(object? sender, EventArgs e)
        {
            TryAutoDetect(showIntroMessage: true);
        }

        private void CalibrateButton_Click(object? sender, EventArgs e)
        {
            MessageBox.Show("Calibration des zones OCR\n\nPour chaque joueur (1 à 5), vous allez sélectionner:\n1. La zone du PSEUDO\n2. La zone du RÔLE (au-dessus du pseudo)\n\nAssurez-vous d'être dans un écran de draft avant de commencer.", "Calibration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            
            var calibratedRegions = new List<(string playerLabel, Rectangle playerRegion, Rectangle roleRegion)>();

            for (int i = 1; i <= 5; i++)
            {
                // Sélectionner la zone du pseudo
                MessageBox.Show($"Joueur {i}\n\nSélectionnez la zone du PSEUDO", $"Joueur {i} - Pseudo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var playerForm = new CalibrationForm($"Joueur {i} - Pseudo");
                if (playerForm.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Calibration annulée", "Annulation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var playerRegion = playerForm.SelectedRegion;

                // Sélectionner la zone du rôle
                MessageBox.Show($"Joueur {i}\n\nSélectionnez la zone du RÔLE (au-dessus du pseudo)", $"Joueur {i} - Rôle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var roleForm = new CalibrationForm($"Joueur {i} - Rôle");
                if (roleForm.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("Calibration annulée", "Annulation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var roleRegion = roleForm.SelectedRegion;

                calibratedRegions.Add(($"Joueur {i}", playerRegion, roleRegion));
                Console.WriteLine($"Joueur {i} - Pseudo: X={playerRegion.X}, Y={playerRegion.Y}, W={playerRegion.Width}, H={playerRegion.Height}");
                Console.WriteLine($"Joueur {i} - Rôle: X={roleRegion.X}, Y={roleRegion.Y}, W={roleRegion.Width}, H={roleRegion.Height}");
            }

            // Sauvegarder les régions calibrées
            if (calibratedRegions.Count == 5 && _draftOcrService != null)
            {
                _draftOcrService.UpdateRegions(calibratedRegions);
                MessageBox.Show($"Calibration terminée!\n{calibratedRegions.Count} joueurs configurés.", "Succès", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }



        private async void RefreshButton_Click(object? sender, EventArgs e)
        {
            if (_riotApiService == null)
            {
                MessageBox.Show("Clé API non trouvée! Assurez-vous que le fichier riot_api_key.txt existe.", "Configuration requise", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            playerStatsContainer.Controls.Clear();
            refreshButton.Enabled = false;
            refreshButton.Text = "Analyse...";

            // Calculer la hauteur fixe pour chaque panel (toujours 1/5 de l'espace disponible)
            int availableHeight = playerStatsContainer.Height - 20; // Marge totale
            int panelHeight = Math.Max(120, (availableHeight / 5) - 10); // Toujours divisé par 5, minimum 120px

            List<(string playerName, string role)> playersToAnalyze;

            if (_useOcr && _draftOcrService != null && _playerResolver != null)
            {
                // Vérifier si une calibration existe
                if (!File.Exists("ocr_regions.json"))
                {
                    var calibrationChoice = MessageBox.Show(
                        "Première utilisation du mode OCR\n\n" +
                        "Une calibration est nécessaire pour détecter les joueurs.\n\n" +
                        "Tentative d'auto-détection automatique...\n\n" +
                        "Assurez-vous d'être dans un écran de draft, puis cliquez sur OK.",
                        "Calibration requise",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information);

                    if (calibrationChoice != DialogResult.OK)
                    {
                        refreshButton.Enabled = true;
                        refreshButton.Text = "Analyser Draft";
                        return;
                    }

                    // Tenter l'auto-détection
                    if (!TryAutoDetect(showIntroMessage: false))
                    {
                        // Si échec, proposer calibration manuelle
                        var manualChoice = MessageBox.Show(
                            "L'auto-détection a échoué.\n\n" +
                            "Voulez-vous effectuer une calibration manuelle ?\n\n" +
                            "(Vous devrez sélectionner manuellement les zones de chaque joueur)",
                            "Calibration manuelle",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (manualChoice == DialogResult.Yes)
                        {
                            CalibrateButton_Click(null, EventArgs.Empty);
                        }

                        refreshButton.Enabled = true;
                        refreshButton.Text = "Analyser Draft";
                        return;
                    }
                }

                // Mode OCR: détecter les joueurs depuis l'écran
                Console.WriteLine("Détection OCR des joueurs...");
                var detectedPlayers = _draftOcrService.DetectPlayersFromDraft();
                
                // Convertir en format (gameName, role) pour la résolution
                var playersToResolve = detectedPlayers
                    .Select(p => (p.PlayerName, p.Role))
                    .ToList();
                
                if (playersToResolve.Count == 0)
                {
                    MessageBox.Show("Aucun joueur détecté. Assurez-vous d'être dans l'écran de draft.", "Aucun joueur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    refreshButton.Enabled = true;
                    refreshButton.Text = "Analyser Draft";
                    return;
                }
                
                // Résoudre les pseudos (gameName -> gameName#tagLine)
                Console.WriteLine("Résolution des pseudos...");
                var resolvedPlayers = await _playerResolver.ResolvePlayersAsync(playersToResolve);
                
                playersToAnalyze = resolvedPlayers
                    .Select(p => ($"{p.gameName}#{p.tagLine}", p.role))
                    .ToList();
                
                if (playersToAnalyze.Count == 0)
                {
                    MessageBox.Show("Impossible de résoudre les pseudos détectés. Vérifiez la console pour plus de détails.", "Erreur de résolution", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    refreshButton.Enabled = true;
                    refreshButton.Text = "Analyser Draft";
                    return;
                }
            }
            else
            {
                // Mode test: utiliser les joueurs de test hardcodés
                Log("=== MODE TEST ===");
                playersToAnalyze = new List<(string, string)>
                {
                    ("Darioush#CRABE", "TOP"),
                    ("OUGOUG#SINJ3", "JUNGLE"),
                    ("OUGOUG#SINJ2", "MID"),
                    ("OUGOUG#SINJ4", "SUPPORT"),
                    ("IdRatherPlayPkm#Isck", "BOTTOM")
                };
                Log($"Joueurs de test: {playersToAnalyze.Count}");
                foreach (var p in playersToAnalyze)
                {
                    Log($"  - {p.Item1} ({p.Item2})");
                }
            }

            foreach (var (playerName, role) in playersToAnalyze)
            {
                Log($"\n=== Traitement de {playerName} ({role}) ===");
                var parts = playerName.Split('#');
                Log($"Parts: {string.Join(", ", parts)} (length: {parts.Length})");
                if (parts.Length < 1) continue;

                var panel = new PlayerStatsPanel(_championIconService);
                panel.Height = panelHeight; // Appliquer la hauteur calculée
                panel.ShowLoading($"{playerName} ({role})");
                playerStatsContainer.Controls.Add(panel);

                try
                {
                    PlayerStats? stats = null;
                    
                    if (parts.Length == 2)
                    {
                        // Format complet: Pseudo#TAG
                        Log($"Appel API: GetPlayerStatsAsync('{parts[0]}', '{parts[1]}')");
                        stats = await _riotApiService.GetPlayerStatsAsync(parts[0], parts[1]);
                        Log($"Stats reçues: {(stats != null ? "OK" : "NULL")}");
                    }
                    else
                    {
                        // Seulement le pseudo (OCR n'a pas pu détecter le TAG)
                        // On pourrait essayer de chercher avec un TAG par défaut ou afficher une erreur
                        panel.ShowError($"TAG manquant pour {parts[0]}");
                        continue;
                    }
                    
                    if (stats != null)
                    {
                        stats.Role = role; // Affecter le rôle détecté
                        panel.UpdatePlayerStats(stats);
                    }
                    else
                    {
                        panel.ShowError("Joueur non trouvé");
                    }
                }
                catch (Exception ex)
                {
                    panel.ShowError($"Erreur: {ex.Message}");
                    // Afficher l'erreur dans la console pour debug
                    Console.WriteLine($"Erreur pour {playerName}: {ex.Message}");
                }

                // Délai pour éviter le rate limiting
                await System.Threading.Tasks.Task.Delay(500);
            }

            refreshButton.Enabled = true;
            refreshButton.Text = "Analyser Draft";
        }

        // Méthodes Windows API pour le clic à travers (non utilisé actuellement)
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
