using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Exportiert eine Sammlung von MapModel-Objekten in verschiedene Fremdformate.
    /// </summary>
    internal static class Exporters
    {
        private static readonly CultureInfo _inv = CultureInfo.InvariantCulture;

        // -----------------------------------------------------------------------
        // A2L (ASAM MCD-2 MC / ASAP2)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Erzeugt eine einfache A2L-Datei mit CHARACTERISTIC MAP-Blöcken für alle Maps.
        /// Maps ohne Addr:-Metazeile erhalten die Adresse 0x00000000.
        /// </summary>
        public static void ExportA2l(IEnumerable<MapModel> maps, string outputPath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("ASAP2_VERSION 1 71");
            sb.AppendLine();
            sb.AppendLine("/begin PROJECT ECM_STAGE_HELPER");
            sb.AppendLine("  /begin MODULE ECM");
            sb.AppendLine();

            foreach (var map in maps)
            {
                string name    = SanitizeName(map.MapName ?? Path.GetFileNameWithoutExtension(map.FilePath));
                string unit    = map.Unit ?? "";
                int    cols    = map.Cols;
                int    rows    = map.Rows;
                long   addr    = BinFlash.ParseAddr(map);
                string addrHex = addr >= 0
                    ? $"0x{addr:X8}"
                    : "0x00000000";

                // X-Achsen-Objekt
                string xName = name + "_X";
                sb.AppendLine($"    /begin AXIS_PTS {xName}");
                sb.AppendLine($"      LONG_IDENTIFIER \"{(map.AxisLabel?.Split('|').Length > 0 ? map.AxisLabel.Split('|')[0] : "X")}\"");
                sb.AppendLine($"      ADDRESS {addrHex}");
                sb.AppendLine($"      INPUT_QUANTITY NO_INPUT_QUANTITY");
                sb.AppendLine($"      DEPOSIT ABSOLUTE");
                sb.AppendLine($"      DISPLAY_IDENTIFIER {xName}");
                sb.AppendLine($"      READ_ONLY");
                sb.AppendLine($"      /begin AXIS_PTS_X {cols}");
                sb.Append("        ");
                foreach (double v in map.XAxis)
                    sb.Append(v.ToString(_inv)).Append(' ');
                sb.AppendLine();
                sb.AppendLine("      /end AXIS_PTS_X");
                sb.AppendLine($"    /end AXIS_PTS {xName}");
                sb.AppendLine();

                // Y-Achsen-Objekt
                string yName = name + "_Y";
                sb.AppendLine($"    /begin AXIS_PTS {yName}");
                sb.AppendLine($"      LONG_IDENTIFIER \"{(map.AxisLabel != null && map.AxisLabel.Contains("|") ? map.AxisLabel.Split('|')[1] : "Y")}\"");
                sb.AppendLine($"      ADDRESS {addrHex}");
                sb.AppendLine($"      INPUT_QUANTITY NO_INPUT_QUANTITY");
                sb.AppendLine($"      DEPOSIT ABSOLUTE");
                sb.AppendLine($"      DISPLAY_IDENTIFIER {yName}");
                sb.AppendLine($"      READ_ONLY");
                sb.AppendLine($"      /begin AXIS_PTS_Y {rows}");
                sb.Append("        ");
                foreach (double v in map.YAxis)
                    sb.Append(v.ToString(_inv)).Append(' ');
                sb.AppendLine();
                sb.AppendLine("      /end AXIS_PTS_Y");
                sb.AppendLine($"    /end AXIS_PTS {yName}");
                sb.AppendLine();

                // CHARACTERISTIC MAP
                sb.AppendLine($"    /begin CHARACTERISTIC {name}");
                sb.AppendLine($"      LONG_IDENTIFIER \"{map.MapName ?? name}\"");
                sb.AppendLine($"      MAP");
                sb.AppendLine($"      ADDRESS {addrHex}");
                sb.AppendLine($"      DEPOSIT ABSOLUTE");
                sb.AppendLine($"      DISPLAY_IDENTIFIER {name}");
                sb.AppendLine($"      UNIT \"{unit}\"");

                sb.AppendLine($"      /begin AXIS_DESCR STD_AXIS NO_INPUT_QUANTITY {xName} {cols} 0.0 65535.0");
                sb.AppendLine($"        AXIS_PTS_REF {xName}");
                sb.AppendLine($"      /end AXIS_DESCR");

                sb.AppendLine($"      /begin AXIS_DESCR STD_AXIS NO_INPUT_QUANTITY {yName} {rows} 0.0 65535.0");
                sb.AppendLine($"        AXIS_PTS_REF {yName}");
                sb.AppendLine($"      /end AXIS_DESCR");

                // Wertetabelle (Zeilenweise)
                sb.AppendLine($"      /begin VALUES");
                for (int r = 0; r < rows; r++)
                {
                    sb.Append("        ");
                    for (int c = 0; c < cols; c++)
                        sb.Append(map.Values[r, c].ToString(_inv)).Append(' ');
                    sb.AppendLine();
                }
                sb.AppendLine($"      /end VALUES");

                sb.AppendLine($"    /end CHARACTERISTIC {name}");
                sb.AppendLine();
            }

            sb.AppendLine("  /end MODULE");
            sb.AppendLine("/end PROJECT");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        // -----------------------------------------------------------------------
        // DAMOS (.dam) – vereinfachtes Siemens/VDO DAMOS-Format
        // -----------------------------------------------------------------------

        /// <summary>
        /// Erzeugt eine DAMOS-Datei mit KENNFELD-Blöcken für alle Maps.
        /// </summary>
        public static void ExportDamos(IEnumerable<MapModel> maps, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("* DAMOS-Export ECM Stage Helper Tool");
            sb.AppendLine($"* Erstellt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            foreach (var map in maps)
            {
                string name = SanitizeName(map.MapName ?? Path.GetFileNameWithoutExtension(map.FilePath));
                int    cols = map.Cols;
                int    rows = map.Rows;
                long   addr = BinFlash.ParseAddr(map);

                sb.AppendLine($"KENNFELD {name}");
                if (addr >= 0)
                    sb.AppendLine($"  ADRESSE 0x{addr:X8}");
                sb.AppendLine($"  EINHEIT_W \"{map.Unit ?? ""}\"");
                sb.AppendLine($"  ST/X {cols}");
                sb.AppendLine($"  ST/Y {rows}");

                // X-Achse
                sb.Append("  SSTX");
                foreach (double v in map.XAxis)
                    sb.Append(' ').Append(v.ToString(_inv));
                sb.AppendLine();

                // Y-Achse
                sb.Append("  SSTY");
                foreach (double v in map.YAxis)
                    sb.Append(' ').Append(v.ToString(_inv));
                sb.AppendLine();

                // Werte (zeilenweise)
                sb.AppendLine("  WERT");
                for (int r = 0; r < rows; r++)
                {
                    sb.Append("   ");
                    for (int c = 0; c < cols; c++)
                        sb.Append(' ').Append(map.Values[r, c].ToString(_inv));
                    sb.AppendLine();
                }
                sb.AppendLine("END");
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        // -----------------------------------------------------------------------
        // Kennfeldpaket (.kp) – eigenes JSON-ähnliches Format
        // -----------------------------------------------------------------------

        /// <summary>
        /// Erzeugt ein einfaches JSON-Kennfeldpaket mit allen Maps.
        /// </summary>
        public static void ExportKennfeldpaket(IEnumerable<MapModel> maps, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"_erstellt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine($"  \"_version\": \"1.0\",");
            sb.AppendLine("  \"kennfelder\": [");

            bool firstMap = true;
            foreach (var map in maps)
            {
                if (!firstMap) sb.AppendLine(",");
                firstMap = false;

                long addr = BinFlash.ParseAddr(map);

                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": {JsonStr(map.MapName ?? Path.GetFileNameWithoutExtension(map.FilePath))},");
                sb.AppendLine($"      \"datei\": {JsonStr(Path.GetFileName(map.FilePath))},");
                sb.AppendLine($"      \"einheit\": {JsonStr(map.Unit ?? "")},");
                sb.AppendLine($"      \"achsenbeschriftung\": {JsonStr(map.AxisLabel ?? "")},");
                if (addr >= 0)
                    sb.AppendLine($"      \"adresse\": \"0x{addr:X8}\",");
                sb.AppendLine($"      \"groesse\": \"{map.Cols}x{map.Rows}\",");

                // X-Achse
                sb.Append("      \"x_achse\": [");
                for (int i = 0; i < map.XAxis.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(map.XAxis[i].ToString(_inv));
                }
                sb.AppendLine("],");

                // Y-Achse
                sb.Append("      \"y_achse\": [");
                for (int i = 0; i < map.YAxis.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(map.YAxis[i].ToString(_inv));
                }
                sb.AppendLine("],");

                // Werte (Array von Arrays)
                sb.AppendLine("      \"werte\": [");
                for (int r = 0; r < map.Rows; r++)
                {
                    sb.Append("        [");
                    for (int c = 0; c < map.Cols; c++)
                    {
                        if (c > 0) sb.Append(", ");
                        sb.Append(map.Values[r, c].ToString(_inv));
                    }
                    sb.Append(']');
                    if (r < map.Rows - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.AppendLine("      ]");
                sb.Append("    }");
            }

            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "MAP";
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            string s = sb.ToString().Trim('_');
            return s.Length == 0 ? "MAP" : s;
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                           .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
    }
}
