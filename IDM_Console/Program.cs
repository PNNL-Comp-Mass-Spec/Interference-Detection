using System;
using System.IO;
using InterDetect;
using PRISM;

namespace IDM_Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var sourceFilePath = "??";

            try
            {

                var sourceFile = new FileInfo(InterferenceDetector.DEFAULT_RESULT_DATABASE_NAME);
                if (sourceFile.Exists)
                {
                    sourceFilePath = sourceFile.FullName;
                }
                else
                {
                    sourceFile = new FileInfo(Path.Combine("..", InterferenceDetector.DEFAULT_RESULT_DATABASE_NAME));
                    if (sourceFile.Exists)
                    {
                        sourceFilePath = sourceFile.FullName;
                    }
                    else
                    {
                        OnWarningEvent("Could not find " + InterferenceDetector.DEFAULT_RESULT_DATABASE_NAME +
                            " in the current directory or in the parent directory; aborting");

                        System.Threading.Thread.Sleep(2000);
                        return;
                    }
                }

                var idm = new InterferenceDetector { ShowProgressAtConsole = false };
                RegisterEvents(idm);
                idm.ThrowEvents = false;

                var success = idm.Run(sourceFile.DirectoryName, sourceFile.Name);

                if (success)
                    Console.WriteLine("Success");
                else
                    ConsoleMsgUtils.ShowError("Failed", null, false);

            }
            catch (Exception ex)
            {
                OnErrorEvent("Error processing " + sourceFilePath + ": " + ex.Message, ex);
            }

            System.Threading.Thread.Sleep(2000);

        }

        private static void RegisterEvents(InterferenceDetector idm)
        {
            idm.ProgressChanged += InterfenceDetectorProgressHandler;

            idm.DebugEvent += OnDebugEvent;
            idm.StatusEvent += OnStatusEvent;
            idm.ErrorEvent += OnErrorEvent;
            idm.WarningEvent += OnWarningEvent;
        }

        private static void OnDebugEvent(string message)
        {
            ConsoleMsgUtils.ShowDebug(message);
        }

        private static void OnWarningEvent(string message)
        {
            ConsoleMsgUtils.ShowWarning(message);
        }

        private static void OnErrorEvent(string errorMessage, Exception ex)
        {
            ConsoleMsgUtils.ShowError(errorMessage, ex);
        }

        private static void OnStatusEvent(string message)
        {
            Console.WriteLine(message);
        }

        private static void InterfenceDetectorProgressHandler(InterferenceDetector id, ProgressInfo e)
        {
            Console.WriteLine(e.ProgressCurrentFile.ToString("0.0") + "% complete; " + e.Value.ToString("0.0") + "% complete overall");
        }
    }
}
