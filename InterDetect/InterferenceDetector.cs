﻿using System;
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
    public class InterferenceDetector : clsEventNotifier
    {
        public const string DEFAULT_RESULT_DATABASE_NAME = "Results.db3";

        private const string RAW_FILE_EXTENSION = ".raw";
        private const string ISOS_FILE_EXTENSION = "_isos.csv";

        private const string SCAN_EVENT_CHARGE_STATE = "Charge State";
        private const string SCAN_EVENT_MONOISOTOPIC_MZ = "Monoisotopic M/Z";
        private const string SCAN_EVENT_MS2_ISOLATION_WIDTH = "MS2 Isolation Width";
        private const string SCAN_EVENT_ION_INJECTION_TIME = "Ion Injection Time (ms)";

        // Auto-properties
        public bool ShowProgressAtConsole { get; set; }
        public string WorkDir { get; set; }

        protected struct ScanEventIndicesType
        {
            public int ChargeState;
            public int Mz;
            public int IsolationWidth;
            public int AgcTime;
        };

        public event ProgressChangedHandler ProgressChanged;
        public delegate void ProgressChangedHandler(InterferenceDetector id, ProgressInfo e);

        private readonly Dictionary<int, string> mFormatStrings;

        private double[,] mSpectraData2D;
        private int mCachedPrecursorScan;

        /// <summary>
        /// When true, events are thrown up the calling tree for the parent class to handle them
        /// </summary>
        /// <remarks>Defaults to true</remarks>
        public bool ThrowEvents { get; set; } = true;

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
        /// Given a datapath makes queries to the database for isos file and raw file paths.  Uses these
        /// to generate an interference table and adds this table to the database
        /// </summary>
        /// <param name="datapath">directory to the database, assumed that database is called Results.db3</param>
        public bool Run(string datapath)
        {
            return Run(datapath, DEFAULT_RESULT_DATABASE_NAME);
        }

        /// <summary>
        /// Given a datapath makes queries to the database for isos file and raw file paths.  Uses these
        /// to generate an interference table and adds this table to the database
        /// </summary>
        /// <param name="databaseFolderPath">directory to the folder with the database</param>
        /// <param name="databaseFileName">Name of the database</param>
        public bool Run(string databaseFolderPath, string databaseFileName)
        {
            // Keys are dataset names; values are the path to the .raw file
            Dictionary<string, string> dctRawFiles;

            // Keys are dataset names; values are the path to the _isos.csv file
            Dictionary<string, string> dctIsosFiles;
            bool success;

            var diDataFolder = new DirectoryInfo(databaseFolderPath);
            if (!diDataFolder.Exists)
            {
                var message = "Database folder not found: " + databaseFolderPath;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new DirectoryNotFoundException(message);
                return false;
            }
            var fiDatabaseFile = new FileInfo(Path.Combine(diDataFolder.FullName, databaseFileName));
            if (!fiDatabaseFile.Exists)
            {
                var message = "Database not found: " + fiDatabaseFile.FullName;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new FileNotFoundException(message);
                return false;
            }

            // build Mage pipeline to read contents of
            // a table in a SQLite database into a data buffer

            // first, make the Mage SQLite reader module
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

        public bool GUI_PerformWork(string outpath, string rawFilePath, string isosFilePath)
        {
            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            var tempPrecFilePath = outpath;

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
                DeleteFile(tempPrecFilePath);
                OnErrorEvent("Error in PerformWork: ParentInfoPass returned null loading " + rawFilePath);
                return false;
            }

            ExportInterferenceScores(lstPrecursorInfo, "000000", tempPrecFilePath);

            OnStatusEvent("Process Complete");
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fiDatabaseFile"></param>
        /// <param name="dctRawFiles">Keys are dataset names; values are the path to the .raw file</param>
        /// <param name="dctIsosFiles">Keys are dataset names; values are the path to the _isos.csv file</param>
        /// <remarks>dctIsosFiles can be null or empty since Isos files are not required</remarks>
        private void PerformWork(FileInfo fiDatabaseFile, IReadOnlyDictionary<string, string> dctRawFiles, IReadOnlyDictionary<string, string> dctIsosFiles)
        {
            var fileCountCurrent = 0;

            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file

            string tempPrecFilePath;

            if (fiDatabaseFile.DirectoryName == null)
                tempPrecFilePath = "prec_info_temp.txt";
            else
                tempPrecFilePath = Path.Combine(fiDatabaseFile.DirectoryName, "prec_info_temp.txt");

            DeleteFile(tempPrecFilePath);

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

                ExportInterferenceScores(lstPrecursorInfo, datasetID, tempPrecFilePath);

                if (ShowProgressAtConsole)
                    OnStatusEvent("Iteration Complete");
            }

            try
            {
                // Create a delimited file reader and write a new table with this info to database
                var delimreader = new DelimitedFileReader
                {
                    FilePath = tempPrecFilePath
                };

                var writer = new SQLiteWriter();
                const string tableName = "t_precursor_interference";
                writer.DbPath = fiDatabaseFile.FullName;
                writer.TableName = tableName;

                ProcessingPipeline.Assemble("ImportToSQLite", delimreader, writer).RunRoot(null);
            }
            catch (Exception ex)
            {
                var message = "Error adding table t_precursor_interference to the SqLite database: " + ex.Message;

                OnErrorEvent(message, ex);
                OnStatusEvent("Results are in file " + tempPrecFilePath);

                if (ThrowEvents)
                    throw new Exception(message, ex);
            }

            if (System.Net.Dns.GetHostName().ToLower().Contains("monroe"))
                return;

            // Delete the file text file that was imported into SQLite
            DeleteFile(tempPrecFilePath);

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

        private bool LookupMSMSFiles(SQLiteReader reader, out Dictionary<string, string> filepaths)
        {
            reader.SQLText = "SELECT * FROM t_msms_raw_files;";

            // Make a Mage sink module (simple row buffer)
            var sink = new SimpleSink();

            // construct and run the Mage pipeline to obtain data from t_msms_raw_files
            ProcessingPipeline.Assemble("Test_Pipeline", reader, sink).RunRoot(null);

            // example of reading the rows in the buffer object
            // (dump folder column values to Console)
            var folderPathIdx = sink.ColumnIndex["Folder"];
            var datasetIDIdx = sink.ColumnIndex["Dataset_ID"];
            var datasetIdx = sink.ColumnIndex["Dataset"];
            filepaths = new Dictionary<string, string>();
            foreach (var row in sink.Rows)
            {
                // Some dataset folders might have multiple .raw files (one starting with x_ and another the real one)
                // This could lead to duplicate key errors when trying to add a new entry in filepaths
                if (!filepaths.ContainsKey(row[datasetIDIdx]))
                    filepaths.Add(row[datasetIDIdx], Path.Combine(row[folderPathIdx], row[datasetIdx] + RAW_FILE_EXTENSION));
            }

            if (filepaths.Count == 0)
            {
                var message = "Error in LookupMSMSFiles; no results found using " + reader.SQLText;
                OnErrorEvent(message);
                if (ThrowEvents)
                    throw new Exception(message);
                return false;
            }

            return true;
        }

        private bool LookupDeconToolsInfo(SQLiteReader reader, out Dictionary<string, string> isosPaths)
        {
            try
            {
                var success = LookupDeconToolsInfo(reader, "T_Results_Metadata_Typed", out isosPaths);
                if (success)
                    return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Table T_Results_Metadata_Typed not found; will look for t_results_metadata", ex);
            }

            try
            {
                var success = LookupDeconToolsInfo(reader, "t_results_metadata", out isosPaths);
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

        private bool LookupDeconToolsInfo(SQLiteReader reader, string tableName, out Dictionary<string, string> isosPaths)
        {
            // Make a Mage sink module (simple row buffer)
            var sink = new SimpleSink();

            //Add rows from other table
            reader.SQLText = "SELECT * FROM " + tableName + " WHERE Tool Like 'Decon%'";

            // construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);

            var datasetID = sink.ColumnIndex["Dataset_ID"];
            var dataset = sink.ColumnIndex["Dataset"];
            var folder = sink.ColumnIndex["Folder"];
            isosPaths = new Dictionary<string, string>();

            //store the paths indexed by datasetID in isosPaths
            foreach (var row in sink.Rows)
            {
                var tempIsosFolder = row[folder];
                if (Directory.Exists(tempIsosFolder))
                {
                    var isosFileCandidate = Directory.GetFiles(tempIsosFolder);
                    if (isosFileCandidate.Length != 0 && File.Exists(isosFileCandidate[0]))
                    {
                        isosPaths.Add(row[datasetID], Path.Combine(row[folder], row[dataset] + ISOS_FILE_EXTENSION));
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
        /// Collects the parent ion information as well as inteference
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
            var fileTools = new clsFileTools();

            var remoteRawFile = new FileInfo(rawFilePath);

            if (!remoteRawFile.Exists)
            {
                OnErrorEvent("File not found: " + remoteRawFile.FullName);
                if (ThrowEvents)
                    throw new FileNotFoundException(remoteRawFile.FullName);
                return new List<PrecursorIntense>();
            }

            var rawFilePathLocal = Path.Combine(WorkDir, remoteRawFile.Name);

            if (!string.Equals(rawFilePath, rawFilePathLocal))
            {
                OnStatusEvent(string.Format("Copying {0} to the local computer", remoteRawFile.FullName));
                fileTools.CopyFileUsingLocks(remoteRawFile.FullName, rawFilePathLocal, "IDM");
            }

            var success = rawFileReader.OpenRawFile(rawFilePathLocal);
            if (!success)
            {
                var message = "File failed to open .Raw file in ParentInfoPass: " + rawFilePathLocal;
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

                    if (!string.Equals(isosFilePath, isosFilePathLocal))
                    {
                        OnStatusEvent(string.Format("Copying {0} to the local computer", remoteIsosFile.FullName));
                        fileTools.CopyFileUsingLocks(remoteIsosFile.FullName, isosFilePathLocal, "IDM");
                    }

                    isosReader = new IsosHandler(isosFilePathLocal, ThrowEvents);
                    RegisterEvents(isosReader);
                }

            }

            var lstPrecursorInfo = new List<PrecursorIntense>();
            var numSpectra = rawFileReader.GetNumScans();

            var currPrecScan = 0;

            //Go into each scan and collect precursor info.
            var progressThreshold = 0.0;

            if (scanStart < 1)
                scanStart = 1;

            if (scanEnd > numSpectra || scanEnd == 0)
                scanEnd = numSpectra;

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

                var msorder = scanInfo.MSLevel;

                if (msorder <= 1)
                {
                    currPrecScan = scanNumber;
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
                    isosReader?.GetChargeState(currPrecScan, mz, ref chargeState);
                }

                // chargeState might still be 0; that's OK
                // InterferenceCalculator.Interference will try to determine it

                var precursorInfo = new PrecursorIntense(mz, isolationWidth, chargeState)
                {
                    ScanNumber = scanNumber,
                    PrecursorScanNumber = currPrecScan
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

            // Delete the locally cached raw file
            DeleteFile(rawFilePathLocal);

            // Delete the locally cached isos file (if it exists)
            DeleteFile(isosFilePathLocal);

            return lstPrecursorInfo;
        }

        /// <summary>
        /// Write out the interference scores to a temporary file
        /// This data will be loaded into SQLite later
        /// </summary>
        /// <param name="lstPrecursorInfo"></param>
        /// <param name="datasetID">Id number is a string because thats what sql gives me and there
        /// is no point in switching it back and forth</param>
        /// <param name="filepath"></param>
        public void ExportInterferenceScores(IEnumerable<PrecursorIntense> lstPrecursorInfo, string datasetID, string filepath)
        {
            var fieldExistance = File.Exists(filepath);
            using (var sw = new StreamWriter(filepath, fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.WriteLine("Dataset_ID\tScanNumber\tPrecursorScan\t" +
                                 "ParentMZ\tChargeState\t" +
                                 "IsoWidth\tInterference\t" +
                                 "PreIntensity\tIonCollectionTime");
                }

                foreach (var info in lstPrecursorInfo)
                {
                    sw.WriteLine(datasetID + "\t" +
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
