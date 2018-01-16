using System;
using System.Reflection;
using InterDetect;
using PRISM;

namespace IDM_Console
{
    class IDMConsoleOptions
    {
        private const string PROGRAM_DATE = "January 15, 2018";

        public IDMConsoleOptions()
        {
            InputFilePath = string.Empty;
            DeleteTempScoresFile = false;
            ChargeStateGuesstimationMassTol = InterferenceCalculator.DEFAULT_CHARGE_STATE_GUESSTIMATION_TOLERANCE;
            PrecursorIonTolerancePPM = InterferenceCalculator.DEFAULT_PRECURSOR_ION_TOLERANCE_PPM;
        }

        [Option("I", ArgPosition = 1, HelpText =
            "SQLite data file with table T_MSMS_Raw_Files and optionally table T_Results_Metadata_Typed or T_Results_Metadata. " +
            "Named " + InterferenceDetector.DEFAULT_RESULT_DATABASE_NAME + " in DMS")]
        public string InputFilePath { get; set; }

        [Option("KeepTemp", "NoDelete", HelpText = "If provided, do not delete the temporary precursor info file, " + InterferenceDetector.PRECURSOR_INFO_FILENAME)]
        public bool DeleteTempScoresFile { get; set; }

        [Option("CSTol", HelpText = "m/z tolerance when guesstimating the charge")]
        public double ChargeStateGuesstimationMassTol { get; set; }

        [Option("Tol", "PrecursorTol", HelpText = "PPM tolerance when looking for the precursor ion")]
        public double PrecursorIonTolerancePPM { get; set; }

        public static string GetAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";

            return version;
        }

        public void OutputSetOptions()
        {
            Console.WriteLine("IDM_Console, version " + GetAppVersion());
            Console.WriteLine();
            Console.WriteLine("Using options:");

            Console.WriteLine(" Input file: {0}", InputFilePath);

            Console.WriteLine(" Charge State Guesstimation Tolerance: +/-{0} m/z", StringUtilities.DblToString(ChargeStateGuesstimationMassTol, 4));

            Console.WriteLine(" Precursor ion tolerance: +/-{0} ppm", StringUtilities.DblToString(PrecursorIonTolerancePPM, 1));

            if (!DeleteTempScoresFile)
                Console.WriteLine(" Will not delete file {0}", InterferenceDetector.PRECURSOR_INFO_FILENAME);

        }

        public bool ValidateArgs()
        {
            if (string.IsNullOrWhiteSpace(InputFilePath))
            {
                Console.WriteLine("Input file not specified");
                return false;
            }

            return true;
        }

    }
}
