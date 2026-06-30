using System;
using System.Collections.Generic;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Enthält die Interpolations- und Propagierungslogik für Achsenänderungen.
    /// Lineare Interpolation innerhalb bekannter Stützpunkte; lineare Extrapolation an den Rändern.
    /// </summary>
    public static class Remapper
    {
        // ---------------------------------------------------------------------------
        // X-Achse
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Ändert den X-Achsenwert an <paramref name="colIndex"/> auf <paramref name="newX"/>
        /// und interpoliert die zugehörige Spalte der Map linear aus den Nachbarspalten.
        /// Bei Randspalten wird ein gleichmäßiges Delta (Durchschnittssteigung aller Zeilen) verwendet,
        /// damit alle Zellen proportional und konsistent verändert werden.
        /// </summary>
        public static void ChangeXAxis(MapModel map, int colIndex, double newX)
        {
            if (colIndex < 0 || colIndex >= map.Cols) throw new ArgumentOutOfRangeException(nameof(colIndex));
            double oldX = map.XAxis[colIndex];
            if (Math.Abs(oldX - newX) < 1e-12) return;

            int left  = colIndex - 1;
            int right = colIndex + 1;

            if (left >= 0 && right < map.Cols)
            {
                // Mittlere Spalte: pro Zeile zwischen Nachbarn interpolieren
                for (int r = 0; r < map.Rows; r++)
                {
                    map.Values[r, colIndex] = Lerp(
                        map.XAxis[left],  map.Values[r, left],
                        map.XAxis[right], map.Values[r, right], newX);
                    map.MarkCellModified(r, colIndex);
                }
            }
            else
            {
                // Randspalte: einheitliches Delta aus mittlerer Steigung über alle Zeilen
                int neighborCol  = left >= 0 ? left : right;
                double neighborX = map.XAxis[neighborCol];
                double xSpan     = oldX - neighborX;

                double avgSlope = 0;
                if (Math.Abs(xSpan) > 1e-12)
                {
                    for (int r = 0; r < map.Rows; r++)
                        avgSlope += (map.Values[r, colIndex] - map.Values[r, neighborCol]) / xSpan;
                    avgSlope /= map.Rows;
                }

                double delta = avgSlope * (newX - oldX);
                for (int r = 0; r < map.Rows; r++)
                {
                    map.Values[r, colIndex] += delta;
                    map.MarkCellModified(r, colIndex);
                }
            }

            map.XAxis[colIndex] = newX;
        }

        // ---------------------------------------------------------------------------
        // Y-Achse
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Ändert den Y-Achsenwert an <paramref name="rowIndex"/> auf <paramref name="newY"/>
        /// und interpoliert die zugehörige Zeile der Map linear aus den Nachbarzeilen.
        /// Bei Randzeilen wird ein gleichmäßiges Delta (Durchschnittssteigung aller Spalten) verwendet.
        /// </summary>
        public static void ChangeYAxis(MapModel map, int rowIndex, double newY)
        {
            if (rowIndex < 0 || rowIndex >= map.Rows) throw new ArgumentOutOfRangeException(nameof(rowIndex));
            double oldY = map.YAxis[rowIndex];
            if (Math.Abs(oldY - newY) < 1e-12) return;

            int up   = rowIndex - 1;
            int down = rowIndex + 1;

            if (up >= 0 && down < map.Rows)
            {
                // Mittlere Zeile: pro Spalte zwischen Nachbarn interpolieren
                for (int c = 0; c < map.Cols; c++)
                {
                    map.Values[rowIndex, c] = Lerp(
                        map.YAxis[up],   map.Values[up, c],
                        map.YAxis[down], map.Values[down, c], newY);
                    map.MarkCellModified(rowIndex, c);
                }
            }
            else
            {
                // Randzeile: einheitliches Delta aus mittlerer Steigung über alle Spalten
                int neighborRow  = up >= 0 ? up : down;
                double neighborY = map.YAxis[neighborRow];
                double ySpan     = oldY - neighborY;

                double avgSlope = 0;
                if (Math.Abs(ySpan) > 1e-12)
                {
                    for (int c = 0; c < map.Cols; c++)
                        avgSlope += (map.Values[rowIndex, c] - map.Values[neighborRow, c]) / ySpan;
                    avgSlope /= map.Cols;
                }

                double delta = avgSlope * (newY - oldY);
                for (int c = 0; c < map.Cols; c++)
                {
                    map.Values[rowIndex, c] += delta;
                    map.MarkCellModified(rowIndex, c);
                }
            }

            map.YAxis[rowIndex] = newY;
        }

        // ---------------------------------------------------------------------------
        // Propagierung auf abhängige Maps
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Wendet dieselbe X-Achsenänderung auf alle Maps mit gleicher Spaltenanzahl an.
        /// </summary>
        public static void PropagateXAxisChange(IEnumerable<MapModel> allMaps, MapModel sourceMap, int colIndex, double newX)
        {
            foreach (var m in allMaps)
            {
                if (m == sourceMap) continue;
                if (m.Cols == sourceMap.Cols)
                    ChangeXAxis(m, colIndex, newX);
            }
        }

        /// <summary>
        /// Wendet dieselbe Y-Achsenänderung auf alle Maps mit gleicher Zeilenanzahl an.
        /// </summary>
        public static void PropagateYAxisChange(IEnumerable<MapModel> allMaps, MapModel sourceMap, int rowIndex, double newY)
        {
            foreach (var m in allMaps)
            {
                if (m == sourceMap) continue;
                if (m.Rows == sourceMap.Rows)
                    ChangeYAxis(m, rowIndex, newY);
            }
        }

        // ---------------------------------------------------------------------------
        // Sortierung
        // ---------------------------------------------------------------------------

        /// <summary>Sortiert Spalten (X-Achse) aufsteigend und ordnet Values entsprechend um.</summary>
        private static void SortColumns(MapModel map)
        {
            // Indizes nach XAxis-Wert aufsteigend sortieren
            var order = new int[map.Cols];
            for (int i = 0; i < map.Cols; i++) order[i] = i;
            Array.Sort(order, (a, b) => map.XAxis[a].CompareTo(map.XAxis[b]));

            var newX = new double[map.Cols];
            var newV = new double[map.Rows, map.Cols];
            var newMod = new HashSet<(int, int)>();

            for (int newC = 0; newC < map.Cols; newC++)
            {
                int oldC = order[newC];
                newX[newC] = map.XAxis[oldC];
                for (int r = 0; r < map.Rows; r++)
                {
                    newV[r, newC] = map.Values[r, oldC];
                    if (map.IsCellModified(r, oldC))
                        newMod.Add((r, newC));
                }
            }

            map.XAxis = newX;
            map.Values = newV;
            map.ModifiedCells.Clear();
            foreach (var cell in newMod) map.ModifiedCells.Add(cell);
        }

        /// <summary>Sortiert Zeilen (Y-Achse) aufsteigend und ordnet Values entsprechend um.</summary>
        private static void SortRows(MapModel map)
        {
            var order = new int[map.Rows];
            for (int i = 0; i < map.Rows; i++) order[i] = i;
            Array.Sort(order, (a, b) => map.YAxis[a].CompareTo(map.YAxis[b]));

            var newY = new double[map.Rows];
            var newV = new double[map.Rows, map.Cols];
            var newMod = new HashSet<(int, int)>();

            for (int newR = 0; newR < map.Rows; newR++)
            {
                int oldR = order[newR];
                newY[newR] = map.YAxis[oldR];
                for (int c = 0; c < map.Cols; c++)
                {
                    newV[newR, c] = map.Values[oldR, c];
                    if (map.IsCellModified(oldR, c))
                        newMod.Add((newR, c));
                }
            }

            map.YAxis = newY;
            map.Values = newV;
            map.ModifiedCells.Clear();
            foreach (var cell in newMod) map.ModifiedCells.Add(cell);
        }

        // ---------------------------------------------------------------------------
        // Mathematik
        // ---------------------------------------------------------------------------

        /// <summary>Lineare Interpolation/Extrapolation zwischen zwei Stützpunkten.</summary>
        private static double Lerp(double x0, double v0, double x1, double v1, double xNew)
        {
            double dx = x1 - x0;
            if (Math.Abs(dx) < 1e-12) return v0; // Division durch 0 vermeiden
            double t = (xNew - x0) / dx;
            return v0 + t * (v1 - v0);
        }
    }
}
