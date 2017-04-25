using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Mage;
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
            return string.Format("{0:F2} m/z, intensity {1:F0}", Mz, Abundance);
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
        public float Value { get; set;}

        /// <summary>
        /// Percent complete for the current file
        /// </summary>
        public float ProgressCurrentFile { get; set; }
    }

    /// <summary>
    /// Interference detection algorithm - uses sqlite, .raw, and _isos.csv as input
    /// </summary>
    public class InterferenceDetector
    {
        public const string DEFAULT_RESULT_DATABASE_NAME = "Results.db3";

        private const string raw_ext = ".raw";
        private const string isos_ext = "_isos.csv";

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
                throw new DirectoryNotFoundException("Database folder not found: " + databaseFolderPath);

            var fiDatabaseFile = new FileInfo(Path.Combine(diDataFolder.FullName, databaseFileName));
            if (!fiDatabaseFile.Exists)
                throw new FileNotFoundException("Database not found: " + fiDatabaseFile.FullName);

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
                throw new Exception("Error calling LookupMSMSFiles: " + ex.Message, ex);
            }

            try
            {
                success = LookupDeconToolsInfo(reader, out dctIsosFiles);
                if (!success)
                    return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Error calling LookupDeconToolsInfo: " + ex.Message, ex);
            }

            if (dctIsosFiles.Count != dctRawFiles.Count)
            {
                throw new Exception("Error in InterferenceDetector.Run: isosPaths.count <> filePaths.count (" + dctIsosFiles.Count + " vs. " + dctRawFiles.Count + ")");
            }

            try
            {
                PerformWork(fiDatabaseFile, dctRawFiles, dctIsosFiles);
            }
            catch (Exception ex)
            {
                throw new Exception("Error calling PerformWork: " + ex.Message, ex);
            }

            return true;
        }

        public bool GUI_PerformWork(string outpath, string rawFilePath, string isosFilePath)
        {
            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            var tempPrecFilePath = outpath;

            Console.WriteLine("Processing file: " + Path.GetFileName(rawFilePath));
            List<PrecursorIntense> lstPrecursorInfo = null;
            try
            {
                lstPrecursorInfo = ParentInfoPass(1, 1, rawFilePath, isosFilePath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            if (lstPrecursorInfo == null)
            {
                DeleteFile(tempPrecFilePath);
                Console.WriteLine("Error in PerformWork: ParentInfoPass returned null loading " + rawFilePath);
                return false;
            }

            ExportInterferenceScores(lstPrecursorInfo, "000000", tempPrecFilePath);

            Console.WriteLine("Process Complete");
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fiDatabaseFile"></param>
        /// <param name="dctRawFiles">Keys are dataset names; values are the path to the .raw file</param>
        /// <param name="dctIsosFiles">KKeys are dataset names; values are the path to the _isos.csv file</param>
        /// <returns></returns>
        private bool PerformWork(FileInfo fiDatabaseFile, Dictionary<string, string> dctRawFiles, Dictionary<string, string> dctIsosFiles)
        {
            var fileCountCurrent = 0;

            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            Debug.Assert(fiDatabaseFile.DirectoryName != null, "fiDatabaseFile.DirectoryName != null");
            var tempPrecFilePath = Path.Combine(fiDatabaseFile.DirectoryName, "prec_info_temp.txt");

            DeleteFile(tempPrecFilePath);

            foreach (var datasetID in dctRawFiles.Keys)
            {
                if (!dctIsosFiles.ContainsKey(datasetID))
                {
                    throw new Exception("Error in PerformWork: Dataset '" + datasetID + "' not found in isosPaths dictionary");
                }

                ++fileCountCurrent;
                Console.WriteLine("Processing file " + fileCountCurrent + " / " + dctRawFiles.Count + ": " + Path.GetFileName(dctRawFiles[datasetID]));

                var lstPrecursorInfo = ParentInfoPass(fileCountCurrent, dctRawFiles.Count, dctRawFiles[datasetID], dctIsosFiles[datasetID]);
                if (lstPrecursorInfo == null)
                {
                    throw new Exception("Error in PerformWork: ParentInfoPass returned null loading " + dctRawFiles[datasetID]);
                }

                ExportInterferenceScores(lstPrecursorInfo, datasetID, tempPrecFilePath);

                if (ShowProgressAtConsole)
                    Console.WriteLine("Iteration Complete");
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
                Console.WriteLine("Error adding table t_precursor_interference to the SqLite database: " + ex.Message);
                Console.WriteLine("Results are in file " + tempPrecFilePath);
                throw new Exception("Error adding table t_precursor_interference to the SqLite database: " + ex.Message, ex);
            }

            if (System.Net.Dns.GetHostName().ToLower().Contains("monroe"))
                return true;

            // Delete the file text file that was imported into SQLite
            DeleteFile(tempPrecFilePath);

            return true;
        }

        private void DeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {
                // Ignore errors here
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
                    filepaths.Add(row[datasetIDIdx], Path.Combine(row[folderPathIdx], row[datasetIdx] + raw_ext));
            }
            if (filepaths.Count == 0)
            {
                throw new Exception("Error in LookupMSMSFiles; no results found using " + reader.SQLText);
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
            catch (Exception)
            {
                Console.WriteLine("Table T_Results_Metadata_Typed not found; will look for  t_results_metadata");
            }

            try
            {
                var success = LookupDeconToolsInfo(reader, "t_results_metadata", out isosPaths);
                if (success)
                    return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
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
                        isosPaths.Add(row[datasetID], Path.Combine(row[folder], row[dataset] + isos_ext));
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

            if (ProgressChanged != null)
            {
                var e = new ProgressInfo
                {
                    Value = progressOverall,
                    ProgressCurrentFile = progressCurrentFile
                };

                ProgressChanged(this, e);
            }
        }

        /// <summary>
        /// Collects the parent ion information as well as inteference
        /// </summary>
        /// <param name="fileCountCurrent">Rank order of the current dataset being processed</param>
        /// <param name="fileCountTotal">Total number of dataset files to process</param>
        /// <param name="rawFilePath">Path to the the .Raw file</param>
        /// <param name="isosFilePath">Path to the _isos.csv file</param>
        /// <returns>Precursor info list</returns>
        public List<PrecursorIntense> ParentInfoPass(int fileCountCurrent, int fileCountTotal, string rawFilePath, string isosFilePath)
        {
            var rawFileReader = new XRawFileIO();

            // Copy the raw file locally to reduce network traffic
            var fileTools = new PRISM.clsFileTools();

            // ReSharper disable once AssignNullToNotNullAttribute
            var rawFilePathLocal = Path.Combine(WorkDir, Path.GetFileName(rawFilePath));

            // ReSharper disable once AssignNullToNotNullAttribute
            var isosFilePathLocal = Path.Combine(WorkDir, Path.GetFileName(isosFilePath));

            if (!string.Equals(rawFilePath, rawFilePathLocal))
                fileTools.CopyFileUsingLocks(rawFilePath, rawFilePathLocal, "IDM");

            if (!string.Equals(isosFilePath, isosFilePathLocal))
                fileTools.CopyFileUsingLocks(isosFilePath, isosFilePathLocal, "IDM");

            var worked = rawFileReader.OpenRawFile(rawFilePathLocal);
            if (!worked)
            {
                throw new Exception("File failed to open .Raw file in ParentInfoPass: " + rawFilePathLocal);
            }

            var isos = new IsosHandler(isosFilePathLocal);

            var lstPrecursorInfo = new List<PrecursorIntense>();
            var numSpectra = rawFileReader.GetNumScans();

            //TODO: Add error code for 0 spectra
            var currPrecScan = 0;
            //Go into each scan and collect precursor info.
            var sr = 0.0;

            const int scanStart = 1;
            var scanEnd = numSpectra;

            for (var scanNumber = 1; scanNumber <= scanEnd; scanNumber++)
            {
                if (scanEnd > scanStart && (scanNumber - scanStart) / (double)(scanEnd - scanStart) > sr)
                {
                    if (sr > 0 && ShowProgressAtConsole)
                        Console.WriteLine("  " + sr * 100 + "% completed");

                    var percentCompleteCurrentFile = (float)sr * 100;
                    var percentCompleteOverall = ((fileCountCurrent - 1) / (float)fileCountTotal + (float)sr / fileCountTotal) * 100;

                    OnProgressChanged(percentCompleteOverall, percentCompleteCurrentFile);

                    sr += .05;
                }

                var msorder = 2;
                if (isos.IsParentScan(scanNumber))
                    msorder = 1;

                rawFileReader.GetScanInfo(scanNumber, out clsScanInfo scanInfo);


                if (msorder > 1)
                {

                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_CHARGE_STATE, out var chargeStateText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_CHARGE_STATE);
                        continue;
                    }

                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MONOISOTOPIC_MZ, out var monoMzText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_MONOISOTOPIC_MZ);
                        continue;
                    }

                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MS2_ISOLATION_WIDTH, out var isolationWidthText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_MS2_ISOLATION_WIDTH);
                        continue;
                    }

                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_ION_INJECTION_TIME, out var agcTimeText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_ION_INJECTION_TIME);
                        continue;
                    }

                    var chargeState = Convert.ToInt32(chargeStateText);

                    double mz;
                    if (Math.Abs(scanInfo.ParentIonMZ) < 1e-6)
                    {
                        mz = Convert.ToDouble(monoMzText);
                    }
                    else
                    {
                        mz = scanInfo.ParentIonMZ;
                    }

                    var isolationWidth = Convert.ToDouble(isolationWidthText);
                    if (chargeState == 0)
                    {
                        if (!isos.GetChargeState(currPrecScan, mz, ref chargeState))
                        {
                            // Unable to determine the charge state; skip this scan
                            continue;
                        }
                    }

                    var precursorInfo = new PrecursorIntense(mz, chargeState, isolationWidth)
                    {
                        ScanNumber = scanNumber,
                        PrecursorScanNumber = currPrecScan,
                        IonCollectionTime = Convert.ToDouble(agcTimeText)
                    };

                    Interference(precursorInfo, rawFileReader);
                    lstPrecursorInfo.Add(precursorInfo);


                }
                else if (msorder == 1)
                {
                    currPrecScan = scanNumber;
                }

            }
            rawFileReader.CloseRawFile();

            // Delete the locally cached raw file
            try
            {
                File.Delete(rawFilePathLocal);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deleting locally cached raw file " + rawFilePathLocal + ": " + ex.Message);
            }

            // Delete the locally cached isos file
            try
            {
                File.Delete(isosFilePathLocal);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deleting locally cached isos file " + isosFilePathLocal + ": " + ex.Message);
            }

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
                }/**/
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

        private void Interference(PrecursorIntense precursorInfo, XRawFileIO raw)
        {

            if (mSpectraData2D == null || mCachedPrecursorScan != precursorInfo.PrecursorScanNumber)
            {
                // Retrieve centroided data as a 2D array of m/z and intensity
                raw.GetScanData2D(precursorInfo.PrecursorScanNumber, out mSpectraData2D, 0, true);

                mCachedPrecursorScan = precursorInfo.PrecursorScanNumber;
            }

            InterferenceCalculator.Interference(precursorInfo, mSpectraData2D);
        }

    }
}
