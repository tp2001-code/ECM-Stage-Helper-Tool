using System;
using System.Collections.Generic;
using System.IO;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Repräsentiert eine einzelne ECM-Kennfeld-Map mit X-/Y-Achsen, Werten und Originalkopie zum Zurücksetzen.
    /// </summary>
    public class MapModel
    {
        public string FilePath { get; private set; }
        public string Name => Path.GetFileName(FilePath);

        /// <summary>Größenangabe aus der CSV-Metazeile (z.B. "16x14"), kann null sein.</summary>
        public string SizeString { get; private set; }

        /// <summary>Kartenname aus der CSV-Metazeile (z.B. "MAP : Begrenzer Kraftstoffmenge"), kann null sein.</summary>
        public string MapName { get; private set; }

        /// <summary>Einheit aus der CSV-Metazeile (z.B. "mm3/Stk"), kann null sein.</summary>
        public string Unit { get; private set; }

        /// <summary>Achsenbeschriftung aus der CSV-Headerzeile (z.B. "RPM|mm3/Stk"), kann null sein.</summary>
        public string AxisLabel { get; private set; }

        /// <summary>
        /// True, wenn die CSV nur einen einzelnen Skalarwert ohne X/Y-Achsen enthielt
        /// (wird als 1x1-Zelle dargestellt).
        /// </summary>
        public bool IsScalar { get; private set; }

        /// <summary>X-Achse (Spaltenköpfe), Länge = Cols</summary>
        public double[] XAxis { get; set; }

        /// <summary>Y-Achse (Zeilenköpfe), Länge = Rows</summary>
        public double[] YAxis { get; set; }

        /// <summary>Zellenwerte [row, col]</summary>
        public double[,] Values { get; set; }

        // Originale für Rücksetzung
        private readonly double[] _originalX;
        private readonly double[] _originalY;
        private readonly double[,] _originalValues;

        /// <summary>Menge der geänderten Zellen (row, col)</summary>
        public HashSet<(int row, int col)> ModifiedCells { get; } = new HashSet<(int row, int col)>();

        public int Rows => YAxis.Length;
        public int Cols => XAxis.Length;

        public MapModel(string filePath, double[] xAxis, double[] yAxis, double[,] values, string sizeString = null, string axisLabel = null, string mapName = null, string unit = null, bool isScalar = false)
        {
            FilePath = filePath;
            SizeString = sizeString;
            AxisLabel = axisLabel;
            MapName = mapName;
            Unit = unit;
            IsScalar = isScalar;
            XAxis = (double[])xAxis.Clone();
            YAxis = (double[])yAxis.Clone();
            Values = (double[,])values.Clone();

            _originalX = (double[])xAxis.Clone();
            _originalY = (double[])yAxis.Clone();
            _originalValues = (double[,])values.Clone();
        }

        public void MarkCellModified(int row, int col) => ModifiedCells.Add((row, col));
        public void UnmarkCell(int row, int col) => ModifiedCells.Remove((row, col));
        public bool IsCellModified(int row, int col) => ModifiedCells.Contains((row, col));

        /// <summary>Setzt alle Achsen und Werte auf den Originalzustand zurück.</summary>
        public void ResetAll()
        {
            XAxis = (double[])_originalX.Clone();
            YAxis = (double[])_originalY.Clone();
            Values = (double[,])_originalValues.Clone();
            ModifiedCells.Clear();
        }

        /// <summary>Setzt eine einzelne Zelle auf den Originalwert zurück.</summary>
        public void ResetCell(int row, int col)
        {
            Values[row, col] = _originalValues[row, col];
            UnmarkCell(row, col);
        }

        /// <summary>
        /// Erklärt den aktuellen Zustand (Achsen + Werte) zur neuen Basis.
        /// Danach gelten alle Zellen als unverändert und IsCellModified gibt false zurück.
        /// </summary>
        public void AcceptCurrentAsOriginal()
        {
            Array.Copy(XAxis,   _originalX,      XAxis.Length);
            Array.Copy(YAxis,   _originalY,      YAxis.Length);
            Array.Copy(Values,  _originalValues, Values.Length);
            ModifiedCells.Clear();
        }

        /// <summary>Gibt den unveränderten Originalwert einer Zelle zurück (für Vorschau).</summary>
        public double GetOriginalValue(int row, int col) => _originalValues[row, col];

        /// <summary>Gibt den ursprünglichen X-Achsenwert (Spaltenkopf) zurück.</summary>
        public double GetOriginalX(int col) => _originalX[col];

        /// <summary>Gibt den ursprünglichen Y-Achsenwert (Zeilenkopf) zurück.</summary>
        public double GetOriginalY(int row) => _originalY[row];
    }
}
