using System.Reflection;

namespace AutoUploadToFTP
{
    internal class Log
    {

        private static ReaderWriterLockSlim _readWriteLock = new();

        public static void Write(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var binPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var logPath = Path.Combine(binPath, "log");
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }
            _readWriteLock.EnterWriteLock();
            try
            {
                var filePath = Path.Combine(logPath, $"log_{DateTime.Today:yyyy-MM-dd}.log");
                File.AppendAllText(filePath, $"【{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}】{text}\r\n\r\n");
            }
            finally
            {
                // Release lock
                _readWriteLock.ExitWriteLock();
            }
        }

       
    }
}
