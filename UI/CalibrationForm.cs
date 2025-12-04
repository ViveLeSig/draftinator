using System;
using System.Drawing;
using System.Windows.Forms;

namespace OverlayApp.UI
{
    /// <summary>
    /// Fenêtre pour calibrer les zones de détection OCR
    /// </summary>
    public class CalibrationForm : Form
    {
        private Panel selectionPanel;
        private Point startPoint;
        private bool isSelecting = false;
        private Label instructionLabel;
        private Button saveButton;
        private TextBox coordinatesTextBox;
        
        public Rectangle SelectedRegion { get; private set; }
        public string RoleName { get; private set; }

        public CalibrationForm(string roleName)
        {
            RoleName = roleName;
            InitializeForm();
        }

        private void InitializeForm()
        {
            this.Text = $"Calibration - {RoleName}";
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.Opacity = 0.3;
            this.Cursor = Cursors.Cross;

            // Instructions
            instructionLabel = new Label
            {
                Text = $"Sélectionnez la zone du pseudo pour {RoleName}\nCliquez et glissez pour créer une zone\nAppuyez sur ESC pour annuler",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.Yellow,
                BackColor = Color.FromArgb(200, 0, 0, 0),
                AutoSize = false,
                Size = new Size(600, 100),
                Location = new Point((this.Width - 600) / 2, 50),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(20)
            };

            // Panel de sélection (invisible au début)
            selectionPanel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(100, 0, 255, 0),
                Visible = false
            };

            // Zone de texte pour afficher les coordonnées
            coordinatesTextBox = new TextBox
            {
                Location = new Point((this.Width - 400) / 2, this.Height - 100),
                Size = new Size(400, 25),
                BackColor = Color.Black,
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center
            };

            // Bouton de sauvegarde (invisible au début)
            saveButton = new Button
            {
                Text = "Valider cette zone",
                Location = new Point((this.Width - 200) / 2, this.Height - 60),
                Size = new Size(200, 40),
                BackColor = Color.Green,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Visible = false
            };
            saveButton.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };

            this.Controls.AddRange(new Control[] {
                instructionLabel,
                selectionPanel,
                coordinatesTextBox,
                saveButton
            });

            // Événements de souris
            this.MouseDown += CalibrationForm_MouseDown;
            this.MouseMove += CalibrationForm_MouseMove;
            this.MouseUp += CalibrationForm_MouseUp;
            this.KeyDown += CalibrationForm_KeyDown;
            this.KeyPreview = true;
        }

        private void CalibrationForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void CalibrationForm_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSelecting = true;
                startPoint = e.Location;
                selectionPanel.Location = e.Location;
                selectionPanel.Size = new Size(0, 0);
                selectionPanel.Visible = true;
                saveButton.Visible = false;
            }
        }

        private void CalibrationForm_MouseMove(object? sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                int x = Math.Min(startPoint.X, e.X);
                int y = Math.Min(startPoint.Y, e.Y);
                int width = Math.Abs(e.X - startPoint.X);
                int height = Math.Abs(e.Y - startPoint.Y);

                selectionPanel.Location = new Point(x, y);
                selectionPanel.Size = new Size(width, height);

                coordinatesTextBox.Text = $"X={x}, Y={y}, W={width}, H={height}";
            }
        }

        private void CalibrationForm_MouseUp(object? sender, MouseEventArgs e)
        {
            if (isSelecting && e.Button == MouseButtons.Left)
            {
                isSelecting = false;

                int x = Math.Min(startPoint.X, e.X);
                int y = Math.Min(startPoint.Y, e.Y);
                int width = Math.Abs(e.X - startPoint.X);
                int height = Math.Abs(e.Y - startPoint.Y);

                if (width > 10 && height > 10) // Zone valide
                {
                    SelectedRegion = new Rectangle(x, y, width, height);
                    saveButton.Visible = true;
                    instructionLabel.Text = $"Zone sélectionnée: {width}x{height}\nCliquez sur 'Valider' ou refaites une sélection";
                }
            }
        }
    }
}
