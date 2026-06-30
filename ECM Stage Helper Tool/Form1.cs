using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Hauptfenster: lädt CSV-Maps aus dem "csv"-Unterordner, stellt sie als editierbare Tabelle dar,
    /// ermöglicht Achsenänderungen mit linearer Interpolation und markiert Änderungen rot.
    /// </summary>
    public partial class Form1 : Form
    {
        // --- 3D-Ansicht ---
        private bool _show3D;

        // --- Daten ---
        private readonly List<MapModel> _maps = new List<MapModel>();
        private MapModel _currentMap;
        private string _csvFolder;
        private bool _showingOriginal;
        private readonly UndoStack _undo = new UndoStack();
        private static readonly CultureInfo _deDe = new CultureInfo("de-DE");

        // --- Interne Zwischenablage (Auswahl kopieren / einfügen) ---
        private double[,] _clipboard;
        private int _clipboardRows, _clipboardCols;

        public Form1()
        {
            InitializeComponent();
            // Icons für View-Buttons
            _btnView2D.Image = MakeGridIcon(28);
            _btnView3D.Image = Make3DIcon(28);
            _toolTip.SetToolTip(_btnView2D, "Tabellenansicht (HEX)");
            _toolTip.SetToolTip(_btnView3D, "3D-Ansicht");
            // Undo-Menü initial deaktivieren
            _miUndo.Enabled = false;
            _miRedo.Enabled = false;
            TryLoadLastFolder();
            if (_currentMap == null) SetUiForNoMap();
        }

        // -----------------------------------------------------------------------
        // Menü-Handler (vom Designer verdrahtet)
        // -----------------------------------------------------------------------

        private void MiOpen_Click(object sender, EventArgs e)   => OpenCsvFolder();
        private void MiExit_Click(object sender, EventArgs e)   => Close();
        private void MiToggle_Click(object sender, EventArgs e) => ToggleOriginalView();
        private void MiUndo_Click(object sender, EventArgs e)   => ExecuteUndo();
        private void MiRedo_Click(object sender, EventArgs e)   => ExecuteRedo();

        private void MBearbeiten_DropDownOpening(object sender, EventArgs e)
        {
            bool hasSel = _dgv.SelectedCells.Count > 1 && !_showingOriginal && _currentMap != null;
            _miUndo.Enabled      = _undo.CanUndo;
            _miRedo.Enabled      = _undo.CanRedo;
            _miInterpolate.Enabled = hasSel;
            _miCopySel.Enabled     = hasSel;
            _miPasteSel.Enabled    = hasSel && _clipboard != null;
        }

        private void BtnView2D_Click(object sender, EventArgs e) => Switch3D(false);
        private void BtnView3D_Click(object sender, EventArgs e) => Switch3D(true);

        private void Form1_Resize(object sender, EventArgs e)
        {
            int menuH = _menuStrip.Height;
            int listW = _lbMaps.Width;
            const int gap = 6;
            int gridX = listW + gap;
            int gridW = ClientSize.Width - listW - gap * 2;

            // --- Info-Bereich oberhalb des Grids ---
            // Zeile 1: Map-Name (volle Breite)
            int row1Y = menuH + gap;
            _lblMapName.Left  = gridX;
            _lblMapName.Top   = row1Y;
            _lblMapName.Width = gridW;

            // Zeile 2: Einheit | Reset-Buttons | Info-Label | View-Buttons
            int row2Y = row1Y + _lblMapName.Height + 2;
            int btnH  = _btnResetMap.Height;

            _lblUnit.Left = gridX;
            _lblUnit.Top  = row2Y + (btnH - _lblUnit.Height) / 2;

            _btnResetMap.Left = gridX + _lblUnit.Width + gap;
            _btnResetMap.Top  = row2Y;

            _btnResetCell.Left = _btnResetMap.Right + gap;
            _btnResetCell.Top  = row2Y;

            _btnView2D.Left = ClientSize.Width - 74;
            _btnView2D.Top  = row2Y + (btnH - _btnView2D.Height) / 2;
            _btnView3D.Left = ClientSize.Width - 38;
            _btnView3D.Top  = row2Y + (btnH - _btnView3D.Height) / 2;

            _lblInfo.Left  = _btnResetCell.Right + gap;
            _lblInfo.Top   = row2Y + (btnH - _lblInfo.Height) / 2;
            _lblInfo.Width = _btnView2D.Left - _lblInfo.Left - gap;

            // --- Grid füllt den Rest unterhalb des Info-Bereichs ---
            int gridY = row2Y + btnH + gap;
            var gridLoc = new Point(gridX, gridY);
            var gridSz  = new Size(gridW, ClientSize.Height - gridY - gap);
            _dgv.Location     = gridLoc;
            _dgv.Size         = gridSz;
            _panel3D.Location = gridLoc;
            _panel3D.Size     = gridSz;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // ESC (nicht im Bearbeitungsmodus): zwischen Original- und Änderungsansicht wechseln
            if (keyData == Keys.Escape && _currentMap != null && !_dgv.IsCurrentCellInEditMode)
            {
                ToggleOriginalView();
                return true;
            }
            // F3: aktuelle Map als "-mod"-Datei speichern
            if (keyData == Keys.F3 && _currentMap != null)
            {
                SaveMapAsMod();
                return true;
            }
            // Strg+C: Auswahl kopieren (nur wenn DGV den Fokus hat)
            if (keyData == (Keys.Control | Keys.C) && _currentMap != null
                && !_dgv.IsCurrentCellInEditMode && _dgv.ContainsFocus)
            {
                ExecuteCopy();
                return true;
            }
            // Strg+V: Auswahl einfügen (nur wenn DGV den Fokus hat)
            if (keyData == (Keys.Control | Keys.V) && _currentMap != null
                && !_dgv.IsCurrentCellInEditMode && _dgv.ContainsFocus)
            {
                ExecutePaste();
                return true;
            }
            // Strg+Z / Strg+Y direkt abfangen, damit _miUndo.Enabled keinen Einfluss hat
            if (keyData == (Keys.Control | Keys.Z) && !_dgv.IsCurrentCellInEditMode)
            {
                ExecuteUndo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y) && !_dgv.IsCurrentCellInEditMode)
            {
                ExecuteRedo();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ToggleOriginalView()
        {
            if (_currentMap == null) return;
            _showingOriginal = !_showingOriginal;
            RenderMap(_currentMap);
            if (_show3D)
                _panel3D.SetOriginalView(_showingOriginal);
        }

        private void ExecuteUndo()
        {
            if (_dgv.IsCurrentCellInEditMode) return;
            if (_undo.TryUndo(out var action))
            {
                _showingOriginal = false;
                action.Undo();
                if (_currentMap != null)
                    RenderMap(_currentMap);
            }
        }

        private void ExecuteRedo()
        {
            if (_dgv.IsCurrentCellInEditMode) return;
            if (_undo.TryRedo(out var action))
            {
                _showingOriginal = false;
                action.Redo();
                if (_currentMap != null)
                    RenderMap(_currentMap);
            }
        }

        // -----------------------------------------------------------------------
        // Auswahl – Kopieren / Einfügen / Interpolieren
        // -----------------------------------------------------------------------

        private bool GetSelectionRect(out int r0, out int c0, out int r1, out int c1)
        {
            r0 = c0 = int.MaxValue;
            r1 = c1 = int.MinValue;
            var cells = _dgv.SelectedCells;
            if (cells.Count == 0) { r0 = c0 = r1 = c1 = 0; return false; }
            foreach (DataGridViewCell cell in cells)
            {
                if (cell.RowIndex    < r0) r0 = cell.RowIndex;
                if (cell.RowIndex    > r1) r1 = cell.RowIndex;
                if (cell.ColumnIndex < c0) c0 = cell.ColumnIndex;
                if (cell.ColumnIndex > c1) c1 = cell.ColumnIndex;
            }
            return true;
        }

        private void ExecuteCopy()
        {
            if (_currentMap == null) return;
            if (!GetSelectionRect(out int r0, out int c0, out int r1, out int c1)) return;
            _clipboardRows = r1 - r0 + 1;
            _clipboardCols = c1 - c0 + 1;
            _clipboard = new double[_clipboardRows, _clipboardCols];
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    _clipboard[r - r0, c - c0] = _currentMap.Values[r, c];
        }

        private void ExecutePaste()
        {
            if (_currentMap == null || _clipboard == null || _showingOriginal) return;
            if (!GetSelectionRect(out int r0, out int c0, out _, out _)) return;
            if (r0 + _clipboardRows > _currentMap.Rows || c0 + _clipboardCols > _currentMap.Cols)
            {
                MessageBox.Show(
                    $"Kein Platz für den kopierten Bereich ({_clipboardRows}×{_clipboardCols}) ab Zelle [{r0},{c0}].",
                    "Einfügen nicht möglich", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var snap = new MapSnapshotUndoAction(new[] { _currentMap });
            for (int r = 0; r < _clipboardRows; r++)
                for (int c = 0; c < _clipboardCols; c++)
                {
                    int mr = r0 + r, mc = c0 + c;
                    _currentMap.Values[mr, mc] = _clipboard[r, c];
                    _currentMap.MarkCellModified(mr, mc);
                }
            snap.CaptureAfterState();
            _undo.Push(snap);
            RenderMap(_currentMap);
        }

        private void ExecuteInterpolate()
        {
            if (_currentMap == null || _showingOriginal) return;
            if (!GetSelectionRect(out int r0, out int c0, out int r1, out int c1)) return;
            int rows = r1 - r0 + 1;
            int cols = c1 - c0 + 1;
            if (rows < 2 || cols < 2)
            {
                MessageBox.Show("Bitte mindestens 2×2 Zellen auswählen.", "Interpolation",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!InterpolateDialog.Show(this, out double tl, out double tr, out double bl, out double br))
                return;
            var snap = new MapSnapshotUndoAction(new[] { _currentMap });
            for (int r = 0; r < rows; r++)
            {
                double ty = (double)r / (rows - 1);
                for (int c = 0; c < cols; c++)
                {
                    double tx  = (double)c / (cols - 1);
                    // bilineare Interpolation des Prozentsatzes über die Auswahlfläche
                    double pct = tl * (1 - tx) * (1 - ty)
                               + tr *      tx  * (1 - ty)
                               + bl * (1 - tx) *      ty
                               + br *      tx  *      ty;
                    double orig = _currentMap.Values[r0 + r, c0 + c];
                    _currentMap.Values[r0 + r, c0 + c] = orig * (1.0 + pct / 100.0);
                    _currentMap.MarkCellModified(r0 + r, c0 + c);
                }
            }
            snap.CaptureAfterState();
            _undo.Push(snap);
            RenderMap(_currentMap);
        }

        private void CtxDgv_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool hasSel = _dgv.SelectedCells.Count > 1 && !_showingOriginal && _currentMap != null;
            if (!hasSel) { e.Cancel = true; return; }
            _cmiInterpolate.Enabled = true;
            _cmiCopySel.Enabled     = true;
            _cmiPasteSel.Enabled    = _clipboard != null;
        }

        private void MiInterpolate_Click(object sender, EventArgs e) => ExecuteInterpolate();
        private void MiCopySel_Click(object sender, EventArgs e)     => ExecuteCopy();
        private void MiPasteSel_Click(object sender, EventArgs e)    => ExecutePaste();

        // -----------------------------------------------------------------------

        /// <summary>Beim Programmstart letzten Ordner aus den Einstellungen laden.</summary>
        private void TryLoadLastFolder()
        {
            string last = Properties.Settings.Default.LastCsvFolder;
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                LoadMapsFromFolder(last);
        }

        /// <summary>Ordner-Dialog anzeigen und Ordner in Einstellungen speichern.</summary>
        private void OpenCsvFolder()
        {
            string selected;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "CSV-Ordner mit Kennfeld-Exporten auswählen:";
                dlg.ShowNewFolderButton = false;
                if (_csvFolder != null && Directory.Exists(_csvFolder))
                    dlg.SelectedPath = _csvFolder;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                selected = dlg.SelectedPath;
            }

            Properties.Settings.Default.LastCsvFolder = selected;
            Properties.Settings.Default.Save();
            LoadMapsFromFolder(selected);
        }

        /// <summary>Maps aus dem angegebenen Ordner laden (kein Dialog).</summary>
        private void LoadMapsFromFolder(string folder)
        {
            _csvFolder = folder;
            Text = $"ECM Stage Helper – {Path.GetFileName(_csvFolder)}";

            try
            {
                _maps.Clear();
                _lbMaps.Items.Clear();
                _currentMap = null;
                _dgv.Columns.Clear();
                _undo.Clear();

                var errors = new List<string>();
                foreach (string f in Directory.GetFiles(_csvFolder, "*.csv"))
                {
                    try
                    {
                        var map = CsvParser.LoadMap(f);
                        _maps.Add(map);
                        string label = map.SizeString != null
                            ? $"{map.Name} [{map.SizeString}]"
                            : map.Name;
                        _lbMaps.Items.Add(label);
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
            _showingOriginal = false;
            _currentMap = _maps[_lbMaps.SelectedIndex];
            RenderMap(_currentMap);
            SetUiForMap();
            if (_show3D)
                _panel3D.SetMap(_currentMap);
        }

        private void SetUiForNoMap()
        {
            _dgv.Visible          = false;
            _lblMapName.Visible   = false;
            _lblUnit.Visible      = false;
            _lblInfo.Visible      = false;
            _btnResetMap.Enabled  = false;
            _btnResetCell.Enabled = false;
            _btnView2D.Enabled    = false;
            _btnView3D.Enabled    = false;
            _miUndo.Enabled       = false;
            _miRedo.Enabled       = false;
            _miToggle.Enabled     = false;
            _mBearbeiten.Enabled  = false;
        }

        private void SetUiForMap()
        {
            _dgv.Visible          = true;
            _lblInfo.Visible      = true;
            _btnResetMap.Enabled  = true;
            _btnResetCell.Enabled = true;
            _btnView2D.Enabled    = true;
            _btnView3D.Enabled    = true;
            _miToggle.Enabled     = true;
            _mBearbeiten.Enabled  = true;
        }

        private void RenderMap(MapModel map)
        {
            try
            {
                _dgv.SuspendLayout();
                _dgv.Columns.Clear();
                _dgv.Rows.Clear();
                _dgv.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

                // Achsenbeschriftung in der Top-Left-Zelle (Schnittfäche Zeilen-/Spaltenköpfe)
                _dgv.TopLeftHeaderCell.Value = map.AxisLabel ?? "";
                _dgv.TopLeftHeaderCell.Style.BackColor = Color.FromArgb(180, 200, 230);
                _dgv.TopLeftHeaderCell.Style.ForeColor = Color.Black;

                // Spalten für X-Achse; Header = X-Wert, grüner Hintergrund
                for (int c = 0; c < map.Cols; c++)
                {
                    var col = new DataGridViewTextBoxColumn
                    {
                        HeaderText = map.XAxis[c].ToString(_deDe),
                        Name       = $"C{c}",
                        Width      = 75,
                        SortMode   = DataGridViewColumnSortMode.NotSortable
                    };
                    col.HeaderCell.Style.BackColor = Color.FromArgb(180, 230, 180);
                    col.HeaderCell.Style.ForeColor = Color.Black;
                    _dgv.Columns.Add(col);
                }

                _dgv.Rows.Add(map.Rows);

                for (int r = 0; r < map.Rows; r++)
                {
                    // Y-Achsenwert im Zeilenkopf – blau hinterlegt
                    _dgv.Rows[r].HeaderCell.Value = map.YAxis[r].ToString(_deDe);
                    _dgv.Rows[r].HeaderCell.Style.BackColor = Color.FromArgb(210, 225, 255);
                    _dgv.Rows[r].HeaderCell.Style.ForeColor = Color.Black;

                    for (int c = 0; c < map.Cols; c++)
                    {
                        var cell = _dgv.Rows[r].Cells[c];
                        if (_showingOriginal)
                        {
                            cell.Value = map.GetOriginalValue(r, c).ToString("F2", _deDe);
                            cell.Style.BackColor = Color.White;
                        }
                        else
                        {
                            cell.Value = map.Values[r, c].ToString("F2", _deDe);
                            cell.Style.BackColor = map.IsCellModified(r, c) ? Color.LightCoral : Color.White;
                        }
                    }
                }

                _lblInfo.Text = _showingOriginal
                    ? "\u25ba ORIGINALWERTE  |  ESC = zurück zu Änderungen"
                    : "ESC = Originalwerte anzeigen  |  Rot = geänderte Werte  |  Kopfzeile klicken = Achse ändern";
                _lblInfo.ForeColor = _showingOriginal ? Color.DarkRed : Color.DimGray;

                _lblMapName.Text    = map.MapName ?? "";
                _lblMapName.Visible = !string.IsNullOrEmpty(map.MapName);
                _lblUnit.Text       = map.Unit ?? "";
                _lblUnit.Visible    = !string.IsNullOrEmpty(map.Unit);
            }
            finally
            {
                _dgv.ResumeLayout();
            }
        }

        private void RefreshCell(int row, int col)
        {
            if (_currentMap == null) return;
            var cell = _dgv.Rows[row].Cells[col];
            if (_showingOriginal)
            {
                cell.Value = _currentMap.GetOriginalValue(row, col).ToString("F2", _deDe);
                cell.Style.BackColor = Color.White;
            }
            else
            {
                cell.Value = _currentMap.Values[row, col].ToString("F2", _deDe);
                cell.Style.BackColor = _currentMap.IsCellModified(row, col) ? Color.LightCoral : Color.White;
            }
        }

        // -----------------------------------------------------------------------
        // Zellenbearbeitung
        // -----------------------------------------------------------------------

        private void Dgv_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (e.Control is TextBox tb)
            {
                tb.KeyPress -= Dgv_EditingTextBox_KeyPress;
                tb.KeyPress += Dgv_EditingTextBox_KeyPress;
            }
        }

        private void Dgv_EditingTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Steuerzeichen (Backspace, Delete usw.) immer erlauben
            if (char.IsControl(e.KeyChar)) return;

            // Ziffern erlauben
            if (char.IsDigit(e.KeyChar)) return;

            // Dezimaltrennzeichen erlauben: Komma und Punkt
            if (e.KeyChar == ',' || e.KeyChar == '.') return;

            // Minuszeichen nur am Anfang erlauben
            if (e.KeyChar == '-' && sender is TextBox tb && tb.SelectionStart == 0 && !tb.Text.Contains('-')) return;

            // Alle anderen Zeichen blockieren
            e.Handled = true;
        }

        private void Dgv_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_currentMap == null) return;
            int gridCol = e.ColumnIndex;
            int row = e.RowIndex;

            int mapCol = gridCol;
            string raw = (_dgv.Rows[row].Cells[gridCol].Value ?? string.Empty).ToString().Trim();

            if (!TryParseDouble(raw, out double val))
            {
                MessageBox.Show($"Ungültiger Wert: \"{raw}\"", "Eingabefehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Original wiederherstellen
                _dgv.Rows[row].Cells[gridCol].Value =
                    _currentMap.Values[row, mapCol].ToString("F2", _deDe);
                return;
            }

            double oldValue = _currentMap.Values[row, mapCol];
            if (Math.Abs(oldValue - val) < 1e-12)
            {
                // Kein tatsächlicher Wertunterschied – Anzeige wiederherstellen
                _dgv.Rows[row].Cells[gridCol].Value = oldValue.ToString("F2", _deDe);
                return;
            }

            _undo.Push(new CellUndoAction(_currentMap, row, mapCol, oldValue, val));
            _currentMap.Values[row, mapCol] = val;
            _currentMap.MarkCellModified(row, mapCol);
            _dgv.Rows[row].Cells[gridCol].Style.BackColor = Color.LightCoral;
        }

        // -----------------------------------------------------------------------
        // Achsenänderungen
        // -----------------------------------------------------------------------

        private void Dgv_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_currentMap == null) return;
            int mapCol = e.ColumnIndex;
            double[] x = _currentMap.XAxis;
            string current = x[mapCol].ToString(_deDe);

            // Erlaubten Bereich für den Nutzer anzeigen
            double lo = mapCol > 0              ? x[mapCol - 1] : double.NegativeInfinity;
            double hi = mapCol < x.Length - 1  ? x[mapCol + 1] : double.PositiveInfinity;
            string hint = mapCol == 0
                ? $"< {hi.ToString(_deDe)}"
                : mapCol == x.Length - 1
                    ? $"> {lo.ToString(_deDe)}"
                    : $"{lo.ToString(_deDe)} – {hi.ToString(_deDe)}";

            string input = PromptDialog.Show(
                $"Neuen X-Wert für Spalte {mapCol} (aktuell: {current})\nErlaubter Bereich: {hint}",
                "X-Achse ändern");
            if (input == null) return;

            if (!TryParseDouble(input, out double newX))
            {
                MessageBox.Show($"Ungültiger X-Wert: \"{input}\"", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Sortierung beibehalten: neuer Wert muss zwischen Nachbarn liegen
            if ((mapCol > 0 && newX <= x[mapCol - 1]) ||
                (mapCol < x.Length - 1 && newX >= x[mapCol + 1]))
            {
                MessageBox.Show(
                    $"X-Wert muss im Bereich ({hint}) liegen, um die Sortierung zu erhalten.",
                    "Ungültiger Bereich", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var affected = _maps.Where(m => m == _currentMap || m.Cols == _currentMap.Cols);
                var snapshotAction = new MapSnapshotUndoAction(affected);
                Remapper.ChangeXAxis(_currentMap, mapCol, newX);
                Remapper.PropagateXAxisChange(_maps, _currentMap, mapCol, newX);
                snapshotAction.CaptureAfterState();
                _undo.Push(snapshotAction);
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
            double[] y = _currentMap.YAxis;
            string current = y[row].ToString(_deDe);

            double lo = row > 0            ? y[row - 1] : double.NegativeInfinity;
            double hi = row < y.Length - 1 ? y[row + 1] : double.PositiveInfinity;
            string hint = row == 0
                ? $"< {hi.ToString(_deDe)}"
                : row == y.Length - 1
                    ? $"> {lo.ToString(_deDe)}"
                    : $"{lo.ToString(_deDe)} – {hi.ToString(_deDe)}";

            string input = PromptDialog.Show(
                $"Neuen Y-Wert für Zeile {row} (aktuell: {current})\nErlaubter Bereich: {hint}",
                "Y-Achse ändern");
            if (input == null) return;

            if (!TryParseDouble(input, out double newY))
            {
                MessageBox.Show($"Ungültiger Y-Wert: \"{input}\"", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if ((row > 0 && newY <= y[row - 1]) ||
                (row < y.Length - 1 && newY >= y[row + 1]))
            {
                MessageBox.Show(
                    $"Y-Wert muss im Bereich ({hint}) liegen, um die Sortierung zu erhalten.",
                    "Ungültiger Bereich", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var affected = _maps.Where(m => m == _currentMap || m.Rows == _currentMap.Rows);
                var snapshotAction = new MapSnapshotUndoAction(affected);
                Remapper.ChangeYAxis(_currentMap, row, newY);
                Remapper.PropagateYAxisChange(_maps, _currentMap, row, newY);
                snapshotAction.CaptureAfterState();
                _undo.Push(snapshotAction);
                RenderMap(_currentMap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Kontextmenü – Map-Liste
        // -----------------------------------------------------------------------

        private void CtxMaps_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_csvFolder == null) { e.Cancel = true; return; }
            var pt = _lbMaps.PointToClient(Cursor.Position);
            int idx = _lbMaps.IndexFromPoint(pt);
            if (idx >= 0)
                _lbMaps.SelectedIndex = idx;
            _cmiSaveAsMod.Enabled = _lbMaps.SelectedIndex >= 0;
        }

        private void CmiRefresh_Click(object sender, EventArgs e) => CheckForNewMaps();
        private void CmiSaveAsMod_Click(object sender, EventArgs e)
        {
            if (_currentMap == null) return;
            SaveMapAsModSilent(_currentMap);
        }

        private void CheckForNewMaps()
        {
            if (_csvFolder == null) return;
            var existingPaths = new System.Collections.Generic.HashSet<string>(
                _maps.Select(m => m.FilePath), StringComparer.OrdinalIgnoreCase);
            var newFiles = Directory.GetFiles(_csvFolder, "*.csv")
                                    .Where(f => !existingPaths.Contains(f)).ToList();
            if (newFiles.Count == 0)
            {
                MessageBox.Show("Keine neuen CSV-Dateien im Ordner gefunden.", "Aktualisieren",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var errors = new List<string>();
            foreach (string f in newFiles)
            {
                try
                {
                    var map = CsvParser.LoadMap(f);
                    _maps.Add(map);
                    string label = map.SizeString != null ? $"{map.Name} [{map.SizeString}]" : map.Name;
                    _lbMaps.Items.Add(label);
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            if (errors.Count > 0)
                MessageBox.Show($"Fehler beim Laden:\n{string.Join("\n", errors)}",
                    "Teilfehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show($"{newFiles.Count} neue CSV-Datei(en) geladen.",
                    "Aktualisieren", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveMapAsModSilent(MapModel map)
        {
            try
            {
                string dir      = Path.GetDirectoryName(map.FilePath);
                string baseName = Path.GetFileNameWithoutExtension(map.FilePath);
                string ext      = Path.GetExtension(map.FilePath);
                string modPath  = Path.Combine(dir, baseName + "-mod" + ext);
                CsvParser.SaveMap(map, modPath);
                MessageBox.Show($"Gespeichert:\n{modPath}", "Gespeichert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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

        private void SaveMapAsMod()
        {
            if (_currentMap == null) return;
            try
            {
                string dir = Path.GetDirectoryName(_currentMap.FilePath);
                string baseName = Path.GetFileNameWithoutExtension(_currentMap.FilePath);
                string ext = Path.GetExtension(_currentMap.FilePath);
                string defaultName = baseName + "-mod" + ext;

                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "Map als Kopie speichern (F3)";
                    dlg.FileName = defaultName;
                    dlg.Filter = "CSV-Dateien|*.csv|Alle Dateien|*.*";
                    dlg.InitialDirectory = dir;
                    dlg.OverwritePrompt = true;
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    CsvParser.SaveMap(_currentMap, dlg.FileName);
                    MessageBox.Show($"Gespeichert:\n{dlg.FileName}", "Gespeichert",
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
                var snapshotAction = new MapSnapshotUndoAction(new[] { _currentMap });
                _currentMap.ResetAll();
                snapshotAction.CaptureAfterState();
                _undo.Push(snapshotAction);
                RenderMap(_currentMap);
            }
        }

        private void BtnResetCell_Click(object sender, EventArgs e)
        {
            if (_currentMap == null) return;
            if (_dgv.CurrentCell == null)
            {
                MessageBox.Show("Bitte eine Datenzelle auswählen.", "Hinweis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int row = _dgv.CurrentCell.RowIndex;
            int mapCol = _dgv.CurrentCell.ColumnIndex;
            if (!_currentMap.IsCellModified(row, mapCol)) return;
            double resetTarget = _currentMap.GetOriginalValue(row, mapCol);
            _undo.Push(new CellUndoAction(_currentMap, row, mapCol, _currentMap.Values[row, mapCol], resetTarget));
            _currentMap.ResetCell(row, mapCol);
            RefreshCell(row, mapCol);
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        private static bool TryParseDouble(string s, out double value)
        {
            s = s.Trim();
            // NumberStyles.Float (kein AllowThousands): verhindert, dass "-2,67" als "-267" geparst wird
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(s, NumberStyles.Float, _deDe, out value)) return true;
            string normalized = s.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // -----------------------------------------------------------------------
        // Ansicht umschalten
        // -----------------------------------------------------------------------

        private void Switch3D(bool show3D)
        {
            _show3D = show3D;
            _dgv.Visible     = !show3D;
            _panel3D.Visible = show3D;

            _btnView2D.FlatAppearance.BorderColor = show3D ? Color.FromArgb(60, 63, 65) : Color.DodgerBlue;
            _btnView3D.FlatAppearance.BorderColor = show3D ? Color.DodgerBlue : Color.FromArgb(60, 63, 65);

            if (show3D && _currentMap != null)
            {
                _panel3D.SetMap(_currentMap);
                _panel3D.SetOriginalView(_showingOriginal);
            }
        }

        // -----------------------------------------------------------------------
        // View-Button-Icons (programmatisch gezeichnet)
        // -----------------------------------------------------------------------

        /// <summary>HEX-Tabellen-Icon: kleines Gitternetz mit "#"-Symbol</summary>
        private static Bitmap MakeGridIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                int m = 3;
                int w = size - m * 2;
                using (var pen = new Pen(Color.FromArgb(200, 200, 200), 1.5f))
                {
                    for (int i = 0; i <= 3; i++)
                    {
                        int x = m + i * w / 3;
                        int y = m + i * w / 3;
                        g.DrawLine(pen, x, m, x, m + w);
                        g.DrawLine(pen, m, y, m + w, y);
                    }
                }
                using (var br = new SolidBrush(Color.DodgerBlue))
                    g.FillRectangle(br, m + w / 3 + 1, m + w / 3 + 1, w / 3 - 1, w / 3 - 1);
            }
            return bmp;
        }

        /// <summary>3D-Würfel-Icon im WinOLS-Stil</summary>
        private static Bitmap Make3DIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                int cols = 5, rows = 4;
                int cw = (size - 6) / cols;
                int ch = (size - 8) / rows;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        float t   = (float)(r * cols + c) / (rows * cols - 1);
                        float hue = (1f - t) * 120f;
                        var col   = HsvToRgbStatic(hue, 0.85f, 0.9f);
                        int px = 3 + c * cw + r;
                        int py = 4 + r * ch - r;
                        using (var br = new SolidBrush(col))
                            g.FillRectangle(br, px, py, cw - 1, ch - 1);
                    }
                }
                using (var pen = new Pen(Color.FromArgb(180, 180, 180), 0.8f))
                {
                    for (int r = 0; r <= rows; r++)
                        g.DrawLine(pen, 3 + r, 4 + r * ch - r, 3 + cols * cw + r, 4 + r * ch - r);
                    for (int c = 0; c <= cols; c++)
                        g.DrawLine(pen, 3 + c * cw, 4, 3 + c * cw + rows, 4 + rows * ch);
                }
            }
            return bmp;
        }

        private static Color HsvToRgbStatic(float h, float s, float v)
        {
            h = ((h % 360f) + 360f) % 360f;
            float sector = h / 60f;
            int   i      = (int)sector;
            float f      = sector - i;
            float p      = v * (1 - s);
            float q      = v * (1 - s * f);
            float t      = v * (1 - s * (1 - f));
            float r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromArgb((int)(r*255),(int)(g*255),(int)(b*255));
        }
    }
}

