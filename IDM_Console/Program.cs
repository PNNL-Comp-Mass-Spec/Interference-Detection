using System;
using System.IO;
using System.Reflection;
using System.Threading;
using InterDetect;
using PRISM;

namespace IDM_Console
{
    public class Program
    {
        // Ignore Spelling: Tol

        private static int Main(string[] args)
        {
            var sourceFilePath = "??";

            try
            {
                var asmName = typeof(Program).GetTypeInfo().Assembly.GetName();
                var exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
                var version = IDMConsoleOptions.GetAppVersion();

                var parser = new CommandLineParser<IDMConsoleOptions>(asmName.Name, version)
                {
                    ProgramInfo = "This program implements an algorithm for quantifying " +
                                  "the homogeneity of species isolated for fragmentation in " +
                                  "LC-MS/MS analyses using data directed acquisition (DDA). " +
                                  "It works with Thermo .Raw files and _isos.csv files from DeconTools. " +
                                  "The interference score computed for each parent ion is the " +
                                  "fraction of ions in the isolation window that are from the precursor " +
                                  "(weighted by intensity). An interference score of 1 means that " +
                                  "all of the peaks in the isolation window were from the precursor ion.",

                    ContactInfo = "Program written by Josh Aldrich for the Department of Energy (PNNL, Richland, WA) in 2012" +
                                  Environment.NewLine + Environment.NewLine +
                                  "E-mail: proteomics@pnnl.gov" + Environment.NewLine +
                                  "Website: https://panomics.pnnl.gov/ or https://omics.pnl.gov or https://github.com/PNNL-Comp-Mass-Spec",

                    UsageExamples = {
                        exeName + " Results.db3",
                        exeName + " Results.db3 /Tol:20",
                        exeName + " Results.db3 /KeepTemp"
                    }
                };

                var parseResults = parser.ParseArgs(args);
                var options = parseResults.ParsedResults;

                if (!parseResults.Success)
                {
                    Thread.Sleep(1500);
                    return -1;
                }

                if (!options.ValidateArgs())
                {
                    parser.PrintHelp();
                    Thread.Sleep(1500);
                    return -1;
                }

                options.OutputSetOptions();

                var sourceFile = new FileInfo(options.InputFilePath);
                if (!sourceFile.Exists)
                {
                    OnWarningEvent("Could not find the input file: " + options.InputFilePath);

                    Thread.Sleep(2000);
                    return -1;
                }

                var idm = new InterferenceDetector { ShowProgressAtConsole = false };
                RegisterEvents(idm);
                idm.ThrowEvents = false;

                // Set options
                idm.ChargeStateGuesstimationMassTol = options.ChargeStateGuesstimationMassTol;
                idm.PrecursorIonTolerancePPM = options.PrecursorIonTolerancePPM;
                idm.DeleteTempScoresFile = options.DeleteTempScoresFile;

                var success = idm.Run(sourceFile.DirectoryName, sourceFile.Name);

                if (success)
                {
                    Console.WriteLine("Success");
                    Thread.Sleep(750);
                    return 0;
                }

                ConsoleMsgUtils.ShowErrorCustom("Failed", null, false);
                Thread.Sleep(2000);
                return -1;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error processing " + sourceFilePath + ": " + ex.Message, ex);
                Thread.Sleep(2000);
                return -1;
            }
        }

        private static void RegisterEvents(InterferenceDetector idm)
        {
            idm.ProgressChanged += InterferenceDetectorProgressHandler;

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

        private static void InterferenceDetectorProgressHandler(InterferenceDetector idm, ProgressInfo e)
        {
            Console.WriteLine(e.ProgressCurrentFile.ToString("0.0") + "% complete; " + e.Value.ToString("0.0") + "% complete overall");
        }
    }
}
