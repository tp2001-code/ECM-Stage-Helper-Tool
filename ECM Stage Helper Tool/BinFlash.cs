using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Kapselt einen rohen Flash-Dump (BIN-Datei) und liest/schreibt Kennfeld-Werte
    /// anhand der in der CSV enthaltenen Hex-Adresse (Addr-Metadaten-Zeile).
    /// Alle Werte im BIN sind Little-Endian UInt16; die Umrechnung auf physikalische
    /// Einheiten erfolgt über die unitabhängigen Faktoren.
    /// </summary>
    public class BinFlash
    {
        // -----------------------------------------------------------------------
        // Faktor-Tabelle (Unit → Skalierungsfaktor + Vorzeichen)
        // -----------------------------------------------------------------------
        private static readonly Dictionary<string, (double Factor, bool Signed)> _factors =
            new Dictionary<string, (double, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            { "mm3/Stk",  (0.01,    false) },
            { "mm³/Stk",  (0.01,    false) },
            { "deg BTDC", (3.0/128, true ) },
            { "°KW",      (3.0/128, true ) },
            { "hPa",      (1.0,     false) },
            { "mbar",     (1.0,     false) },
            { "bar",      (0.1,     false) },
            { "us",       (0.4,     false) },
            { "µs",       (0.4,     false) },
            { "Nm",       (0.1,     false) },
            { "Lambda",   (0.001,   false) },
            { "%",        (0.01,    false) },
            { "mg/Hub",   (0.01,    false) },
            { "mg",       (0.01,    false) },
            { "RPM",      (1.0,     false) },
            { "°C",       (1.0,     true ) },
            { "km/h",     (1.0,     false) },
            { "A",        (0.001,   false) },
            { "V",        (0.001,   false) },
            { "ms",       (0.04,    false) },
        };

        // -----------------------------------------------------------------------
        // Felder
        // -----------------------------------------------------------------------
        private readonly byte[] _data;
        public string FilePath { get; private set; }
        public bool IsModified { get; private set; }

        // -----------------------------------------------------------------------
        // Konstruktor / Laden / Speichern
        // -----------------------------------------------------------------------

        private BinFlash(string filePath, byte[] data)
        {
            FilePath = filePath;
            _data    = data;
        }

        public static BinFlash Load(string filePath) =>
            new BinFlash(filePath, File.ReadAllBytes(filePath));

        public void Save(string filePath)
        {
            File.WriteAllBytes(filePath, _data);
            FilePath   = filePath;
            IsModified = false;
        }

        // -----------------------------------------------------------------------
        // Öffentliche API: Werte lesen / schreiben
        // -----------------------------------------------------------------------

        /// <summary>
        /// Liest alle Kennfeldwerte einer Map aus dem BIN und gibt sie als physikalische
        /// Fließkommazahlen zurück (gleiche Dimension wie <see cref="MapModel.Values"/>).
        /// Gibt null zurück, wenn die Map keine Addr-Metazeile hat oder die Adresse
        /// außerhalb des Dumps liegt.
        /// </summary>
        public double[,] ReadMapValues(MapModel map)
        {
            long addr   = ParseAddr(map);
            if (addr < 0) return null;

            int rows   = map.Rows;
            int cols   = map.Cols;
            long end   = addr + (long)rows * cols * 2;
            if (end > _data.Length) return null;

            (double factor, bool signed) = GetFactor(map.Unit);
            var result = new double[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int offset = (int)(addr + (r * cols + c) * 2);
                    double raw = signed
                        ? (double)(short)(_data[offset] | (_data[offset + 1] << 8))
                        : (double)(ushort)(_data[offset] | (_data[offset + 1] << 8));
                    result[r, c] = raw * factor;
                }
            }
            return result;
        }

        /// <summary>
        /// Schreibt alle Werte aus <see cref="MapModel.Values"/> in den BIN-Puffer.
        /// Die Zellen werden auf den nächsten darstellbaren UInt16-Wert gerundet.
        /// Gibt false zurück wenn kein Addr vorhanden oder Adresse außerhalb liegt.
        /// </summary>
        public bool WriteMapValues(MapModel map)
        {
            long addr = ParseAddr(map);
            if (addr < 0) return false;

            int rows  = map.Rows;
            int cols  = map.Cols;
            long end  = addr + (long)rows * cols * 2;
            if (end > _data.Length) return false;

            (double factor, bool signed) = GetFactor(map.Unit);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int offset = (int)(addr + (r * cols + c) * 2);
                    double physical = map.Values[r, c];
                    ushort raw;
                    if (signed)
                    {
                        short s = (short)Math.Round(physical / factor);
                        raw = (ushort)s;
                    }
                    else
                    {
                        raw = (ushort)Math.Round(physical / factor);
                    }
                    _data[offset]     = (byte)(raw & 0xFF);
                    _data[offset + 1] = (byte)(raw >> 8);
                }
            }
            IsModified = true;
            return true;
        }

        /// <summary>
        /// Liest alle Kennfeldwerte aus dem BIN und schreibt sie in <see cref="MapModel.Values"/>.
        /// Zellen werden als geändert markiert wenn der BIN-Wert vom CSV-Originalwert abweicht.
        /// Gibt false zurück wenn keine Adresse vorhanden oder die Adresse außerhalb liegt.
        /// </summary>
        public bool ReadMapValuesIntoMap(MapModel map)
        {
            double[,] binValues = ReadMapValues(map);
            if (binValues == null) return false;

            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    map.Values[r, c] = binValues[r, c];
                    if (Math.Abs(map.GetOriginalValue(r, c) - binValues[r, c]) > 1e-9)
                        map.MarkCellModified(r, c);
                    else
                        map.UnmarkCell(r, c);
                }
            return true;
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden
        // -----------------------------------------------------------------------

        /// <summary>Liest die Addr-Zeile aus der CSV-Datei der Map und gibt die Adresse zurück (-1 = nicht gefunden).</summary>
        public static long ParseAddr(MapModel map)
        {
            try
            {
                if (!File.Exists(map.FilePath)) return -1;
                foreach (string line in File.ReadLines(map.FilePath, System.Text.Encoding.Default))
                {
                    if (line.StartsWith("Addr:", StringComparison.OrdinalIgnoreCase))
                    {
                        string hex = line.Substring(5).Trim();
                        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            hex = hex.Substring(2);
                        if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long addr))
                            return addr;
                    }
                }
            }
            catch { /* I/O-Fehler ignorieren */ }
            return -1;
        }

        private static (double Factor, bool Signed) GetFactor(string unit)
        {
            if (unit != null && _factors.TryGetValue(unit.Trim(), out var entry))
                return entry;
            return (1.0, false); // Fallback: kein Faktor
        }
    }
}
