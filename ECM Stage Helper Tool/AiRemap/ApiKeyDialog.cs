using System;
using System.IO;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// Dialog zur Eingabe des Anthropic API-Keys für Claude Sonnet.
    /// API-Key unter console.anthropic.com erstellen.
    /// Der Key wird in aiconfig.json neben der EXE gespeichert.
    /// </summary>
    public partial class ApiKeyDialog : Form
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aiconfig.json");

        public string ApiKey { get; private set; }

        public ApiKeyDialog()
        {
            InitializeComponent();
            string saved = LoadSavedKey();
            if (!string.IsNullOrEmpty(saved))
                _txtKey.Text = saved;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string key = _txtKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Bitte einen Anthropic API-Key eingeben.", "API-Key fehlt",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ApiKey = key;
            SaveKey(key);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static string LoadSavedKey()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    int i = json.IndexOf("\"anthropic_key\"", StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        int q = json.IndexOf('"', i + 15);
                        if (q >= 0)
                        {
                            int end = json.IndexOf('"', q + 1);
                            if (end >= 0)
                            {
                                string key = json.Substring(q + 1, end - q - 1);
                                if (!string.IsNullOrEmpty(key)) return key;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SaveKey(string key)
        {
            try
            {
                string json = "{\"anthropic_key\":\"" + key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
                File.WriteAllText(ConfigPath, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Lädt den gespeicherten API-Key. Gibt null zurück wenn keiner gespeichert ist.
        /// </summary>
        public static string GetSavedKey() => LoadSavedKey();
    }
}
