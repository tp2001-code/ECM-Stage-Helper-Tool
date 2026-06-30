using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Lädt und speichert ECM-Kennfeld-CSVs im ECM Titanium Export-Format.
    /// Format:
    ///   1. Optionale Metadaten-Zeilen (z.B. "Size: 16x14", "MAP: ...", werden übersprungen)
    ///   2. Achsen-Header: Feld[0] = Beschriftung (z.B. "RPM|hPa"), Feld[1..n] = X-Achsenwerte
    ///   3. Datenzeilen:   Feld[0] = Y-Achsenwert, Feld[1..n] = Zellwerte
    /// Unterstützt Semikolon und Komma als Trennzeichen sowie Punkt/Komma als Dezimaltrennzeichen.
    /// </summary>
    public static class CsvParser
    {
        public static MapModel LoadMap(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path, Encoding.Default)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                if (lines.Length < 2)
                    throw new InvalidDataException("CSV muss mindestens eine Achsenzeile und eine Datenzeile enthalten.");

                char sep = DetectSeparator(lines);

                // --- Metadaten lesen und Daten-Header-Zeile suchen ---
                // Daten-Header erkannt, wenn: ≥2 Felder UND zweites Feld ist numerisch
                string sizeString = null;
                string mapName    = null;
                string unit       = null;
                int dataStart = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // Size-Metazeile extrahieren (z.B. "Size: 16x14")
                    if (line.StartsWith("Size:", StringComparison.OrdinalIgnoreCase))
                        sizeString = line.Substring(5).Trim();

                    // MAP-Name extrahieren (z.B. "MAP : Begrenzer Kraftstoffmenge")
                    if (line.StartsWith("MAP", StringComparison.OrdinalIgnoreCase))
                    {
                        int colon = line.IndexOf(':');
                        if (colon >= 0)
                            mapName = line.Substring(colon + 1).Trim();
                    }

                    // Unit extrahieren (z.B. "Unit: mm3/Stk")
                    if (line.StartsWith("Unit:", StringComparison.OrdinalIgnoreCase))
                        unit = line.Substring(5).Trim();

                    var fields = SplitLine(line, sep);
                    if (fields.Length >= 2 && TryParseDouble(fields[1], out _))
                    {
                        dataStart = i;
                        break;
                    }
                }

                if (dataStart < 0)
                    throw new InvalidDataException("Keine Achsen-Header-Zeile gefunden (zweites Feld muss numerisch sein).");

                // --- X-Achse aus der Daten-Header-Zeile (erstes Feld = Achsenbeschriftung) ---
                var headerFields = SplitLine(lines[dataStart], sep);
                if (headerFields.Length < 2)
                    throw new InvalidDataException("Erste CSV-Zeile enthält keine X-Achsenwerte.");

                string axisLabel = headerFields[0].Length > 0 ? headerFields[0] : null;

                var xList = new List<double>();
                for (int i = 1; i < headerFields.Length; i++)
                {
                    if (TryParseDouble(headerFields[i], out double x))
                        xList.Add(x);
                    else
                        throw new InvalidDataException($"Ungültiger X-Achsenwert '{headerFields[i]}' in {path}");
                }

                var yList = new List<double>();
                var rowData = new List<double[]>();

                for (int r = dataStart + 1; r < lines.Length; r++)
                {
                    var fields = SplitLine(lines[r], sep);
                    if (fields.Length < 2) continue;

                    if (!TryParseDouble(fields[0], out double y))
                        throw new InvalidDataException($"Ungültiger Y-Achsenwert '{fields[0]}' in Zeile {r + 1} von {path}");

                    yList.Add(y);

                    if (fields.Length - 1 < xList.Count)
                        throw new InvalidDataException($"Zeile {r + 1} hat zu wenige Spalten ({fields.Length - 1} statt {xList.Count})");

                    var row = new double[xList.Count];
                    for (int c = 0; c < xList.Count; c++)
                    {
                        if (!TryParseDouble(fields[c + 1], out row[c]))
                            throw new InvalidDataException($"Ungültiger Wert '{fields[c + 1]}' an Zeile {r + 1}, Spalte {c + 1} in {path}");
                    }
                    rowData.Add(row);
                }

                var values = new double[yList.Count, xList.Count];
                for (int r = 0; r < yList.Count; r++)
                    for (int c = 0; c < xList.Count; c++)
                        values[r, c] = rowData[r][c];

                return new MapModel(path, xList.ToArray(), yList.ToArray(), values, sizeString, axisLabel, mapName, unit);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Fehler beim Einlesen von '{path}': {ex.Message}", ex);
            }
        }

        public static void SaveMap(MapModel map, string path)
        {
            var sb = new StringBuilder();

            // Header-Zeile: leer, dann X-Achse
            sb.Append(string.Empty);
            for (int c = 0; c < map.Cols; c++)
            {
                sb.Append(",");
                sb.Append(ToStr(map.XAxis[c]));
            }
            sb.AppendLine();

            // Datenzeilen
            for (int r = 0; r < map.Rows; r++)
            {
                sb.Append(ToStr(map.YAxis[r]));
                for (int c = 0; c < map.Cols; c++)
                {
                    sb.Append(",");
                    sb.Append(ToStr(map.Values[r, c]));
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.Default);
        }

        // --- Hilfsmethoden ---

        private static char DetectSeparator(string[] lines)
        {
            // Erste Zeile mit mindestens 2 Semikolons gewinnt (dt. CSV-Format: ; = Spalte, , = Dezimal)
            foreach (string line in lines)
            {
                if (line.Count(ch => ch == ';') >= 2)
                    return ';';
            }
            // Fallback: Komma als Trennzeichen
            return ',';
        }

        private static string[] SplitLine(string line, char sep)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char ch in line)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (!inQuotes && ch == sep)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
            result.Add(current.ToString().Trim());
            return result.ToArray();
        }

        private static bool TryParseDouble(string s, out double value)
        {
            s = s.Trim();
            // NumberStyles.Float (kein AllowThousands): verhindert, dass "-2,67" als "-267" geparst wird
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(s, NumberStyles.Float, new CultureInfo("de-DE"), out value)) return true;
            // Fallback: Komma durch Punkt ersetzen
            string normalized = s.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string ToStr(double d) => d.ToString(CultureInfo.InvariantCulture);
    }
}
