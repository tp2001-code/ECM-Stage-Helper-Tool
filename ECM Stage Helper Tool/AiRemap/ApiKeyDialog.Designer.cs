namespace ECM_Stage_Helper_Tool.AiRemap
{
    partial class ApiKeyDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._lblInfo  = new System.Windows.Forms.Label();
            this._lblKey   = new System.Windows.Forms.Label();
            this._txtKey   = new System.Windows.Forms.TextBox();
            this._btnOk    = new System.Windows.Forms.Button();
            this._btnCancel = new System.Windows.Forms.Button();
            this._lnkGet   = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();

            // _lblInfo
            this._lblInfo.Location  = new System.Drawing.Point(12, 12);
            this._lblInfo.Size      = new System.Drawing.Size(460, 40);
            this._lblInfo.Text      = "Gib deinen Anthropic API-Key ein. Der Key wird lokal in aiconfig.json gespeichert und nur zur Kommunikation mit der Claude-API verwendet.";

            // _lnkGet
            this._lnkGet.Location  = new System.Drawing.Point(12, 54);
            this._lnkGet.Size      = new System.Drawing.Size(460, 20);
            this._lnkGet.Text      = "API-Key erstellen: console.anthropic.com/account/keys";
            this._lnkGet.LinkClicked += (s, e) =>
                System.Diagnostics.Process.Start("https://console.anthropic.com/account/keys");

            // _lblKey
            this._lblKey.Location  = new System.Drawing.Point(12, 86);
            this._lblKey.Size      = new System.Drawing.Size(80, 22);
            this._lblKey.Text      = "API-Key:";
            this._lblKey.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // _txtKey
            this._txtKey.Location    = new System.Drawing.Point(96, 84);
            this._txtKey.Size        = new System.Drawing.Size(376, 22);
            this._txtKey.PasswordChar = '●';
            this._txtKey.Font        = new System.Drawing.Font("Consolas", 9F);

            // _btnOk
            this._btnOk.Location     = new System.Drawing.Point(296, 124);
            this._btnOk.Size         = new System.Drawing.Size(86, 28);
            this._btnOk.Text         = "OK";
            this._btnOk.DialogResult = System.Windows.Forms.DialogResult.None;
            this._btnOk.Click       += new System.EventHandler(this.BtnOk_Click);

            // _btnCancel
            this._btnCancel.Location     = new System.Drawing.Point(386, 124);
            this._btnCancel.Size         = new System.Drawing.Size(86, 28);
            this._btnCancel.Text         = "Abbrechen";
            this._btnCancel.DialogResult = System.Windows.Forms.DialogResult.None;
            this._btnCancel.Click       += new System.EventHandler(this.BtnCancel_Click);

            // Form
            this.AcceptButton    = this._btnOk;
            this.CancelButton    = this._btnCancel;
            this.ClientSize      = new System.Drawing.Size(484, 164);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.StartPosition   = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text            = "Anthropic API-Key";
            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                this._lblInfo, this._lnkGet, this._lblKey,
                this._txtKey, this._btnOk, this._btnCancel });
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Label     _lblInfo;
        private System.Windows.Forms.LinkLabel _lnkGet;
        private System.Windows.Forms.Label     _lblKey;
        private System.Windows.Forms.TextBox   _txtKey;
        private System.Windows.Forms.Button    _btnOk;
        private System.Windows.Forms.Button    _btnCancel;
    }
}
