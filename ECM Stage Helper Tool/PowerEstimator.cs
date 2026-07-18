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

        public string FormatSummary()
        {
            if (!HasData)
                return "Leistungsschaetzung: Keine Nm-Map erkannt  (Unit: Nm + Achse |RPM erwartet)";
            return string.Format(
                "\u2248 {0:F0} kW  /  {1:F0} PS    |    Max: {2:F0} Nm @ {3:F0} RPM    |    Leistungsmax @ {4:F0} RPM",
                MaxKw, MaxPs, MaxNm, RpmAtMaxNm, RpmAtMaxPower);
        }
    }

    public static class PowerEstimator
    {
        private const double PsPerWatt = 1.0 / 735.499;

        public static PowerResult Calculate(IEnumerable<MapModel> maps)
        {
            var result    = new PowerResult();
            var torqueMap = FindTorqueMap(maps);
            if (torqueMap == null) return result;

            result.HasData       = true;
            result.TorqueMapName = torqueMap.MapName ?? torqueMap.Name;

            double maxPowerW = 0.0;
            double maxNm     = 0.0;

            for (int c = 0; c < torqueMap.Cols; c++)
            {
                double rpm = torqueMap.XAxis[c];
                if (rpm <= 0) continue;

                double nmMax = 0.0;
                for (int r = 0; r < torqueMap.Rows; r++)
                    if (torqueMap.Values[r, c] > nmMax) nmMax = torqueMap.Values[r, c];

                if (nmMax <= 0) continue;

                double powerW = nmMax * (2.0 * Math.PI * rpm / 60.0);

                if (powerW > maxPowerW) { maxPowerW = powerW; result.RpmAtMaxPower = rpm; }
                if (nmMax  > maxNm)    { maxNm     = nmMax;   result.RpmAtMaxNm   = rpm; }
            }

            result.MaxKw = maxPowerW / 1000.0;
            result.MaxPs = maxPowerW * PsPerWatt;
            result.MaxNm = maxNm;
            return result;
        }

        private static MapModel FindTorqueMap(IEnumerable<MapModel> maps)
        {
            foreach (var m in maps)
            {
                bool unitIsNm   = m.Unit      != null && m.Unit.IndexOf("Nm", StringComparison.OrdinalIgnoreCase) >= 0;
                bool xAxisIsRpm = m.AxisLabel != null && m.AxisLabel.IndexOf("|RPM", StringComparison.OrdinalIgnoreCase) >= 0;
                if (unitIsNm && xAxisIsRpm) return m;
            }
            return null;
        }
    }
}
