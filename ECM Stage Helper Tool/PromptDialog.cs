using System.Drawing;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Einfacher Eingabedialog (Texteingabe mit OK/Abbrechen).
    /// </summary>
    public static class PromptDialog
    {
        /// <summary>
        /// Zeigt einen modalen Eingabedialog und gibt den eingegebenen Text zurück.
        /// Gibt <c>null</c> zurück, wenn der Benutzer abbricht.
        /// </summary>
        public static string Show(string message, string title)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Width = 420;
                form.Height = 150;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                var label = new Label { Left = 10, Top = 10, Text = message, AutoSize = true };
                var textBox = new TextBox { Left = 10, Top = 36, Width = 380 };
                var btnOk = new Button { Text = "OK", Left = 220, Width = 80, Top = 68, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Abbrechen", Left = 310, Width = 80, Top = 68, DialogResult = DialogResult.Cancel };

                btnOk.Click += (s, e) => form.Close();

                form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
            }
        }

        /// <summary>
        /// Zeigt einen modalen Eingabedialog mit optionaler Checkbox.
        /// Gibt (text, checked) zurück. text ist null bei Abbruch.
        /// </summary>
        public static (string text, bool optionChecked) ShowWithOption(string message, string title, string optionLabel)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Width = 440;
                form.Height = 180;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                var label = new Label { Left = 10, Top = 10, Text = message, AutoSize = true };
                var textBox = new TextBox { Left = 10, Top = 36, Width = 400 };
                var chk = new CheckBox { Left = 10, Top = 64, Text = optionLabel, AutoSize = true };
                var btnOk = new Button { Text = "OK", Left = 240, Width = 80, Top = 100, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Abbrechen", Left = 330, Width = 80, Top = 100, DialogResult = DialogResult.Cancel };

                btnOk.Click += (s, e) => form.Close();

                form.Controls.AddRange(new Control[] { label, textBox, chk, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog() == DialogResult.OK)
                    return (textBox.Text.Trim(), chk.Checked);
                return (null, false);
            }
        }
    }
}
