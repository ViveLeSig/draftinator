using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using OverlayApp.Services;
using OverlayApp.UI;

namespace OverlayApp
{
    public class OverlayForm : Form
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private RiotApiService? _riotApiService;
        private ScreenCaptureService? _screenCaptureService;
        private FlowLayoutPanel playerStatsContainer;
        private Button refreshButton;
        private Button configButton;
        private TextBox apiKeyTextBox;
        private Panel configPanel;

        public OverlayForm()
        {
            _hookID = SetHook(_proc);
            InitializeOverlay();
            InitializeServices();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
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
            
            // NE PAS activer le clic à travers pour pouvoir interagir avec les boutons
            // Si vous voulez activer le clic à travers :
            // int initialStyle = GetWindowLong(this.Handle, -20);
            // SetWindowLong(this.Handle, -20, initialStyle | 0x80000 | 0x20);
            
            // Ajouter l'interface utilisateur
            AddOverlayUI();
        }

        private void InitializeServices()
        {
            _screenCaptureService = new ScreenCaptureService();
            
            // Charger la clé API sauvegardée si elle existe
            try
            {
                if (System.IO.File.Exists("riot_api_key.txt"))
                {
                    var apiKey = System.IO.File.ReadAllText("riot_api_key.txt");
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        _riotApiService = new RiotApiService(apiKey, "euw1", "europe");
                        apiKeyTextBox.Text = apiKey;
                        Console.WriteLine("Clé API chargée depuis le fichier");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur chargement clé API: {ex.Message}");
            }
        }

        private void AddOverlayUI()
        {
            // Panel de configuration (en haut à droite)
            configPanel = new Panel
            {
                Location = new Point(this.Width - 350, 20),
                Size = new Size(330, 150),
                BackColor = Color.FromArgb(220, 20, 30, 40),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            var configLabel = new Label
            {
                Text = "Riot API Key:",
                Location = new Point(10, 10),
                ForeColor = Color.White,
                AutoSize = true
            };

            apiKeyTextBox = new TextBox
            {
                Location = new Point(10, 35),
                Size = new Size(300, 25),
                PlaceholderText = "RGAPI-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            };

            var regionLabel = new Label
            {
                Text = "Région: EUW (modifiable dans le code)",
                Location = new Point(10, 70),
                ForeColor = Color.LightGray,
                AutoSize = true,
                Font = new Font("Segoe UI", 8)
            };

            var saveButton = new Button
            {
                Text = "Sauvegarder",
                Location = new Point(10, 100),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveButton.Click += SaveApiKey_Click;

            configPanel.Controls.AddRange(new Control[] {
                configLabel,
                apiKeyTextBox,
                regionLabel,
                saveButton
            });

            // Bouton de configuration (en haut à droite)
            configButton = new Button
            {
                Text = "Config",
                Location = new Point(this.Width - 100, 20),
                Size = new Size(80, 35),
                BackColor = Color.FromArgb(220, 60, 70, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10)
            };
            configButton.Click += (s, e) => configPanel.Visible = !configPanel.Visible;

            // Bouton de rafraîchissement
            refreshButton = new Button
            {
                Text = "Analyser Draft",
                Location = new Point(this.Width - 230, 20),
                Size = new Size(120, 35),
                BackColor = Color.FromArgb(220, 0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            refreshButton.Click += RefreshButton_Click;

            // Container pour les stats des joueurs
            playerStatsContainer = new FlowLayoutPanel
            {
                Location = new Point(20, 70),
                Size = new Size(this.Width - 40, this.Height - 100),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.Transparent
            };

            // Label d'instructions
            var instructionsLabel = new Label
            {
                Text = "1. Configurez votre Riot API Key\n2. Lancez une partie (draft pick)\n3. Cliquez sur 'Analyser Draft'\n\nESC pour fermer l'overlay",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(180, 0, 0, 0),
                AutoSize = false,
                Size = new Size(400, 120),
                Location = new Point((this.Width - 400) / 2, (this.Height - 120) / 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(20)
            };

            this.Controls.AddRange(new Control[] {
                instructionsLabel,
                refreshButton,
                configButton,
                configPanel,
                playerStatsContainer
            });
        }

        private void SaveApiKey_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(apiKeyTextBox.Text))
            {
                MessageBox.Show("Veuillez entrer une clé API valide.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _riotApiService = new RiotApiService(apiKeyTextBox.Text, "euw1", "europe");
            
            // Sauvegarder la clé API dans un fichier
            try
            {
                System.IO.File.WriteAllText("riot_api_key.txt", apiKeyTextBox.Text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur sauvegarde clé API: {ex.Message}");
            }
            
            configPanel.Visible = false;
            MessageBox.Show("Clé API sauvegardée!", "Succès", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void RefreshButton_Click(object? sender, EventArgs e)
        {
            if (_riotApiService == null)
            {
                MessageBox.Show("Veuillez d'abord configurer votre Riot API Key.", "Configuration requise", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                configPanel.Visible = true;
                return;
            }

            playerStatsContainer.Controls.Clear();
            refreshButton.Enabled = false;
            refreshButton.Text = "Analyse...";

            // Noms de test de joueurs européens
            // TODO: Implémenter la détection OCR réelle
            var testPlayers = new[] { 
                "Darioush#CRABE", 
                "OUGOUG#SINJ3", 
                "OUGOUG#SINJ2", 
                "OUGOUG#SINJ4", 
                "IdRatherPlayPkm#Isck" 
            };

            foreach (var playerName in testPlayers)
            {
                var parts = playerName.Split('#');
                if (parts.Length != 2) continue;

                var panel = new PlayerStatsPanel();
                panel.ShowLoading(playerName);
                playerStatsContainer.Controls.Add(panel);

                try
                {
                    var stats = await _riotApiService.GetPlayerStatsAsync(parts[0], parts[1]);
                    if (stats != null)
                    {
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

        // Hook clavier global pour capturer ESC
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == (int)Keys.Escape)
                {
                    Application.Exit();
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // Méthodes pour permettre le clic à travers (Windows API)
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
