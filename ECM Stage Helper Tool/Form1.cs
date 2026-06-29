using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Hauptfenster: lädt CSV-Maps aus dem "csv"-Unterordner, stellt sie als editierbare Tabelle dar,
    /// ermöglicht Achsenänderungen mit linearer Interpolation und markiert Änderungen rot.
    /// </summary>
    public partial class Form1 : Form
    {
        // --- Steuerelemente ---
        private readonly ListBox _lbMaps = new ListBox();
        private readonly DataGridView _dgv = new DataGridView();
        private readonly Button _btnReload = new Button();
        private readonly Button _btnSave = new Button();
        private readonly Button _btnResetMap = new Button();
        private readonly Button _btnResetCell = new Button();
        private readonly Label _lblInfo = new Label();

        // --- Daten ---
        private readonly List<MapModel> _maps = new List<MapModel>();
        private MapModel _currentMap;

        public Form1()
        {
            InitializeComponent();
            BuildUi();
            LoadMaps();
        }

        // -----------------------------------------------------------------------
        // UI-Aufbau
        // -----------------------------------------------------------------------

        private void BuildUi()
        {
            Text = "ECM Stage Helper – Kennfeld Remapping";
            Width = 1150;
            Height = 720;
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;

            // Mapliste links
            _lbMaps.Dock = DockStyle.Left;
            _lbMaps.Width = 230;
            _lbMaps.Font = new Font("Consolas", 9f);
            _lbMaps.SelectedIndexChanged += LbMaps_SelectedIndexChanged;

            // Tabelle rechts
            _dgv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _dgv.Location = new Point(240, 10);
            _dgv.Size = new Size(ClientSize.Width - 250, ClientSize.Height - 70);
            _dgv.AllowUserToAddRows = false;
            _dgv.AllowUserToDeleteRows = false;
            _dgv.Font = new Font("Consolas", 9f);
            _dgv.RowHeadersWidth = 80;
            _dgv.CellEndEdit += Dgv_CellEndEdit;
            _dgv.ColumnHeaderMouseClick += Dgv_ColumnHeaderMouseClick;
            _dgv.RowHeaderMouseClick += Dgv_RowHeaderMouseClick;

            // Buttons (unten links)
            int btnY = ClientSize.Height - 52;
            SetupButton(_btnReload, "Neu laden", new Point(5, btnY), BtnReload_Click);
            SetupButton(_btnSave, "CSV speichern", new Point(110, btnY), BtnSave_Click);
            SetupButton(_btnResetMap, "Map zurücksetzen", new Point(230, btnY), BtnResetMap_Click);
            SetupButton(_btnResetCell, "Zelle zurücksetzen", new Point(360, btnY), BtnResetCell_Click);

            _lblInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lblInfo.Location = new Point(500, btnY + 8);
            _lblInfo.AutoSize = false;
            _lblInfo.Size = new Size(600, 36);
            _lblInfo.Text = "Tipp: Spalten-/Zeilenkopf klicken = Achse ändern  |  Rot = geänderte Werte";
            _lblInfo.ForeColor = Color.DimGray;

            Controls.AddRange(new Control[] { _lbMaps, _dgv, _btnReload, _btnSave, _btnResetMap, _btnResetCell, _lblInfo });

            Resize += (s, e) => OnFormResize();
        }

        private static void SetupButton(Button btn, string text, Point location, EventHandler handler)
        {
            btn.Text = text;
            btn.Location = location;
            btn.Size = new Size(100, 36);
            btn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btn.Click += handler;
        }

        private void OnFormResize()
        {
            int btnY = ClientSize.Height - 52;
            _dgv.Size = new Size(ClientSize.Width - 250, ClientSize.Height - 70);
            _lbMaps.Height = ClientSize.Height;
            foreach (Control c in new Control[] { _btnReload, _btnSave, _btnResetMap, _btnResetCell })
                c.Top = btnY;
            _lblInfo.Top = btnY + 8;
            _lblInfo.Width = ClientSize.Width - 510;
        }

        // -----------------------------------------------------------------------
        // Laden
        // -----------------------------------------------------------------------

        private void LoadMaps()
        {
            try
            {
                _maps.Clear();
                _lbMaps.Items.Clear();
                _currentMap = null;
                _dgv.Columns.Clear();

                string csvFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "csv");
                if (!Directory.Exists(csvFolder))
                {
                    MessageBox.Show($"CSV-Ordner nicht gefunden:\n{csvFolder}", "Hinweis",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var errors = new List<string>();
                foreach (string f in Directory.GetFiles(csvFolder, "*.csv"))
                {
                    try
                    {
                        var map = CsvParser.LoadMap(f);
                        _maps.Add(map);
                        _lbMaps.Items.Add(map.Name);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex.Message);
                    }
                }

                if (_maps.Count == 0)
                    MessageBox.Show("Keine CSV-Dateien im Ordner gefunden.", "Info",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (errors.Count > 0)
                    MessageBox.Show($"Fehler beim Laden einiger CSVs:\n{string.Join("\n", errors)}",
                        "Teilfehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden: {ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Darstellung
        // -----------------------------------------------------------------------

        private void LbMaps_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lbMaps.SelectedIndex < 0) return;
            _currentMap = _maps[_lbMaps.SelectedIndex];
            RenderMap(_currentMap);
        }

        private void RenderMap(MapModel map)
        {
            try
            {
                _dgv.SuspendLayout();
                _dgv.Columns.Clear();
                _dgv.Rows.Clear();

                // Erste Spalte: Y-Achsen-Label (read-only)
                var colY = new DataGridViewTextBoxColumn { HeaderText = "Y \\ X", ReadOnly = true, Width = 80 };
                _dgv.Columns.Add(colY);

                // Spalten für X-Achse; Header = X-Wert
                for (int c = 0; c < map.Cols; c++)
                {
                    var col = new DataGridViewTextBoxColumn
                    {
                        HeaderText = map.XAxis[c].ToString(CultureInfo.InvariantCulture),
                        Name = $"C{c}",
                        Width = 75
                    };
                    _dgv.Columns.Add(col);
                }

                _dgv.Rows.Add(map.Rows);

                for (int r = 0; r < map.Rows; r++)
                {
                    // Y-Achsenbeschriftung in erster Spalte
                    _dgv.Rows[r].Cells[0].Value = map.YAxis[r].ToString(CultureInfo.InvariantCulture);
                    _dgv.Rows[r].Cells[0].Style.BackColor = Color.WhiteSmoke;

                    for (int c = 0; c < map.Cols; c++)
                    {
                        var cell = _dgv.Rows[r].Cells[c + 1];
                        cell.Value = map.Values[r, c].ToString("G6", CultureInfo.InvariantCulture);
                        cell.Style.BackColor = map.IsCellModified(r, c) ? Color.LightCoral : Color.White;
                    }
                }
            }
            finally
            {
                _dgv.ResumeLayout();
            }
        }

        private void RefreshCell(int row, int col)
        {
            if (_currentMap == null) return;
            var cell = _dgv.Rows[row].Cells[col + 1];
            cell.Value = _currentMap.Values[row, col].ToString("G6", CultureInfo.InvariantCulture);
            cell.Style.BackColor = _currentMap.IsCellModified(row, col) ? Color.LightCoral : Color.White;
        }

        // -----------------------------------------------------------------------
        // Zellenbearbeitung
        // -----------------------------------------------------------------------

        private void Dgv_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_currentMap == null) return;
            int gridCol = e.ColumnIndex;
            int row = e.RowIndex;
            if (gridCol == 0) return; // Y-Label-Spalte

            int mapCol = gridCol - 1;
            string raw = (_dgv.Rows[row].Cells[gridCol].Value ?? string.Empty).ToString().Trim();

            if (!TryParseDouble(raw, out double val))
            {
                MessageBox.Show($"Ungültiger Wert: \"{raw}\"", "Eingabefehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Original wiederherstellen
                _dgv.Rows[row].Cells[gridCol].Value =
                    _currentMap.Values[row, mapCol].ToString("G6", CultureInfo.InvariantCulture);
                return;
            }

            _currentMap.Values[row, mapCol] = val;
            _currentMap.MarkCellModified(row, mapCol);
            _dgv.Rows[row].Cells[gridCol].Style.BackColor = Color.LightCoral;
        }

        // -----------------------------------------------------------------------
        // Achsenänderungen
        // -----------------------------------------------------------------------

        private void Dgv_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_currentMap == null || e.ColumnIndex == 0) return;
            int mapCol = e.ColumnIndex - 1;
            string current = _currentMap.XAxis[mapCol].ToString(CultureInfo.InvariantCulture);
            string input = PromptDialog.Show($"Neuen X-Wert für Spalte {mapCol} (aktuell: {current})", "X-Achse ändern");
            if (input == null) return;

            if (!TryParseDouble(input, out double newX))
            {
                MessageBox.Show($"Ungültiger X-Wert: \"{input}\"", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Remapper.ChangeXAxis(_currentMap, mapCol, newX);
                Remapper.PropagateXAxisChange(_maps, _currentMap, mapCol, newX);
                RenderMap(_currentMap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Dgv_RowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_currentMap == null) return;
            int row = e.RowIndex;
            string current = _currentMap.YAxis[row].ToString(CultureInfo.InvariantCulture);
            string input = PromptDialog.Show($"Neuen Y-Wert für Zeile {row} (aktuell: {current})", "Y-Achse ändern");
            if (input == null) return;

            if (!TryParseDouble(input, out double newY))
            {
                MessageBox.Show($"Ungültiger Y-Wert: \"{input}\"", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Remapper.ChangeYAxis(_currentMap, row, newY);
                Remapper.PropagateYAxisChange(_maps, _currentMap, row, newY);
                RenderMap(_currentMap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Buttons
        // -----------------------------------------------------------------------

        private void BtnReload_Click(object sender, EventArgs e) => LoadMaps();

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_currentMap == null) return;
            try
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.FileName = _currentMap.Name;
                    dlg.Filter = "CSV-Dateien|*.csv|Alle Dateien|*.*";
                    dlg.InitialDirectory = Path.GetDirectoryName(_currentMap.FilePath);
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    CsvParser.SaveMap(_currentMap, dlg.FileName);
                    MessageBox.Show($"Gespeichert:\n{dlg.FileName}", "OK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnResetMap_Click(object sender, EventArgs e)
        {
            if (_currentMap == null) return;
            if (MessageBox.Show($"Map '{_currentMap.Name}' vollständig zurücksetzen?", "Bestätigung",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _currentMap.ResetAll();
                RenderMap(_currentMap);
            }
        }

        private void BtnResetCell_Click(object sender, EventArgs e)
        {
            if (_currentMap == null) return;
            if (_dgv.CurrentCell == null || _dgv.CurrentCell.ColumnIndex == 0)
            {
                MessageBox.Show("Bitte eine Datenzelle auswählen.", "Hinweis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int row = _dgv.CurrentCell.RowIndex;
            int mapCol = _dgv.CurrentCell.ColumnIndex - 1;
            _currentMap.ResetCell(row, mapCol);
            RefreshCell(row, mapCol);
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        private static bool TryParseDouble(string s, out double value)
        {
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(s, NumberStyles.Any, new CultureInfo("de-DE"), out value)) return true;
            string normalized = s.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }
    }
}

