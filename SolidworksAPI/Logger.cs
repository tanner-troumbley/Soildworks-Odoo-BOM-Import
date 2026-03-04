using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class Logger
{
    private static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
    private static readonly AutoResetEvent logSignal = new AutoResetEvent(false);
    private static readonly CancellationTokenSource cts = new CancellationTokenSource();

    private static string logDirectory;
    private static string logFilePath;
    private static Task loggingTask;
    private static DateTime currentLogDate;

    /// <summary>
    /// Initializes the logger with a directory path.
    /// </summary>
    public static void Init(string directoryPath = null)
    {
        logDirectory = directoryPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        currentLogDate = DateTime.Now.Date;
        logFilePath = GetLogFilePath(currentLogDate);

        loggingTask = Task.Run(() => ProcessLogQueueAsync(cts.Token));
    }

    /// <summary>
    /// Logs an INFO message.
    /// </summary>
    public static void Info(string message) => EnqueueLog("INFO", message);

    /// <summary>
    /// Logs a WARNING message.
    /// </summary>
    public static void Warn(string message) => EnqueueLog("WARN", message);

    /// <summary>
    /// Logs an ERROR message.
    /// </summary>
    public static void Error(string message) => EnqueueLog("ERROR", message);

    /// <summary>
    /// Stops the logger and flushes remaining logs.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        cts.Cancel();
        logSignal.Set();
        if (loggingTask != null)
        {
            await loggingTask;
        }
    }

    // ----------------- Private Helpers -----------------

    private static void EnqueueLog(string level, string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        logQueue.Enqueue(logEntry);
        logSignal.Set();
    }

    private static async Task ProcessLogQueueAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested || !logQueue.IsEmpty)
            {
                logSignal.WaitOne();

                // Rotate log file daily
                if (DateTime.Now.Date != currentLogDate)
                {
                    currentLogDate = DateTime.Now.Date;
                    logFilePath = GetLogFilePath(currentLogDate);
                }

                using (var writer = new StreamWriter(logFilePath, append: true))
                {
                    while (logQueue.TryDequeue(out string logEntry))
                    {
                        await writer.WriteLineAsync(logEntry);
                    }
                    await writer.FlushAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Logger error: " + ex.Message);
        }
    }

    private static string GetLogFilePath(DateTime date)
    {
        string fileName = $"log_{date:yyyy-MM-dd}.log";
        return Path.Combine(logDirectory, fileName);
    }
}
