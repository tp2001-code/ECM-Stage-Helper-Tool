namespace ECM_Stage_Helper_Tool
{
    partial class Form1
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Vom Windows Form-Designer generierter Code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._menuStrip = new System.Windows.Forms.MenuStrip();
            this._mDatei = new System.Windows.Forms.ToolStripMenuItem();
            this._miOpen = new System.Windows.Forms.ToolStripMenuItem();
            this._miSave = new System.Windows.Forms.ToolStripMenuItem();
            this._miSep1 = new System.Windows.Forms.ToolStripSeparator();
            this._miExit = new System.Windows.Forms.ToolStripMenuItem();
            this._mBearbeiten = new System.Windows.Forms.ToolStripMenuItem();
            this._miUndo = new System.Windows.Forms.ToolStripMenuItem();
            this._miRedo = new System.Windows.Forms.ToolStripMenuItem();
            this._miSep2 = new System.Windows.Forms.ToolStripSeparator();
            this._miInterpolate = new System.Windows.Forms.ToolStripMenuItem();
            this._miCopySel = new System.Windows.Forms.ToolStripMenuItem();
            this._miPasteSel = new System.Windows.Forms.ToolStripMenuItem();
            this._mAnsicht = new System.Windows.Forms.ToolStripMenuItem();
            this._miToggle = new System.Windows.Forms.ToolStripMenuItem();
            this._lbMaps = new System.Windows.Forms.ListBox();
            this._ctxMaps = new System.Windows.Forms.ContextMenuStrip(this.components);
            this._cmiRefresh = new System.Windows.Forms.ToolStripMenuItem();
            this._cmiSepMaps = new System.Windows.Forms.ToolStripSeparator();
            this._cmiSaveAsMod = new System.Windows.Forms.ToolStripMenuItem();
            this._dgv = new System.Windows.Forms.DataGridView();
            this._ctxDgv = new System.Windows.Forms.ContextMenuStrip(this.components);
            this._cmiInterpolate = new System.Windows.Forms.ToolStripMenuItem();
            this._cmiSepCtx = new System.Windows.Forms.ToolStripSeparator();
            this._cmiCopySel = new System.Windows.Forms.ToolStripMenuItem();
            this._cmiPasteSel = new System.Windows.Forms.ToolStripMenuItem();
            this._panel3D = new ECM_Stage_Helper_Tool.Map3DPanel();
            this._btnView2D = new System.Windows.Forms.Button();
            this._btnView3D = new System.Windows.Forms.Button();
            this._btnResetMap = new System.Windows.Forms.Button();
            this._btnResetCell = new System.Windows.Forms.Button();
            this._lblInfo = new System.Windows.Forms.Label();
            this._lblMapName = new System.Windows.Forms.Label();
            this._lblUnit = new System.Windows.Forms.Label();
            this._toolTip = new System.Windows.Forms.ToolTip(this.components);
            this._menuStrip.SuspendLayout();
            this._ctxMaps.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgv)).BeginInit();
            this._ctxDgv.SuspendLayout();
            this.SuspendLayout();
            // 
            // _menuStrip
            // 
            this._menuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._mDatei,
            this._mBearbeiten,
            this._mAnsicht});
            this._menuStrip.Location = new System.Drawing.Point(0, 0);
            this._menuStrip.Name = "_menuStrip";
            this._menuStrip.Padding = new System.Windows.Forms.Padding(5, 2, 0, 2);
            this._menuStrip.Size = new System.Drawing.Size(972, 24);
            this._menuStrip.TabIndex = 0;
            // 
            // _mDatei
            // 
            this._mDatei.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._miOpen,
            this._miSave,
            this._miSep1,
            this._miExit});
            this._mDatei.Name = "_mDatei";
            this._mDatei.Size = new System.Drawing.Size(46, 20);
            this._mDatei.Text = "&Datei";
            // 
            // _miOpen
            // 
            this._miOpen.Name = "_miOpen";
            this._miOpen.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this._miOpen.Size = new System.Drawing.Size(168, 22);
            this._miOpen.Text = "&Öffnen...";
            this._miOpen.Click += new System.EventHandler(this.MiOpen_Click);
            // 
            // _miSave
            // 
            this._miSave.Name = "_miSave";
            this._miSave.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this._miSave.Size = new System.Drawing.Size(168, 22);
            this._miSave.Text = "&Speichern";
            this._miSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // _miSep1
            // 
            this._miSep1.Name = "_miSep1";
            this._miSep1.Size = new System.Drawing.Size(165, 6);
            // 
            // _miExit
            // 
            this._miExit.Name = "_miExit";
            this._miExit.Size = new System.Drawing.Size(168, 22);
            this._miExit.Text = "&Beenden";
            this._miExit.Click += new System.EventHandler(this.MiExit_Click);
            // 
            // _mBearbeiten
            // 
            this._mBearbeiten.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._miUndo,
            this._miRedo,
            this._miSep2,
            this._miInterpolate,
            this._miCopySel,
            this._miPasteSel});
            this._mBearbeiten.Name = "_mBearbeiten";
            this._mBearbeiten.Size = new System.Drawing.Size(75, 20);
            this._mBearbeiten.Text = "&Bearbeiten";
            this._mBearbeiten.DropDownOpening += new System.EventHandler(this.MBearbeiten_DropDownOpening);
            // 
            // _miUndo
            // 
            this._miUndo.Name = "_miUndo";
            this._miUndo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z)));
            this._miUndo.Size = new System.Drawing.Size(205, 22);
            this._miUndo.Text = "&Rückgängig";
            this._miUndo.Click += new System.EventHandler(this.MiUndo_Click);
            // 
            // _miRedo
            // 
            this._miRedo.Name = "_miRedo";
            this._miRedo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y)));
            this._miRedo.Size = new System.Drawing.Size(205, 22);
            this._miRedo.Text = "&Wiederherstellen";
            this._miRedo.Click += new System.EventHandler(this.MiRedo_Click);
            // 
            // _miSep2
            // 
            this._miSep2.Name = "_miSep2";
            this._miSep2.Size = new System.Drawing.Size(202, 6);
            // 
            // _miInterpolate
            // 
            this._miInterpolate.Name = "_miInterpolate";
            this._miInterpolate.Size = new System.Drawing.Size(205, 22);
            this._miInterpolate.Text = "&Interpolieren...";
            this._miInterpolate.Click += new System.EventHandler(this.MiInterpolate_Click);
            // 
            // _miCopySel
            // 
            this._miCopySel.Name = "_miCopySel";
            this._miCopySel.ShortcutKeyDisplayString = "Strg+C";
            this._miCopySel.Size = new System.Drawing.Size(205, 22);
            this._miCopySel.Text = "&Kopieren";
            this._miCopySel.Click += new System.EventHandler(this.MiCopySel_Click);
            // 
            // _miPasteSel
            // 
            this._miPasteSel.Name = "_miPasteSel";
            this._miPasteSel.ShortcutKeyDisplayString = "Strg+V";
            this._miPasteSel.Size = new System.Drawing.Size(205, 22);
            this._miPasteSel.Text = "&Einfügen";
            this._miPasteSel.Click += new System.EventHandler(this.MiPasteSel_Click);
            // 
            // _mAnsicht
            // 
            this._mAnsicht.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._miToggle});
            this._mAnsicht.Name = "_mAnsicht";
            this._mAnsicht.Size = new System.Drawing.Size(59, 20);
            this._mAnsicht.Text = "&Ansicht";
            // 
            // _miToggle
            // 
            this._miToggle.Name = "_miToggle";
            this._miToggle.ShortcutKeyDisplayString = "ESC";
            this._miToggle.Size = new System.Drawing.Size(164, 22);
            this._miToggle.Text = "&Umschalten";
            this._miToggle.Click += new System.EventHandler(this.MiToggle_Click);
            // 
            // _lbMaps
            // 
            this._lbMaps.ContextMenuStrip = this._ctxMaps;
            this._lbMaps.Dock = System.Windows.Forms.DockStyle.Left;
            this._lbMaps.Font = new System.Drawing.Font("Consolas", 9F);
            this._lbMaps.FormattingEnabled = true;
            this._lbMaps.ItemHeight = 14;
            this._lbMaps.Location = new System.Drawing.Point(0, 24);
            this._lbMaps.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._lbMaps.Name = "_lbMaps";
            this._lbMaps.Size = new System.Drawing.Size(198, 489);
            this._lbMaps.TabIndex = 1;
            this._lbMaps.SelectedIndexChanged += new System.EventHandler(this.LbMaps_SelectedIndexChanged);
            // 
            // _ctxMaps
            // 
            this._ctxMaps.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._cmiRefresh,
            this._cmiSepMaps,
            this._cmiSaveAsMod});
            this._ctxMaps.Name = "_ctxMaps";
            this._ctxMaps.Size = new System.Drawing.Size(178, 54);
            this._ctxMaps.Opening += new System.ComponentModel.CancelEventHandler(this.CtxMaps_Opening);
            // 
            // _cmiRefresh
            // 
            this._cmiRefresh.Name = "_cmiRefresh";
            this._cmiRefresh.Size = new System.Drawing.Size(177, 22);
            this._cmiRefresh.Text = "&Aktualisieren";
            this._cmiRefresh.Click += new System.EventHandler(this.CmiRefresh_Click);
            // 
            // _cmiSepMaps
            // 
            this._cmiSepMaps.Name = "_cmiSepMaps";
            this._cmiSepMaps.Size = new System.Drawing.Size(174, 6);
            // 
            // _cmiSaveAsMod
            // 
            this._cmiSaveAsMod.Name = "_cmiSaveAsMod";
            this._cmiSaveAsMod.Size = new System.Drawing.Size(177, 22);
            this._cmiSaveAsMod.Text = "&Als -mod speichern";
            this._cmiSaveAsMod.Click += new System.EventHandler(this.CmiSaveAsMod_Click);
            // 
            // _dgv
            // 
            this._dgv.AllowUserToAddRows = false;
            this._dgv.AllowUserToDeleteRows = false;
            this._dgv.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._dgv.ColumnHeadersHeight = 29;
            this._dgv.ContextMenuStrip = this._ctxDgv;
            this._dgv.EnableHeadersVisualStyles = false;
            this._dgv.Font = new System.Drawing.Font("Consolas", 9F);
            this._dgv.Location = new System.Drawing.Point(206, 29);
            this._dgv.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._dgv.Name = "_dgv";
            this._dgv.RowHeadersWidth = 80;
            this._dgv.Size = new System.Drawing.Size(754, 437);
            this._dgv.TabIndex = 2;
            this._dgv.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.Dgv_CellEndEdit);
            this._dgv.ColumnHeaderMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.Dgv_ColumnHeaderMouseClick);
            this._dgv.EditingControlShowing += new System.Windows.Forms.DataGridViewEditingControlShowingEventHandler(this.Dgv_EditingControlShowing);
            this._dgv.RowHeaderMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.Dgv_RowHeaderMouseClick);
            // 
            // _ctxDgv
            // 
            this._ctxDgv.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._ctxDgv.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._cmiInterpolate,
            this._cmiSepCtx,
            this._cmiCopySel,
            this._cmiPasteSel});
            this._ctxDgv.Name = "_ctxDgv";
            this._ctxDgv.Size = new System.Drawing.Size(166, 76);
            this._ctxDgv.Opening += new System.ComponentModel.CancelEventHandler(this.CtxDgv_Opening);
            // 
            // _cmiInterpolate
            // 
            this._cmiInterpolate.Name = "_cmiInterpolate";
            this._cmiInterpolate.Size = new System.Drawing.Size(165, 22);
            this._cmiInterpolate.Text = "&Interpolieren...";
            this._cmiInterpolate.Click += new System.EventHandler(this.MiInterpolate_Click);
            // 
            // _cmiSepCtx
            // 
            this._cmiSepCtx.Name = "_cmiSepCtx";
            this._cmiSepCtx.Size = new System.Drawing.Size(162, 6);
            // 
            // _cmiCopySel
            // 
            this._cmiCopySel.Name = "_cmiCopySel";
            this._cmiCopySel.ShortcutKeyDisplayString = "Strg+C";
            this._cmiCopySel.Size = new System.Drawing.Size(165, 22);
            this._cmiCopySel.Text = "&Kopieren";
            this._cmiCopySel.Click += new System.EventHandler(this.MiCopySel_Click);
            // 
            // _cmiPasteSel
            // 
            this._cmiPasteSel.Name = "_cmiPasteSel";
            this._cmiPasteSel.ShortcutKeyDisplayString = "Strg+V";
            this._cmiPasteSel.Size = new System.Drawing.Size(165, 22);
            this._cmiPasteSel.Text = "&Einfügen";
            this._cmiPasteSel.Click += new System.EventHandler(this.MiPasteSel_Click);
            // 
            // _panel3D
            // 
            this._panel3D.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._panel3D.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(35)))), ((int)(((byte)(45)))));
            this._panel3D.Cursor = System.Windows.Forms.Cursors.SizeAll;
            this._panel3D.Location = new System.Drawing.Point(206, 42);
            this._panel3D.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._panel3D.Name = "_panel3D";
            this._panel3D.Size = new System.Drawing.Size(754, 424);
            this._panel3D.TabIndex = 8;
            this._panel3D.Visible = false;
            // 
            // _btnView2D
            // 
            this._btnView2D.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._btnView2D.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(48)))), ((int)(((byte)(52)))));
            this._btnView2D.FlatAppearance.BorderColor = System.Drawing.Color.DodgerBlue;
            this._btnView2D.FlatAppearance.BorderSize = 2;
            this._btnView2D.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnView2D.Location = new System.Drawing.Point(241, 0);
            this._btnView2D.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._btnView2D.Name = "_btnView2D";
            this._btnView2D.Size = new System.Drawing.Size(28, 28);
            this._btnView2D.TabIndex = 6;
            this._btnView2D.UseVisualStyleBackColor = false;
            this._btnView2D.Click += new System.EventHandler(this.BtnView2D_Click);
            // 
            // _btnView3D
            // 
            this._btnView3D.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._btnView3D.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(48)))), ((int)(((byte)(52)))));
            this._btnView3D.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(63)))), ((int)(((byte)(65)))));
            this._btnView3D.FlatAppearance.BorderSize = 2;
            this._btnView3D.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnView3D.Location = new System.Drawing.Point(284, 0);
            this._btnView3D.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._btnView3D.Name = "_btnView3D";
            this._btnView3D.Size = new System.Drawing.Size(28, 28);
            this._btnView3D.TabIndex = 7;
            this._btnView3D.UseVisualStyleBackColor = false;
            this._btnView3D.Click += new System.EventHandler(this.BtnView3D_Click);
            // 
            // _btnResetMap
            // 
            this._btnResetMap.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this._btnResetMap.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.5F);
            this._btnResetMap.Location = new System.Drawing.Point(208, 468);
            this._btnResetMap.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._btnResetMap.Name = "_btnResetMap";
            this._btnResetMap.Size = new System.Drawing.Size(90, 38);
            this._btnResetMap.TabIndex = 5;
            this._btnResetMap.Text = "Map zurücksetzen";
            this._btnResetMap.Click += new System.EventHandler(this.BtnResetMap_Click);
            // 
            // _btnResetCell
            // 
            this._btnResetCell.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this._btnResetCell.Font = new System.Drawing.Font("Microsoft Sans Serif", 7F);
            this._btnResetCell.Location = new System.Drawing.Point(306, 468);
            this._btnResetCell.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this._btnResetCell.Name = "_btnResetCell";
            this._btnResetCell.Size = new System.Drawing.Size(104, 38);
            this._btnResetCell.TabIndex = 6;
            this._btnResetCell.Text = "Zelle zurücksetzen";
            this._btnResetCell.Click += new System.EventHandler(this.BtnResetCell_Click);
            // 
            // _lblInfo
            // 
            this._lblInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._lblInfo.ForeColor = System.Drawing.Color.DimGray;
            this._lblInfo.Location = new System.Drawing.Point(428, 482);
            this._lblInfo.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this._lblInfo.Name = "_lblInfo";
            this._lblInfo.Size = new System.Drawing.Size(514, 24);
            this._lblInfo.TabIndex = 9;
            this._lblInfo.Text = "ESC = Originalwerte  |  Ctrl+Z = Rückgängig  |  Ctrl+Y = Wiederherstellen  |  F3 " +
    "= Speichern als Kopie";
            // 
            // _lblMapName
            // 
            this._lblMapName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._lblMapName.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this._lblMapName.ForeColor = System.Drawing.Color.SteelBlue;
            this._lblMapName.Location = new System.Drawing.Point(208, 442);
            this._lblMapName.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this._lblMapName.Name = "_lblMapName";
            this._lblMapName.Size = new System.Drawing.Size(560, 22);
            this._lblMapName.TabIndex = 11;
            this._lblMapName.Text = "";
            // 
            // _lblUnit
            // 
            this._lblUnit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this._lblUnit.Font = new System.Drawing.Font("Consolas", 13F, System.Drawing.FontStyle.Bold);
            this._lblUnit.ForeColor = System.Drawing.Color.DarkOrange;
            this._lblUnit.Location = new System.Drawing.Point(208, 464);
            this._lblUnit.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this._lblUnit.Name = "_lblUnit";
            this._lblUnit.Size = new System.Drawing.Size(200, 22);
            this._lblUnit.TabIndex = 10;
            this._lblUnit.Text = "";
            // 
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(972, 513);
            this.Controls.Add(this._lblInfo);
            this.Controls.Add(this._lblUnit);
            this.Controls.Add(this._lblMapName);
            this.Controls.Add(this._btnResetCell);
            this.Controls.Add(this._btnResetMap);
            this.Controls.Add(this._btnView3D);
            this.Controls.Add(this._btnView2D);
            this.Controls.Add(this._panel3D);
            this.Controls.Add(this._dgv);
            this.Controls.Add(this._lbMaps);
            this.Controls.Add(this._menuStrip);
            this.MainMenuStrip = this._menuStrip;
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MinimumSize = new System.Drawing.Size(688, 438);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ECM Stage Helper – Kennfeld Remapping";
            this.Resize += new System.EventHandler(this.Form1_Resize);
            this._menuStrip.ResumeLayout(false);
            this._menuStrip.PerformLayout();
            this._ctxMaps.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._dgv)).EndInit();
            this._ctxDgv.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        // Controls (Designer verwaltet diese Felder)
        private System.Windows.Forms.MenuStrip       _menuStrip;
        private System.Windows.Forms.ToolStripMenuItem _mDatei;
        private System.Windows.Forms.ToolStripMenuItem _miOpen;
        private System.Windows.Forms.ToolStripMenuItem _miSave;
        private System.Windows.Forms.ToolStripSeparator _miSep1;
        private System.Windows.Forms.ToolStripMenuItem _miExit;
        private System.Windows.Forms.ToolStripMenuItem _mBearbeiten;
        private System.Windows.Forms.ToolStripMenuItem _miUndo;
        private System.Windows.Forms.ToolStripMenuItem _miRedo;
        private System.Windows.Forms.ToolStripMenuItem _mAnsicht;
        private System.Windows.Forms.ToolStripMenuItem _miToggle;
        private System.Windows.Forms.ListBox          _lbMaps;
        private System.Windows.Forms.DataGridView     _dgv;
        private ECM_Stage_Helper_Tool.Map3DPanel      _panel3D;
        private System.Windows.Forms.Button           _btnView2D;
        private System.Windows.Forms.Button           _btnView3D;
        private System.Windows.Forms.Button           _btnResetMap;
        private System.Windows.Forms.Button           _btnResetCell;
        private System.Windows.Forms.Label            _lblInfo;
        private System.Windows.Forms.Label            _lblMapName;
        private System.Windows.Forms.Label            _lblUnit;
        private System.Windows.Forms.ToolTip          _toolTip;
        private System.Windows.Forms.ContextMenuStrip   _ctxMaps;
        private System.Windows.Forms.ToolStripMenuItem  _cmiRefresh;
        private System.Windows.Forms.ToolStripSeparator _cmiSepMaps;
        private System.Windows.Forms.ToolStripMenuItem  _cmiSaveAsMod;
        private System.Windows.Forms.ContextMenuStrip   _ctxDgv;
        private System.Windows.Forms.ToolStripMenuItem  _cmiInterpolate;
        private System.Windows.Forms.ToolStripSeparator _cmiSepCtx;
        private System.Windows.Forms.ToolStripMenuItem  _cmiCopySel;
        private System.Windows.Forms.ToolStripMenuItem  _cmiPasteSel;
        private System.Windows.Forms.ToolStripSeparator _miSep2;
        private System.Windows.Forms.ToolStripMenuItem  _miInterpolate;
        private System.Windows.Forms.ToolStripMenuItem  _miCopySel;
        private System.Windows.Forms.ToolStripMenuItem  _miPasteSel;
    }
}

