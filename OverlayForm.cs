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
    public class TestPlayer
    {
        public string playerName { get; set; } = "";
        public string tagLine { get; set; } = "";
        public string role { get; set; } = "";
    }
    
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
        private Button? ocrToggleButton;
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
                // Trouver la fenêtre League of Legends
                var lolWindow = _screenCaptureService.GetLeagueClientWindow();
                
                if (!lolWindow.HasValue)
                {
                    // Pas de fenêtre LoL trouvée, ne rien faire
                    return;
                }
                
                int screenWidth = lolWindow.Value.Width;
                int screenHeight = lolWindow.Value.Height;
                int windowX = lolWindow.Value.X;
                int windowY = lolWindow.Value.Y;
                
                // Stratégie multi-critères pour éviter les faux positifs:
                // 1. Panel gauche sombre avec texte clair (pseudos des joueurs)
                // 2. Grille de champions colorée au centre
                // 3. Texte "CHOISISSEZ VOTRE CHAMPION" visible en haut
                
                int leftPanelWidth = (int)(screenWidth * 0.25); // 25% de la largeur de la fenêtre LoL
                int leftPanelHeight = (int)(screenHeight * 0.8); // 80% de la hauteur (plus grande zone)
                int leftPanelY = windowY; // Commence TOUT EN HAUT de la fenêtre
                int leftPanelX = windowX; // Bord gauche de la fenêtre LoL
                
                Log($"📏 Résolution LoL: {screenWidth}x{screenHeight}");
                Log($"📍 Panel: X={leftPanelX}, Y={leftPanelY}, W={leftPanelWidth}, H={leftPanelHeight}");
                
                var leftBitmap = _screenCaptureService.CaptureScreenRegion(leftPanelX, leftPanelY, leftPanelWidth, leftPanelHeight);
                
                // SAUVEGARDER POUR DEBUG
                if (leftBitmap != null)
                {
                    leftBitmap.Save("detection_panel.png");
                    Log("💾 Image panel sauvegardée: detection_panel.png");
                }
                
                bool hasLeftDarkPanel = false;
                bool hasDraftTitle = false;
                bool hasChampionGrid = false;
                
                if (leftBitmap != null)
                {
                    hasLeftDarkPanel = AnalyzeDarkPanel(leftBitmap);
                    leftBitmap.Dispose();
                }

                // Vérifier le titre en haut (zone spécifique pour "CHOISISSEZ VOTRE CHAMPION")
                int titleY = windowY + (int)(screenHeight * 0.05); // Plus haut: 5% au lieu de 12%
                int titleWidth = (int)(screenWidth * 0.5); // Plus large: 50% au lieu de 40%
                int titleX = windowX + (screenWidth - titleWidth) / 2;
                
                var titleBitmap = _screenCaptureService.CaptureScreenRegion(titleX, titleY, titleWidth, 60);
                
                // SAUVEGARDER POUR DEBUG
                if (titleBitmap != null)
                {
                    titleBitmap.Save("detection_titre.png");
                }
                
                if (titleBitmap != null)
                {
                    hasDraftTitle = AnalyzeDraftTitle(titleBitmap);
                    titleBitmap.Dispose();
                }
                
                // Vérifier la grille de champions
                int centerX = windowX + screenWidth / 2;
                int centerY = windowY + screenHeight / 2;
                int gridWidth = (int)(screenWidth * 0.35);
                int gridHeight = (int)(screenHeight * 0.45);
                
                var centerBitmap = _screenCaptureService.CaptureScreenRegion(
                    centerX - gridWidth/2, centerY - gridHeight/2, gridWidth, gridHeight);

                // SAUVEGARDER POUR DEBUG
                if (centerBitmap != null)
                {
                    centerBitmap.Save("detection_grille.png");
                }

                if (centerBitmap != null)
                {
                    hasChampionGrid = AnalyzeChampionGrid(centerBitmap);
                    centerBitmap.Dispose();
                }

                // Afficher tous les résultats
                Log($"  ✓ Panel gauche: {hasLeftDarkPanel}");
                Log($"  ✓ Titre draft: {hasDraftTitle}");
                Log($"  ✓ Grille champions: {hasChampionGrid}");
                
                // EXIGER LES 3 CRITÈRES pour éviter tout faux positif
                int criteriaCount = (hasLeftDarkPanel ? 1 : 0) + (hasDraftTitle ? 1 : 0) + (hasChampionGrid ? 1 : 0);
                Log($"📊 Critères validés: {criteriaCount}/3");
                
                if (criteriaCount >= 3)
                {
                    OnDraftDetected();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur détection draft: {ex.Message}");
            }
        }

        private bool AnalyzeDarkPanel(Bitmap bitmap)
        {
            // Analyser si l'image contient une zone très sombre avec du texte clair (panneau des joueurs)
            int darkPixelCount = 0;
            int brightPixelCount = 0;
            int totalPixels = 0;
            int sampleStep = 5;

            for (int y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    totalPixels++;

                    // Pixel très sombre (fond du panel)
                    if ((pixel.R < 50 && pixel.G < 50 && pixel.B < 60) || 
                        (pixel.R < 40 && pixel.G < 40 && pixel.B < 40))
                    {
                        darkPixelCount++;
                    }
                    // Pixel clair/blanc (texte des pseudos) - CRITÈRE ASSOUPLI
                    else if (pixel.R > 120 && pixel.G > 120 && pixel.B > 120)  // Était 180, puis 150, maintenant 120
                    {
                        brightPixelCount++;
                    }
                }
            }

            float darkRatio = (float)darkPixelCount / totalPixels;
            float brightRatio = (float)brightPixelCount / totalPixels;
            
            Log($"    Panel: sombre={darkRatio:F2}, clair={brightRatio:F2}");
            
            // Panel draft = majoritairement sombre AVEC du texte clair visible
            // Valeurs réelles observées: sombre=0.89, clair=0.02
            return darkRatio > 0.55f && brightRatio > 0.015f && brightRatio < 0.25f;  // 1.5% minimum
        }

        private bool AnalyzeDraftTitle(Bitmap bitmap)
        {
            // Détecter la présence du titre "CHOISISSEZ VOTRE CHAMPION" 
            // qui a des pixels clairs/dorés sur fond sombre
            int brightPixelCount = 0;
            int totalPixels = 0;
            int goldenPixelCount = 0;
            int sampleStep = 3;

            for (int y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    totalPixels++;

                    // Pixels clairs (texte blanc/doré)
                    if (pixel.R > 160 && pixel.G > 160 && pixel.B > 140)
                    {
                        brightPixelCount++;
                        
                        // Teinte dorée spécifique du titre LoL
                        if (pixel.R > 180 && pixel.G > 165 && pixel.B > 120 && pixel.R > pixel.B)
                        {
                            goldenPixelCount++;
                        }
                    }
                }
            }

            float brightRatio = (float)brightPixelCount / totalPixels;
            float goldenRatio = (float)goldenPixelCount / totalPixels;
            
            Log($"    Titre: clair={brightRatio:F2}, doré={goldenRatio:F2}");
            
            // Valeurs réelles observées: clair=0.04, doré=0.04
            // Le titre occupe environ 3-35% de la zone avec une teinte claire/dorée
            return brightRatio > 0.03f && brightRatio < 0.35f;
        }

        private bool AnalyzeChampionGrid(Bitmap bitmap)
        {
            // Analyser si l'image contient une grille colorée variée (icônes de champions)
            // Les icônes LoL ont des couleurs très variées et saturées
            
            int colorfulPixelCount = 0;
            int veryColorfulPixelCount = 0;
            int totalPixels = 0;
            int sampleStep = 8;

            for (int y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    totalPixels++;

                    int maxChannel = Math.Max(Math.Max(pixel.R, pixel.G), pixel.B);
                    int minChannel = Math.Min(Math.Min(pixel.R, pixel.G), pixel.B);
                    int channelDiff = maxChannel - minChannel;

                    // Pixel coloré
                    if (channelDiff > 30 && maxChannel > 50 && maxChannel < 255)
                    {
                        colorfulPixelCount++;
                        
                        // Pixel très saturé (caractéristique des icônes LoL)
                        if (channelDiff > 60 && maxChannel > 100)
                        {
                            veryColorfulPixelCount++;
                        }
                    }
                }
            }
            
            float colorfulRatio = (float)colorfulPixelCount / totalPixels;
            float veryColorfulRatio = (float)veryColorfulPixelCount / totalPixels;
            
            Log($"    Grille: coloré={colorfulRatio:F2}, saturé={veryColorfulRatio:F2}");
            
            // Grille de champions = beaucoup de pixels colorés dont beaucoup très saturés
            return colorfulRatio > 0.20f && veryColorfulRatio > 0.07f;
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

                // Lancer l'auto-calibration à chaque détection de draft
                // (les positions peuvent changer si fenêtre redimensionnée)
                if (!_autoCalibrationDone)
                {
                    _autoCalibrationDone = true;
                    
                    LogStatic("🎯 Draft détecté - Lancement auto-calibration...");
                    
                    // Trouver la fenêtre League of Legends
                    var lolWindow = _screenCaptureService?.GetLeagueClientWindow();
                    
                    if (lolWindow.HasValue)
                    {
                        LogStatic($"🎮 Fenêtre LoL trouvée: X={lolWindow.Value.X}, Y={lolWindow.Value.Y}, W={lolWindow.Value.Width}, H={lolWindow.Value.Height}");
                        
                        // Zone gauche de la fenêtre LoL (15% de la largeur de la fenêtre)
                        var autoZone = new Rectangle(
                            lolWindow.Value.X,  // X: début de la fenêtre LoL
                            lolWindow.Value.Y,  // Y: haut de la fenêtre LoL
                            (int)(lolWindow.Value.Width * 0.25),  // W: 15% de la largeur de la fenêtre
                            lolWindow.Value.Height  // H: toute la hauteur de la fenêtre
                        );
                        
                        LogStatic($"📍 Zone auto (partie gauche LoL): X={autoZone.X}, Y={autoZone.Y}, W={autoZone.Width}, H={autoZone.Height}");
                        
                        // Lancer l'auto-détection automatique (sans demander de zone)
                        TryAutoDetect(showIntroMessage: false, automaticZone: autoZone);
                    }
                    else
                    {
                        LogStatic("❌ Fenêtre League of Legends non trouvée");
                    }
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
            ocrToggleButton = new Button
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

        private bool TryAutoDetect(bool showIntroMessage = true, Rectangle? automaticZone = null)
        {
            Rectangle searchRegion;

            if (automaticZone.HasValue)
            {
                // Mode automatique : utiliser la zone fournie
                searchRegion = automaticZone.Value;
                Console.WriteLine($"🤖 Auto-détection automatique - Zone: X={searchRegion.X}, Y={searchRegion.Y}, W={searchRegion.Width}, H={searchRegion.Height}");
            }
            else
            {
                // Mode manuel : demander à l'utilisateur
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

                searchRegion = selectionForm.SelectedRegion;
                Console.WriteLine($"Zone de recherche manuelle: X={searchRegion.X}, Y={searchRegion.Y}, W={searchRegion.Width}, H={searchRegion.Height}");
            }

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
                
                // Afficher le message seulement si pas en mode automatique
                if (!automaticZone.HasValue)
                {
                    MessageBox.Show(
                        $"Auto-détection réussie!\n\n{detectedPlayers.Count} joueurs trouvés:\n\n{detectedInfo}",
                        "Succès",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    LogStatic($"✅ Auto-détection réussie! {detectedPlayers.Count} joueurs trouvés");
                    
                    // Lancer la recherche et l'affichage en arrière-plan
                    Task.Run(async () =>
                    {
                        try
                        {
                            LogStatic("🔍 Résolution des pseudos via API Riot...");
                            
                            if (_playerResolver == null)
                            {
                                LogStatic("❌ PlayerResolver non initialisé");
                                return;
                            }
                            
                            // Préparer la liste pour l'API
                            var playersToResolve = detectedPlayers
                                .Take(5)
                                .Where(p => !string.IsNullOrWhiteSpace(p.playerName))
                                .Select(p => (gameName: p.playerName, role: p.role))
                                .ToList();
                            
                            LogStatic($"📝 Recherche de {playersToResolve.Count} joueurs");
                            
                            // Résoudre via l'API Riot
                            var resolvedPlayers = await _playerResolver.ResolvePlayersAsync(playersToResolve);
                            
                            LogStatic($"✅ {resolvedPlayers.Count} joueurs résolus");
                            
                            // Afficher les stats pour chaque joueur (comme en mode TEST)
                            this.Invoke((Action)(() =>
                            {
                                DisplayPlayerStats(resolvedPlayers);
                            }));
                        }
                        catch (Exception ex)
                        {
                            LogStatic($"❌ Erreur: {ex.Message}");
                        }
                    });
                }
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

            List<(string gameName, string tagLine, string role)> playersToAnalyze;

            if (_useOcr && _draftOcrService != null && _playerResolver != null)
            {
                // Vérifier si une calibration existe
                if (!File.Exists("ocr_regions.json"))
                {
                    LogStatic("⚙️ Première utilisation OCR - Calibration automatique...");
                    
                    // Trouver la fenêtre League of Legends
                    var lolWindow = _screenCaptureService?.GetLeagueClientWindow();
                    
                    if (lolWindow.HasValue)
                    {
                        LogStatic($"🎮 Fenêtre LoL trouvée: X={lolWindow.Value.X}, Y={lolWindow.Value.Y}, W={lolWindow.Value.Width}, H={lolWindow.Value.Height}");
                        
                        // Zone gauche de la fenêtre LoL (25% de la largeur de la fenêtre)
                        var autoZone = new Rectangle(
                            lolWindow.Value.X,  // X: début de la fenêtre LoL
                            lolWindow.Value.Y,  // Y: haut de la fenêtre LoL
                            (int)(lolWindow.Value.Width * 0.25),  // W: 25% de la largeur de la fenêtre
                            lolWindow.Value.Height  // H: toute la hauteur de la fenêtre
                        );
                        
                        LogStatic($"📍 Zone auto: X={autoZone.X}, Y={autoZone.Y}, W={autoZone.Width}, H={autoZone.Height}");

                        // Tenter l'auto-détection avec la zone automatique
                        if (!TryAutoDetect(showIntroMessage: false, automaticZone: autoZone))
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
                    else
                    {
                        LogStatic("❌ Fenêtre League of Legends non trouvée");
                        MessageBox.Show(
                            "Impossible de trouver la fenêtre League of Legends.\n\n" +
                            "Assurez-vous que le jeu est lancé et visible.",
                            "Fenêtre non trouvée",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
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
                    var choice = MessageBox.Show(
                        "❌ Aucun joueur détecté par l'OCR.\n\n" +
                        "💡 SOLUTION SIMPLE : Utilisez le mode Test\n\n" +
                        "Le mode Test vous permet d'analyser des joueurs spécifiques.\n" +
                        "Éditez le fichier 'test_players.json' pour mettre vos pseudos.\n\n" +
                        "Voulez-vous basculer en mode Test maintenant ?",
                        "OCR échoué - Mode Test recommandé",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    
                    if (choice == DialogResult.Yes)
                    {
                        _useOcr = false;
                        if (ocrToggleButton != null)
                        {
                            ocrToggleButton.Text = "Mode: Test";
                            ocrToggleButton.BackColor = Color.FromArgb(220, 100, 100, 100);
                        }
                        MessageBox.Show(
                            "✅ Mode Test activé !\n\n" +
                            "Éditez 'test_players.json' pour configurer vos joueurs.\n" +
                            "Format : {\"playerName\":\"Pseudo\", \"tagLine\":\"TAG\", \"role\":\"TOP\"}\n\n" +
                            "Cliquez à nouveau sur 'Analyser Draft'.",
                            "Mode Test",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    
                    refreshButton.Enabled = true;
                    refreshButton.Text = "Analyser Draft";
                    return;
                }
                
                // Résoudre les pseudos (gameName -> gameName#tagLine)
                Console.WriteLine("Résolution des pseudos...");
                playersToAnalyze = await _playerResolver.ResolvePlayersAsync(playersToResolve);
                
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
                // Mode test: utiliser les joueurs du fichier test_players.json
                Log("=== MODE TEST ===");
                playersToAnalyze = LoadTestPlayers();
                
                Log($"Joueurs de test: {playersToAnalyze.Count}");
                foreach (var p in playersToAnalyze)
                {
                    Log($"  - {p.gameName}#{p.tagLine} ({p.role})");
                }
            }

            // Utiliser la méthode commune pour afficher les stats
            DisplayPlayerStats(playersToAnalyze);
        }

        private List<(string gameName, string tagLine, string role)> LoadTestPlayers()
        {
            string testPlayersFile = "test_players.json";
            if (File.Exists(testPlayersFile))
            {
                try
                {
                    var json = File.ReadAllText(testPlayersFile);
                    var testPlayers = System.Text.Json.JsonSerializer.Deserialize<List<TestPlayer>>(json);
                    
                    if (testPlayers != null && testPlayers.Count > 0)
                    {
                        Log($"Joueurs chargés depuis {testPlayersFile}: {testPlayers.Count}");
                        return testPlayers
                            .Select(p => (p.playerName, p.tagLine, p.role))
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Erreur lecture {testPlayersFile}: {ex.Message}");
                }
            }
            
            // Joueurs par défaut
            return new List<(string, string, string)>
            {
                ("Darioush", "CRABE", "TOP"),
                ("OUGOUG", "SINJ3", "JUNGLE"),
                ("OUGOUG", "SINJ2", "MID"),
                ("OUGOUG", "SINJ4", "SUPPORT"),
                ("IdRatherPlayPkm", "Isck", "BOTTOM")
            };
        }

        private async void DisplayPlayerStats(List<(string gameName, string tagLine, string role)> players)
        {
            Log("📊 Affichage des stats des joueurs...");
            
            // Vider les anciens panels
            playerStatsContainer?.Controls.Clear();
            
            if (refreshButton != null)
            {
                refreshButton.Enabled = false;
                refreshButton.Text = "Chargement...";
            }
            
            int panelHeight = (playerStatsContainer!.Height - 20) / 5;
            
            foreach (var (gameName, tagLine, role) in players)
            {
                var fullName = $"{gameName}#{tagLine}";
                Log($"\n=== Traitement de {fullName} ({role}) ===");
                
                var panel = new PlayerStatsPanel(_championIconService);
                panel.Height = panelHeight;
                panel.ShowLoading($"{fullName} ({role})");
                playerStatsContainer.Controls.Add(panel);
                
                try
                {
                    var stats = await _riotApiService!.GetPlayerStatsAsync(gameName, tagLine, role);
                    Log($"Stats reçues: {(stats != null ? "OK" : "NULL")}");
                    
                    if (stats != null)
                    {
                        stats.Role = role;
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
                    Log($"Erreur pour {fullName}: {ex.Message}");
                }
                
                await Task.Delay(500);
            }
            
            if (refreshButton != null)
            {
                refreshButton.Enabled = true;
                refreshButton.Text = "Analyser Draft";
            }
        }

        // Méthodes Windows API pour le clic à travers (non utilisé actuellement)
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}

