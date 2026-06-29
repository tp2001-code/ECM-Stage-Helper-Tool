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
        /// </summary>
        public static void ChangeXAxis(MapModel map, int colIndex, double newX)
        {
            if (colIndex < 0 || colIndex >= map.Cols) throw new ArgumentOutOfRangeException(nameof(colIndex));
            if (Math.Abs(map.XAxis[colIndex] - newX) < 1e-12) return;

            int left = colIndex - 1;
            int right = colIndex + 1;

            for (int r = 0; r < map.Rows; r++)
            {
                map.Values[r, colIndex] = InterpolateColumn(map, r, colIndex, newX, left, right);
                map.MarkCellModified(r, colIndex);
            }

            map.XAxis[colIndex] = newX;
        }

        private static double InterpolateColumn(MapModel map, int row, int colIndex, double newX, int left, int right)
        {
            if (left >= 0 && right < map.Cols)
            {
                // Interpolation zwischen linkem und rechtem Nachbarn
                return Lerp(map.XAxis[left], map.Values[row, left],
                            map.XAxis[right], map.Values[row, right], newX);
            }
            if (left >= 0)
            {
                // Rechter Rand: extrapoliere mit den zwei Nachbarn links
                int left2 = Math.Max(0, left - 1);
                return Lerp(map.XAxis[left2], map.Values[row, left2],
                            map.XAxis[left], map.Values[row, left], newX);
            }
            if (right < map.Cols)
            {
                // Linker Rand: extrapoliere mit den zwei Nachbarn rechts
                int right2 = Math.Min(map.Cols - 1, right + 1);
                return Lerp(map.XAxis[right], map.Values[row, right],
                            map.XAxis[right2], map.Values[row, right2], newX);
            }
            // Einspaltiger Sonderfall
            return map.Values[row, colIndex];
        }

        // ---------------------------------------------------------------------------
        // Y-Achse
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Ändert den Y-Achsenwert an <paramref name="rowIndex"/> auf <paramref name="newY"/>
        /// und interpoliert die zugehörige Zeile der Map linear aus den Nachbarzeilen.
        /// </summary>
        public static void ChangeYAxis(MapModel map, int rowIndex, double newY)
        {
            if (rowIndex < 0 || rowIndex >= map.Rows) throw new ArgumentOutOfRangeException(nameof(rowIndex));
            if (Math.Abs(map.YAxis[rowIndex] - newY) < 1e-12) return;

            int up = rowIndex - 1;
            int down = rowIndex + 1;

            for (int c = 0; c < map.Cols; c++)
            {
                map.Values[rowIndex, c] = InterpolateRow(map, c, rowIndex, newY, up, down);
                map.MarkCellModified(rowIndex, c);
            }

            map.YAxis[rowIndex] = newY;
        }

        private static double InterpolateRow(MapModel map, int col, int rowIndex, double newY, int up, int down)
        {
            if (up >= 0 && down < map.Rows)
            {
                return Lerp(map.YAxis[up], map.Values[up, col],
                            map.YAxis[down], map.Values[down, col], newY);
            }
            if (up >= 0)
            {
                int up2 = Math.Max(0, up - 1);
                return Lerp(map.YAxis[up2], map.Values[up2, col],
                            map.YAxis[up], map.Values[up, col], newY);
            }
            if (down < map.Rows)
            {
                int down2 = Math.Min(map.Rows - 1, down + 1);
                return Lerp(map.YAxis[down], map.Values[down, col],
                            map.YAxis[down2], map.Values[down2, col], newY);
            }
            return map.Values[rowIndex, col];
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
