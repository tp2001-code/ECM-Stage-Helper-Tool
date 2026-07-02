using System.Collections.Generic;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    // ---------------------------------------------------------------------------
    // Ergebnis der Analyse-Phase (Phase 1)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Enthält die von Claude ermittelten aktuellen Kenndaten aller Maps.
    /// </summary>
    public class AnalysisResult
    {
        /// <summary>Maximale Einspritzmenge in der Einspritz-Map (mg oder mm³/Hub).</summary>
        public double MaxFuelQuantity { get; set; }

        /// <summary>Einheit der Einspritzmenge, z.B. "mg" oder "mm3/Hub".</summary>
        public string FuelUnit { get; set; }

        /// <summary>Geschätzte maximale Motorleistung in kW (aus Drehmoment-Map berechnet).</summary>
        public double MaxPowerKw { get; set; }

        /// <summary>Geschätzte maximale Motorleistung in PS.</summary>
        public double MaxPowerPs => MaxPowerKw * 1.35962;

        /// <summary>Geschätztes maximales Drehmoment in Nm.</summary>
        public double MaxTorqueNm { get; set; }

        /// <summary>RPM bei maximalem Drehmoment.</summary>
        public double MaxTorqueRpm { get; set; }

        /// <summary>RPM bei maximaler Leistung.</summary>
        public double MaxPowerRpm { get; set; }

        /// <summary>Menschenlesbare Zusammenfassung der Analyse fuer die UI.</summary>
        public string Summary { get; set; }

        /// <summary>Maximaler Ladedruck (hPa) aus den Ladedruck-Maps (Volllast).</summary>
        public double MaxBoostHpa { get; set; }

        /// <summary>Maximaler Raildruck (bar) aus den Raildruck-Maps.</summary>
        public double MaxRailPressureBar { get; set; }

        /// <summary>Liste aller Maps die beim Remap geaendert werden muessen.</summary>
        public List<string> MapsToChange { get; set; } = new List<string>();
    }

    // ---------------------------------------------------------------------------
    // Ziel-Limits fuer den Remap (vom User vorgegeben)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Vom User vorgegebene neue Maximalwerte pro physikalischer Groesse.
    /// RemapEngine stellt sicher dass kein berechneter Wert das jeweilige Limit ueberschreitet.
    /// </summary>
    public class RemapLimits
    {
        /// <summary>Ziel-Einspritzmenge (mm3/Hub) – Hauptsteuergroesse.</summary>
        public double TargetFuelMm3 { get; set; }

        /// <summary>Max. Ladedruck (hPa). 0 = proportional skalieren ohne hartes Limit.</summary>
        public double MaxBoostHpa { get; set; }

        /// <summary>Max. Raildruck (bar). 0 = Standardlimit 1850 bar.</summary>
        public double MaxRailPressureBar { get; set; }

        /// <summary>Max. Drehmoment (Nm). 0 = proportional skalieren.</summary>
        public double MaxTorqueNm { get; set; }

        /// <summary>Effektiver Boost-Grenzwert: nimmt das gesetzte Limit oder faellt auf 0 zurueck.</summary>
        public double EffectiveBoostLimit => MaxBoostHpa > 0 ? MaxBoostHpa : double.MaxValue;

        /// <summary>Effektiver Raildruck-Grenzwert.</summary>
        public double EffectiveRailLimit => MaxRailPressureBar > 0 ? MaxRailPressureBar : 1850.0;

        /// <summary>Effektives Drehmoment-Limit.</summary>
        public double EffectiveTorqueLimit => MaxTorqueNm > 0 ? MaxTorqueNm : double.MaxValue;
    }

    // ---------------------------------------------------------------------------
    // Ergebnis der Remap-Phase (Phase 3)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Enthält die von Claude berechneten neuen Werte für alle betroffenen Maps.
    /// </summary>
    public class RemapResult
    {
        /// <summary>
        /// Neue Werte pro Map. Key = Dateiname der CSV (z.B. "Einspritzmenge.csv").
        /// Value = berechnete neue Werte und Achsen.
        /// </summary>
        public List<MapRemap> Maps { get; set; } = new List<MapRemap>();

        /// <summary>Erwartete Leistung nach dem Remap in kW.</summary>
        public double ExpectedPowerKw { get; set; }

        /// <summary>Erwartete Leistung nach dem Remap in PS.</summary>
        public double ExpectedPowerPs => ExpectedPowerKw * 1.35962;

        /// <summary>Erwartetes maximales Drehmoment nach dem Remap in Nm.</summary>
        public double ExpectedTorqueNm { get; set; }

        /// <summary>Kommentar / Begründung von Claude.</summary>
        public string Notes { get; set; }
    }

    /// <summary>
    /// Die neuen Daten für eine einzelne Map nach dem KI-Remap.
    /// </summary>
    public class MapRemap
    {
        /// <summary>Dateiname der CSV (Key zur Zuordnung zur MapModel-Instanz).</summary>
        public string FileName { get; set; }

        /// <summary>Neue X-Achsenwerte. Kann länger sein als das Original (Achsenerweiterung).</summary>
        public double[] NewXAxis { get; set; }

        /// <summary>Neue Y-Achsenwerte (meist unverändert).</summary>
        public double[] NewYAxis { get; set; }

        /// <summary>Neue Zellenwerte [row, col]. Dimensionen passen zu NewXAxis × NewYAxis.</summary>
        public double[,] NewValues { get; set; }

        /// <summary>Kurze Beschreibung was an dieser Map geändert wurde.</summary>
        public string ChangeDescription { get; set; }
    }
}
