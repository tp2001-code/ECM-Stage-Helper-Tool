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

        // --- BIN ---
        private BinFlash _binFlash;

        // --- Interne Zwischenablage (Auswahl kopieren / einfügen) ---
        private double[,] _clipboard;
        private int _clipboardRows, _clipboardCols;

        // --- Formgröße bei geladener Map ---
        private Size _normalSize;

        // --- +/- Zellen-Schritt (Undo-Batching bei gehaltenem Tastendruck) ---
        private bool      _stepPending;
        private int       _stepPendingRow, _stepPendingCol;
        private double    _stepBaseValue;
        private double[,] _stepSnapshotBefore; // fuer Multi-Zell-Undo

        public Form1()
        {
            InitializeComponent();
            // Icons für View-Buttons
            _btnView2D.Image = MakeGridIcon(28);
            _btnView3D.Image = Make3DIcon(28);
            _toolTip.SetToolTip(_btnView2D, "Tabellenansicht (HEX)");
            _toolTip.SetToolTip(_btnView3D, "3D-Ansicht");
            // Undo-Menü initial deaktivieren
            _miUndo.Enabled    = false;
            _miRedo.Enabled    = false;
            _miSaveBin.Enabled = false;
            _dgv.KeyUp += Dgv_KeyUp;
            TryLoadLastFolder();
            if (_currentMap == null) SetUiForNoMap();
        }

        // -----------------------------------------------------------------------
        // Menü-Handler (vom Designer verdrahtet)
        // -----------------------------------------------------------------------

        private void MiNew_Click(object sender, EventArgs e)   => ResetAll();
        private void MiOpen_Click(object sender, EventArgs e)   => OpenCsvFolder();
        private void MiUnloadCsv_Click(object sender, EventArgs e) => UnloadAllCsv();
        private void MiExit_Click(object sender, EventArgs e)   => Close();
        private void MiToggle_Click(object sender, EventArgs e) => ToggleOriginalView();
        private void MiUndo_Click(object sender, EventArgs e)   => ExecuteUndo();
        private void MiRedo_Click(object sender, EventArgs e)   => ExecuteRedo();
        private void MiOpenBin_Click(object sender, EventArgs e) => OpenBin();
        private void MiSaveBin_Click(object sender, EventArgs e) => SaveBin();

        private void MiExportA2l_Click(object sender, EventArgs e)   => RunExport("A2L-Datei exportieren",   "A2L-Dateien|*.a2l|Alle Dateien|*.*", ".a2l",  Exporters.ExportA2l);
        private void MiExportDamos_Click(object sender, EventArgs e) => RunExport("DAMOS-Datei exportieren", "DAMOS-Dateien|*.dam|Alle Dateien|*.*", ".dam", Exporters.ExportDamos);
        private void MiExportKp_Click(object sender, EventArgs e)    => RunExport("Kennfeldpaket exportieren","Kennfeldpakete|*.kp|Alle Dateien|*.*", ".kp",  Exporters.ExportKennfeldpaket);

        private void RunExport(string title, string filter, string ext,
            Action<IEnumerable<MapModel>, string> exportFn)
        {
            if (_maps.Count == 0)
            {
                MessageBox.Show("Es sind keine Maps geladen.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = title;
                dlg.Filter = filter;
                dlg.DefaultExt = ext.TrimStart('.');
                dlg.FileName = (Path.GetFileName(_csvFolder) ?? "export") + ext;
                if (_csvFolder != null && Directory.Exists(_csvFolder))
                    dlg.InitialDirectory = _csvFolder;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    exportFn(_maps, dlg.FileName);
                    MessageBox.Show($"Export erfolgreich:\n{dlg.FileName}", "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Export:\n{ex.Message}", "Export-Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void MBearbeiten_DropDownOpening(object sender, EventArgs e)
        {
            bool hasSel    = _dgv.SelectedCells.Count > 1 && !_showingOriginal && _currentMap != null;
            bool hasBin    = _binFlash != null && _maps.Count > 0;
            _miUndo.Enabled          = _undo.CanUndo;
            _miRedo.Enabled          = _undo.CanRedo;
            _miInterpolate.Enabled   = hasSel;
            _miCopySel.Enabled       = hasSel;
            _miPasteSel.Enabled      = hasSel && _clipboard != null;
            _miAllCsvToBin.Enabled   = hasBin;
            _miAllBinToCsv.Enabled   = hasBin;
            _mKiRemap.Enabled        = _maps.Count > 0;
            _miShiftTorque.Enabled   = _maps.Any(m => m.Name.ToLowerInvariant().Contains("drehmoment") && m.Name.ToLowerInvariant().Contains("begrenzer"));
        }

        private void BtnView2D_Click(object sender, EventArgs e) => Switch3D(false);
        private void BtnView3D_Click(object sender, EventArgs e) => Switch3D(true);

        private void Form1_Resize(object sender, EventArgs e)
        {
            // Normale Größe merken (nur wenn Map geladen, nicht im komprimierten Zustand)
            if (_currentMap != null)
                _normalSize = Size;

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

            // Zeile 2: Einheit | View-Icons | Reset-Buttons | Info-Label
            int row2Y = row1Y + _lblMapName.Height + 2;
            int btnH  = _btnResetMap.Height;

            _lblUnit.Left = gridX;
            _lblUnit.Top  = row2Y + (btnH - _lblUnit.Height) / 2;

            _btnView2D.Left = gridX + _lblUnit.Width + gap;
            _btnView2D.Top  = row2Y + (btnH - _btnView2D.Height) / 2;
            _btnView3D.Left = _btnView2D.Right + 2;
            _btnView3D.Top  = row2Y + (btnH - _btnView3D.Height) / 2;

            _btnResetMap.Left = _btnView3D.Right + gap;
            _btnResetMap.Top  = row2Y;

            _btnResetCell.Left = _btnResetMap.Right + gap;
            _btnResetCell.Top  = row2Y;

            _lblInfo.Left  = _btnResetCell.Right + gap;
            _lblInfo.Top   = row2Y + (btnH - _lblInfo.Height) / 2;
            _lblInfo.Width = ClientSize.Width - _lblInfo.Left - gap;

            // Zeile 3: Leistungsschätzung
            int row3Y = row2Y + btnH + 2;
            _lblPower.Left  = gridX;
            _lblPower.Top   = row3Y;
            _lblPower.Width = gridW;

            // --- Grid füllt den Rest unterhalb des Info-Bereichs ---
            int gridY = row3Y + _lblPower.Height + gap;
            int gridX2 = gridX;
            int gridW2 = gridW;

            bool binVisible = _dgvBin != null && _dgvBin.Visible;

            if (binVisible)
            {
                // Wenn BIN-Tabelle sichtbar: verfügbaren Raum 50/50 teilen
                int totalH    = ClientSize.Height - gridY - gap;
                int headerH   = _lblBinHeader.Height + 2;
                int btnBinH   = _btnApplyToBin.Height;
                int csvH      = (totalH - headerH - btnBinH - gap * 2) / 2;
                int binH      = totalH - csvH - headerH - btnBinH - gap * 2;
                if (csvH < 60) csvH = 60;
                if (binH < 60) binH = 60;

                _dgv.Location     = new Point(gridX2, gridY);
                _dgv.Size         = new Size(gridW2, csvH);
                _panel3D.Location = new Point(gridX2, gridY);
                _panel3D.Size     = new Size(gridW2, csvH);

                int binHeaderY = _dgv.Bottom + gap;
                _lblBinHeader.Left   = gridX2;
                _lblBinHeader.Top    = binHeaderY;
                _lblBinHeader.Width  = gridW2 - _btnApplyToBin.Width - _btnReadFromBin.Width - gap * 2;

                _btnApplyToBin.Left  = ClientSize.Width - _btnApplyToBin.Width - gap;
                _btnApplyToBin.Top   = binHeaderY;

                _btnReadFromBin.Left = _btnApplyToBin.Left - _btnReadFromBin.Width - gap;
                _btnReadFromBin.Top  = binHeaderY;

                _dgvBin.Location = new Point(gridX2, binHeaderY + _lblBinHeader.Height + 2);
                _dgvBin.Size     = new Size(gridW2, binH);
            }
            else
            {
                // Kein BIN: normales Grid füllt alles
                var gridLoc = new Point(gridX2, gridY);
                var gridSz  = new Size(gridW2, ClientSize.Height - gridY - gap);
                _dgv.Location     = gridLoc;
                _dgv.Size         = gridSz;
                _panel3D.Location = gridLoc;
                _panel3D.Size     = gridSz;
            }

            // Border-Panel immer passend hinter _dgv legen
            const int border = 3;
            _pnlOriginalBorder.Location = new Point(_dgv.Left - border, _dgv.Top - border);
            _pnlOriginalBorder.Size     = new Size(_dgv.Width + border * 2, _dgv.Height + border * 2);
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
            // A: 3D-/HEX-Ansicht umschalten
            if (keyData == Keys.A && _currentMap != null && !_dgv.IsCurrentCellInEditMode)
            {
                Switch3D(!_show3D);
                return true;
            }
            // + / -: ausgewählte Zelle um 1 erhöhen / verringern
            bool isPlus  = keyData == Keys.Add      || keyData == Keys.Oemplus || keyData == (Keys.Oemplus | Keys.Shift);
            bool isMinus = keyData == Keys.Subtract || keyData == Keys.OemMinus;
            if ((isPlus || isMinus) && _currentMap != null && !_showingOriginal)
            {
                if (!_show3D && !_dgv.IsCurrentCellInEditMode && _dgv.ContainsFocus)
                {
                    StepCell(isPlus ? +1 : -1);
                    return true;
                }
                if (_show3D && _panel3D.ContainsFocus && _panel3D.SelectedRow >= 0)
                {
                    StepCell(isPlus ? +1 : -1);
                    UpdatePowerEstimate();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ToggleOriginalView()
        {
            if (_currentMap == null) return;
            _showingOriginal = !_showingOriginal;
            if (_show3D)
            {
                _panel3D.SetOriginalView(_showingOriginal);
            }
            else
            {
                RenderMap(_currentMap);
                UpdateDgvBorderPanel();
            }
        }

        /// <summary>Erzwingt ein Neuzeichnen des DGV, damit der Außenrahmen aktualisiert wird.</summary>
        private void UpdateDgvBorderPanel()
        {
            _pnlOriginalBorder.Visible = false; // Panel nicht verwendet
            _dgv.Invalidate();
        }

        /// <summary>
        /// Zeichnet auf die äußersten Datenzellen einen grünen Außenrahmen,
        /// wenn die Original-Ansicht aktiv ist.
        /// </summary>
        private void Dgv_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (!_showingOriginal) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return; // Header überspringen

            int lastRow = _dgv.Rows.Count - 1;
            int lastCol = _dgv.Columns.Count - 1;

            bool isTop    = e.RowIndex == 0;
            bool isBottom = e.RowIndex == lastRow;
            bool isLeft   = e.ColumnIndex == 0;
            bool isRight  = e.ColumnIndex == lastCol;

            if (!isTop && !isBottom && !isLeft && !isRight) return;

            // Zelle normal zeichnen lassen
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);

            using (var pen = new Pen(Color.FromArgb(40, 180, 40), 2f))
            {
                var r = e.CellBounds;
                // Die 2px-Linie liegt 1px innen, damit sie nicht abgeschnitten wird
                if (isTop)    e.Graphics.DrawLine(pen, r.Left,  r.Top + 1,  r.Right - 1, r.Top + 1);
                if (isBottom) e.Graphics.DrawLine(pen, r.Left,  r.Bottom - 2, r.Right - 1, r.Bottom - 2);
                if (isLeft)   e.Graphics.DrawLine(pen, r.Left + 1, r.Top, r.Left + 1,  r.Bottom - 1);
                if (isRight)  e.Graphics.DrawLine(pen, r.Right - 2, r.Top, r.Right - 2, r.Bottom - 1);
            }

            e.Handled = true;
        }

        private void ExecuteUndo()
        {
            if (_dgv.IsCurrentCellInEditMode) return;
            if (_undo.TryUndo(out var action))
            {
                _showingOriginal = false;
                action.Undo();
                if (_currentMap != null)
                {
                    RenderMap(_currentMap);
                    if (_binFlash != null) RenderBinMap(_currentMap);
                }
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
                {
                    RenderMap(_currentMap);
                    if (_binFlash != null) RenderBinMap(_currentMap);
                }
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
        private void MiAllCsvToBin_Click(object sender, EventArgs e) => ExecuteAllCsvToBin();
        private void MiAllBinToCsv_Click(object sender, EventArgs e) => ExecuteAllBinToCsv();

        private void MiAiRemap_Click(object sender, EventArgs e)
        {
            using (var wizard = new AiRemap.AiRemapWizard(_maps))
            {
                if (wizard.ShowDialog(this) == DialogResult.OK && _currentMap != null)
                {
                    RenderMap(_currentMap);
                    if (_binFlash != null) RenderBinMap(_currentMap);
                    UpdatePowerEstimate();
                }
            }
        }

        private void MiShiftTorquePlateau_Click(object sender, EventArgs e)
        {
            // Drehmoment-Begrenzer in geladenen Maps suchen
            var torqueMap = _maps.FirstOrDefault(m =>
                m.Name.ToLowerInvariant().Contains("drehmoment") &&
                m.Name.ToLowerInvariant().Contains("begrenzer"));

            if (torqueMap == null)
            {
                MessageBox.Show("Keine Drehmoment-Begrenzer-Map geladen.\nBitte zuerst die CSV-Maps laden.",
                    "Map nicht gefunden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Dialog fuer RPM-Verschiebung
            string input = PromptDialog.Show(
                $"Drehmoment-Plateau verschieben\n\nMap: {torqueMap.Name}\n\nRPM-Verschiebung (positiv = Plateau zu hoeherem Drehzahlbereich verschieben):",
                "Drehmoment-Plateau verschieben");

            if (input == null) return;
            if (!double.TryParse(input.Replace(",", "."),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double shift) || shift == 0)
            {
                MessageBox.Show("Ungueltige Eingabe.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

                    bool changed = AiRemap.RemapEngine.ShiftTorquePlateauRpm(torqueMap, shift);
                    if (!changed)
                    {
                        MessageBox.Show("Keine Aenderung vorgenommen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // Ansicht aktualisieren
                    if (_currentMap == torqueMap)
                    {
                        RenderMap(_currentMap);
                        if (_binFlash != null) RenderBinMap(_currentMap);
                    }
                    UpdatePowerEstimate();

                    MessageBox.Show(
                        $"Drehmoment-Plateau um {shift:F0} RPM verschoben.\nAenderungen sind rot markiert.",
                        "Fertig", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

        private void CmiInc_Click(object sender, EventArgs e)
        {
            if (_currentMap == null || _showingOriginal) return;
            var item = sender as System.Windows.Forms.ToolStripMenuItem;
            if (item == null) return;

            // Prozentwert aus dem Item-Text lesen ("+1 %" → 0.01)
            string txt = item.Text.Replace("+", "").Replace("%", "").Replace(",", ".").Trim();
            if (!double.TryParse(txt, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pct)) return;
            double factor = 1.0 + pct / 100.0;

            var cells = _dgv.SelectedCells;
            if (cells.Count == 0) return;

            // Snapshot vor der Änderung
            var snap = new MapSnapshotUndoAction(new[] { _currentMap });

            foreach (System.Windows.Forms.DataGridViewCell cell in cells)
            {
                int r = cell.RowIndex;
                int c = cell.ColumnIndex;
                _currentMap.Values[r, c] = _currentMap.Values[r, c] * factor;
                _currentMap.MarkCellModified(r, c);
            }

            snap.CaptureAfterState();
            _undo.Push(snap);
            RenderMap(_currentMap);
        }

        // -----------------------------------------------------------------------

        /// <summary>Beim Programmstart letzten Ordner aus den Einstellungen laden.</summary>
        private void TryLoadLastFolder()
        {
            BuildRecentFoldersMenu();
            string last = Properties.Settings.Default.LastCsvFolder;
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                LoadMapsFromFolder(last);
        }

        private const int MaxRecentFolders = 10;

        /// <summary>Ordner zur Liste der zuletzt verwendeten Ordner hinzufügen und speichern.</summary>
        private void AddRecentFolder(string folder)
        {
            var list = Properties.Settings.Default.RecentFolders
                       ?? new System.Collections.Specialized.StringCollection();

            // Duplikat entfernen (case-insensitive)
            for (int i = list.Count - 1; i >= 0; i--)
                if (string.Equals(list[i], folder, StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(i);

            list.Insert(0, folder);

            while (list.Count > MaxRecentFolders)
                list.RemoveAt(list.Count - 1);

            Properties.Settings.Default.RecentFolders = list;
            Properties.Settings.Default.Save();

            BuildRecentFoldersMenu();
        }

        /// <summary>Untermenü "Letzte Ordner" anhand der gespeicherten Liste neu aufbauen.</summary>
        private void BuildRecentFoldersMenu()
        {
            _miRecentFolders.DropDownItems.Clear();
            var list = Properties.Settings.Default.RecentFolders;

            if (list == null || list.Count == 0)
            {
                _miRecentFolders.Enabled = false;
                return;
            }

            _miRecentFolders.Enabled = true;
            foreach (string folder in list)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                string path = folder; // Closure-Kopie
                var item = new ToolStripMenuItem(path)
                {
                    ToolTipText = path,
                    Enabled = Directory.Exists(path)
                };
                item.Click += (s, e) => LoadMapsFromFolder(path);
                _miRecentFolders.DropDownItems.Add(item);
            }
        }

        /// <summary>
        /// Alle Daten zurücksetzen (Neu). Fragt vorher ob ungespeicherte Änderungen
        /// gespeichert werden sollen – getrennt für CSV und BIN.
        /// </summary>
        private void ResetAll()
        {
            // --- CSV: Warnung wenn mindestens eine Map geändert wurde ---
            bool csvModified = _maps.Any(m => m.ModifiedCells.Count > 0);
            if (csvModified)
            {
                var ans = MessageBox.Show(
                    "Es gibt ungespeicherte CSV-Änderungen.\n\n" +
                    "Soll die aktuelle CSV-Map jetzt als »-mod«-Datei gespeichert werden,\n" +
                    "bevor alle Daten gelöscht werden?",
                    "Ungespeicherte CSV-Änderungen",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                if (ans == DialogResult.Cancel) return;
                if (ans == DialogResult.Yes && _currentMap != null)
                    SaveMapAsModSilent(_currentMap);
            }

            // --- BIN: Warnung wenn BIN geladen und im Speicher verändert ---
            if (_binFlash != null && _binFlash.IsModified)
            {
                var ans = MessageBox.Show(
                    "Die BIN-Datei wurde verändert aber noch nicht gespeichert.\n\n" +
                    "Soll die BIN jetzt gespeichert werden,\n" +
                    "bevor alle Daten gelöscht werden?",
                    "Ungespeicherte BIN-Änderungen",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                if (ans == DialogResult.Cancel) return;
                if (ans == DialogResult.Yes) SaveBin();
            }

            // --- Alles zurücksetzen ---
            _maps.Clear();
            _lbMaps.Items.Clear();
            _currentMap   = null;
            _csvFolder    = null;
            _showingOriginal = false;
            _clipboard    = null;
            _binFlash     = null;
            _dgv.Columns.Clear();
            _dgv.Rows.Clear();
            _dgvBin.Columns.Clear();
            _dgvBin.Rows.Clear();
            _undo.Clear();

            _miSaveBin.Enabled = false;
            Text = "ECM Stage Helper – Kennfeld Remapping";
            SetUiForNoMap();
        }

        /// <summary>Alle CSV-Maps aus dem Speicher entladen (ohne BIN zu beeinflussen).</summary>
        private void UnloadAllCsv()
        {
            if (_maps.Count == 0) return;

            bool csvModified = _maps.Any(m => m.ModifiedCells.Count > 0);
            if (csvModified)
            {
                var ans = MessageBox.Show(
                    "Es gibt ungespeicherte CSV-Änderungen.\nTrotzdem alle CSV entladen?",
                    "Ungespeicherte Änderungen",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (ans != DialogResult.Yes) return;
            }

            _maps.Clear();
            _lbMaps.Items.Clear();
            _currentMap = null;
            _csvFolder = null;
            _showingOriginal = false;
            _clipboard = null;
            _dgv.Columns.Clear();
            _dgv.Rows.Clear();
            _undo.Clear();

            Text = "ECM Stage Helper – Kennfeld Remapping";
            SetUiForNoMap();
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
            AddRecentFolder(selected);
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

                // Letzte ausgewählte Map wiederherstellen
                string lastMap = Properties.Settings.Default.LastSelectedMap;
                if (!string.IsNullOrEmpty(lastMap))
                {
                    int idx = _maps.FindIndex(m => m.Name == lastMap);
                    if (idx >= 0)
                        _lbMaps.SelectedIndex = idx;
                    else if (_maps.Count > 0)
                        _lbMaps.SelectedIndex = 0;
                }
                else if (_maps.Count > 0)
                {
                    _lbMaps.SelectedIndex = 0;
                }
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

            // Auswahl persistieren
            Properties.Settings.Default.LastSelectedMap = _currentMap.Name;
            Properties.Settings.Default.Save();

            RenderMap(_currentMap);
            SetUiForMap();
            if (_show3D)
                _panel3D.SetMap(_currentMap);
            // BIN-Tabelle bei Map-Wechsel aktualisieren
            if (_binFlash != null)
            {
                RenderBinMap(_currentMap);
                _dgvBin.Visible         = true;
                _lblBinHeader.Visible   = true;
                _btnApplyToBin.Visible  = true;
                _btnReadFromBin.Visible = true;
                Form1_Resize(this, EventArgs.Empty);
            }
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
            _dgvBin.Visible         = false;
            _lblBinHeader.Visible   = false;
            _btnApplyToBin.Visible  = false;
            _btnReadFromBin.Visible = false;
            _lblPower.Visible      = false;

            // Formgröße auf Titelleiste + Menü einschränken
            int compactH = _menuStrip.Bottom + SystemInformation.FrameBorderSize.Height * 2
                           + SystemInformation.CaptionHeight;
            MaximumSize = new Size(0, compactH);   // Breite 0 = unbegrenzt
            MinimumSize = new Size(300, compactH);
            Height = compactH;
        }

        private void SetUiForMap()
        {
            // Normale Größe wiederherstellen
            MaximumSize = Size.Empty;   // keine Einschränkung
            MinimumSize = new Size(688, 438);
            if (_normalSize.Width > 0 && _normalSize.Height > 0)
                Size = _normalSize;

            _dgv.Visible          = !_show3D;
            _panel3D.Visible      = _show3D;
            _lblInfo.Visible      = true;
            _btnResetMap.Enabled  = true;
            _btnResetCell.Enabled = true;
            _btnView2D.Enabled    = true;
            _btnView3D.Enabled    = true;
            _miToggle.Enabled     = true;
            _mBearbeiten.Enabled  = true;
            _lblPower.Visible     = true;
        }

        private void RenderMap(MapModel map)
        {
            // Im 3D-Modus nur das Panel aktualisieren – DGV nicht anfassen
            if (_show3D)
            {
                _panel3D.SetMap(map);
                _panel3D.SetOriginalView(_showingOriginal);
                UpdatePowerEstimate();
                return;
            }

            try
            {
                using (new DrawingLocker(_dgv))
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
                    string headerText = _showingOriginal
                        ? map.GetOriginalX(c).ToString(_deDe)
                        : map.XAxis[c].ToString(_deDe);
                    var col = new DataGridViewTextBoxColumn
                    {
                        HeaderText = headerText,
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
                    string rowHeader = _showingOriginal
                        ? map.GetOriginalY(r).ToString(_deDe)
                        : map.YAxis[r].ToString(_deDe);
                    _dgv.Rows[r].HeaderCell.Value = rowHeader;
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
                _dgv.ResumeLayout();
                } // DrawingLocker.Dispose → einmaliges Refresh

                UpdatePowerEstimate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Rendern: {ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StepCell(int delta)
        {
            if (_currentMap == null || _showingOriginal) return;

            if (_show3D)
            {
                // 3D-Modus: selektierte Zelle vom Panel
                int r3 = _panel3D.SelectedRow;
                int c3 = _panel3D.SelectedCol;
                if (r3 < 0 || c3 < 0) return;

                double oldVal = _currentMap.Values[r3, c3];
                if (!_stepPending || _stepPendingRow != r3 || _stepPendingCol != c3)
                {
                    _stepPending    = true;
                    _stepPendingRow = r3;
                    _stepPendingCol = c3;
                    _stepBaseValue  = oldVal;
                }
                _currentMap.Values[r3, c3] = oldVal + delta;
                _currentMap.MarkCellModified(r3, c3);
                _panel3D.RefreshValues();
                if (_binFlash != null) RenderBinMap(_currentMap);
                return;
            }

            // 2D-Modus: alle selektierten Zellen
            var cells = _dgv.SelectedCells;
            if (cells.Count == 0 && _dgv.CurrentCell != null)
            {
                // Fallback: nur aktuelle Zelle
                int row = _dgv.CurrentCell.RowIndex;
                int col = _dgv.CurrentCell.ColumnIndex;
                if (row < 0 || col < 0) return;

                double oldValue = _currentMap.Values[row, col];
                if (!_stepPending || _stepPendingRow != row || _stepPendingCol != col)
                {
                    _stepPending    = true;
                    _stepPendingRow = row;
                    _stepPendingCol = col;
                    _stepBaseValue  = oldValue;
                }
                _currentMap.Values[row, col] = oldValue + delta;
                _currentMap.MarkCellModified(row, col);
                RefreshCell(row, col);
                if (_binFlash != null) RenderBinMap(_currentMap);
                return;
            }

            // Mehrere Zellen markiert: alle gleichzeitig um delta verschieben
            // Undo: ein gemeinsamer Snapshot vor dem ersten Tastendruck
            if (!_stepPending)
            {
                _stepPending    = true;
                _stepPendingRow = -1; // Multi-Zell-Marker
                _stepPendingCol = -1;
                // Snapshot der aktuellen Map-Werte für Undo
                _stepSnapshotBefore = (double[,])_currentMap.Values.Clone();
            }

            foreach (DataGridViewCell cell in cells)
            {
                int r = cell.RowIndex;
                int c = cell.ColumnIndex;
                if (r < 0 || c < 0) continue;
                _currentMap.Values[r, c] += delta;
                _currentMap.MarkCellModified(r, c);
                RefreshCell(r, c);
            }
            if (_binFlash != null) RenderBinMap(_currentMap);
        }

        private void CommitStep()
        {
            if (!_stepPending) return;
            _stepPending = false;
            if (_currentMap == null) return;

            if (_stepPendingRow >= 0)
            {
                // Einzelne Zelle
                double finalValue = _currentMap.Values[_stepPendingRow, _stepPendingCol];
                if (Math.Abs(finalValue - _stepBaseValue) >= 1e-12)
                    _undo.Push(new CellUndoAction(_currentMap, _stepPendingRow, _stepPendingCol, _stepBaseValue, finalValue));
            }
            else if (_stepSnapshotBefore != null)
            {
                // Mehrere Zellen: Snapshot-Undo via MapSnapshotUndoAction
                var snapshotAction = new MapSnapshotUndoAction(new[] { _currentMap });
                // Werte temporaer tauschen: aktuell -> after, snapshot -> before
                var afterValues = (double[,])_currentMap.Values.Clone();
                Array.Copy(_stepSnapshotBefore, _currentMap.Values, _stepSnapshotBefore.Length);
                // before-Snapshot aufzeichnen (bereits im Feld-Zustand)
                // Dann wieder auf after zurücksetzen
                Array.Copy(afterValues, _currentMap.Values, afterValues.Length);
                snapshotAction.CaptureAfterState();
                // before wiederherstellen fuer Undo korrekt -> einfacherer Weg: CellUndoActions
                // Stattdessen: direkt alle geaenderten Zellen als Einzel-Undo pushen
                foreach (DataGridViewCell cell in _dgv.SelectedCells)
                {
                    int r = cell.RowIndex; int c = cell.ColumnIndex;
                    if (r < 0 || c < 0) continue;
                    double before = _stepSnapshotBefore[r, c];
                    double after  = _currentMap.Values[r, c];
                    if (Math.Abs(before - after) >= 1e-12)
                        _undo.Push(new CellUndoAction(_currentMap, r, c, before, after));
                }
                _stepSnapshotBefore = null;
            }
        }

        private void Dgv_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Add      || e.KeyCode == Keys.Oemplus ||
                e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
                CommitStep();
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
            UpdatePowerEstimate();
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

            var (input, rescale) = PromptDialog.ShowWithOption(
                $"Neuen X-Wert für Spalte {mapCol} (aktuell: {current})\nErlaubter Bereich: {hint}",
                "X-Achse ändern",
                "Zellenwerte proportional umrechnen (Dreisatz)");
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

                if (rescale)
                {
                    double oldX = x[mapCol];
                    if (Math.Abs(oldX) > 1e-12)
                    {
                        double factor = newX / oldX;
                        for (int r = 0; r < _currentMap.Rows; r++)
                        {
                            _currentMap.Values[r, mapCol] = _currentMap.Values[r, mapCol] * factor;
                            _currentMap.MarkCellModified(r, mapCol);
                        }
                    }
                    x[mapCol] = newX;
                }
                else
                {
                    Remapper.ChangeXAxis(_currentMap, mapCol, newX);
                    Remapper.PropagateXAxisChange(_maps, _currentMap, mapCol, newX);
                }

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

            var (input, rescale) = PromptDialog.ShowWithOption(
                $"Neuen Y-Wert für Zeile {row} (aktuell: {current})\nErlaubter Bereich: {hint}",
                "Y-Achse ändern",
                "Zellenwerte proportional umrechnen (Dreisatz)");
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

                if (rescale)
                {
                    double oldY = y[row];
                    if (Math.Abs(oldY) > 1e-12)
                    {
                        double factor = newY / oldY;
                        for (int c = 0; c < _currentMap.Cols; c++)
                        {
                            _currentMap.Values[row, c] = _currentMap.Values[row, c] * factor;
                            _currentMap.MarkCellModified(row, c);
                        }
                    }
                    y[row] = newY;
                }
                else
                {
                    Remapper.ChangeYAxis(_currentMap, row, newY);
                    Remapper.PropagateYAxisChange(_maps, _currentMap, row, newY);
                }

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

        private void MiSaveAll_Click(object sender, EventArgs e) => ExecuteSaveAll();

        private void ExecuteSaveAll()
        {
            if (_maps.Count == 0) return;

            int modCount = 0;
            foreach (var m in _maps)
                if (m.ModifiedCells.Count > 0) modCount++;

            if (MessageBox.Show(
                    $"Alle {_maps.Count} CSV-Maps in die jeweilige Originaldatei speichern?\n" +
                    $"({modCount} Map(s) mit Änderungen)\n\n" +
                    "Danach gelten alle aktuellen Werte als neue Basis –\n" +
                    "die roten Markierungen verschwinden.",
                    "Alle CSV speichern",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;

            var errors = new List<string>();
            foreach (var map in _maps)
            {
                try
                {
                    CsvParser.SaveMap(map, map.FilePath);
                    map.AcceptCurrentAsOriginal();
                }
                catch (Exception ex)
                {
                    errors.Add($"{map.Name}: {ex.Message}");
                }
            }

            _undo.Clear();
            _miUndo.Enabled = false;
            _miRedo.Enabled = false;

            if (_currentMap != null)
            {
                RenderMap(_currentMap);
                if (_binFlash != null) RenderBinMap(_currentMap);
            }

            if (errors.Count > 0)
                MessageBox.Show($"Fehler bei {errors.Count} Map(s):\n{string.Join("\n", errors)}",
                    "Teilfehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // -----------------------------------------------------------------------
        // BIN laden / speichern
        // -----------------------------------------------------------------------

        /// <summary>
        /// Befüllt _dgvBin mit den Rohwerten aus dem geladenen BIN-Dump für die
        /// angegebene Map.
        /// <para>Normalmodus (<paramref name="highlightWritten"/>=false):
        /// Zellen, deren CSV-Wert vom BIN-Wert abweicht, werden rot markiert.</para>
        /// <para>Nach-Übernehmen-Modus (<paramref name="highlightWritten"/>=true):
        /// Die Zellen aus <see cref="MapModel.ModifiedCells"/> werden rot markiert
        /// (zeigt an, was gerade in die BIN geschrieben wurde).</para>
        /// </summary>
        private void RenderBinMap(MapModel map, bool highlightWritten = false)
        {
            if (_binFlash == null || map == null) return;

            double[,] binValues = _binFlash.ReadMapValues(map);

            using (new DrawingLocker(_dgvBin))
            {
            _dgvBin.SuspendLayout();
            _dgvBin.Columns.Clear();
            _dgvBin.Rows.Clear();
            _dgvBin.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _dgvBin.TopLeftHeaderCell.Value = map.AxisLabel ?? "";
            _dgvBin.TopLeftHeaderCell.Style.BackColor = Color.FromArgb(180, 200, 230);
            _dgvBin.TopLeftHeaderCell.Style.ForeColor = Color.Black;

            if (binValues == null)
            {
                _dgvBin.Columns.Add("info", "–");
                _dgvBin.Rows.Add(1);
                _dgvBin.Rows[0].Cells[0].Value = "Keine BIN-Adresse für diese Map verfügbar.";
                _dgvBin.ResumeLayout();
                return;
            }

            for (int c = 0; c < map.Cols; c++)
            {
                var col = new DataGridViewTextBoxColumn
                {
                    HeaderText = map.XAxis[c].ToString(_deDe),
                    Name       = $"B{c}",
                    Width      = 75,
                    SortMode   = DataGridViewColumnSortMode.NotSortable,
                    ReadOnly   = true
                };
                col.HeaderCell.Style.BackColor = Color.FromArgb(180, 230, 180);
                col.HeaderCell.Style.ForeColor = Color.Black;
                _dgvBin.Columns.Add(col);
            }

            _dgvBin.Rows.Add(map.Rows);

            for (int r = 0; r < map.Rows; r++)
            {
                _dgvBin.Rows[r].HeaderCell.Value = map.YAxis[r].ToString(_deDe);
                _dgvBin.Rows[r].HeaderCell.Style.BackColor = Color.FromArgb(210, 225, 255);
                _dgvBin.Rows[r].HeaderCell.Style.ForeColor = Color.Black;

                for (int c = 0; c < map.Cols; c++)
                {
                    var cell = _dgvBin.Rows[r].Cells[c];
                    double binVal = binValues[r, c];
                    double csvVal = map.Values[r, c];
                    cell.Value = binVal.ToString("F2", _deDe);

                    bool mark = highlightWritten
                        ? map.IsCellModified(r, c)
                        : Math.Abs(csvVal - binVal) > 0.005;
                    cell.Style.BackColor = mark ? Color.LightCoral : Color.White;
                    cell.Style.ForeColor = mark ? Color.DarkRed    : Color.Black;
                }
            }

            _dgvBin.ResumeLayout();
            } // DrawingLocker.Dispose → einmaliges Refresh
        }

        private void OpenBin()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "BIN-Flash-Dump öffnen";
                dlg.Filter = "BIN-Dateien|*.bin|Alle Dateien|*.*";
                if (_csvFolder != null)
                    dlg.InitialDirectory = _csvFolder;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _binFlash = BinFlash.Load(dlg.FileName);
                    Text = $"ECM Stage Helper – {Path.GetFileName(dlg.FileName)}";
                    // BIN-Controls sichtbar machen
                    _miSaveBin.Enabled   = true;
                    _btnApplyToBin.Visible  = _currentMap != null;
                    _btnReadFromBin.Visible = _currentMap != null;
                    _lblBinHeader.Visible   = _currentMap != null;
                    _dgvBin.Visible         = _currentMap != null;
                    if (_currentMap != null) RenderBinMap(_currentMap);
                    Form1_Resize(this, EventArgs.Empty);
                    MessageBox.Show($"BIN geladen:\n{dlg.FileName}", "BIN geöffnet",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Laden der BIN:\n{ex.Message}", "Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveBin()
        {
            if (_binFlash == null) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "BIN-Flash-Dump speichern";
                dlg.Filter = "BIN-Dateien|*.bin|Alle Dateien|*.*";
                var binName = Path.GetFileNameWithoutExtension(_binFlash.FilePath) + "_mod" + Path.GetExtension(_binFlash.FilePath);
                dlg.FileName = binName;
                if (_csvFolder != null)
                    dlg.InitialDirectory = _csvFolder;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _binFlash.Save(dlg.FileName);
                    MessageBox.Show($"BIN gespeichert:\n{dlg.FileName}", "Gespeichert",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Speichern der BIN:\n{ex.Message}", "Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnApplyToBin_Click(object sender, EventArgs e)
        {
            if (_binFlash == null || _currentMap == null) return;

            var result = MessageBox.Show(
                $"CSV-Werte von\n\"{_currentMap.MapName ?? _currentMap.Name}\"\nin die BIN übernehmen?\n\n" +
                "Die BIN-Datei wird im Speicher verändert.\n" +
                "Danach mit \"BIN Speichern\" sichern.",
                "Werte übernehmen – Rückfrage",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes) return;

            if (!_binFlash.WriteMapValues(_currentMap))
            {
                MessageBox.Show(
                    "Die Map hat keine gültige BIN-Adresse oder liegt außerhalb des Dumps.\n" +
                    "Es wurden keine Werte geschrieben.",
                    "Nicht möglich",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // BIN-Tabelle neu rendern: jetzt BIN == CSV, aber geänderte Zellen rot markieren
            RenderBinMap(_currentMap, highlightWritten: true);
            MessageBox.Show(
                "CSV-Werte wurden in die BIN übertragen.\n" +
                "Vergiss nicht, die BIN zu speichern (Datei → BIN speichern).",
                "Übernommen",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void BtnReadFromBin_Click(object sender, EventArgs e)
        {
            if (_binFlash == null || _currentMap == null) return;

            var confirm = MessageBox.Show(
                $"BIN-Werte von\n\"{_currentMap.MapName ?? _currentMap.Name}\"\nin die CSV-Map übernehmen?\n\n" +
                "Die CSV-Map im Speicher wird mit den BIN-Werten überschrieben.\n" +
                "Der Vorgang kann per Rückgängig (Strg+Z) rückgängig gemacht werden.",
                "BIN → CSV – Rückfrage",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            var snap = new MapSnapshotUndoAction(new[] { _currentMap });
            if (!_binFlash.ReadMapValuesIntoMap(_currentMap))
            {
                MessageBox.Show(
                    "Die Map hat keine gültige BIN-Adresse oder liegt außerhalb des Dumps.\n" +
                    "Es wurden keine Werte übernommen.",
                    "Nicht möglich",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            snap.CaptureAfterState();
            _undo.Push(snap);
            RenderMap(_currentMap);
            RenderBinMap(_currentMap);
            UpdatePowerEstimate();
            MessageBox.Show(
                "BIN-Werte wurden in die CSV-Map übernommen.\n" +
                "Vergiss nicht, die CSV zu speichern (Datei → Speichern).",
                "Übernommen",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ExecuteAllCsvToBin()
        {
            if (_binFlash == null || _maps.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Alle {_maps.Count} CSV-Maps in die BIN übernehmen?\n\n" +
                "Maps ohne gültige BIN-Adresse werden übersprungen.\n" +
                "Die BIN-Datei wird im Speicher verändert.",
                "Alle CSV → BIN – Rückfrage",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            int ok = 0, skip = 0;
            foreach (var map in _maps)
            {
                if (_binFlash.WriteMapValues(map)) ok++;
                else                               skip++;
            }

            if (_currentMap != null) RenderBinMap(_currentMap, highlightWritten: true);
            MessageBox.Show(
                $"{ok} Map(s) übertragen, {skip} übersprungen (keine Adresse).\n" +
                "Vergiss nicht, die BIN zu speichern (Datei → BIN speichern).",
                "Alle CSV → BIN",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ExecuteAllBinToCsv()
        {
            if (_binFlash == null || _maps.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Alle {_maps.Count} CSV-Maps mit BIN-Werten überschreiben?\n\n" +
                "Maps ohne gültige BIN-Adresse werden übersprungen.\n" +
                "Der Vorgang kann per Rückgängig (Strg+Z) rückgängig gemacht werden.",
                "Alle BIN → CSV – Rückfrage",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            var snap = new MapSnapshotUndoAction(_maps);
            int ok = 0, skip = 0;
            foreach (var map in _maps)
            {
                if (_binFlash.ReadMapValuesIntoMap(map)) ok++;
                else                                     skip++;
            }
            snap.CaptureAfterState();
            _undo.Push(snap);

            if (_currentMap != null) { RenderMap(_currentMap); RenderBinMap(_currentMap); }
            UpdatePowerEstimate();
            MessageBox.Show(
                $"{ok} Map(s) übernommen, {skip} übersprungen (keine Adresse).\n" +
                "Vergiss nicht, die CSVs zu speichern (Datei → Speichern).",
                "Alle BIN → CSV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

            bool canUseBin = _binFlash != null &&
                             _binFlash.ReadMapValues(_currentMap) != null;

            DialogResult choice;
            if (canUseBin)
            {
                choice = MessageBox.Show(
                    $"Map '{_currentMap.Name}' zurücksetzen auf:\n\n" +
                    "  [Ja]           = CSV-Originalwerte\n" +
                    "  [Nein]         = BIN-Werte\n" +
                    "  [Abbrechen]    = Abbrechen",
                    "Zurücksetzen – Quelle wählen",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
            }
            else
            {
                choice = MessageBox.Show(
                    $"Map '{_currentMap.Name}' auf CSV-Originalwerte zurücksetzen?",
                    "Bestätigung",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes
                    ? DialogResult.Yes : DialogResult.Cancel;
            }

            if (choice == DialogResult.Cancel) return;

            var snapshotAction = new MapSnapshotUndoAction(new[] { _currentMap });

            if (choice == DialogResult.Yes)
            {
                _currentMap.ResetAll();
            }
            else // DialogResult.No → BIN-Werte
            {
                if (!_binFlash.ReadMapValuesIntoMap(_currentMap))
                {
                    MessageBox.Show(
                        "BIN-Werte konnten nicht gelesen werden.",
                        "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            snapshotAction.CaptureAfterState();
            _undo.Push(snapshotAction);
            RenderMap(_currentMap);
            if (_binFlash != null) RenderBinMap(_currentMap);
            UpdatePowerEstimate();
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
            if (_binFlash != null) RenderBinMap(_currentMap);
            UpdatePowerEstimate();
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        // -----------------------------------------------------------------------
        // Leistungsschätzung
        // -----------------------------------------------------------------------

        private void UpdatePowerEstimate()
        {
            if (_maps.Count == 0) return;
            var result = PowerEstimator.Calculate(_maps);
            _lblPower.Text      = result.FormatSummary();
            _lblPower.ForeColor = result.HasData
                ? System.Drawing.Color.FromArgb(80, 200, 80)
                : System.Drawing.Color.DimGray;
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

            if (_currentMap == null) return;

            if (show3D)
            {
                _panel3D.SetMap(_currentMap);
                _panel3D.SetOriginalView(_showingOriginal);
                _panel3D.CellSelected -= Panel3D_CellSelected; // doppeltes Registrieren verhindern
                _panel3D.CellSelected += Panel3D_CellSelected;
                _panel3D.KeyDown      -= Panel3D_KeyDown;
                _panel3D.KeyDown      += Panel3D_KeyDown;
                _panel3D.KeyUp        -= Panel3D_KeyUp;
                _panel3D.KeyUp        += Panel3D_KeyUp;
                _panel3D.Focus();
            }
            else
            {
                // Zurück zur Tabelle: DGV mit aktuellem Zustand füllen
                RenderMap(_currentMap);
            }
        }

        private void Panel3D_CellSelected(int row, int col)
        {
            // Zelle selektiert → Panel bekommt Fokus damit +/- funktioniert
            _panel3D.Focus();
        }

        private void Panel3D_KeyDown(object sender, KeyEventArgs e)
        {
            if (_show3D && _currentMap != null && !_showingOriginal &&
                _panel3D.SelectedRow >= 0 && _panel3D.SelectedCol >= 0)
            {
                bool isPlus  = e.KeyCode == Keys.Add      || e.KeyCode == Keys.Oemplus;
                bool isMinus = e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus;
                if (isPlus || isMinus)
                {
                    StepCell(isPlus ? +1 : -1);
                    e.Handled = true;
                    UpdatePowerEstimate();
                }
            }
        }

        private void Panel3D_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus ||
                e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
                CommitStep();
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

