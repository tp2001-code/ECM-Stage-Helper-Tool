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
    ///   2. Achsen-Header: Feld[0] = Beschriftung (z.B. "RPM|hPa" oder "|" = kein Label), Feld[1..n] = X-Achsenwerte
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

                if (lines.Length < 1)
                    throw new InvalidDataException("CSV ist leer.");

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
                {
                    // --- Sonderfall: Ein-Zellen-CSV (einzelner Skalarwert ohne X/Y-Achsen) ---
                    if (TryParseScalar(lines, out double scalar))
                        return new MapModel(path,
                            new double[] { 0 },          // Dummy-X-Achse
                            new double[] { 0 },          // Dummy-Y-Achse
                            new double[,] { { scalar } },
                            sizeString, axisLabel: null, mapName: mapName, unit: unit, isScalar: true);

                    throw new InvalidDataException("Keine Achsen-Header-Zeile gefunden (zweites Feld muss numerisch sein).");
                }

                // --- X-Achse aus der Daten-Header-Zeile (erstes Feld = Achsenbeschriftung) ---
                var headerFields = SplitLine(lines[dataStart], sep);
                if (headerFields.Length < 2)
                    throw new InvalidDataException("Erste CSV-Zeile enthält keine X-Achsenwerte.");

                // "|" alleinstehend = kein Achslabel (ECM Titanium Platzhalter)
                string axisLabel = (headerFields[0].Length > 0 && headerFields[0] != "|")
                    ? headerFields[0]
                    : null;

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

            // --- Sonderfall: Ein-Zellen-CSV (nur Skalarwert, keine Achsen) ---
            if (map.IsScalar)
            {
                // Nur echte Metazeilen (Size/MAP/Unit) aus dem Original uebernehmen
                if (File.Exists(map.FilePath))
                {
                    foreach (string origLine in File.ReadAllLines(map.FilePath, Encoding.Default))
                    {
                        string t = origLine.Trim();
                        if (t.StartsWith("Size:", StringComparison.OrdinalIgnoreCase) ||
                            t.StartsWith("MAP",   StringComparison.OrdinalIgnoreCase) ||
                            t.StartsWith("Unit:", StringComparison.OrdinalIgnoreCase))
                            sb.AppendLine(origLine);
                    }
                }
                sb.AppendLine(map.Values[0, 0].ToString(new CultureInfo("de-DE")));
                File.WriteAllText(path, sb.ToString(), Encoding.Default);
                return;
            }

            char sep = ';'; // ECM-Titanium-Format: Semikolon als Trenner

            // --- Metadaten-Zeilen aus der Originaldatei übernehmen ---
            if (File.Exists(map.FilePath))
            {
                var origLines = File.ReadAllLines(map.FilePath, Encoding.Default)
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToArray();
                sep = DetectSeparator(origLines);

                foreach (string origLine in origLines)
                {
                    var fields = SplitLine(origLine, sep);
                    bool isDataHeader = fields.Length >= 2 && TryParseDouble(fields[1], out _);
                    if (isDataHeader) break;
                    sb.AppendLine(origLine); // Metazeile unverändert kopieren
                }
            }

            // Dezimaltrennzeichen passend zum Feldseparator wählen
            var numCulture = sep == ';' ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture;

            // --- Achsen-Header: Beschriftung + X-Achse ---
            // Kein Label → "|" als Platzhalter schreiben (ECM Titanium Format)
            sb.Append(map.AxisLabel ?? "|");
            for (int c = 0; c < map.Cols; c++)
            {
                sb.Append(sep);
                sb.Append(map.XAxis[c].ToString(numCulture));
            }
            sb.AppendLine();

            // --- Datenzeilen ---
            for (int r = 0; r < map.Rows; r++)
            {
                sb.Append(map.YAxis[r].ToString(numCulture));
                for (int c = 0; c < map.Cols; c++)
                {
                    sb.Append(sep);
                    sb.Append(map.Values[r, c].ToString(numCulture));
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.Default);
        }

        // --- Hilfsmethoden ---

        private static char DetectSeparator(string[] lines)
        {
            // Semikolon als Trenner erkannt, wenn mindestens eine Zeile ≥1 Semikolon enthält.
            // In dt. CSV-Format: ; = Spaltentrenner, , = Dezimaltrenner (niemals Feldtrenner).
            foreach (string line in lines)
            {
                // Metadaten-Zeilen überspringen (könnten zufällig ; enthalten)
                if (line.StartsWith("Size:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("MAP",   StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Unit:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.Contains(';'))
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

        /// <summary>
        /// Versucht aus den CSV-Zeilen einen einzelnen Skalarwert zu lesen.
        /// Metadaten-Zeilen (Size, MAP, Unit) werden übersprungen.
        /// Anwendungsfall: CSV enthält nur einen Wert ohne Achsenstruktur.
        /// </summary>
        private static bool TryParseScalar(string[] lines, out double scalar)
        {
            scalar = 0;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                // Metadaten-Zeilen überspringen
                if (trimmed.StartsWith("Size:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("MAP",   StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Unit:", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Erste nicht-leere, nicht-Meta-Zeile als Skalar parsen
                if (!string.IsNullOrWhiteSpace(trimmed) && TryParseDouble(trimmed, out scalar))
                    return true;
            }
            return false;
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
