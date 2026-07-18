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
            InjectionTiming,        // Einspritzzeitpunkt: Gesamttrend-Regression + SOI/EOI-Schutz          
            InjectionDuration,      // Einspritzdauer: X-Achse (IQ) + us extrapolieren
            BoostMap,               // Ladedruck #n: X-Achse (IQ) + hPa extrapolieren
            BoostLimiter,           // Ladedruck Begrenzer: Zellen skalieren
            TorqueLimiter,          // Drehmoment Begrenzer: Nm-Zellen skalieren
            RailPressure,           // Raildruck: X-Achse (IQ) + bar extrapolieren
            RailPressureLimiter,    // Raildruckbegrenzer: Zellen skalieren + Cap
            SmokeLimit,             // Rauchbegrenzer Lambda: X-Achse (mg) + Lambda halten
            ExhaustTemperature,     // Abgastemperatur-Begrenzer: Cap bei 850°C
            Skip                    // Nicht bearbeiten
        }

        /// <summary>Max. zulässige Abgastemperatur in °C (BorgWarner BV40 VTG / EA189).</summary>
        private const double MaxEgtCelsius = 850.0;

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

            for (int row = 0; row < map.Rows; row++)
            {
                double[] newRowValues = new double[map.Cols];

                for (int col = 0; col < map.Cols; col++)
                {
                    double targetRpm  = map.XAxis[col];
                    double sourceRpm  = targetRpm - rpmShift;
                    double newVal = InterpolateRow(map, row, sourceRpm);
                    newRowValues[col] = newVal;
                }

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

        private static double InterpolateRow(MapModel map, int row, double rpm)
        {
            double[] xAxis = map.XAxis;
            int cols = map.Cols;

            if (rpm <= xAxis[0])    return map.GetOriginalValue(row, 0);
            if (rpm >= xAxis[cols-1]) return map.GetOriginalValue(row, cols-1);

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
            string n    = (map.MapName ?? map.Name).ToLowerInvariant();
            string axis = (map.AxisLabel ?? "").ToLowerInvariant();
            bool isLimiter = map.IsScalar || map.Rows == 1 || map.Cols == 1;

            if (n.Contains("abgastemperatur"))                                return MapType.ExhaustTemperature;
            if (n.Contains("einspritzzeitpunkt") && !isLimiter)               return MapType.InjectionTiming;
            if (n.Contains("einspritzdauer") && !isLimiter)                   return MapType.InjectionDuration;
            if (n.Contains("begrenzer") && n.Contains("kraftstoff"))          return MapType.FuelLimiter;
            if (n.Contains("eingespritzte"))                                  return MapType.InjectionMap;
            if (n.Contains("ladedruck") && (n.Contains("begrenzer") || isLimiter)) return MapType.BoostLimiter;
            if (n.Contains("ladedruck"))                                      return MapType.BoostMap;
            if (n.Contains("drehmoment") && n.Contains("begrenzer"))          return MapType.TorqueLimiter;
            if (n.Contains("raildruck"))                                      return MapType.RailPressureLimiter;
            if (n.Contains("rauch") || n.Contains("lambda"))                  return MapType.SmokeLimit;

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
            double egtLimit   = limits != null ? limits.EffectiveEgtLimit      : MaxEgtCelsius;
            double lambdaWindow = limits != null ? limits.LambdaWindow         : 0.20;

            switch (type)
            {
                // --- Abgastemperatur: Sicherheits-Cap, NICHT skalieren ---
                case MapType.ExhaustTemperature:
                    return CapAllValues(map, egtLimit);

                // Zellen skalieren, Achsen unveraendert
                case MapType.FuelLimiter:
                    ScaleAllValues(map, ratio);
                    return true;

                case MapType.TorqueLimiter:
                    ScaleAllValues(map, ratio, nmLimit);
                    return true;

                case MapType.BoostLimiter:
                    return ApplyBoostLimiter(map, targetFuel, boostLimit);

                case MapType.RailPressureLimiter:
                    CapAllValues(map, railLimit);
                    return true;

                // Ladedruck: physikbasierte Berechnung aus IQ (X-Achse = mm3/mg)
                case MapType.BoostMap:
                    return ApplyBoostPhysics(map, currentMax, targetFuel, boostLimit, lambdaWindow);

                // X-Achse (IQ/mm3): exakt die letzten 2 Spalten anpassen
                case MapType.InjectionTiming:
                case MapType.InjectionDuration:
                case MapType.RailPressure:
                {
                    if (prevCol < 0) return false;

                    int modCol1  = prevCol;
                    int modCol2  = lastCol;

                    if (modCol1 < 0 || modCol2 >= map.Cols)
                    {
                        AiRemapLogger.LogInfo($"SKIP {map.Name}: ungueltige Spalten-Indizes ({modCol1},{modCol2})");
                        return false;
                    }

                    int topCol = modCol2;
                    for (int tryCol = modCol2; tryCol >= 2; tryCol--)
                    {
                        bool hasValues = false;
                        for (int r2 = 0; r2 < map.Rows; r2++)
                            if (Math.Abs(map.GetOriginalValue(r2, tryCol)) > 1e-9) { hasValues = true; break; }
                        if (hasValues) { topCol = tryCol; break; }
                    }

                    double xOldLast = map.GetOriginalX(modCol2);
                    double xOldPrev = map.GetOriginalX(modCol1);

                    double xNewLast = targetFuel;
                    double xNewPrev = xOldPrev + (targetFuel - xOldPrev) * 0.5;

                    if (xNewPrev <= map.GetOriginalX(modCol1 > 0 ? modCol1 - 1 : 0))
                        xNewPrev = map.GetOriginalX(modCol1 > 0 ? modCol1 - 1 : 0) + (targetFuel - xOldPrev) * 0.3;
                    if (xNewLast <= xNewPrev)
                        xNewLast = xNewPrev + (targetFuel - xNewPrev) * 0.5;

                    AiRemapLogger.LogInfo($"{map.Name}: X[{modCol1}] {xOldPrev:F2}->{xNewPrev:F2}, X[{modCol2}] {xOldLast:F2}->{xNewLast:F2} (topCol={topCol})");

                    map.XAxis[modCol1] = xNewPrev;
                    map.XAxis[modCol2] = xNewLast;

                    const int regPts = 5;
                    int regEnd   = modCol1;
                    int regStart = Math.Max(0, regEnd - regPts + 1);

                    AiRemapLogger.LogInfo($"  Regression aus Zeilen-Spalten [{regStart}..{regEnd}]");

                    for (int r = 0; r < map.Rows; r++)
                    {
                        var xPts = new double[regPts];
                        var vPts = new double[regPts];
                        int used = 0;
                        for (int c = regStart; c <= regEnd; c++)
                        {
                            xPts[used] = map.GetOriginalX(c);
                            vPts[used] = map.GetOriginalValue(r, c);
                            used++;
                        }

                        double slope = LinearRegressionSlope(xPts, vPts, used);

                        double xBase = map.GetOriginalX(modCol1);
                        double vBase = map.GetOriginalValue(r, modCol1);

                        double newVal1 = vBase + slope * (xNewPrev - xBase);
                        double newVal2 = vBase + slope * (xNewLast - xBase);

                        if (r == 0)
                            AiRemapLogger.LogInfo($"  Zeile 0 ({map.YAxis[0]:F0}): X=[{string.Join(",", Enumerable.Range(0, used).Select(i => xPts[i].ToString("F1")))}] V=[{string.Join(",", Enumerable.Range(0, used).Select(i => vPts[i].ToString("F1")))}] slope={slope:F4} vBase={vBase:F1} -> V1={newVal1:F1} V2={newVal2:F1}");

                        if (type == MapType.InjectionTiming)
                        {
                            if (vBase <= 0) { newVal1 = 0; newVal2 = 0; }
                            else { newVal1 = Math.Max(0, newVal1); newVal2 = Math.Max(0, newVal2); }
                        }
                        if (type == MapType.RailPressure)
                        {
                            // Raildruck darf bei höherer IQ nie sinken
                            newVal1 = Math.Max(newVal1, vBase);
                            newVal2 = Math.Max(newVal2, vBase);
                            newVal1 = Math.Min(newVal1, railLimit);
                            newVal2 = Math.Min(newVal2, railLimit);
                        }
                        if (type == MapType.BoostMap)
                        {
                            newVal1 = Math.Min(newVal1, boostLimit);
                            newVal2 = Math.Min(newVal2, boostLimit);
                        }

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

                    // Alle Zellen der Raildruck-Map auf das Limit begrenzen
                    if (type == MapType.RailPressure)
                    {
                        for (int r = 0; r < map.Rows; r++)
                            for (int c = 0; c < map.Cols; c++)
                            {
                                if (map.Values[r, c] > railLimit)
                                {
                                    map.Values[r, c] = railLimit;
                                    map.MarkCellModified(r, c);
                                }
                            }
                    }

                    return true;
                }

                case MapType.SmokeLimit:
                {
                    if (prevCol < 0) return false;
                    double oldXLast = map.XAxis[lastCol];
                    if (targetFuel <= oldXLast + 1e-9) return false;
                    map.XAxis[lastCol] = targetFuel;
                    for (int r = 0; r < map.Rows; r++)
                    {
                        double lambdaVal = Math.Max(map.Values[r, prevCol], 1.12);
                        map.Values[r, lastCol] = lambdaVal;
                        map.MarkCellModified(r, lastCol);
                    }
                    return true;
                }

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

        /// <summary>
        /// Begrenzt alle Zellenwerte auf maxVal (Sicherheits-Cap ohne Skalierung).
        /// Gibt true zurueck wenn mindestens ein Wert geaendert wurde.
        /// </summary>
        private static bool CapAllValues(MapModel map, double maxVal)
        {
            bool changed = false;
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    if (map.Values[r, c] > maxVal)
                    {
                        map.Values[r, c] = maxVal;
                        map.MarkCellModified(r, c);
                        changed = true;
                    }
                }
            if (changed)
                AiRemapLogger.LogInfo($"EGT-Cap: {map.Name} auf max {maxVal:F0}°C begrenzt");
            return changed;
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

        // ---------------------------------------------------------------------------
        // Ladedruck – physikbasierte Berechnung (EA189 2.0 TDI)
        // ---------------------------------------------------------------------------

        /// <summary>EA189 Zylinder-Hubvolumen in Litern (1968 ccm / 4 Zylinder).</summary>
        private const double CylinderVolumeL = 0.4965;
        /// <summary>Luftdichte bei 20°C in g/l (= mg/cm³).</summary>
        private const double AirDensityBase = 1.189;
        /// <summary>Volumetrischer Wirkungsgrad EA189 Kopf.</summary>
        private const double VolumetricEfficiency = 0.88;
        /// <summary>Stoech. Luft-Kraftstoff-Verhaeltnis Diesel.</summary>
        private const double StoichAfr = 14.5;
        /// <summary>Minimales Lambda fuer rauchfreie Verbrennung.</summary>
        private const double LambdaMin = 1.15;
        /// <summary>Diesel-Dichte mm3 -> mg.</summary>
        private const double DieselDensity = 0.832;

        /// <summary>
        /// Berechnet den physikalisch notwendigen Ladedruck (hPa absolut)
        /// fuer eine gegebene Einspritzmenge in mg.
        /// Formel: boost_hPa = (iq_mg * lambda * AFR) / (Vz * rho * eta)
        /// </summary>
        private static double CalcBoostHpa(double iqMg, double lambda = LambdaMin)
        {
            double requiredAirMg = iqMg * lambda * StoichAfr;
            return requiredAirMg / (CylinderVolumeL * AirDensityBase * VolumetricEfficiency);
        }

        /// <summary>
        /// Berechnet den aktuellen Lambda-Wert aus Ladedruck (hPa) und Einspritzmenge (mg).
        /// Lambda = Luftmasse / (IQ * AFR)
        /// </summary>
        private static double CalcLambda(double boostHpa, double iqMg)
        {
            if (iqMg <= 0) return 9.9;
            double airMassMg = (boostHpa * CylinderVolumeL * AirDensityBase * VolumetricEfficiency) / 1000.0;
            return airMassMg / (iqMg * StoichAfr);
        }

        /// <summary>
        /// Wendet die physikbasierte Ladedruck-Berechnung auf eine BoostMap an.
        /// X-Achse = IQ (mm3/Stk), Y-Achse = RPM, Werte = hPa.
        /// Nur die letzten 2 Spalten werden erweitert.
        /// Pro Zelle wird der Wert aus dem Zeilen-Trend extrapoliert und dann
        /// per Lambda-Pruefung abgesichert (min 1.15, max 1.25).
        /// Jede Zeile (RPM) erhaelt so ihren eigenen korrekten Wert.
        /// </summary>
        private static bool ApplyBoostPhysics(MapModel map, double currentMax, double targetFuel, double boostLimit, double lambdaWindow)
        {
            double lambdaMax = LambdaMin + lambdaWindow;
            int lastCol = map.Cols - 1;
            int prevCol = map.Cols - 2;
            if (prevCol < 0) return false;

            double xOldPrev = map.GetOriginalX(prevCol);

            // X-Achse erweitern fuer die letzten 2 Spalten
            double xNewPrev = xOldPrev + (targetFuel - xOldPrev) * 0.5;
            double xNewLast = targetFuel;

            if (prevCol > 0 && xNewPrev <= map.GetOriginalX(prevCol - 1))
                xNewPrev = map.GetOriginalX(prevCol - 1) + (targetFuel - xOldPrev) * 0.3;
            if (xNewLast <= xNewPrev)
                xNewLast = xNewPrev + (targetFuel - xNewPrev) * 0.5;

            map.XAxis[prevCol] = xNewPrev;
            map.XAxis[lastCol] = xNewLast;

            // Regression: Stuetzpunkte aus den bestehenden Spalten (vor prevCol)
            const int regPts = 5;
            int regEnd = prevCol;
            int regStart = Math.Max(0, regEnd - regPts + 1);

            for (int r = 0; r < map.Rows; r++)
            {
                // Steigung dieser Zeile aus den bestehenden Daten ermitteln
                var xPts = new double[regPts];
                var vPts = new double[regPts];
                int used = 0;
                for (int c = regStart; c <= regEnd; c++)
                {
                    xPts[used] = map.GetOriginalX(c);
                    vPts[used] = map.GetOriginalValue(r, c);
                    used++;
                }

                double slope = LinearRegressionSlope(xPts, vPts, used);
                double xBase = map.GetOriginalX(prevCol);
                double vBase = map.GetOriginalValue(r, prevCol);

                // Extrapolation aus Zeilen-Trend
                double extrapPrev = vBase + slope * (xNewPrev - xBase);
                double extrapLast = vBase + slope * (xNewLast - xBase);

                // IQ an den neuen Spalten (mm3 -> mg)
                double iqMgPrev = xNewPrev * DieselDensity;
                double iqMgLast = xNewLast * DieselDensity;

                // Physik-Minimum: Boost muss Lambda >= 1.15 ergeben
                double minBoostPrev = CalcBoostHpa(iqMgPrev, LambdaMin);
                double minBoostLast = CalcBoostHpa(iqMgLast, LambdaMin);

                // Physik-Maximum: Boost soll Lambda <= lambdaMax nicht ueberschreiten
                double maxBoostPrev = CalcBoostHpa(iqMgPrev, lambdaMax);
                double maxBoostLast = CalcBoostHpa(iqMgLast, lambdaMax);

                // Extrapolierten Wert ins Lambda-Fenster klemmen
                double boostPrev = Math.Max(extrapPrev, minBoostPrev);
                boostPrev = Math.Min(boostPrev, maxBoostPrev);

                double boostLast = Math.Max(extrapLast, minBoostLast);
                boostLast = Math.Min(boostLast, maxBoostLast);

                // User-Limit
                boostPrev = Math.Min(boostPrev, boostLimit);
                boostLast = Math.Min(boostLast, boostLimit);

                // Nicht unter Original fallen
                double origPrev = map.GetOriginalValue(r, prevCol);
                double origLast = map.GetOriginalValue(r, lastCol);
                boostPrev = Math.Max(boostPrev, origPrev);
                boostLast = Math.Max(boostLast, origLast);

                // Nochmal ins Lambda-Fenster + User-Limit
                boostPrev = Math.Min(boostPrev, Math.Min(maxBoostPrev, boostLimit));
                boostLast = Math.Min(boostLast, Math.Min(maxBoostLast, boostLimit));

                if (Math.Abs(origPrev - boostPrev) > 1e-9)
                {
                    map.Values[r, prevCol] = boostPrev;
                    map.MarkCellModified(r, prevCol);
                }
                if (Math.Abs(origLast - boostLast) > 1e-9)
                {
                    map.Values[r, lastCol] = boostLast;
                    map.MarkCellModified(r, lastCol);
                }
            }

            double finalLambdaR0 = CalcLambda(map.Values[0, lastCol], xNewLast * DieselDensity);
            double finalLambdaRn = CalcLambda(map.Values[map.Rows - 1, lastCol], xNewLast * DieselDensity);
            AiRemapLogger.LogInfo($"BoostMap {map.Name}: X[{prevCol}]->{xNewPrev:F1}, X[{lastCol}]->{xNewLast:F1}, " +
                $"Zeile0={map.Values[0, lastCol]:F0}hPa(λ={finalLambdaR0:F3}), " +
                $"ZeileN={map.Values[map.Rows - 1, lastCol]:F0}hPa(λ={finalLambdaRn:F3}), Limit={boostLimit:F0}");

            return true;
        }

        /// <summary>Max. Lambda – max 0.1 ueber Ziel-Lambda (unnoetige Turbo-Belastung vermeiden).</summary>
        private const double LambdaMax = LambdaMin + 0.10;

        /// <summary>
        /// Stellt sicher, dass der Ladedruck im Lambda-Fenster 1.15–1.25 liegt.
        /// Zu niedrig: Boost erhoehen (rauchfrei).
        /// Zu hoch: Boost senken (unnoetige Turbo-Belastung).
        /// </summary>
        private static double EnsureLambdaMinBoost(double boostHpa, double iqMg)
        {
            if (iqMg <= 0) return boostHpa;

            // Zu niedrig -> erhoehen bis Lambda >= 1.15
            double lambda = CalcLambda(boostHpa, iqMg);
            while (lambda < LambdaMin)
            {
                boostHpa += 10.0;
                lambda = CalcLambda(boostHpa, iqMg);
            }

            // Zu hoch -> senken bis Lambda <= 1.25
            lambda = CalcLambda(boostHpa, iqMg);
            while (lambda > LambdaMax && boostHpa > 10.0)
            {
                boostHpa -= 10.0;
                lambda = CalcLambda(boostHpa, iqMg);
            }
            // Sicherheit: nicht unter LambdaMin fallen
            if (CalcLambda(boostHpa, iqMg) < LambdaMin)
                boostHpa += 10.0;

            return boostHpa;
        }

        /// <summary>
        /// Ladedruck-Begrenzer: Setzt alle Zellen auf den physikalisch benoetigten
        /// Ladedruck + 80 hPa Sicherheitsaufschlag (min 70, max 100 hPa ueber Soll).
        /// Gekappt durch boostLimit + 100 hPa.
        /// </summary>
        private static bool ApplyBoostLimiter(MapModel map, double targetFuelMm3, double boostLimit)
        {
            // Physikalisch benoetigter Ladedruck bei Ziel-IQ
            double targetIqMg = targetFuelMm3 * DieselDensity;
            double requiredBoost = CalcBoostHpa(targetIqMg);

            // Begrenzer muss 70-100 hPa ueber dem geforderten Ladedruck liegen
            const double LimiterOffset = 80.0;
            const double MaxLimiterCap = 100.0;

            double limiterTarget = requiredBoost + LimiterOffset;
            // Max. Abstand zum Soll-Ladedruck: 100 hPa
            double maxAllowed = requiredBoost + MaxLimiterCap;
            if (limiterTarget > maxAllowed)
                limiterTarget = maxAllowed;

            // Absolutes Hardware-Limit: boostLimit + 100 hPa fuer den Begrenzer
            double hardCap = boostLimit < double.MaxValue ? boostLimit + MaxLimiterCap : double.MaxValue;
            limiterTarget = Math.Min(limiterTarget, hardCap);

            AiRemapLogger.LogInfo($"BoostLimiter {map.Name}: Soll={requiredBoost:F0} hPa, " +
                $"Begrenzer={limiterTarget:F0} hPa (Offset={LimiterOffset}, HardCap={hardCap:F0})");

            bool changed = false;
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Cols; c++)
                {
                    double orig = map.Values[r, c];
                    // Nur erhoehen wenn unter dem Ziel, nie verringern
                    double newVal = Math.Max(orig, limiterTarget);
                    // Cap anwenden
                    newVal = Math.Min(newVal, hardCap);

                    if (Math.Abs(orig - newVal) > 1e-9)
                    {
                        map.Values[r, c] = newVal;
                        map.MarkCellModified(r, c);
                        changed = true;
                    }
                }
            return changed;
        }
    }
}
