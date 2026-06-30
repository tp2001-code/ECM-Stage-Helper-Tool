using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Rendert ein Kennfeld als interaktives 3-D-Gitternetz (GDI+, Painter's Algorithm).
    /// Maus-Drag dreht die Ansicht (Yaw + Pitch), Scroll zoomt.
    /// Farbverlauf: Grün (Min) → Gelb → Rot (Max), wie WinOLS/ECM Titanium.
    /// </summary>
    internal sealed class Map3DPanel : Panel
    {
        // -----------------------------------------------------------------------
        // Kamera-Parameter
        // -----------------------------------------------------------------------
        private float _yaw   = -30f;   // Grad, Drehung links/rechts
        private float _pitch =  35f;   // Grad, Drehung oben/unten
        private float _zoom  = 1.0f;

        private Point  _lastMouse;
        private bool   _dragging;

        // -----------------------------------------------------------------------
        // Daten
        // -----------------------------------------------------------------------
        private MapModel _map;
        private double   _minVal, _maxVal;
        private bool     _showOriginal;

        // -----------------------------------------------------------------------
        // Stil
        // -----------------------------------------------------------------------
        private static readonly Font  _labelFont   = new Font("Segoe UI", 7f);
        private static readonly Font  _axisFont    = new Font("Segoe UI", 8f, FontStyle.Bold);
        private static readonly Pen   _gridPen     = new Pen(Color.FromArgb(180, 60, 60, 60), 1f);
        private static readonly Pen   _edgePen     = new Pen(Color.FromArgb(220, 30, 30, 30), 1.2f);
        private static readonly Brush _floorBrush  = new SolidBrush(Color.FromArgb(30, 100, 100, 100));
        private static readonly Color _bgTop       = Color.FromArgb(30, 35, 45);
        private static readonly Color _bgBottom    = Color.FromArgb(18, 22, 30);

        public Map3DPanel()
        {
            DoubleBuffered = true;
            BackColor      = _bgTop;

            MouseDown  += (s, e) => { if (e.Button == MouseButtons.Left)  { _dragging = true;  _lastMouse = e.Location; } };
            MouseUp    += (s, e) => { if (e.Button == MouseButtons.Left)  { _dragging = false; } };
            MouseMove  += OnMouseMove;
            MouseWheel += OnMouseWheel;
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        public void SetMap(MapModel map)
        {
            _map = map;
            if (map == null) { Invalidate(); return; }
            RecalcMinMax();
            Invalidate();
        }

        /// <summary>Schaltet zwischen Original- und geänderter Ansicht um (wie ESC in der Tabelle).</summary>
        public void SetOriginalView(bool showOriginal)
        {
            _showOriginal = showOriginal;
            if (_map != null) RecalcMinMax();
            Invalidate();
        }

        private void RecalcMinMax()
        {
            _minVal = double.MaxValue;
            _maxVal = double.MinValue;
            for (int r = 0; r < _map.Rows; r++)
                for (int c = 0; c < _map.Cols; c++)
                {
                    double v = GetVal(r, c);
                    if (v < _minVal) _minVal = v;
                    if (v > _maxVal) _maxVal = v;
                }
            if (Math.Abs(_maxVal - _minVal) < 1e-9) _maxVal = _minVal + 1;
        }

        // Liefert Original- oder aktuellen Wert je nach Anzeigemodus
        private double GetVal(int r, int c)
            => _showOriginal ? _map.GetOriginalValue(r, c) : _map.Values[r, c];

        public void ResetCamera()
        {
            _yaw   = -30f;
            _pitch =  35f;
            _zoom  =  1.0f;
            Invalidate();
        }

        // -----------------------------------------------------------------------
        // Maussteuerung
        // -----------------------------------------------------------------------

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            float dx = e.X - _lastMouse.X;
            float dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;
            _yaw   += dx * 0.4f;
            _pitch  = Clamp(_pitch + dy * 0.4f, -89f, 89f);
            Invalidate();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            _zoom = Clamp(_zoom + e.Delta / 1200f, 0.3f, 3.0f);
            Invalidate();
        }

        // -----------------------------------------------------------------------
        // Paint
        // -----------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Verlaufshintergrund
            using (var bg = new LinearGradientBrush(
                ClientRectangle, _bgTop, _bgBottom, LinearGradientMode.Vertical))
                g.FillRectangle(bg, ClientRectangle);

            if (_map == null || _map.Rows < 2 || _map.Cols < 2)
            {
                DrawNoData(g);
                return;
            }

            int rows = _map.Rows;
            int cols = _map.Cols;

            // --- 3D-Koordinaten normalisieren ---
            // X: 0..1 über Spalten, Z: 0..1 über Zeilen, Y: 0..1 über Wertbereich
            float scaleX = 1f / (cols - 1);
            float scaleZ = 1f / (rows - 1);
            float scaleY = 1f / (float)(_maxVal - _minVal);

            // Aspect: X/Z-Ausdehnung 2 Einheiten, Y-Ausdehnung 1 Einheit → typisches WinOLS-Verhältnis
            const float spanXZ = 2f;
            const float spanY  = 1.2f;

            // Alle 3D-Punkte berechnen
            var pts3 = new Vector3[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    pts3[r, c] = new Vector3(
                        (c * scaleX - 0.5f) * spanXZ,
                        (float)((GetVal(r, c) - _minVal) * scaleY) * spanY,
                        (r * scaleZ - 0.5f) * spanXZ);

            // --- Projektion ---
            float yawRad   = DegToRad(_yaw);
            float pitchRad = DegToRad(_pitch);
            float cy = (float)Math.Cos(yawRad),   sy = (float)Math.Sin(yawRad);
            float cp = (float)Math.Cos(pitchRad), sp = (float)Math.Sin(pitchRad);

            float fov    = 900f * _zoom;
            float cx2    = ClientSize.Width  / 2f;
            float cy2    = ClientSize.Height / 2f;
            float camDist = 4.5f;

            // Projektion: Yaw um Y-Achse, dann Pitch um X-Achse
            var proj = new PointF[rows, cols];
            var depth = new float[rows, cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var p = pts3[r, c];
                    // Yaw
                    float x1 =  p.X * cy + p.Z * sy;
                    float z1 = -p.X * sy + p.Z * cy;
                    float y1 =  p.Y;
                    // Pitch
                    float y2 =  y1 * cp - z1 * sp;
                    float z2 =  y1 * sp + z1 * cp;

                    float zz   = z2 + camDist;
                    float scale = fov / Math.Max(zz, 0.1f);

                    proj[r, c]  = new PointF(cx2 + x1 * scale, cy2 - y2 * scale);
                    depth[r, c] = z2;
                }

            // --- Painter's Algorithm: Polygone nach Tiefe sortieren ---
            var faces = new List<Face>();
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    float avgDepth = (depth[r, c] + depth[r, c + 1] + depth[r + 1, c] + depth[r + 1, c + 1]) / 4f;
                    double avgVal  = (GetVal(r, c) + GetVal(r, c + 1) + GetVal(r + 1, c) + GetVal(r + 1, c + 1)) / 4.0;
                    faces.Add(new Face
                    {
                        R = r, C = c,
                        Depth  = avgDepth,
                        Color  = ValueToColor(avgVal)
                    });
                }
            faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));  // weiter weg zuerst

            // --- Polygone zeichnen ---
            foreach (var f in faces)
            {
                int r = f.R, c = f.C;
                var poly = new PointF[]
                {
                    proj[r,   c],
                    proj[r,   c+1],
                    proj[r+1, c+1],
                    proj[r+1, c]
                };

                using (var br = new SolidBrush(f.Color))
                    g.FillPolygon(br, poly);

                g.DrawPolygon(_edgePen, poly);
            }

            // --- Bodengitter (flache Z=0-Ebene) ---
            DrawFloor(g, proj, rows, cols);

            // --- Achsenbeschriftungen ---
            DrawAxisLabels(g, proj, rows, cols);

            // --- Farbskala rechts ---
            DrawColorScale(g);

            // --- Achsentitel ---
            DrawAxisTitles(g);

            // --- Original-Hinweis ---
            if (_showOriginal)
            {
                string hint = "► ORIGINALWERTE";
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (var br = new SolidBrush(Color.FromArgb(210, 200, 60, 60)))
                {
                    var sz = g.MeasureString(hint, f);
                    g.DrawString(hint, f, br, (ClientSize.Width - sz.Width) / 2f, 6f);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Bodengitter
        // -----------------------------------------------------------------------

        private void DrawFloor(Graphics g, PointF[,] proj, int rows, int cols)
        {
            using (var pen = new Pen(Color.FromArgb(60, 140, 140, 170), 0.8f))
            {
                // untere Kante entlang X-Achse (unterste Zeile)
                for (int c = 0; c < cols - 1; c++)
                    g.DrawLine(pen, proj[rows - 1, c], proj[rows - 1, c + 1]);

                // linke Kante entlang Z-Achse (erste Spalte)
                for (int r = 0; r < rows - 1; r++)
                    g.DrawLine(pen, proj[r, 0], proj[r + 1, 0]);
            }
        }

        // -----------------------------------------------------------------------
        // Achsenbeschriftungen
        // -----------------------------------------------------------------------

        private void DrawAxisLabels(Graphics g, PointF[,] proj, int rows, int cols)
        {
            // X-Achse (unterste Zeile, alle Spalten)
            int step = Math.Max(1, cols / 10);
            for (int c = 0; c < cols; c += step)
            {
                var pt = proj[rows - 1, c];
                string lbl = _map.XAxis[c].ToString("G4");
                var sz = g.MeasureString(lbl, _labelFont);
                g.DrawString(lbl, _labelFont, Brushes.LightSteelBlue,
                    pt.X - sz.Width / 2, pt.Y + 3);
            }

            // Y-Achse (erste Spalte, alle Zeilen)
            step = Math.Max(1, rows / 8);
            for (int r = 0; r < rows; r += step)
            {
                var pt = proj[r, 0];
                string lbl = _map.YAxis[r].ToString("G4");
                var sz = g.MeasureString(lbl, _labelFont);
                g.DrawString(lbl, _labelFont, Brushes.PeachPuff,
                    pt.X - sz.Width - 4, pt.Y - sz.Height / 2);
            }
        }

        // -----------------------------------------------------------------------
        // Achsentitel (unten)
        // -----------------------------------------------------------------------

        private void DrawAxisTitles(Graphics g)
        {
            if (_map?.AxisLabel == null) return;
            var parts = _map.AxisLabel.Split('|');
            string xTitle = parts.Length > 0 ? parts[0].Trim() : "";
            string yTitle = parts.Length > 1 ? parts[1].Trim() : "";

            if (!string.IsNullOrEmpty(xTitle))
                g.DrawString($"X: {xTitle}", _axisFont, Brushes.LightSteelBlue, 8, ClientSize.Height - 38);
            if (!string.IsNullOrEmpty(yTitle))
                g.DrawString($"Y: {yTitle}", _axisFont, Brushes.PeachPuff, 8, ClientSize.Height - 22);

            // Min/Max-Info
            string range = $"Min: {_minVal:F2}  Max: {_maxVal:F2}";
            var sz = g.MeasureString(range, _labelFont);
            g.DrawString(range, _labelFont, Brushes.Silver,
                ClientSize.Width - sz.Width - 14, ClientSize.Height - 22);
        }

        // -----------------------------------------------------------------------
        // Farbskala
        // -----------------------------------------------------------------------

        private void DrawColorScale(Graphics g)
        {
            int barH = Math.Min(ClientSize.Height - 80, 200);
            int barW = 14;
            int x = ClientSize.Width - 36;
            int y = (ClientSize.Height - barH) / 2;

            var rect = new Rectangle(x, y, barW, barH);
            using (var lgb = new LinearGradientBrush(
                new Point(x, y + barH), new Point(x, y),
                ValueToColor(_minVal), ValueToColor(_maxVal)))
                g.FillRectangle(lgb, rect);

            g.DrawRectangle(_gridPen, rect);

            // Beschriftung
            g.DrawString(_maxVal.ToString("F1"), _labelFont, Brushes.White, x - 2, y - 14);
            g.DrawString(_minVal.ToString("F1"), _labelFont, Brushes.White, x - 2, y + barH + 3);
        }

        // -----------------------------------------------------------------------
        // Kein Datenlabel
        // -----------------------------------------------------------------------

        private void DrawNoData(Graphics g)
        {
            string msg = _map == null ? "Keine Map ausgewählt" : "Map hat zu wenig Punkte für 3D-Ansicht";
            using (var f = new Font("Segoe UI", 12f))
            {
                var sz = g.MeasureString(msg, f);
                g.DrawString(msg, f, Brushes.Gray,
                    (ClientSize.Width  - sz.Width)  / 2,
                    (ClientSize.Height - sz.Height) / 2);
            }
        }

        // -----------------------------------------------------------------------
        // Farb-Mapping: Grün → Gelb → Rot (HSV-ähnlich, schnell)
        // -----------------------------------------------------------------------

        private Color ValueToColor(double value)
        {
            float t = (float)((value - _minVal) / (_maxVal - _minVal));
            t = Math.Max(0f, Math.Min(1f, t));

            // 0 = reines Grün (120°), 0.5 = Gelb (60°), 1 = reines Rot (0°)
            float hue = (1f - t) * 120f;          // 120→0
            float sat = 0.85f;
            float val = 0.9f + t * 0.1f;

            return HsvToRgb(hue, sat, val);
        }

        private static Color HsvToRgb(float h, float s, float v)
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
            return Color.FromArgb(
                (int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        private static float DegToRad(float deg) => deg * (float)Math.PI / 180f;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // -----------------------------------------------------------------------
        // Typen
        // -----------------------------------------------------------------------

        private struct Vector3
        {
            public float X, Y, Z;
            public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        private struct Face
        {
            public int   R, C;
            public float Depth;
            public Color Color;
        }
    }
}
