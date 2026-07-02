using System;
using System.IO;
using System.Text;

namespace ECM_Stage_Helper_Tool.AiRemap
{
    /// <summary>
    /// Schreibt alle KI-Remap-Aktivitäten (Request, Response, Fehler) in
    /// airemap_log.txt neben der EXE. Jeder Eintrag beginnt mit Timestamp.
    /// </summary>
    public static class AiRemapLogger
    {
        private static readonly string LogPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "airemap_log.txt");

        private static readonly object _lock = new object();

        // -----------------------------------------------------------------------
        // Öffentliche Methoden
        // -----------------------------------------------------------------------

        public static void LogRequest(string phase, string requestJson)
        {
            int bytes = Encoding.UTF8.GetByteCount(requestJson);
            Write("REQUEST",
                $"Phase       : {phase}\n" +
                $"Größe       : {bytes:N0} Bytes  ({bytes / 1024.0:F1} KB)\n" +
                $"--- Body ---\n{requestJson}");
        }

        public static void LogResponse(string phase, int statusCode, string responseBody)
        {
            Write("RESPONSE",
                $"Phase       : {phase}\n" +
                $"HTTP-Status : {statusCode}\n" +
                $"--- Body ---\n{responseBody}");
        }

        public static void LogError(string context, Exception ex)
        {
            Write("ERROR",
                $"Kontext : {context}\n" +
                $"Typ     : {ex.GetType().Name}\n" +
                $"Meldung : {ex.Message}\n" +
                $"Stack   :\n{ex.StackTrace}");
        }

        public static void LogInfo(string message)
        {
            Write("INFO", message);
        }

        /// <summary>Gibt den Pfad zur Log-Datei zurück.</summary>
        public static string FilePath => LogPath;

        /// <summary>Öffnet die Log-Datei im Standard-Editor.</summary>
        public static void OpenLogFile()
        {
            if (File.Exists(LogPath))
                System.Diagnostics.Process.Start(LogPath);
        }

        // -----------------------------------------------------------------------
        // Interna
        // -----------------------------------------------------------------------

        private static void Write(string level, string body)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}]");
                sb.AppendLine(body);
                sb.AppendLine(new string('-', 80));

                lock (_lock)
                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* Logging darf die App nie abstürzen lassen */ }
        }
    }
}
