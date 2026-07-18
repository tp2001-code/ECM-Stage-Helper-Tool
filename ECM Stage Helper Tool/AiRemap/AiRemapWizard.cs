using System;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// 3-Schritt-Wizard für das lokale Remap:
    ///   Schritt 1 – Analyse:  Lokale Berechnung von Max-Einspritzmenge, Max-Leistung, Max-Nm
    ///   Schritt 2 – Zielwert: Benutzer gibt Ziel-Einspritzmenge ein, Vorschau der erwarteten Leistung
    ///   Schritt 3 – Remap:   RemapEngine berechnet neue Werte, Änderungen werden temporär (rot) eingetragen
    /// </summary>
    public partial class AiRemapWizard : Form
    {
        private readonly List<MapModel> _maps;
        private AnalysisResult          _analysis;
        private RemapResult             _remapResult;

        public AiRemapWizard(List<MapModel> maps)
        {
            _maps = maps;
            InitializeComponent();
            ShowStep(1);
        }

        // -----------------------------------------------------------------------
        // Schritt-Navigation
        // -----------------------------------------------------------------------

        private void ShowStep(int step)
        {
            _panStep1.Visible = step == 1;
            _panStep2.Visible = step == 2;
            _panStep3.Visible = step == 3;
            _lblStep.Text = $"Schritt {step} von 3";
        }

        // -----------------------------------------------------------------------
        // Schritt 1 – Analyse
        // -----------------------------------------------------------------------

        private void BtnAnalyse_Click(object sender, EventArgs e)
        {
            SetBusy(true, "Analysiere Maps…");
            try
            {
                _analysis = AiRemapClient.AnalyseLocal(_maps);
                PopulateAnalysis();
                ShowStep(2);
            }
            catch (Exception ex)
            {
                AiRemapLogger.LogError("BtnAnalyse_Click", ex);
                MessageBox.Show(
                    $"Fehler bei der Analyse:\n{ex.Message}",
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        private void PopulateAnalysis()
        {
            _lblMaxFuel.Text  = $"{_analysis.MaxFuelQuantity:F2} {_analysis.FuelUnit}";
            _lblMaxPower.Text = $"{_analysis.MaxPowerKw:F1} kW  /  {_analysis.MaxPowerPs:F0} PS  (bei {_analysis.MaxPowerRpm:F0} RPM)";
            _lblMaxNm.Text    = $"{_analysis.MaxTorqueNm:F1} Nm  (bei {_analysis.MaxTorqueRpm:F0} RPM)";
            _lblSummary.Text  = _analysis.Summary;

            _lstMapsToChange.Items.Clear();
            foreach (var m in _analysis.MapsToChange)
                _lstMapsToChange.Items.Add(m);

            // Zielfeld vorbelegen mit aktuellem Wert + 25 %
            _numTargetFuel.Value = (decimal)Math.Round(_analysis.MaxFuelQuantity * 1.25, 1);

            // Limits mit aktuellen Werten vorbelegen
            if (_analysis.MaxBoostHpa > 0)
            {
                _lblCurrentBoost.Text    = $"(aktuell: {_analysis.MaxBoostHpa:F0})";
                _numLimitBoost.Value     = 0; // 0 = automatisch (Lambda-basiert)
            }
            if (_analysis.MaxRailPressureBar > 0)
            {
                _lblCurrentRail.Text     = $"(aktuell: {_analysis.MaxRailPressureBar:F0})";
                _numLimitRail.Value      = (decimal)Math.Min(Math.Round(_analysis.MaxRailPressureBar * 1.10, 0), 1850);
            }
            if (_analysis.MaxTorqueNm > 0)
            {
                _lblCurrentNm.Text       = $"(aktuell: {_analysis.MaxTorqueNm:F0})";
                _numLimitNm.Value        = (decimal)Math.Round(_analysis.MaxTorqueNm * 1.25, 0);
            }

            UpdatePowerPreview();
        }

        // -----------------------------------------------------------------------
        // Schritt 2 – Zielwert & Leistungsvorschau
        // -----------------------------------------------------------------------

        private void NumTargetFuel_ValueChanged(object sender, EventArgs e) => UpdatePowerPreview();

        private void UpdatePowerPreview()
        {
            if (_analysis == null) return;
            double target = (double)_numTargetFuel.Value;
            double ratio  = _analysis.MaxFuelQuantity > 0 ? target / _analysis.MaxFuelQuantity : 1.0;
            double newNm  = _analysis.MaxTorqueNm * ratio;

            // Durch Nm-Limit begrenzen (physisches Motorlimit)
            double nmLimit = (double)_numLimitNm.Value;
            if (nmLimit > 0 && newNm > nmLimit)
                newNm = nmLimit;

            // Leistung: Nm bei MaxTorqueRpm, PS separat bei MaxPowerRpm
            // Effektive Leistungssteigerung = MIN(ratio, nmLimit/OrigNm)
            double effectiveRatio = _analysis.MaxTorqueNm > 0 ? newNm / _analysis.MaxTorqueNm : 1.0;
            double newKw = _analysis.MaxPowerKw * effectiveRatio;
            _lblExpPower.Text = $"≈ {newKw:F1} kW  /  {newKw * 1.36:F0} PS";
            _lblExpNm.Text    = $"≈ {newNm:F1} Nm";
        }

        private void BtnConfirmTarget_Click(object sender, EventArgs e)
        {
            ShowStep(3);
            BuildConfirmationList();
        }

        // -----------------------------------------------------------------------
        // Schritt 3 – Remap ausführen
        // -----------------------------------------------------------------------

        private void BuildConfirmationList()
        {
            _lstConfirm.Items.Clear();
            foreach (var m in _analysis.MapsToChange)
                _lstConfirm.Items.Add("  [ ]  " + m);
        }

        private void MarkConfirmListItem(string fileName, string status)
        {
            for (int i = 0; i < _lstConfirm.Items.Count; i++)
            {
                string item = _lstConfirm.Items[i].ToString();
                string itemName = item.Contains("]") ? item.Substring(item.LastIndexOf(']') + 1).Trim() : item.Trim();
                if (string.Equals(itemName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    _lstConfirm.Items[i] = status + fileName;
                    _lstConfirm.Refresh();
                    return;
                }
            }
            _lstConfirm.Items.Add(status + fileName);
            _lstConfirm.Refresh();
        }

        private int _appliedCount = 0;

        private void OnMapDone(string fileName, MapRemap mr, bool success)
        {
            if (mr == null)
            {
                // Vor der Berechnung: "wird verarbeitet"
                SetBusy(true, $"[{_appliedCount + 1}] Berechne: {fileName}");
                MarkConfirmListItem(fileName, "  [...] ");
                Application.DoEvents();
                return;
            }

            // Map wurde von RemapEngine direkt modifiziert
            bool changed = success && _maps.Any(m =>
                string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase) &&
                m.ModifiedCells.Count > 0);

            if (changed) _appliedCount++;

            string status = success ? "  [OK] " : "  [--] ";
            MarkConfirmListItem(fileName, status);
            AiRemapLogger.LogInfo($"{status.Trim()}: {fileName} ({mr.ChangeDescription})");
            Application.DoEvents();
        }

        private void BtnExecuteRemap_Click(object sender, EventArgs e)
        {
            double target = (double)_numTargetFuel.Value;
            _appliedCount = 0;
            SetBusy(true, "Berechne Remap...");
            try
            {
                var limits = new RemapLimits
                {
                    TargetFuelMm3      = target,
                    MaxBoostHpa        = (double)_numLimitBoost.Value,
                    MaxRailPressureBar = (double)_numLimitRail.Value,
                    MaxTorqueNm        = (double)_numLimitNm.Value,
                    LambdaWindow       = (double)_numLambdaWindow.Value
                };
                AiRemapLogger.LogInfo($"Limits: Boost={limits.MaxBoostHpa:F0} hPa, Rail={limits.MaxRailPressureBar:F0} bar, Nm={limits.MaxTorqueNm:F0}");

                _remapResult = AiRemapClient.RemapLocal(_maps, target, _analysis, OnMapDone, limits);

                MessageBox.Show(
                    $"Remap abgeschlossen!\n\n" +
                    $"Maps bearbeitet: {_appliedCount}\n" +
                    $"Erwartete Leistung: {_remapResult.ExpectedPowerKw:F1} kW / {_remapResult.ExpectedPowerPs:F0} PS\n" +
                    $"Erwartetes Drehmoment: {_remapResult.ExpectedTorqueNm:F1} Nm\n\n" +
                    "Die Aenderungen sind rot markiert. Bitte pruefen und dann speichern oder in BIN schreiben.",
                    "Remap abgeschlossen",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                AiRemapLogger.LogError("BtnExecuteRemap_Click", ex);
                MessageBox.Show(
                    $"Fehler beim Remap:\n{ex.Message}",
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        // ApplyRemapToMaps entfernt - wird direkt in OnMapDone ausgefuehrt

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------



        private void SetBusy(bool busy, string message = "")
        {
            _progressBar.Visible = busy;
            _lblStatus.Text      = message;
            _btnAnalyse.Enabled          = !busy;
            _btnConfirmTarget.Enabled    = !busy;
            _btnExecuteRemap.Enabled     = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void BtnBack2_Click(object sender, EventArgs e) => ShowStep(1);
        private void BtnBack3_Click(object sender, EventArgs e) => ShowStep(2);
        private void BtnCancelWizard_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
