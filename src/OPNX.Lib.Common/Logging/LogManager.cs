using Serilog;
using Serilog.Core;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Common.Logging
{
    /// <summary>
    /// OPNX 서버/클라이언트 공용 LogManager
    /// - ProgramData(%ProgramData%) 하위에 exe명 기준으로 로그 폴더 생성
    /// - Thread-safe Lazy 초기화
    /// - Exception 전달 시 Serilog가 StackTrace 자동 출력
    /// - DEBUG 빌드에서만 Debug 출력
    /// - ProcessExit/UnhandledException에서 자동 Flush 시도
    /// </summary>
    public static class LogManager
    {
        private static readonly Lazy<Logger> _logger = new(CreateLogger, isThreadSafe: true);
        private static int _flushOnce;

        private static Logger Logger => _logger.Value;

        private static Logger CreateLogger()
        {
            string commonAppDataPath =
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            string appName =
                Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName);

            string baseDir = Path.Combine(commonAppDataPath, appName);
            string logsDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logsDir);

            string logFile = Path.Combine(logsDir, "log_.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
#if DEBUG
                .MinimumLevel.Debug()
#endif
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    logFile,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,       // 14일치 유지 (원하면 조절)
                    fileSizeLimitBytes: 50_000_000,   // 50MB마다 추가 롤링 (원하면 조절)
                    rollOnFileSizeLimit: true,
                    shared: true)
                .CreateLogger();

            Log.Logger = logger;

            HookAutoFlush();
            return logger;
        }

        private static void HookAutoFlush()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, __) => FlushOnce();

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                        Error(ex, "Unhandled exception");
                    else
                        Error("Unhandled exception (non-Exception object)");
                }
                catch { /* ignore */ }

                FlushOnce();
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try { Error(e.Exception, "Unobserved task exception"); }
                catch { /* ignore */ }

                FlushOnce();
                e.SetObserved();
            };
        }

        private static void FlushOnce()
        {
            if (Interlocked.Exchange(ref _flushOnce, 1) != 0)
                return;

            try { Log.CloseAndFlush(); }
            catch { /* ignore */ }
        }

        public static void Info(string messageTemplate, params object[] args)
            => Logger.Information(messageTemplate, args);

        public static void Warning(string messageTemplate, params object[] args)
            => Logger.Warning(messageTemplate, args);

        public static void Warning(Exception ex, params object[] args)
            => Logger.Warning(ex, ex.Message, args);

        public static void Error(string messageTemplate, params object[] args)
            => Logger.Error(messageTemplate, args);

        public static void Error(Exception ex, params object[] args)
            => Logger.Error(ex, ex.Message, args);

        public static void Error(Exception ex, string messageTemplate, params object[] args)
            => Logger.Error(ex, messageTemplate, args);

        public static void Verbose(string messageTemplate, params object[] args)
            => Logger.Verbose(messageTemplate, args);

        public static void Verbose(Exception ex, params object[] args)
            => Logger.Verbose(ex, ex.Message, args);

        public static void Verbose(Exception ex, string messageTemplate, params object[] args)
            => Logger.Verbose(ex, messageTemplate, args);


        public static void Debug(Exception ex, string messageTemplate, params object[] args)
        {
#if DEBUG
            Logger.Debug(ex, messageTemplate, args);
#endif
        }

        public static void Debug(Exception ex, params object[] args)
        {
#if DEBUG
            Logger.Debug(ex, ex.Message, args);
#endif
        }

        public static void Debug(string messageTemplate, params object[] args)
        {
#if DEBUG
            Logger.Debug(messageTemplate, args);
#endif
        }
        public static void ErrorWithCaller(
            Exception ex,
            string message,
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0,
            [CallerMemberName] string member = "")
        {
            string fileName = Path.GetFileName(file);
            Logger.Error(ex, "{File}({Line}) {Member}: {Message}", fileName, line, member, message);
        }
    }
}
