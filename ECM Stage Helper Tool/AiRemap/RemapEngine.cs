using System;
using System.Collections.Generic;
using System.Linq;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// Fuehrt den mathematisch exakten Remap lokal durch – ohne KI-Mathematik.
    /// Die KI liefert nur den MapType (Entscheidung WAS zu tun ist),
    /// dieser Engine berechnet das WIE per linearer Extrapolation.
    /// </summary>
    public static class RemapEngine
    {
        public enum MapType
        {
            Unknown,
            FuelLimiter,            // Begrenzer Kraftstoffmenge: Zellen skalieren
            InjectionMap,           // Eingespritzte KST f(RPM,Nm): X-letzt + Zellen extrapolieren
            InjectionTiming,        // Einspritzzeitpunkt: X-Achse (IQ) + BTDC extrapolieren
            InjectionDuration,      // Einspritzdauer: X-Achse (IQ) + us extrapolieren
            BoostMap,               // Ladedruck #n: X-Achse (IQ) + hPa extrapolieren
            BoostLimiter,           // Ladedruck Begrenzer: Zellen skalieren
            TorqueLimiter,          // Drehmoment Begrenzer: Nm-Zellen skalieren
            RailPressure,           // Raildruck: X-Achse (IQ) + bar extrapolieren
            SmokeLimit,             // Rauchbegrenzer Lambda: X-Achse (mg) + Lambda halten
            Skip                    // Nicht bearbeiten
        }

        /// <summary>
        /// Verschiebt das Drehmoment-Plateau des Nm-Begrenzers um rpmShift RPM nach oben.
        /// X-Achse = RPM, Y-Achse = hPa (APS), Zellen = Nm.
        /// Alle Nm-Werte werden per Interpolation so neu berechnet als waere die
        /// Kennlinie um rpmShift nach rechts verschoben.
        /// </summary>
        public static bool ShiftTorquePlateauRpm(MapModel map, double rpmShift)
        {
            if (map == null || rpmShift == 0) return false;
            string n = map.Name.ToLowerInvariant();
            if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) return false;

            AiRemapLogger.LogInfo($"ShiftTorquePlateau: {map.Name}, RPM-Verschiebung={rpmShift:F0}");

            // X-Achse = RPM (unveraendert) - wir interpolieren die Werte neu
            // Fuer jeden RPM-Wert: alten Wert bei (RPM - rpmShift) nachschlagen
            for (int row = 0; row < map.Rows; row++)
            {
                double[] newRowValues = new double[map.Cols];

                for (int col = 0; col < map.Cols; col++)
                {
                    double targetRpm  = map.XAxis[col];
                    double sourceRpm  = targetRpm - rpmShift; // wo der Wert herkam

                    // Interpoliere Original-Wert bei sourceRpm
                    double newVal = InterpolateRow(map, row, sourceRpm);
                    newRowValues[col] = newVal;
                }

                // Neue Werte zurueckschreiben (nur wenn veraendert)
                for (int col = 0; col < map.Cols; col++)
                {
                    double orig = map.GetOriginalValue(row, col);
                    if (Math.Abs(orig - newRowValues[col]) > 1e-9)
                    {
                        map.Values[row, col] = newRowValues[col];
                        map.MarkCellModified(row, col);
                    }
                }
            }
            return true;
        }

        /// <summary>Interpoliert den Nm-Wert einer Zeile linear bei einem beliebigen RPM-Wert.</summary>
        private static double InterpolateRow(MapModel map, int row, double rpm)
        {
            double[] xAxis = map.XAxis;
            int cols = map.Cols;

            // Ausserhalb des Bereichs: Randwerte zurueckgeben
            if (rpm <= xAxis[0])    return map.GetOriginalValue(row, 0);
            if (rpm >= xAxis[cols-1]) return map.GetOriginalValue(row, cols-1);

            // Lineare Interpolation zwischen benachbarten Stuetzpunkten
            for (int c = 0; c < cols - 1; c++)
            {
                if (rpm >= xAxis[c] && rpm <= xAxis[c+1])
                {
                    double t = (rpm - xAxis[c]) / (xAxis[c+1] - xAxis[c]);
                    double v0 = map.GetOriginalValue(row, c);
                    double v1 = map.GetOriginalValue(row, c+1);
                    return v0 + t * (v1 - v0);
                }
            }
            return map.GetOriginalValue(row, cols-1);
        }

        /// <summary>Ermittelt den MapType automatisch anhand von Name und Achsenbeschriftung.</summary>
        public static MapType Classify(MapModel map)
        {
            string n    = map.Name.ToLowerInvariant();
            string axis = (map.AxisLabel ?? "").ToLowerInvariant();

            if (n.Contains("einspritzzeitpunkt"))                         return MapType.InjectionTiming;
            if (n.Contains("einspritzdauer"))                             return MapType.InjectionDuration;
            if (n.Contains("begrenzer") && n.Contains("kraftstoff"))      return MapType.FuelLimiter;
            if (n.Contains("eingespritzte"))                              return MapType.InjectionMap;
            if (n.Contains("ladedruck") && n.Contains("begrenzer"))       return MapType.BoostLimiter;
            if (n.Contains("ladedruck"))                                  return MapType.BoostMap;
            if (n.Contains("drehmoment") && n.Contains("begrenzer"))      return MapType.TorqueLimiter;
            if (n.Contains("raildruck"))                                  return MapType.RailPressure;
            if (n.Contains("rauch") || n.Contains("lambda"))              return MapType.SmokeLimit;

            return MapType.Unknown;
        }

        /// <summary>
        /// Wendet den Remap mathematisch korrekt auf eine Map an.
        /// Gibt true zurueck wenn die Map veraendert wurde.
        /// limits darf null sein (dann gelten nur Hardware-Defaults).
        /// </summary>
        public static bool Apply(MapModel map, double currentMax, double targetFuel,
                                 MapType type, RemapLimits limits = null)
        {
            if (type == MapType.Skip || type == MapType.Unknown) return false;

            double ratio   = targetFuel / currentMax;
            int    lastCol = map.Cols - 1;
            int    prevCol = map.Cols - 2;

            // Effektive Limits (Hardware-Defaults wenn nicht gesetzt)
            double boostLimit = limits != null ? limits.EffectiveBoostLimit    : double.MaxValue;
            double railLimit  = limits != null ? limits.EffectiveRailLimit     : 1850.0;
            double nmLimit    = limits != null ? limits.EffectiveTorqueLimit   : double.MaxValue;

            switch (type)
            {
                // Zellen skalieren, Achsen unveraendert
                case MapType.FuelLimiter:
                    ScaleAllValues(map, ratio);
                    return true;

                case MapType.TorqueLimiter:
                    ScaleAllValues(map, ratio, nmLimit);
                    return true;

                case MapType.BoostLimiter:
                    ScaleAllValues(map, ratio, boostLimit);
                    return true;

                // X-Achse (IQ/mm3): exakt die letzten 2 Spalten anpassen
                case MapType.InjectionTiming:
                case MapType.InjectionDuration:
                case MapType.BoostMap:
                case MapType.RailPressure:
                {
                    if (prevCol < 0) return false;

                    // FEST: Immer genau lastCol und prevCol (lastCol-1) veraendern
                    // Keine Verschiebung auf andere Spalten!
                    int modCol1  = prevCol;   // vorletzte Spalte
                    int modCol2  = lastCol;   // letzte Spalte

                    // Sicherheitscheck: modCol1 und modCol2 muessen gueltige Indizes sein
                    if (modCol1 < 0 || modCol2 >= map.Cols)
                    {
                        AiRemapLogger.LogInfo($"SKIP {map.Name}: ungueltige Spalten-Indizes ({modCol1},{modCol2})");
                        return false;
                    }

                    // Finde den letzten Regressionsbasis-Bereich: letzte Spalte MIT Werten
                    // (Einspritzzeitpunkt: ganz rechts oft 0-Spalten -> trotzdem bleibt modCol2=lastCol)
                    int topCol = modCol2; // Startpunkt fuer Null-Suche
                    for (int tryCol = modCol2; tryCol >= 2; tryCol--)
                    {
                        bool hasValues = false;
                        for (int r2 = 0; r2 < map.Rows; r2++)
                            if (Math.Abs(map.GetOriginalValue(r2, tryCol)) > 1e-9) { hasValues = true; break; }
                        if (hasValues) { topCol = tryCol; break; }
                    }

                    // Neue X-Achsenwerte fuer die beiden letzten Spalten
                    double xOldLast = map.GetOriginalX(modCol2);
                    double xOldPrev = map.GetOriginalX(modCol1);

                    // Neue Werte: prevCol = Mittelwert zwischen xOldPrev und targetFuel,
                    //             lastCol = targetFuel
                    double xNewLast = targetFuel;
                    double xNewPrev = xOldPrev + (targetFuel - xOldPrev) * 0.5;

                    // Sicherheit: neue Achswerte muessen aufsteigend sein
                    if (xNewPrev <= map.GetOriginalX(modCol1 > 0 ? modCol1 - 1 : 0))
                        xNewPrev = map.GetOriginalX(modCol1 > 0 ? modCol1 - 1 : 0) + (targetFuel - xOldPrev) * 0.3;
                    if (xNewLast <= xNewPrev)
                        xNewLast = xNewPrev + (targetFuel - xNewPrev) * 0.5;

                    AiRemapLogger.LogInfo($"{map.Name}: X[{modCol1}] {xOldPrev:F2}->{xNewPrev:F2}, X[{modCol2}] {xOldLast:F2}->{xNewLast:F2} (topCol={topCol})");

                    // X-Achse nur dieser 2 Spalten setzen
                    map.XAxis[modCol1] = xNewPrev;
                    map.XAxis[modCol2] = xNewLast;

                    // Regressionsbasispunkte: die letzten 5 Spalten der ZEILE endend bei modCol1
                    // (aus derselben Zeile, nicht aus derselben Spalte!)
                    const int regPts = 5;
                    int regEnd   = modCol1;                       // letzte unveraenderte Spalte
                    int regStart = Math.Max(0, regEnd - regPts + 1); // 5 Punkte: regStart..regEnd

                    AiRemapLogger.LogInfo($"  Regression aus Zeilen-Spalten [{regStart}..{regEnd}]");

                    for (int r = 0; r < map.Rows; r++)
                    {
                        // Stuetzpunkte: Original-Werte dieser Zeile, Spalten regStart..regEnd
                        var xPts = new double[regPts];
                        var vPts = new double[regPts];
                        int used = 0;
                        for (int c = regStart; c <= regEnd; c++)
                        {
                            xPts[used] = map.GetOriginalX(c);
                            vPts[used] = map.GetOriginalValue(r, c);  // Wert dieser Zeile r, Spalte c
                            used++;
                        }

                        double slope = LinearRegressionSlope(xPts, vPts, used);

                        // Ankerpunkt = letzter unveraenderter Wert dieser Zeile (modCol1)
                        double xBase = map.GetOriginalX(modCol1);
                        double vBase = map.GetOriginalValue(r, modCol1);

                        // Neuen Wert fuer modCol1 (vorletzte Spalte)
                        double newVal1 = vBase + slope * (xNewPrev - xBase);
                        // Neuen Wert fuer modCol2 (letzte Spalte)
                        double newVal2 = vBase + slope * (xNewLast - xBase);

                        // Erste Zeile loggen fuer Debugging
                        if (r == 0)
                            AiRemapLogger.LogInfo($"  Zeile 0 ({map.YAxis[0]:F0}): X=[{string.Join(",", System.Linq.Enumerable.Range(0, used).Select(i => xPts[i].ToString("F1")))}] V=[{string.Join(",", System.Linq.Enumerable.Range(0, used).Select(i => vPts[i].ToString("F1")))}] slope={slope:F4} vBase={vBase:F1} -> V1={newVal1:F1} V2={newVal2:F1}");

                        // Typ-spezifische Grenzen
                        if (type == MapType.InjectionTiming)
                        {
                            if (vBase <= 0) { newVal1 = 0; newVal2 = 0; }
                            else { newVal1 = Math.Max(0, newVal1); newVal2 = Math.Max(0, newVal2); }
                        }
                        if (type == MapType.RailPressure)
                        {
                            newVal1 = Math.Min(newVal1, railLimit);
                            newVal2 = Math.Min(newVal2, railLimit);
                        }
                        if (type == MapType.BoostMap)
                        {
                            newVal1 = Math.Min(newVal1, boostLimit);
                            newVal2 = Math.Min(newVal2, boostLimit);
                        }

                        // NUR modCol1 und modCol2 schreiben
                        double origVal1 = map.GetOriginalValue(r, modCol1);
                        double origVal2 = map.GetOriginalValue(r, modCol2);
                        if (Math.Abs(origVal1 - newVal1) > 1e-9)
                        {
                            map.Values[r, modCol1] = newVal1;
                            map.MarkCellModified(r, modCol1);
                        }
                        if (Math.Abs(origVal2 - newVal2) > 1e-9)
                        {
                            map.Values[r, modCol2] = newVal2;
                            map.MarkCellModified(r, modCol2);
                        }
                    }
                    return true;
                }
                    // X-Achse letzte Spalte auf targetFuel, Lambda-Werte = vorletzte Spalte
                    case MapType.SmokeLimit:
                {
                    if (prevCol < 0) return false;
                    double oldXLast = map.XAxis[lastCol];
                    if (targetFuel <= oldXLast + 1e-9) return false; // bereits ausreichend
                    map.XAxis[lastCol] = targetFuel;
                    for (int r = 0; r < map.Rows; r++)
                    {
                        // Lambda beibehalten (nicht unter 1.12)
                        double lambdaVal = Math.Max(map.Values[r, prevCol], 1.12);
                        map.Values[r, lastCol] = lambdaVal;
                        map.MarkCellModified(r, lastCol);
                    }
                    return true;
                }

                // Eingespritzte KST f(RPM,Nm): X-Achse (Nm) letzte Spalte * ratio, Werte extrapolieren
                case MapType.InjectionMap:
                {
                    if (prevCol < 0) return false;
                    double oldXLast = map.XAxis[lastCol];
                    double oldXPrev = map.XAxis[prevCol];
                    map.XAxis[lastCol] = oldXLast * ratio;

                    for (int r = 0; r < map.Rows; r++)
                    {
                        double vPrev = map.Values[r, prevCol];
                        double vLast = map.Values[r, lastCol];
                        double xSpan = oldXLast - oldXPrev;
                        double slope = Math.Abs(xSpan) > 1e-9
                            ? (vLast - vPrev) / xSpan
                            : 0;
                        double newVal = vLast + slope * (map.XAxis[lastCol] - oldXLast);
                        map.Values[r, lastCol] = Math.Max(0, newVal);
                        map.MarkCellModified(r, lastCol);
                    }
                    return true;
                }
            }
            return false;
        }

        private static void ScaleAllValues(MapModel map, double ratio, double maxVal = double.MaxValue)
        {
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    double newV = Math.Min(map.Values[r, c] * ratio, maxVal);
                    if (Math.Abs(map.Values[r, c] - newV) > 1e-9)
                    {
                        map.Values[r, c] = newV;
                        map.MarkCellModified(r, c);
                    }
                }
        }

        /// <summary>
        /// Berechnet den Steigungskoeffizienten einer linearen Regression (Least Squares) ueber n Punkte.
        /// Gibt die Steigung dV/dX zurueck.
        /// </summary>
        private static double LinearRegressionSlope(double[] x, double[] v, int n)
        {
            if (n < 2) return 0;

            double sumX = 0, sumV = 0, sumXX = 0, sumXV = 0;
            int    used = 0;
            for (int i = 0; i < n; i++)
            {
                // Punkte mit v=0 beim Einspritzzeitpunkt ausschliessen (Null = kein Foerderbeginn)
                sumX  += x[i];
                sumV  += v[i];
                sumXX += x[i] * x[i];
                sumXV += x[i] * v[i];
                used++;
            }
            double denom = used * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-12) return 0;
            return (used * sumXV - sumX * sumV) / denom;
        }
    }
}
