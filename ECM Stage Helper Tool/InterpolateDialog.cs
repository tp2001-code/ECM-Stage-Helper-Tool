using System;
using System.Globalization;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Dialog zur prozentualen Interpolation: Der Benutzer gibt für jede der vier
    /// Ecken der Auswahl einen Prozentwert ein (+10 = +10 %, -5 = -5 %).
    /// Alle Zellen dazwischen werden bilinear interpoliert.
    /// </summary>
    internal static class InterpolateDialog
    {
        /// <summary>
        /// Zeigt den Dialog und gibt die vier Eckwerte zurück.
        /// Gibt <c>false</c> zurück, wenn der Benutzer abbricht oder eine ungültige Eingabe macht.
        /// </summary>
        public static bool Show(Form owner,
            out double topLeft, out double topRight,
            out double bottomLeft, out double bottomRight)
        {
            topLeft = topRight = bottomLeft = bottomRight = 0;

            using (var form = new Form())
            {
                form.Text = "Prozentuale Interpolation";
                form.Width = 390;
                form.Height = 215;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                var hint = new Label
                {
                    Text = "Prozentuale Anpassung je Ecke (+10 = +10 %, -5 = -5 %):",
                    Left = 12, Top = 12, Width = 360, Height = 18
                };

                // Zeile 1: oben links / oben rechts
                var lblTL = MakeLabel("oben links:",  12, 46);
                var tbTL  = MakeBox(110, 44);
                var lblTR = MakeLabel("oben rechts:", 210, 46);
                var tbTR  = MakeBox(308, 44);

                // Zeile 2: unten links / unten rechts
                var lblBL = MakeLabel("unten links:",  12, 82);
                var tbBL  = MakeBox(110, 80);
                var lblBR = MakeLabel("unten rechts:", 210, 82);
                var tbBR  = MakeBox(308, 80);

                var btnOk     = new Button { Text = "OK",         Left = 210, Top = 140, Width = 75, Height = 26, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Abbrechen",  Left = 295, Top = 140, Width = 82, Height = 26, DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[]
                    { hint, lblTL, tbTL, lblTR, tbTR, lblBL, tbBL, lblBR, tbBR, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog(owner) != DialogResult.OK) return false;

                if (!TryParse(tbTL.Text, out topLeft)    ||
                    !TryParse(tbTR.Text, out topRight)   ||
                    !TryParse(tbBL.Text, out bottomLeft) ||
                    !TryParse(tbBR.Text, out bottomRight))
                {
                    MessageBox.Show("Ungültige Eingabe. Bitte Zahlen eingeben.", "Fehler",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                return true;
            }
        }

        private static Label MakeLabel(string text, int left, int top) =>
            new Label
            {
                Text = text, Left = left, Top = top,
                Width = 95, Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight
            };

        private static TextBox MakeBox(int left, int top) =>
            new TextBox { Left = left, Top = top, Width = 65, Text = "0" };

        private static bool TryParse(string s, out double val)
        {
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val)) return true;
            if (double.TryParse(s, NumberStyles.Float, new CultureInfo("de-DE"), out val)) return true;
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out val);
        }
    }
}
