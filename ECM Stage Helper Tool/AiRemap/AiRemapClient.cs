using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// Kommuniziert direkt mit der Anthropic Claude REST API.
    /// BenÃ¶tigt einen Anthropic API-Key (console.anthropic.com).
    ///   Phase 1 â€“ Analyse: ermittelt Max-Einspritzmenge, Max-Leistung, Max-Nm
    ///   Phase 2 â€“ Remap:   berechnet neue Map-Werte fÃ¼r eine Ziel-Einspritzmenge
    /// </summary>
    public class AiRemapClient
    {
        private const string ApiUrl     = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";
        private const string Model      = "claude-sonnet-4-5";
        private const int    MaxTokens  = 16000;

        private readonly string _apiKey;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = System.TimeSpan.FromMinutes(10)
        };

        public AiRemapClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        // -----------------------------------------------------------------------
        // Phase 1 â€“ Analyse
        // -----------------------------------------------------------------------

        public async Task<AnalysisResult> AnalyseAsync(IEnumerable<MapModel> maps)
        {
            var mapList = maps.ToList();

            // ---------------------------------------------------------------
            // Lokale Vorberechnung â€“ kein Verlass auf KI fÃ¼r kritische Werte
            // ---------------------------------------------------------------
            double localMaxFuel        = CalcMaxFuelFromLimiter(mapList);
            double localMaxTorque      = CalcMaxTorqueFromLimiter(mapList);
            double localMaxTorqueRpm   = CalcMaxTorqueRpm(mapList);
            double localMaxBoost       = CalcMaxBoost(mapList);
            double localMaxRailPressure = CalcMaxRailPressure(mapList);
            double localMaxPowerKw = localMaxTorque > 0 && localMaxTorqueRpm > 0
                ? (localMaxTorque * localMaxTorqueRpm) / 9549.3
                : 0;
            double localMaxPowerPs = localMaxTorque > 0 && localMaxTorqueRpm > 0
                ? (localMaxTorque * localMaxTorqueRpm) / 7023.5
                : 0;

            AiRemapLogger.LogInfo(
                $"Lokale Vorberechnung: MaxFuel={localMaxFuel:F2}, MaxTorque={localMaxTorque:F1} Nm @ {localMaxTorqueRpm:F0} RPM, " +
                $"MaxPower={localMaxPowerKw:F1} kW / {localMaxPowerPs:F0} PS, MaxBoost={localMaxBoost:F0} hPa, MaxRail={localMaxRailPressure:F0} bar");

            string mapsBlock = BuildMapsBlock(mapList);
            AiRemapLogger.LogInfo($"Phase 1 Analyse - Maps-Block: {System.Text.Encoding.UTF8.GetByteCount(mapsBlock):N0} Bytes, {mapList.Count} Maps");
            _currentPhase = "Phase-1-Analyse";

            string fileList = string.Join("\n", mapList.Select(m => "  - " + m.Name));

            string systemPrompt =
                "Du bist ein ECU-Tuning-Experte fuer Dieselmotoren. " +
                "Du analysierst ECU-Kennfeld-Maps im CSV-Format und antwortest AUSSCHLIESSLICH mit validem JSON. " +
                "Kein erklaerende Text, keine Markdown-Code-Bloecke, nur reines JSON.";

            string userPrompt =
                "WICHTIG: Verwende fuer 'maps_to_change' AUSSCHLIESSLICH diese exakten Dateinamen:\n" +
                fileList + "\n\n" +
                $"Die lokale Analyse ergibt bereits folgende Werte (verwende diese!):\n" +
                $"  max_fuel_quantity = {localMaxFuel:F2} (aus 'Begrenzer Kraftstoffmenge.CSV')\n" +
                $"  max_torque_nm = {localMaxTorque:F1} (aus 'Max. Drehmoment Begrenzer f(APS) #2.CSV')\n" +
                $"  max_torque_rpm = {localMaxTorqueRpm:F0}\n" +
                $"  max_power_kw = {localMaxPowerKw:F1} (berechnet: M x 2pi x n / 60)\n" +
                $"  max_boost_hpa = {localMaxBoost:F0} (Volllast aus Ladedruck-Maps)\n" +
                $"  max_rail_pressure_bar = {localMaxRailPressure:F0} (aus Raildruck-Maps)\n\n" +
                "Analysiere die Maps und beantworte:\n" +
                "1. Welche Maps angepasst werden muessen wenn die Einspritzmenge erhoeht wird\n" +
                "2. Schreibe eine kurze deutsche Zusammenfassung des Ist-Zustands\n\n" +
                "Antworte mit diesem JSON-Schema (alle Felder Pflicht):\n" +
                "{\n" +
                $"  \"max_fuel_quantity\": {localMaxFuel:F2},\n" +
                "  \"fuel_unit\": \"mm3/Stk\",\n" +
                $"  \"max_power_kw\": {localMaxPowerKw:F1},\n" +
                $"  \"max_torque_nm\": {localMaxTorque:F1},\n" +
                $"  \"max_torque_rpm\": {localMaxTorqueRpm:F0},\n" +
                $"  \"max_power_rpm\": {localMaxTorqueRpm:F0},\n" +
                $"  \"max_boost_hpa\": {localMaxBoost:F0},\n" +
                $"  \"max_rail_pressure_bar\": {localMaxRailPressure:F0},\n" +
                "  \"summary\": \"<kurze dt. Zusammenfassung>\",\n" +
                "  \"maps_to_change\": [\"<exakter Dateiname>\", ...]\n" +
                "}\n\n" +
                "Maps zur Analyse:\n\n" + mapsBlock;

            string json = await CallApiAsync(systemPrompt, userPrompt);
            return ParseAnalysisResult(json);
        }

        // -----------------------------------------------------------------------
        // Phase 2 - Remap (jede Map einzeln, sofort anwenden)

        public async Task<RemapResult> RemapAsync(IEnumerable<MapModel> maps, double targetFuel,
            AnalysisResult analysis, Action<string, MapRemap, bool> mapDoneCallback = null,
            RemapLimits limits = null)
        {
            var mapList = maps.ToList();
            _currentPhase = "Phase-2-Remap";

            double currentMax  = analysis.MaxFuelQuantity;
            double lambdaXMax  = CalcLambdaFuelLimit(mapList);
            double newTorque   = analysis.MaxTorqueNm * (targetFuel / currentMax);
            double newPowerKw  = (newTorque * analysis.MaxTorqueRpm) / 9549.3;
            bool   needsLambda = targetFuel > lambdaXMax;

            AiRemapLogger.LogInfo($"Remap: {currentMax:F2} -> {targetFuel:F2} mm3/Hub, Faktor={targetFuel/currentMax:F4}");

            var result = new RemapResult
            {
                ExpectedPowerKw  = newPowerKw,
                ExpectedTorqueNm = newTorque,
                Notes = $"Remap {currentMax:F2} -> {targetFuel:F2} mm3/Hub",
                Maps  = new List<MapRemap>()
            };

            var mapsToProcess = mapList.Where(m => NeedsRemap(m, targetFuel, needsLambda)).ToList();
            AiRemapLogger.LogInfo($"{mapsToProcess.Count} von {mapList.Count} Maps werden verarbeitet.");

            foreach (var map in mapsToProcess)
            {
                // Callback VOR dem API-Call: zeigt "wird verarbeitet..."
                mapDoneCallback?.Invoke(map.Name, null, false);

                AiRemapLogger.LogInfo($"Verarbeite: {map.Name}");
                MapRemap mr = null;
                bool success = false;
                try
                {
                    mr = await ProcessSingleMapAsync(map, targetFuel, currentMax, lambdaXMax, needsLambda, limits);
                    if (mr != null)
                    {
                        result.Maps.Add(mr);
                        success = true;
                    }
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

                // Callback NACH dem API-Call: Map sofort anwenden + Haken setzen
                mapDoneCallback?.Invoke(map.Name, mr, success);
            }
            return result;
        }

        private static bool NeedsRemap(MapModel m, double targetFuel, bool needsLambda)
        {
            string n    = m.Name.ToLowerInvariant();
            string axis = (m.AxisLabel ?? "").ToLowerInvariant();
            if (axis.Contains("mm3") && m.XAxis[m.Cols - 1] < targetFuel * 0.99) return true;
            if (n.Contains("begrenzer") && n.Contains("kraftstoff"))             return true;
            if (n.Contains("drehmoment") && n.Contains("begrenzer"))             return true;
            if (n.Contains("ladedruck")  && n.Contains("begrenzer"))             return true;
            if ((n.Contains("rauch") || n.Contains("lambda")) && needsLambda)    return true;
            return false;
        }

        private async Task<MapRemap> ProcessSingleMapAsync(MapModel map, double targetFuel,
            double currentMax, double lambdaXMax, bool needsLambda, RemapLimits limits = null)
        {
            RemapEngine.MapType mapType = await ClassifyMapAsync(map, targetFuel, currentMax);
            AiRemapLogger.LogInfo($"  Klassifiziert als: {mapType}");

            bool changed = RemapEngine.Apply(map, currentMax, targetFuel, mapType, limits);

            return new MapRemap
            {
                FileName          = map.Name,
                ChangeDescription = $"{mapType}: {(changed ? "geaendert" : "unveraendert")}",
                NewXAxis          = map.XAxis,
                NewYAxis          = map.YAxis,
                NewValues         = map.Values
            };
        }

        /// <summary>
        /// Fragt Claude NUR nach dem Map-Typ (kurze Antwort, wenige Token).
        /// Mathematik macht RemapEngine lokal.
        /// </summary>
        private async Task<RemapEngine.MapType> ClassifyMapAsync(MapModel map, double targetFuel, double currentMax)
        {
            // Zuerst lokal klassifizieren – meist korrekt und spart API-Aufruf
            var localType = RemapEngine.Classify(map);
            if (localType != RemapEngine.MapType.Unknown)
            {
                AiRemapLogger.LogInfo($"  Lokale Klassifizierung: {localType} (kein API-Call noetig)");
                return localType;
            }

            // Nur bei Unknown: KI fragen
            string systemPrompt =
                "Du bist ein EDC17-Kennfeld-Klassifizierer. " +
                "Antworte NUR mit einem einzigen Wort aus dieser Liste: " +
                "FuelLimiter, InjectionMap, InjectionTiming, InjectionDuration, " +
                "BoostMap, BoostLimiter, TorqueLimiter, RailPressure, SmokeLimit, Skip";

            string userPrompt =
                $"Kennfeld: {map.Name}\n" +
                $"Einheit: {map.Unit ?? "?"}\n" +
                $"Achse: {map.AxisLabel ?? "?"}\n" +
                $"Letzter X-Wert: {map.XAxis[map.Cols-1]:F2}\n" +
                $"Ziel-IQ: {targetFuel:F2} mg/Hub\n\n" +
                "Welcher Typ ist dieses Kennfeld? Antworte NUR mit einem Wort.";

            string response = (await CallApiAsync(systemPrompt, userPrompt)).Trim();
            AiRemapLogger.LogInfo($"  KI-Klassifizierung Antwort: '{response}'");

            if (Enum.TryParse<RemapEngine.MapType>(response, true, out var parsed))
                return parsed;

            return RemapEngine.MapType.Skip;
        }

        private static string BuildSingleMapBlock(MapModel map)
        {
            var sb = new StringBuilder();
            sb.Append(map.AxisLabel ?? "Y\\X");
            for (int c = 0; c < map.Cols; c++) sb.Append($";{map.XAxis[c]:F2}");
            sb.AppendLine();
            for (int r = 0; r < map.Rows; r++)
            {
                sb.Append($"{map.YAxis[r]:F2}");
                for (int c = 0; c < map.Cols; c++) sb.Append($";{map.Values[r, c]:F4}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // -----------------------------------------------------------------------
        // HTTP-Aufruf
        // -----------------------------------------------------------------------

        private async Task<string> CallApiAsync(string systemPrompt, string userPrompt)
        {
            var requestBody = new
            {
                model      = Model,
                max_tokens = MaxTokens,
                system     = systemPrompt,
                messages   = new object[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            string requestJson = SimpleJsonSerialize(requestBody);

            // Logging â€“ Request
            AiRemapLogger.LogRequest(_currentPhase, requestJson);

            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
            {
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);
                request.Content = content;

                using (var response = await _http.SendAsync(request))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();

                    // Logging â€“ Response
                    AiRemapLogger.LogResponse(_currentPhase, (int)response.StatusCode, responseBody);

                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"Anthropic API Fehler {(int)response.StatusCode}: {responseBody}");

                    return ExtractTextFromResponse(responseBody);
                }
            }
        }

        private string _currentPhase = "?";

        // -----------------------------------------------------------------------
        // Prompt-Bau
        // -----------------------------------------------------------------------

        // -----------------------------------------------------------------------
        // Lokale Berechnungen (kein KI-Aufruf nÃ¶tig)
        // -----------------------------------------------------------------------

        /// <summary>Ermittelt den maximalen Ladedruck aus den Ladedruck-Maps (Volllast = letzte Y-Zeile).</summary>
        private static double CalcMaxBoost(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("ladedruck") || n.Contains("begrenzer")) continue;
                // Volllast = letzte Y-Zeile (hoechste RPM oder hoechste IQ-Spalte)
                int lastRow = m.Rows - 1;
                for (int c = 0; c < m.Cols; c++)
                    if (m.Values[lastRow, c] > max && m.Values[lastRow, c] < 5000)
                        max = m.Values[lastRow, c];
            }
            return max;
        }

        /// <summary>Ermittelt den maximalen Raildruck aus den Raildruck-Maps.</summary>
        private static double CalcMaxRailPressure(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("raildruck")) continue;
                for (int r = 0; r < m.Rows; r++)
                    for (int c = 0; c < m.Cols; c++)
                        if (m.Values[r, c] > max && m.Values[r, c] < 3000)
                            max = m.Values[r, c];
            }
            return max;
        }

        /// <summary>Gibt den hoechsten X-Achsenwert (mm3/Stk) aus den Rauchbegrenzer-Lambda-Maps zurueck.</summary>
        private static double CalcLambdaFuelLimit(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("rauchbegrenzer") && !n.Contains("lambda")) continue;
                foreach (double x in m.XAxis)
                    if (x > max && x < 2000) max = x;
            }
            return max > 0 ? max : double.MaxValue;
        }

        /// <summary>Liest den hoechsten Wert aus der Begrenzer-Kraftstoffmenge-Map.</summary>
        private static double CalcMaxFuelFromLimiter(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("begrenzer") || !n.Contains("kraftstoff")) continue;
                for (int r = 0; r < m.Rows; r++)
                    for (int c = 0; c < m.Cols; c++)
                        if (m.Values[r, c] > max) max = m.Values[r, c];
            }
            // Fallback: hÃ¶chster Wert aller Einspritz-Begrenzer-Maps
            if (max < 1)
            {
                foreach (var m in maps)
                {
                    string n = m.Name.ToLowerInvariant();
                    if (!n.Contains("begrenzer")) continue;
                    for (int r = 0; r < m.Rows; r++)
                        for (int c = 0; c < m.Cols; c++)
                            if (m.Values[r, c] > max && m.Values[r, c] < 200) max = m.Values[r, c];
                }
            }
            return max;
        }

        /// <summary>Liest den hÃ¶chsten Nm-Wert aus der Drehmoment-Begrenzer-Map.</summary>
        private static double CalcMaxTorqueFromLimiter(IEnumerable<MapModel> maps)
        {
            double max = 0;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                // Volllast = letzte Y-Zeile (hoechster hPa-Wert = hoechster Ladedruck)
                int fullLoadRow = m.Rows - 1;
                for (int c = 0; c < m.Cols; c++)
                    if (m.Values[fullLoadRow, c] > max && m.Values[fullLoadRow, c] < 1000)
                        max = m.Values[fullLoadRow, c];
            }
            // Fallback: globales Maximum
            if (max < 1)
            {
                foreach (var m in maps)
                {
                    string n = m.Name.ToLowerInvariant();
                    if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                    for (int r = 0; r < m.Rows; r++)
                        for (int c = 0; c < m.Cols; c++)
                            if (m.Values[r, c] > max && m.Values[r, c] < 1000) max = m.Values[r, c];
                }
            }
            return max;
        }

        /// <summary>Gibt die RPM bei maximalem Drehmoment (Volllast-Zeile) zurueck.</summary>
        private static double CalcMaxTorqueRpm(IEnumerable<MapModel> maps)
        {
            double maxVal = 0; double rpmAtMax = 2000;
            foreach (var m in maps)
            {
                string n = m.Name.ToLowerInvariant();
                if (!n.Contains("drehmoment") || !n.Contains("begrenzer")) continue;
                // Volllast = letzte Y-Zeile (hoechster hPa); X-Achse = RPM
                int fullLoadRow = m.Rows - 1;
                for (int c = 0; c < m.Cols; c++)
                    if (m.Values[fullLoadRow, c] > maxVal && m.Values[fullLoadRow, c] < 1000)
                    {
                        maxVal    = m.Values[fullLoadRow, c];
                        rpmAtMax  = m.XAxis[c];
                    }
            }
            return rpmAtMax > 0 ? rpmAtMax : 2000;
        }

        // -----------------------------------------------------------------------
        // Prompt-Bau
        // -----------------------------------------------------------------------

        private static string BuildMapsBlock(IEnumerable<MapModel> maps)
        {
            var sb = new StringBuilder();
            foreach (var map in maps)
            {
                sb.AppendLine($"=== {map.Name} ===");
                if (!string.IsNullOrEmpty(map.MapName))
                    sb.AppendLine($"Bezeichnung: {map.MapName}");
                if (!string.IsNullOrEmpty(map.Unit))
                    sb.AppendLine($"Einheit: {map.Unit}");

                // Achsen-Header
                sb.Append(map.AxisLabel ?? "Y\\X");
                for (int c = 0; c < map.Cols; c++)
                    sb.Append($"\t{map.XAxis[c]:F2}");
                sb.AppendLine();

                // Datenzeilen
                for (int r = 0; r < map.Rows; r++)
                {
                    sb.Append($"{map.YAxis[r]:F2}");
                    for (int c = 0; c < map.Cols; c++)
                        sb.Append($"\t{map.Values[r, c]:F2}");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // -----------------------------------------------------------------------
        // JSON-Parsing
        // -----------------------------------------------------------------------

        private static string ExtractTextFromResponse(string responseJson)
        {
            // Anthropic-Format: {"content":[{"type":"text","text":"..."}],...}
            int textIdx = responseJson.IndexOf("\"text\":", StringComparison.Ordinal);
            if (textIdx < 0)
                throw new Exception("Kein 'text'-Feld in der API-Antwort gefunden.\n" + responseJson);

            int pos = textIdx + 7;
            while (pos < responseJson.Length && responseJson[pos] == ' ') pos++;

            if (pos >= responseJson.Length || responseJson[pos] != '"')
                throw new Exception("Unerwartetes Format im 'text'-Feld.");

            var sb = new StringBuilder();
            int i = pos + 1;
            while (i < responseJson.Length)
            {
                char ch = responseJson[i];
                if (ch == '"') break;
                if (ch == '\\' && i + 1 < responseJson.Length)
                {
                    char next = responseJson[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  i += 2; continue;
                        case '\\': sb.Append('\\'); i += 2; continue;
                        case 'n':  sb.Append('\n'); i += 2; continue;
                        case 'r':  sb.Append('\r'); i += 2; continue;
                        case 't':  sb.Append('\t'); i += 2; continue;
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString().Trim();
        }

        private static AnalysisResult ParseAnalysisResult(string json)
        {
            json = StripCodeFences(json);

            var result = new AnalysisResult();
            result.MaxFuelQuantity     = GetJsonDouble(json, "max_fuel_quantity");
            result.FuelUnit            = GetJsonString(json, "fuel_unit") ?? "mg";
            result.MaxPowerKw          = GetJsonDouble(json, "max_power_kw");
            result.MaxTorqueNm         = GetJsonDouble(json, "max_torque_nm");
            result.MaxTorqueRpm        = GetJsonDouble(json, "max_torque_rpm");
            result.MaxPowerRpm         = GetJsonDouble(json, "max_power_rpm");
            result.MaxBoostHpa         = GetJsonDouble(json, "max_boost_hpa");
            result.MaxRailPressureBar  = GetJsonDouble(json, "max_rail_pressure_bar");
            result.Summary             = GetJsonString(json, "summary") ?? "";
            result.MapsToChange        = GetJsonStringArray(json, "maps_to_change");
            return result;
        }

        private static RemapResult ParseRemapResult(string json)
        {
            json = StripCodeFences(json);

            var result = new RemapResult();
            result.ExpectedPowerKw  = GetJsonDouble(json, "expected_power_kw");
            result.ExpectedTorqueNm = GetJsonDouble(json, "expected_torque_nm");
            result.Notes            = GetJsonString(json, "notes") ?? "";
            result.Maps             = ParseMapRemaps(json);
            return result;
        }

        private static List<MapRemap> ParseMapRemaps(string json)
        {
            var list = new List<MapRemap>();

            int mapsIdx = json.IndexOf("\"maps\"", StringComparison.Ordinal);
            if (mapsIdx < 0) return list;

            int arrStart = json.IndexOf('[', mapsIdx);
            if (arrStart < 0) return list;

            // Jedes Objekt { } im Array extrahieren
            int depth = 0;
            int objStart = -1;
            for (int i = arrStart; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string objJson = json.Substring(objStart, i - objStart + 1);
                        var mr = new MapRemap();
                        mr.FileName          = GetJsonString(objJson, "file_name") ?? "";
                        mr.ChangeDescription = GetJsonString(objJson, "change_description") ?? "";
                        mr.NewXAxis          = GetJsonDoubleArray(objJson, "new_x_axis");
                        mr.NewYAxis          = GetJsonDoubleArray(objJson, "new_y_axis");
                        mr.NewValues         = GetJsonDouble2DArray(objJson, "new_values");
                        if (!string.IsNullOrEmpty(mr.FileName))
                            list.Add(mr);
                        objStart = -1;
                    }
                }
            }
            return list;
        }

        // -----------------------------------------------------------------------
        // Minimal-JSON-Hilfsmethoden (kein externes NuGet nÃ¶tig)
        // -----------------------------------------------------------------------

        private static string StripCodeFences(string s)
        {
            s = s.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(nl + 1);
                if (s.EndsWith("```")) s = s.Substring(0, s.Length - 3);
            }
            return s.Trim();
        }

        private static double GetJsonDouble(string json, string key)
        {
            string pat = $"\"{key}\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return 0;
            int colon = json.IndexOf(':', k + pat.Length);
            if (colon < 0) return 0;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\n' || json[start] == '\r')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+' || json[end] == 'e' || json[end] == 'E')) end++;
            if (end == start) return 0;
            double.TryParse(json.Substring(start, end - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val);
            return val;
        }

        private static string GetJsonString(string json, string key)
        {
            string pat = $"\"{key}\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k + pat.Length);
            if (colon < 0) return null;
            int q = json.IndexOf('"', colon + 1);
            if (q < 0) return null;
            var sb = new StringBuilder();
            int i = q + 1;
            while (i < json.Length)
            {
                char ch = json[i];
                if (ch == '"') break;
                if (ch == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i += 2; continue; }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        private static List<string> GetJsonStringArray(string json, string key)
        {
            var list = new List<string>();
            string pat = $"\"{key}\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return list;
            int arrStart = json.IndexOf('[', k + pat.Length);
            int arrEnd   = json.IndexOf(']', arrStart >= 0 ? arrStart : 0);
            if (arrStart < 0 || arrEnd < 0) return list;
            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            foreach (var part in inner.Split(','))
            {
                string s = part.Trim().Trim('"');
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }

        private static double[] GetJsonDoubleArray(string json, string key)
        {
            string pat = $"\"{key}\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return new double[0];
            int arrStart = json.IndexOf('[', k + pat.Length);
            int arrEnd   = json.IndexOf(']', arrStart >= 0 ? arrStart : 0);
            if (arrStart < 0 || arrEnd < 0) return new double[0];
            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            var parts = inner.Split(',');
            var result = new List<double>();
            foreach (var p in parts)
            {
                if (double.TryParse(p.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        private static double[,] GetJsonDouble2DArray(string json, string key)
        {
            string pat = $"\"{key}\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return new double[0, 0];

            int outerStart = json.IndexOf('[', k + pat.Length);
            if (outerStart < 0) return new double[0, 0];

            // Finde das zugehÃ¶rige schlieÃŸende ] auf gleicher Tiefe
            int depth = 0;
            int outerEnd = -1;
            for (int i = outerStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { outerEnd = i; break; } }
            }
            if (outerEnd < 0) return new double[0, 0];

            // Innere Arrays extrahieren
            var rows = new List<double[]>();
            int pos = outerStart + 1;
            while (pos < outerEnd)
            {
                int rowStart = json.IndexOf('[', pos);
                if (rowStart < 0 || rowStart >= outerEnd) break;
                int rowEnd = json.IndexOf(']', rowStart + 1);
                if (rowEnd < 0) break;
                string rowStr = json.Substring(rowStart + 1, rowEnd - rowStart - 1);
                var cells = new List<double>();
                foreach (var p in rowStr.Split(','))
                {
                    if (double.TryParse(p.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                        cells.Add(v);
                }
                rows.Add(cells.ToArray());
                pos = rowEnd + 1;
            }

            if (rows.Count == 0) return new double[0, 0];
            int cols = rows.Max(r => r.Length);
            var result = new double[rows.Count, cols];
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    result[r, c] = rows[r][c];
            return result;
        }

        // -----------------------------------------------------------------------
        // Minimaler JSON-Serializer fÃ¼r den Request (kein NuGet nÃ¶tig)
        // -----------------------------------------------------------------------

        private static string SimpleJsonSerialize(object obj)
        {
            if (obj == null) return "null";
            var t = obj.GetType();

            if (obj is string s)
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                               .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";

            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is double || obj is float)
                return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);

            if (t.IsArray)
            {
                var arr = (Array)obj;
                var parts = new List<string>();
                foreach (var item in arr) parts.Add(SimpleJsonSerialize(item));
                return "[" + string.Join(",", parts) + "]";
            }

            // Anonymes Objekt / POCO â†’ via Reflection
            var props = t.GetProperties();
            var kvs = new List<string>();
            foreach (var p in props)
            {
                var val = p.GetValue(obj, null);
                kvs.Add("\"" + p.Name + "\":" + SimpleJsonSerialize(val));
            }
            return "{" + string.Join(",", kvs) + "}";
        }
    }
}
