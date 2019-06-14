using System;
using System.Collections.Generic;
using System.IO;
using Mage;
using PRISM;
using ThermoRawFileReader;

namespace InterDetect
{
    /// <summary>
    /// Simple peak object
    /// </summary>
    public struct Peak
    {
        /// <summary>
        /// m/z
        /// </summary>
        public double Mz;

        /// <summary>
        /// Intensity/abundance
        /// </summary>
        public double Abundance;

        public override string ToString()
        {
            return string.Format("{0:F4} m/z, intensity {1:F0}", Mz, Abundance);
        }
    };

    /// <summary>
    /// Progress reporting info
    /// </summary>
    public class ProgressInfo : EventArgs
    {
        /// <summary>
        /// Overall percent complete (value between 0 and 100)
        /// </summary>
        public float Value { get; set; }

        /// <summary>
        /// Percent complete for the current file
        /// </summary>
        public float ProgressCurrentFile { get; set; }
    }

    /// <summary>
    /// Interference detection algorithm - uses sqlite, .raw, and _isos.csv as input
    /// </summary>
    public class InterferenceDetector : EventNotifier
    {
        public const string DEFAULT_RESULT_DATABASE_NAME = "Results.db3";

        public const string PRECURSOR_INFO_FILENAME = "prec_info_temp.txt";

        private const string RAW_FILE_EXTENSION = ".raw";
        private const string ISOS_FILE_EXTENSION = "_isos.csv";

        private const string SCAN_EVENT_CHARGE_STATE = "Charge State";
        private const string SCAN_EVENT_MONOISOTOPIC_MZ = "Monoisotopic M/Z";
        private const string SCAN_EVENT_MS2_ISOLATION_WIDTH = "MS2 Isolation Width";
        private const string SCAN_EVENT_ION_INJECTION_TIME = "Ion Injection Time (ms)";

        // Auto-properties
        public bool ShowProgressAtConsole { get; set; }
        public string WorkDir { get; set; }

        public event ProgressChangedHandler ProgressChanged;
        public delegate void ProgressChangedHandler(InterferenceDetector id, ProgressInfo e);

        private readonly Dictionary<int, string> mFormatStrings;

        private double[,] mSpectraData2D;
        private int mCachedPrecursorScan;

        #region "Properties"

        /// <summary>
        /// Mass tolerance (in m/z) to use when guesstimating charge state
        /// </summary>
        public double ChargeStateGuesstimationMassTol
        {
            get => InterferenceCalculator.ChargeStateGuesstimationMassTol;
            set => InterferenceCalculator.ChargeStateGuesstimationMassTol = value;
        }

        /// <summary>
        /// When true, delete the temporary scores file (prec_info_temp.txt) after adding the data to the SQLite database
        /// </summary>
        public bool DeleteTempScoresFile { get; set; }

        /// <summary>
        /// Tolerance (in ppm) when finding the precursor ion in the isolation window
        /// </summary>
        public double PrecursorIonTolerancePPM
        {
            get => InterferenceCalculator.PrecursorIonTolerancePPM;
            set => InterferenceCalculator.PrecursorIonTolerancePPM = value;
        }

        /// <summary>
        /// When true, events are thrown up the calling tree for the parent class to handle them
        /// </summary>
        /// <remarks>Defaults to true</remarks>
        public bool ThrowEvents { get; set; } = true;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public InterferenceDetector()
        {
            ShowProgressAtConsole = true;
            WorkDir = ".";
            mFormatStrings = new Dictionary<int, string>();
        }

        /// <summary>
        /// Obtain isos and raw file paths from a SQLite database named Results.db3
        /// Use these to compute precursor interference values, storing the results in table t_precursor_interference in the database
        /// </summary>
        /// <param name="databaseDirectoryPath">Directory with the SQLite database</param>
        public bool Run(string databaseDirectoryPath)
        {
            return Run(databaseDirectoryPath, DEFAULT_RESULT_DATABASE_NAME);
        }

        /// <summary>
        /// Obtain isos and raw file paths from the specified SQLite database.
        /// Use these to compute precursor interference values, storing the results in table t_precursor_interference in the database
        /// </summary>
        /// <param name="databaseDirectoryPath">Directory with the SQLite database</param>
        /// <param name="databaseFileName">Name of the database</param>
        public bool Run(string databaseDirectoryPath, string databaseFileName)
        {
            // Keys are dataset IDs; values are the path to the .raw file
            Dictionary<string, string> dctRawFiles;

            // Keys are dataset IDs; values are the path to the _isos.csv file
            Dictionary<string, string> dctIsosFiles;
            bool success;

            var databaseDirectory = new DirectoryInfo(databaseDirectoryPath);
            if (!databaseDirectory.Exists)
            {
                var message = "SQLite database directory not found: " + databaseDirectoryPath;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new DirectoryNotFoundException(message);
                return false;
            }
            var fiDatabaseFile = new FileInfo(Path.Combine(diDataFolder.FullName, databaseFileName));
            if (!fiDatabaseFile.Exists)
            {
                var message = "SQLite database not found: " + fiDatabaseFile.FullName;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new FileNotFoundException(message);
                return false;
            }

            // Build Mage pipeline to read contents of
            // a table in a SQLite database into a data buffer

            // First, make the Mage SQLite reader module
            // and configure it to read the table
            var reader = new SQLiteReader
            {
                Database = fiDatabaseFile.FullName
            };

            try
            {
                success = LookupMSMSFiles(reader, out dctRawFiles);
                if (!success)
                    return false;
            }
            catch (Exception ex)
            {
                var message = "Error calling LookupMSMSFiles: " + ex.Message;
                OnErrorEvent(message, ex);
                if (ThrowEvents)
                    throw new Exception(message, ex);
                return false;
            }

            try
            {
                success = LookupDeconToolsInfo(reader, out dctIsosFiles);
                if (!success)
                {
                    // DeconIsos file not found; this is not a critical error
                }
            }
            catch (Exception ex)
            {
                var message = "Error calling LookupDeconToolsInfo: " + ex.Message;
                OnErrorEvent(message, ex);
                if (ThrowEvents)
                    throw new Exception(message, ex);
                return false;
            }

            try
            {
                PerformWork(fiDatabaseFile, dctRawFiles, dctIsosFiles);
            }
            catch (Exception ex)
            {
                var message = "Error calling PerformWork: " + ex.Message;
                OnErrorEvent(message, ex);
                if (ThrowEvents)
                    throw new Exception(message, ex);
                return false;
            }

            return true;
        }

        public bool GUI_PerformWork(string outputFilePath, string rawFilePath, string isosFilePath)
        {
            // Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            var tempPrecursorInfoFile = outputFilePath;

            OnStatusEvent("Processing file: " + Path.GetFileName(rawFilePath));
            List<PrecursorIntense> lstPrecursorInfo = null;
            try
            {
                lstPrecursorInfo = ParentInfoPass(1, 1, rawFilePath, isosFilePath);
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in GUI_PerformWork: " + ex.Message, ex);
            }

            if (lstPrecursorInfo == null)
            {
                DeleteFile(tempPrecursorInfoFile);
                OnErrorEvent("Error in PerformWork: ParentInfoPass returned null loading " + rawFilePath);
                return false;
            }

            ExportInterferenceScores(lstPrecursorInfo, "000000", tempPrecursorInfoFile);

            OnStatusEvent("Process Complete");
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fiDatabaseFile"></param>
        /// <param name="dctRawFiles">Keys are dataset IDs; values are the path to the .raw file</param>
        /// <param name="dctIsosFiles">Keys are dataset IDs; values are the path to the _isos.csv file</param>
        /// <remarks>dctIsosFiles can be null or empty since Isos files are not required</remarks>
        private void PerformWork(FileInfo fiDatabaseFile, IReadOnlyDictionary<string, string> dctRawFiles, IReadOnlyDictionary<string, string> dctIsosFiles)
        {
            var fileCountCurrent = 0;

            // Calculate the needed info and generate a temporary file, keep adding each dataset to this file

            string tempPrecursorInfoFile;

            if (fiDatabaseFile.DirectoryName == null)
                tempPrecursorInfoFile = PRECURSOR_INFO_FILENAME;
            else
                tempPrecursorInfoFile = Path.Combine(fiDatabaseFile.DirectoryName, "prec_info_temp.txt");

            DeleteFile(tempPrecursorInfoFile);

            foreach (var datasetID in dctRawFiles.Keys)
            {
                if (dctIsosFiles == null || !dctIsosFiles.TryGetValue(datasetID, out var isosFilePath))
                    isosFilePath = string.Empty;

                fileCountCurrent++;

                Console.WriteLine();
                OnStatusEvent("Processing file " + fileCountCurrent + " / " + dctRawFiles.Count + ": " + Path.GetFileName(dctRawFiles[datasetID]));

                var lstPrecursorInfo = ParentInfoPass(fileCountCurrent, dctRawFiles.Count, dctRawFiles[datasetID], isosFilePath);
                if (lstPrecursorInfo == null)
                {
                    var message = "Error in PerformWork: ParentInfoPass returned null loading " + dctRawFiles[datasetID];
                    OnErrorEvent(message);
                    if (ThrowEvents)
                        throw new Exception(message);
                    return;
                }

                ExportInterferenceScores(lstPrecursorInfo, datasetID, tempPrecursorInfoFile);

                if (ShowProgressAtConsole)
                    OnStatusEvent("Iteration Complete");
            }

            try
            {
                // Create a delimited file reader and write a new table with this info to database
                var reader = new DelimitedFileReader
                {
                    FilePath = tempPrecursorInfoFile
                };

                var writer = new SQLiteWriter();
                const string tableName = "t_precursor_interference";
                writer.DbPath = fiDatabaseFile.FullName;
                writer.TableName = tableName;

                ProcessingPipeline.Assemble("ImportToSQLite", reader, writer).RunRoot(null);
            }
            catch (Exception ex)
            {
                var message = "Error adding table t_precursor_interference to the SqLite database: " + ex.Message;

                OnErrorEvent(message, ex);
                OnStatusEvent("Results are in file " + tempPrecursorInfoFile);

                if (ThrowEvents)
                    throw new Exception(message, ex);
            }

            if (!DeleteTempScoresFile)
                return;

            // Delete the text file that was imported into SQLite
            DeleteFile(tempPrecursorInfoFile);

        }

        private void DeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error deleting locally cached file " + filePath + ": " + ex.Message, ex);
            }
        }
        /// <summary>
        /// Query table t_msms_raw_files in the SQLite database to determine the Thermo .raw files to process
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="dctRawFiles">Keys are dataset IDs; values are the path to the .raw file</param>
        /// <returns>True if success, otherwise false</returns>
        /// <remarks>Does not validate that each .raw file exists</remarks>
        private bool LookupMSMSFiles(SQLiteReader reader, out Dictionary<string, string> dctRawFiles)
        {
            reader.SQLText = "SELECT * FROM t_msms_raw_files;";

            // Make a Mage sink module (simple row buffer)
            var sink = new SimpleSink();

            // Construct and run a Mage pipeline to obtain data from t_msms_raw_files
            ProcessingPipeline.Assemble("Test_Pipeline", reader, sink).RunRoot(null);

            // Example of reading the rows in the buffer object
            // (dump folder column values to Console)
            var colIndexFolderPath= sink.ColumnIndex["Folder"];
            var colIndexDatasetID = sink.ColumnIndex["Dataset_ID"];
            var colIndexDatasetName = sink.ColumnIndex["Dataset"];
            dctRawFiles = new Dictionary<string, string>();
            foreach (var row in sink.Rows)
            {
                // Some dataset folders might have multiple .raw files (one starting with x_ and another the real one)
                // Check for this

                var datasetID = row[colIndexDatasetID];

                if (dctRawFiles.TryGetValue(datasetID, out var existingRawValue))
                {
                    if (!existingRawValue.ToLower().StartsWith("x_"))
                        continue;

                    dctRawFiles.Remove(datasetID);
                }

                var rawFilePath = Path.Combine(row[colIndexFolderPath], row[colIndexDatasetName] + RAW_FILE_EXTENSION);
                dctRawFiles.Add(datasetID, rawFilePath);
            }

            if (dctRawFiles.Count == 0)
            {
                var message = "Error in LookupMSMSFiles; no results found using " + reader.SQLText;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new Exception(message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Query table T_Results_Metadata_Typed in the SQLite database to determine the DeconTools _isos.csv files to use
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="dctIsosFiles">Keys are dataset IDs; values are the path to the _isos.csv file</param>
        /// <returns>True if success, otherwise false</returns>
        /// <remarks>Does not validate that each _isos.csv file exists; only that the results folder exists and is not empty</remarks>
        private bool LookupDeconToolsInfo(SQLiteReader reader, out Dictionary<string, string> dctIsosFiles)
        {
            try
            {
                var success = LookupDeconToolsInfo(reader, "T_Results_Metadata_Typed", out dctIsosFiles);
                if (success)
                    return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Table T_Results_Metadata_Typed not found; will look for t_results_metadata", ex);
            }

            try
            {
                var success = LookupDeconToolsInfo(reader, "t_results_metadata", out dctIsosFiles);
                if (success)
                    return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in LookupDeconToolsInfo: " + ex.Message, ex);
                throw;
            }

            return false;
        }

        /// <summary>
        /// Look for DeconTools analysis jobs in the given table
        /// </summary>
        /// <param name="reader">SQLite Reader</param>
        /// <param name="tableName">Table with analysis job info</param>
        /// <param name="dctIsosFiles">Keys are dataset IDs; values are the path to the _isos.csv file</param>
        /// <returns></returns>
        private bool LookupDeconToolsInfo(SQLiteReader reader, string tableName, out Dictionary<string, string> dctIsosFiles)
        {
            // Make a Mage sink module (simple row buffer)
            var sink = new SimpleSink();

            // Add rows from other table
            reader.SQLText = "SELECT * FROM " + tableName + " WHERE Tool Like 'Decon%'";

            // Construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);

            var colIndexDatasetID = sink.ColumnIndex["Dataset_ID"];
            var colIndexDatasetName = sink.ColumnIndex["Dataset"];
            var colIndexFolder = sink.ColumnIndex["Folder"];
            dctIsosFiles = new Dictionary<string, string>();

            // Store the paths indexed by datasetID in isosPaths
            foreach (var row in sink.Rows)
            {
                var tempIsosFolder = row[colIndexFolder];
                if (Directory.Exists(tempIsosFolder))
                {
                    var isosFileCandidate = Directory.GetFiles(tempIsosFolder);
                    if (isosFileCandidate.Length > 0)
                    {
                        var datasetID = row[colIndexDatasetID];

                        if (dctIsosFiles.ContainsKey(datasetID))
                            continue;

                        var isosFilePath = Path.Combine(row[colIndexFolder], row[colIndexDatasetName] + ISOS_FILE_EXTENSION);
                        dctIsosFiles.Add(datasetID, isosFilePath);
                    }

                }
            }

            return true;
        }

        /// <summary>
        /// Report progress
        /// </summary>
        /// <param name="progressOverall">Progress percent complete overall (value between 0 and 100)</param>
        /// <param name="progressCurrentFile">Progress for the current file (value between 0 and 100)</param>
        protected void OnProgressChanged(float progressOverall, float progressCurrentFile)
        {
            OnProgressUpdate("Processing", progressOverall);

            if (ProgressChanged == null) return;

            var e = new ProgressInfo
            {
                Value = progressOverall,
                ProgressCurrentFile = progressCurrentFile
            };

            ProgressChanged(this, e);
        }

        /// <summary>
        /// Collects the parent ion information as well as interference
        /// </summary>
        /// <param name="fileCountCurrent">Rank order of the current dataset being processed</param>
        /// <param name="fileCountTotal">Total number of dataset files to process</param>
        /// <param name="rawFilePath">Path to the the .Raw file</param>
        /// <param name="isosFilePath">Path to the _isos.csv file</param>
        /// <param name="scanStart">Start scan; 0 to start at scan 1</param>
        /// <param name="scanEnd">End scan; 0 to process from scanStart to the end</param>
        /// <returns>Precursor info list</returns>
        public List<PrecursorIntense> ParentInfoPass(
            int fileCountCurrent, int fileCountTotal,
            string rawFilePath, string isosFilePath,
            int scanStart = 0, int scanEnd = 0)
        {
            var rawFileReader = new XRawFileIO();

            // Copy the raw file locally to reduce network traffic
            var fileTools = new FileTools();

            var remoteRawFile = new FileInfo(rawFilePath);

            if (!remoteRawFile.Exists)
            {
                OnErrorEvent("File not found: " + remoteRawFile.FullName);
                if (ThrowEvents)
                    throw new FileNotFoundException(remoteRawFile.FullName);
                return new List<PrecursorIntense>();
            }

            var localRawFile = new FileInfo(Path.Combine(WorkDir, remoteRawFile.Name));
            bool deleteLocalFile;

            if (!string.Equals(remoteRawFile.FullName, localRawFile.FullName, StringComparison.OrdinalIgnoreCase))
            {
                OnStatusEvent(string.Format("Copying {0} to {1}", remoteRawFile.FullName, localRawFile.FullName));
                fileTools.CopyFileUsingLocks(remoteRawFile.FullName, localRawFile.FullName, "IDM");
                deleteLocalFile = true;
            }
            else
            {
                deleteLocalFile = false;
            }

            var success = rawFileReader.OpenRawFile(localRawFile.FullName);
            if (!success)
            {
                var message = "Failed to open .Raw file in ParentInfoPass: " + localRawFile.FullName;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new Exception(message);
                return new List<PrecursorIntense>();
            }

            string isosFilePathLocal;
            IsosHandler isosReader;

            if (string.IsNullOrEmpty(isosFilePath))
            {
                isosFilePathLocal = "";
                isosReader = null;
            }
            else
            {
                var remoteIsosFile = new FileInfo(isosFilePath);
                if (!remoteIsosFile.Exists)
                {
                    OnStatusEvent("Warning, remote isos file not found: " + remoteIsosFile.FullName);
                    OnStatusEvent("If a precursor ion's charge is reported as 0 by the Thermo reader, we will try to determine it empirically");
                    isosFilePathLocal = "";
                    isosReader = null;
                }
                else
                {

                    isosFilePathLocal = Path.Combine(WorkDir, remoteIsosFile.Name);
                    var localIsosFile = new FileInfo(isosFilePathLocal);

                    if (!string.Equals(remoteIsosFile.FullName, localIsosFile.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        OnStatusEvent(string.Format("Copying {0} to {1}", remoteIsosFile.FullName, localIsosFile.FullName));
                        fileTools.CopyFileUsingLocks(remoteIsosFile.FullName, localIsosFile.FullName, "IDM");
                    }

                    isosReader = new IsosHandler(isosFilePathLocal, ThrowEvents);
                    RegisterEvents(isosReader);
                }

            }

            var lstPrecursorInfo = new List<PrecursorIntense>();
            var numSpectra = rawFileReader.GetNumScans();

            var currentPrecursorScan = 0;

            // Go into each scan and collect precursor info.
            var progressThreshold = 0.0;

            if (scanStart < 1)
                scanStart = 1;

            if (scanEnd > numSpectra || scanEnd == 0)
                scanEnd = numSpectra;

            SkipConsoleWriteIfNoProgressListener = true;

            var interferenceCalc = new InterferenceCalculator();

            for (var scanNumber = scanStart; scanNumber <= scanEnd; scanNumber++)
            {
                if (scanEnd > scanStart && (scanNumber - scanStart) / (double)(scanEnd - scanStart) > progressThreshold)
                {
                    if (progressThreshold > 0 && ShowProgressAtConsole)
                        OnDebugEvent("  " + progressThreshold * 100 + "% completed");

                    var percentCompleteCurrentFile = (float)progressThreshold * 100;
                    var percentCompleteOverall = ((fileCountCurrent - 1) / (float)fileCountTotal + (float)progressThreshold / fileCountTotal) * 100;

                    OnProgressChanged(percentCompleteOverall, percentCompleteCurrentFile);

                    progressThreshold += .05;
                }


                rawFileReader.GetScanInfo(scanNumber, out clsScanInfo scanInfo);

                var msOrder = scanInfo.MSLevel;

                if (msOrder <= 1)
                {
                    currentPrecursorScan = scanNumber;
                    continue;
                }

                int chargeState;
                if (!scanInfo.TryGetScanEvent(SCAN_EVENT_CHARGE_STATE, out var chargeStateText, true))
                {
                    OnWarningEvent(string.Format(
                        "Warning, scan {0} does not have scan event '{1}'; will try to determine the charge empirically",
                        scanNumber, SCAN_EVENT_CHARGE_STATE));

                    chargeState = 0;
                }
                else
                {
                    if (!int.TryParse(chargeStateText, out chargeState))
                        chargeState = 0;
                }

                if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MS2_ISOLATION_WIDTH, out var isolationWidthText, true))
                {
                    OnWarningEvent(
                        string.Format("Skipping scan {0} since scan event '{1}' not found",
                                      scanNumber, SCAN_EVENT_MS2_ISOLATION_WIDTH));
                    continue;
                }

                scanInfo.TryGetScanEvent(SCAN_EVENT_ION_INJECTION_TIME, out var agcTimeText, true);

                double mz;
                if (Math.Abs(scanInfo.ParentIonMZ) < 1e-6)
                {
                    // ThermoRawFileReader could not determine the parent ion m/z value
                    // Use scan event "Monoisotopic M/Z" instead
                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MONOISOTOPIC_MZ, out var monoMzText, true))
                    {
                        OnWarningEvent(
                            string.Format("Skipping scan {0} since scan event '{1}' not found",
                                          scanNumber, SCAN_EVENT_MONOISOTOPIC_MZ));
                        continue;
                    }

                    if (!double.TryParse(monoMzText, out mz))
                    {
                        OnWarningEvent(
                            string.Format("Skipping scan {0} since scan event '{1}' was not a number: {2}",
                                          scanNumber, SCAN_EVENT_MONOISOTOPIC_MZ, monoMzText));
                        continue;
                    }
                }
                else
                {
                    mz = scanInfo.ParentIonMZ;
                }

                var isolationWidth = Convert.ToDouble(isolationWidthText);

                if (chargeState == 0)
                {
                    // In some cases the raw file fails to provide a charge state, if that is the case
                    // check the isos file to see if DeconTools could figure it out.
                    isosReader?.GetChargeState(currentPrecursorScan, mz, ref chargeState);
                }

                // chargeState might still be 0; that's OK
                // InterferenceCalculator.Interference will try to determine it

                var precursorInfo = new PrecursorIntense(mz, isolationWidth, chargeState)
                {
                    ScanNumber = scanNumber,
                    PrecursorScanNumber = currentPrecursorScan
                };

                if (!string.IsNullOrEmpty(agcTimeText) && double.TryParse(agcTimeText, out var ionCollectionTime))
                {
                    precursorInfo.IonCollectionTime = ionCollectionTime;
                }

                ComputeInterference(interferenceCalc, precursorInfo, rawFileReader);
                lstPrecursorInfo.Add(precursorInfo);
            }
            rawFileReader.CloseRawFile();

            if (interferenceCalc.UnknownChargeCount > 0)
            {
                OnWarningEvent(string.Format(
                    "Charge could not be determined for {0:F1}% of the precursors ({1} / {2})",
                    interferenceCalc.UnknownChargeCount / (double)lstPrecursorInfo.Count * 100,
                    interferenceCalc.UnknownChargeCount, lstPrecursorInfo.Count));
            }

            if (deleteLocalFile)
            {
                // Delete the locally cached raw file
                DeleteFile(localRawFile.FullName);
            }

            // Delete the locally cached isos file (if it exists)
            DeleteFile(isosFilePathLocal);

            return lstPrecursorInfo;
        }

        /// <summary>
        /// Write out the interference scores to a temporary file
        /// This data will be loaded into SQLite later
        /// </summary>
        /// <param name="lstPrecursorInfo"></param>
        /// <param name="datasetID">Id number is a string because that's what sql gives me and there
        /// is no point in switching it back and forth</param>
        /// <param name="filepath"></param>
        public void ExportInterferenceScores(IEnumerable<PrecursorIntense> lstPrecursorInfo, string datasetID, string filepath)
        {
            var fileExists = File.Exists(filepath);
            using (var writer = new StreamWriter(filepath, fileExists))
            {
                if (!fileExists)
                {
                    // Add the header line
                    writer.WriteLine("Dataset_ID\tScanNumber\tPrecursorScan\t" +
                                     "ParentMZ\tChargeState\t" +
                                     "IsoWidth\tInterference\t" +
                                     "PreIntensity\tIonCollectionTime");
                }

                foreach (var info in lstPrecursorInfo)
                {
                    writer.WriteLine(datasetID + "\t" +
                                     info.ScanNumber + "\t" +
                                     info.PrecursorScanNumber + "\t" +
                                     NumToString(info.IsolationMass, 5) + "\t" +
                                     info.ChargeState + "\t" +
                                     NumToString(info.IsolationWidth, 3) + "\t" +
                                     NumToString(info.Interference, 4) + "\t" +
                                     NumToString(info.PrecursorIntensity, 2) + "\t" +
                                     NumToString(info.IonCollectionTime, 2));
                }
            }
        }

        private string NumToString(double value, int digitsOfPrecision)
        {
            if (digitsOfPrecision == 0 && Math.Abs(value) <= double.Epsilon)
                return "0";

            if (!mFormatStrings.TryGetValue(digitsOfPrecision, out var formatString))
            {
                if (digitsOfPrecision < 1)
                    formatString = "0";
                else
                    formatString = "0." + new string('0', digitsOfPrecision);

                mFormatStrings.Add(digitsOfPrecision, formatString);
            }

            var valueText = value.ToString(formatString);
            if (digitsOfPrecision > 0)
                return valueText.TrimEnd('0').TrimEnd('.');

            return valueText;
        }

        private void ComputeInterference(InterferenceCalculator interferenceCalc, PrecursorIntense precursorInfo, XRawFileIO raw)
        {

            if (mSpectraData2D == null || mCachedPrecursorScan != precursorInfo.PrecursorScanNumber)
            {
                // Retrieve centroided data as a 2D array of m/z and intensity
                raw.GetScanData2D(precursorInfo.PrecursorScanNumber, out mSpectraData2D, 0, true);

                mCachedPrecursorScan = precursorInfo.PrecursorScanNumber;
            }

            interferenceCalc.Interference(precursorInfo, mSpectraData2D);
        }

    }
}
