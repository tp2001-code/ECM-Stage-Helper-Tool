using System;
using System.Collections.Generic;
using System.Linq;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// Lokale Remap-Berechnung fuer EA189 2.0 TDI (EDC17).
    /// Kein API-Key noetig – alle Berechnungen erfolgen offline.
    /// </summary>
    public static class AiRemapClient
    {
        // Physikalische Konstanten
        private const double DieselDensity = 0.832;       // g/cm³ ? mm³ × 0.832 = mg
        private const double PsConstant    = 7120.6;      // PS = (Nm × RPM) / 7120.6
        private const double KwConstant    = 9549.3;      // kW = (Nm × RPM) / 9549.3
        private const double StoichAfr     = 14.5;        // Stoechiometrisches Luft-Kraftstoff-Verhaeltnis Diesel
        private const double LambdaLimit   = 1.15;        // Min. Lambda fuer rauchfreie Verbrennung

        // Stuetzstellen: RPM ? IQ-zu-indiziertes-Nm Faktor
        private static readonly double[] FactorRpm = { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500 };
        private static readonly double[] FactorVal = { 5.80, 5.80, 5.75, 5.70, 5.50, 5.20, 4.80, 4.50 };

        // Stuetzstellen: RPM ? Reibungsverluste (Nm)
        private static readonly double[] FrictionRpm = { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500 };
        private static readonly double[] FrictionVal = {   30,   35,   40,   45,   48,   52,   55,   58 };

        /// <summary>Lineare Interpolation in einer Stuetzstellen-Tabelle.</summary>
        private static double InterpolatePhysics(double[] xTable, double[] yTable, double x)
        {
            if (x <= xTable[0]) return yTable[0];
            if (x >= xTable[xTable.Length - 1]) return yTable[yTable.Length - 1];
            for (int i = 1; i < xTable.Length; i++)
            {
                if (x <= xTable[i])
                {
                    double frac = (x - xTable[i - 1]) / (xTable[i] - xTable[i - 1]);
                    return yTable[i - 1] + frac * (yTable[i] - yTable[i - 1]);
                }
            }
            return yTable[yTable.Length - 1];
        }

        // -----------------------------------------------------------------------
        // Phase 1 – Analyse (komplett lokal)
        // -----------------------------------------------------------------------

        public static AnalysisResult AnalyseLocal(IEnumerable<MapModel> maps)
        {
            var mapList = maps.ToList();

            double localMaxFuel         = CalcMaxFuelFromLimiter(mapList);
            double localMaxTorque       = CalcMaxTorqueFromLimiter(mapList);
            double localMaxTorqueRpm    = CalcMaxTorqueRpm(mapList);
            double localMaxBoost        = CalcMaxBoost(mapList);
            double localMaxRailPressure = CalcMaxRailPressure(mapList);

            var powerResult      = CalcMaxPower(mapList);
            double localMaxPowerKw  = powerResult.kw;
            double localMaxPowerPs  = powerResult.ps;
            double localMaxPowerRpm = powerResult.rpm;

            AiRemapLogger.LogInfo(
                $"Lokale Analyse: MaxFuel={localMaxFuel:F2}, MaxTorque={localMaxTorque:F1} Nm @ {localMaxTorqueRpm:F0} RPM, " +
                $"MaxPower={localMaxPowerKw:F1} kW / {localMaxPowerPs:F0} PS, MaxBoost={localMaxBoost:F0} hPa, MaxRail={localMaxRailPressure:F0} bar");

            // MapsToChange lokal bestimmen
            double lambdaXMax  = CalcLambdaFuelLimit(mapList);
            double targetEst   = localMaxFuel * 1.25;
            bool   needsLambda = targetEst > lambdaXMax;
            var mapsToChange = mapList
                .Where(m => NeedsRemap(m, targetEst, needsLambda))
                .Select(m => m.Name)
                .ToList();

            // Summary lokal generieren
            string summary = $"EA189 2.0 TDI – Ist-Zustand: " +
                $"{localMaxPowerKw:F1} kW / {localMaxPowerPs:F0} PS bei {localMaxPowerRpm:F0} RPM, " +
                $"max. {localMaxTorque:F0} Nm bei {localMaxTorqueRpm:F0} RPM, " +
                $"IQ={localMaxFuel:F1} mm3/Stk, Ladedruck={localMaxBoost:F0} hPa, " +
                $"Raildruck={localMaxRailPressure:F0} bar.";

            return new AnalysisResult
            {
                MaxFuelQuantity    = localMaxFuel,
                FuelUnit           = "mm3/Stk",
                MaxPowerKw         = localMaxPowerKw,
                MaxTorqueNm        = localMaxTorque,
                MaxTorqueRpm       = localMaxTorqueRpm,
                MaxPowerRpm        = localMaxPowerRpm,
                MaxBoostHpa        = localMaxBoost,
                MaxRailPressureBar = localMaxRailPressure,
                Summary            = summary,
                MapsToChange       = mapsToChange
            };
        }

        // -----------------------------------------------------------------------
        // Phase 2 – Remap (komplett lokal)
        // -----------------------------------------------------------------------

        public static RemapResult RemapLocal(IEnumerable<MapModel> maps, double targetFuel,
            AnalysisResult analysis, Action<string, MapRemap, bool> mapDoneCallback = null,
            RemapLimits limits = null)
        {
            var mapList = maps.ToList();

            double currentMax  = analysis.MaxFuelQuantity;
            double lambdaXMax  = CalcLambdaFuelLimit(mapList);
            double ratio       = targetFuel / currentMax;
            bool   needsLambda = targetFuel > lambdaXMax;

            AiRemapLogger.LogInfo($"Remap: {currentMax:F2} -> {targetFuel:F2} mm3/Hub, Faktor={targetFuel / currentMax:F4}");

            var result = new RemapResult
            {
                ExpectedPowerKw  = 0,
                ExpectedTorqueNm = 0,
                Notes = $"Remap {currentMax:F2} -> {targetFuel:F2} mm3/Hub",
                Maps  = new List<MapRemap>()
            };

            var mapsToProcess = mapList.Where(m => NeedsRemap(m, targetFuel, needsLambda)).ToList();
            AiRemapLogger.LogInfo($"{mapsToProcess.Count} von {mapList.Count} Maps werden verarbeitet.");

            foreach (var map in mapsToProcess)
            {
                mapDoneCallback?.Invoke(map.Name, null, false);

                AiRemapLogger.LogInfo($"Verarbeite: {map.Name}");
                MapRemap mr = null;
                bool success = false;
                try
                {
                    var mapType = RemapEngine.Classify(map);
                    AiRemapLogger.LogInfo($"  Klassifiziert als: {mapType}");

                    bool changed = RemapEngine.Apply(map, currentMax, targetFuel, mapType, limits);

                    mr = new MapRemap
                    {
                        FileName          = map.Name,
                        ChangeDescription = $"{mapType}: {(changed ? "geaendert" : "unveraendert")}",
                        NewXAxis          = map.XAxis,
                        NewYAxis          = map.YAxis,
                        NewValues         = map.Values
                    };
                    result.Maps.Add(mr);
                    success = changed;
                }
                catch (Exception ex)
                {
                    AiRemapLogger.LogError($"ProcessMap {map.Name}", ex);
                    mr = new MapRemap
                    {
                        FileName = map.Name,
                        ChangeDescription = $"FEHLER: {ex.Message}",
                        NewXAxis = map.XAxis, NewYAxis = map.YAxis, NewValues = map.Values
                    };
                    result.Maps.Add(mr);
                }

                mapDoneCallback?.Invoke(map.Name, mr, success);
            }

            // Tatsaechliche Leistung nach Remap berechnen (mit physischen Caps)
            var postPower = PowerEstimator.Calculate(mapList);
            if (postPower.HasData)
            {
                result.ExpectedPowerKw  = postPower.MaxKw;
                result.ExpectedTorqueNm = postPower.MaxNm;
            }
            else
            {
                // Fallback: linear begrenzt durch Nm-Limit
                double nmLim = limits != null ? limits.EffectiveTorqueLimit : double.MaxValue;
                double estNm = Math.Min(analysis.MaxTorqueNm * ratio, nmLim);
                result.ExpectedTorqueNm = estNm;
                result.ExpectedPowerKw  = (estNm * analysis.MaxPowerRpm) / KwConstant;
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // NeedsRemap
        // -----------------------------------------------------------------------

        private static bool NeedsRemap(MapModel m, double targetFuel, bool needsLambda)
        {
            string n    = (m.MapName ?? m.Name).ToLowerInvariant();
            string axis = (m.AxisLabel ?? "").ToLowerInvariant();
            if (n.Contains("abgastemperatur"))                                return true;
            if (axis.Contains("mm3") && m.XAxis[m.Cols - 1] < targetFuel * 0.99) return true;
            if (n.Contains("begrenzer") && n.Contains("kraftstoff"))          return true;
            if (n.Contains("drehmoment") && n.Contains("begrenzer"))          return true;
            if (n.Contains("ladedruck")  && n.Contains("begrenzer"))          return true;
            if (n.Contains("raildruck"))                                      return true;
            if ((n.Contains("rauch") || n.Contains("lambda")) && needsLambda) return true;
            return false;
        }

        // -----------------------------------------------------------------------
        // Lokale Berechnungen
        // -----------------------------------------------------------------------

        private static double CalcMaxBoost(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("ladedruck") || n.Contains("begrenzer")) continue;
                int lastRow = m.Rows - 1;
                for (int c = 0; c < m.Cols; c++)
                    if (m.Values[lastRow, c] > max && m.Values[lastRow, c] < 5000)
                        max = m.Values[lastRow, c];
            }
            return max;
        }

        private static double CalcMaxRailPressure(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("raildruck")) continue;
                for (int r = 0; r < m.Rows; r++)
                    for (int c = 0; c < m.Cols; c++)
                        if (m.Values[r, c] > max && m.Values[r, c] < 3000)
                            max = m.Values[r, c];
            }
            return max;
        }

        private static double CalcLambdaFuelLimit(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("rauchbegrenzer") && !n.Contains("lambda")) continue;
                foreach (double x in m.XAxis)
                    if (x > max && x < 2000) max = x;
            }
            return max > 0 ? max : double.MaxValue;
        }

        private static double CalcMaxFuelFromLimiter(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("begrenzer") || !n.Contains("kraftstoffmenge")) continue;
                for (int r = 0; r < m.Rows; r++)
                    for (int c = 0; c < m.Cols; c++)
                        if (m.Values[r, c] > max && m.Values[r, c] < 200) max = m.Values[r, c];
            }
            // Fallback: breitere Suche
            if (max < 1)
            {
                foreach (var m in maps)
                {
                    string n = (m.MapName ?? m.Name).ToLowerInvariant();
                    if (!n.Contains("begrenzer") || !n.Contains("kraftstoff")) continue;
                    if (n.Contains("druck")) continue;
                    for (int r = 0; r < m.Rows; r++)
                        for (int c = 0; c < m.Cols; c++)
                            if (m.Values[r, c] > max && m.Values[r, c] < 200) max = m.Values[r, c];
                }
            }
            return max;
        }

        private static double CalcMaxTorqueFromLimiter(IEnumerable<MapModel> maps)
        {
            var mapList = maps as IList<MapModel> ?? maps.ToList();
            double scalarCap = GetScalarNmCap(mapList);

            // 2D Drehmoment-Begrenzer: Max bei Volllast
            MapModel torqueLimMap = FindTorqueLimiterMap(mapList);
            double nmFromMap = double.MaxValue;
            if (torqueLimMap != null)
            {
                int fullLoadRow = torqueLimMap.Rows - 1;
                double max = 0;
                for (int c = 0; c < torqueLimMap.Cols; c++)
                    if (torqueLimMap.Values[fullLoadRow, c] > max)
                        max = torqueLimMap.Values[fullLoadRow, c];
                if (max > 0) nmFromMap = max;
            }

            // MIN(2D-Map, skalar)
            double result = Math.Min(nmFromMap, scalarCap);
            return result < double.MaxValue ? result : 0;
        }

        // RPM-abhaengige Wirkungsgrad-Korrektur (relativ zu Peak bei ~2000 RPM)
        private static readonly double[] CorrRpm = { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500 };
        private static readonly double[] CorrVal = { 0.97, 0.99, 1.00, 0.99, 0.96, 0.91, 0.84, 0.78 };

        private static (double kw, double ps, double rpm) CalcMaxPower(IEnumerable<MapModel> maps)
        {
            var mapList = maps as IList<MapModel> ?? maps.ToList();

            MapModel fuelLimMap = null;
            foreach (var m in mapList)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("begrenzer") || !n.Contains("kraftstoff")) continue;
                if (n.Contains("druck")) continue;
                if (m.Rows > 1 && m.Cols > 1) { fuelLimMap = m; break; }
            }
            if (fuelLimMap == null) return (0, 0, 0);

            MapModel torqueLimMap = FindTorqueLimiterMap(mapList);
            double scalarNmCap = GetScalarNmCap(mapList);

            int lastCol = fuelLimMap.Cols - 1;

            // Kalibrierung: Faktor aus ORIGINAL Peak-IQ und ORIGINAL skalar Cap
            double calibFactor = 0;
            double origNmCap = GetOriginalScalarNmCap(mapList);
            if (origNmCap < double.MaxValue)
            {
                double peakIqMg = 0;
                for (int r = 0; r < fuelLimMap.Rows; r++)
                {
                    double rpm2 = fuelLimMap.YAxis[r];
                    if (rpm2 < 1500 || rpm2 > 2500) continue;
                    double iqMg2 = fuelLimMap.GetOriginalValue(r, lastCol) * DieselDensity;
                    if (iqMg2 > peakIqMg) peakIqMg = iqMg2;
                }
                if (peakIqMg > 1)
                    calibFactor = origNmCap / peakIqMg;
            }
            if (calibFactor <= 0) calibFactor = 5.15;

            double maxKw = 0, maxPs = 0, maxRpm = 0;

            for (int r = 0; r < fuelLimMap.Rows; r++)
            {
                double rpm = fuelLimMap.YAxis[r];
                if (rpm <= 0) continue;

                double iqMm3 = fuelLimMap.Values[r, lastCol];
                if (iqMm3 <= 0) continue;

                double iqMg = iqMm3 * DieselDensity;
                double nmFromFuel = iqMg * calibFactor * InterpolatePhysics(CorrRpm, CorrVal, rpm);

                double nmFromMap = double.MaxValue;
                if (torqueLimMap != null)
                {
                    double limit = GetTorqueLimitAtRpm(torqueLimMap, rpm);
                    if (limit > 0) nmFromMap = limit;
                }

                double nm = Math.Min(nmFromFuel, Math.Min(nmFromMap, scalarNmCap));
                if (nm <= 0) continue;

                double ps = (nm * rpm) / PsConstant;
                double kw = (nm * rpm) / KwConstant;
                if (ps > maxPs)
                {
                    maxKw  = kw;
                    maxPs  = ps;
                    maxRpm = rpm;
                }
            }
            return (maxKw, maxPs, maxRpm);
        }

        private static double GetScalarNmCap(IList<MapModel> maps)
        {
            double nmCap = double.MaxValue;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                if (m.IsScalar || (m.Rows == 1 && m.Cols == 1))
                {
                    double val = m.Values[0, 0];
                    if (val > 0 && val < nmCap) nmCap = val;
                }
            }
            return nmCap;
        }

        private static double GetOriginalScalarNmCap(IList<MapModel> maps)
        {
            double nmCap = double.MaxValue;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                if (m.IsScalar || (m.Rows == 1 && m.Cols == 1))
                {
                    double val = m.GetOriginalValue(0, 0);
                    if (val > 0 && val < nmCap) nmCap = val;
                }
            }
            return nmCap;
        }

        private static double CalcMaxTorqueRpm(IEnumerable<MapModel> maps)
        {
            MapModel fuelLimMap = null;
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("begrenzer") || !n.Contains("kraftstoff")) continue;
                if (n.Contains("druck")) continue;
                if (m.Rows > 1 && m.Cols > 1) { fuelLimMap = m; break; }
            }
            if (fuelLimMap == null) return 2000;

            int lastCol = fuelLimMap.Cols - 1;
            double maxIq = 0, rpmAtMax = 2000;
            for (int r = 0; r < fuelLimMap.Rows; r++)
            {
                if (fuelLimMap.Values[r, lastCol] > maxIq)
                {
                    maxIq = fuelLimMap.Values[r, lastCol];
                    rpmAtMax = fuelLimMap.YAxis[r];
                }
            }
            return rpmAtMax > 0 ? rpmAtMax : 2000;
        }

        // -----------------------------------------------------------------------
        // Hilfsmethoden fuer Drehmoment-Begrenzer
        // -----------------------------------------------------------------------

        private static MapModel FindTorqueLimiterMap(IEnumerable<MapModel> maps)
        {
            foreach (var m in maps)
            {
                string n = (m.MapName ?? m.Name).ToLowerInvariant();
                if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                if (m.IsScalar || (m.Rows == 1 && m.Cols == 1)) continue;
                bool hasRpm = m.AxisLabel != null &&
                    m.AxisLabel.IndexOf("RPM", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasRpm && m.Rows > 1 && m.Cols > 1) return m;
            }
            return null;
        }

        private static double GetTorqueLimitAtRpm(MapModel torqueLimMap, double rpm)
        {
            int fullLoadRow = torqueLimMap.Rows - 1;

            if (rpm <= torqueLimMap.XAxis[0])
                return torqueLimMap.Values[fullLoadRow, 0];
            if (rpm >= torqueLimMap.XAxis[torqueLimMap.Cols - 1])
                return torqueLimMap.Values[fullLoadRow, torqueLimMap.Cols - 1];

            for (int c = 1; c < torqueLimMap.Cols; c++)
            {
                if (torqueLimMap.XAxis[c] >= rpm)
                {
                    double rpmLo = torqueLimMap.XAxis[c - 1];
                    double rpmHi = torqueLimMap.XAxis[c];
                    double valLo = torqueLimMap.Values[fullLoadRow, c - 1];
                    double valHi = torqueLimMap.Values[fullLoadRow, c];
                    double span = rpmHi - rpmLo;
                    if (span < 1e-9) return valLo;
                    double frac = (rpm - rpmLo) / span;
                    return valLo + frac * (valHi - valLo);
                }
            }
            return torqueLimMap.Values[fullLoadRow, torqueLimMap.Cols - 1];
        }
    }
}
