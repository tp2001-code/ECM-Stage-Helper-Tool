using System;
using System.Collections.Generic;

namespace ECM_Stage_Helper_Tool
{
    public class PowerResult
    {
        public bool   HasData          { get; set; }
        public string TorqueMapName    { get; set; }
        public double MaxKw            { get; set; }
        public double MaxPs            { get; set; }
        public double MaxNm            { get; set; }
        public double RpmAtMaxPower    { get; set; }
        public double RpmAtMaxNm       { get; set; }
        public double YAxisUsed        { get; set; }
        public string YAxisLabel       { get; set; }

        public string FormatSummary()
        {
            if (!HasData)
                return "Leistungsschaetzung: Keine Nm-Map erkannt  (Unit: Nm + Achse |RPM erwartet)";

            string yInfo = YAxisLabel != null
                ? string.Format("  [{0}: {1:F0}]", YAxisLabel, YAxisUsed)
                : string.Empty;

            return string.Format(
                "\u2248 {0:F0} kW  /  {1:F0} PS    |    Max: {2:F0} Nm @ {3:F0} RPM    |    Leistungsmax @ {4:F0} RPM{5}",
                MaxKw, MaxPs, MaxNm, RpmAtMaxNm, RpmAtMaxPower, yInfo);
        }
    }

    public static class PowerEstimator
    {
        // PS = (Nm * RPM) / 7023.5  (identisch zu P_W = Nm * 2pi * RPM/60, dann PS = P_W / 735.499)
        private static double NmRpmToPs(double nm, double rpm) => (nm * rpm) / 7023.5;
        private static double NmRpmToKw(double nm, double rpm) => (nm * rpm) / 9549.3;

        public static PowerResult Calculate(IEnumerable<MapModel> maps)
        {
            var result = new PowerResult();
            var torqueMap = FindTorqueMap(maps);
            if (torqueMap == null)
                return result;

            result.HasData       = true;
            result.TorqueMapName = torqueMap.MapName ?? torqueMap.Name;

            string yLabel = torqueMap.AxisLabel != null
                ? torqueMap.AxisLabel.Split('|')[0].Trim()
                : null;

            double maxNm    = 0.0;
            int    maxNmRow = 0;

            for (int c = 0; c < torqueMap.Cols; c++)
            {
                double rpm = torqueMap.XAxis[c];
                if (rpm <= 0) continue;

                // Maximum-Nm über alle Zeilen (Y-Achse) für diese RPM-Spalte
                double nmMax    = 0.0;
                int    nmMaxRow = 0;
                for (int r = 0; r < torqueMap.Rows; r++)
                {
                    if (torqueMap.Values[r, c] > nmMax)
                    {
                        nmMax    = torqueMap.Values[r, c];
                        nmMaxRow = r;
                    }
                }

                if (nmMax <= 0) continue;

                double powerPs = NmRpmToPs(nmMax, rpm);
                double powerKw = NmRpmToKw(nmMax, rpm);

                if (powerPs > result.MaxPs)
                {
                    result.MaxPs         = powerPs;
                    result.MaxKw         = powerKw;
                    result.RpmAtMaxPower = rpm;
                    result.YAxisUsed     = torqueMap.YAxis[nmMaxRow];
                    result.YAxisLabel    = yLabel;
                }
                if (nmMax > maxNm)
                {
                    maxNm    = nmMax;
                    maxNmRow = nmMaxRow;
                    result.RpmAtMaxNm = rpm;
                }
            }

            result.MaxNm = maxNm;
            return result;
        }

        private static MapModel FindTorqueMap(IEnumerable<MapModel> maps)
        {
            foreach (var m in maps)
            {
                bool unitIsNm = m.Unit != null &&
                    m.Unit.IndexOf("Nm", StringComparison.OrdinalIgnoreCase) >= 0;
                bool xAxisIsRpm = m.AxisLabel != null &&
                    m.AxisLabel.IndexOf("|RPM", StringComparison.OrdinalIgnoreCase) >= 0;
                if (unitIsNm && xAxisIsRpm)
                    return m;
            }
            return null;
        }
    }
}
