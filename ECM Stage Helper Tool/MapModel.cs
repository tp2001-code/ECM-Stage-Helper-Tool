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

        public MapModel(string filePath, double[] xAxis, double[] yAxis, double[,] values)
        {
            FilePath = filePath;
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
    }
}
