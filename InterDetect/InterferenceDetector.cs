using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
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

        /// <summary>
        /// Constructor
        /// </summary>
        public InterferenceDetector()
        {
            ShowProgressAtConsole = true;
            WorkDir = ".";
        }

        /// <summary>
        /// Given a datapath makes queries to the database for isos file and raw file paths.  Uses these
        /// to generate an interference table and adds this table to the database
        /// </summary>
        /// <param name="datapath">directory to the database, assumed that database is called Results.db3</param>
        public bool Run(string datapath)
        {
            return Run(datapath, "Results.db3");
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

            PrintInterference(lstPrecursorInfo, "000000", tempPrecFilePath);

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

                PrintInterference(lstPrecursorInfo, datasetID, tempPrecFilePath);

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

            //cleanup
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
            var fileTools = new PRISM.Files.clsFileTools();

            // ReSharper disable once AssignNullToNotNullAttribute
            var rawFilePathLocal = Path.Combine(WorkDir, Path.GetFileName(rawFilePath));

            // ReSharper disable once AssignNullToNotNullAttribute
            var isosFilePathLocal = Path.Combine(WorkDir, Path.GetFileName(isosFilePath));

            if (!String.Equals(rawFilePath, rawFilePathLocal))
                fileTools.CopyFileUsingLocks(rawFilePath, rawFilePathLocal, "IDM");

            if (!String.Equals(isosFilePath, isosFilePathLocal))
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

                clsScanInfo scanInfo;
                rawFileReader.GetScanInfo(scanNumber, out scanInfo);


                if (msorder > 1)
                {

                    string chargeStateText;
                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_CHARGE_STATE, out chargeStateText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_CHARGE_STATE);
                        continue;
                    }

                    string monoMzText;
                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MONOISOTOPIC_MZ, out monoMzText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_MONOISOTOPIC_MZ);
                        continue;
                    }

                    string isolationWidthText;
                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_MS2_ISOLATION_WIDTH, out isolationWidthText, true))
                    {
                        Console.WriteLine("Skipping scan {0} since scan event '{1}' not found", scanNumber, SCAN_EVENT_MS2_ISOLATION_WIDTH);
                        continue;
                    }

                    string agcTimeText;
                    if (!scanInfo.TryGetScanEvent(SCAN_EVENT_ION_INJECTION_TIME, out agcTimeText, true))
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

                    var precursorInfo = new PrecursorIntense
                    {
                        IsolationMass = mz,
                        ScanNumber = scanNumber,
                        PrecursorScanNumber = currPrecScan,
                        ChargeState = chargeState,
                        IsolationWidth = isolationWidth,
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
        /// Print our table to a temporary file
        /// </summary>
        /// <param name="lstPrecursorInfo"></param>
        /// <param name="datasetID">Id number is a string because thats what sql gives me and there
        /// is no point in switching it back and forth</param>
        /// <param name="filepath"></param>
        private void PrintInterference(List<PrecursorIntense> lstPrecursorInfo, string datasetID, string filepath)
        {
            var fieldExistance = File.Exists(filepath);
            using (var sw = new StreamWriter(filepath, fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\tPreIntensity\tIonCollectionTime\n");
                }

                foreach (var info in lstPrecursorInfo)
                {
                    sw.Write(datasetID + "\t" + info.ScanNumber + "\t" + info.PrecursorScanNumber + "\t" +
                        info.IsolationMass + "\t" + info.ChargeState + "\t" +
                        info.IsolationWidth + "\t" + info.Interference + "\t" +
                        info.PrecursorIntensity + "\t" + info.IonCollectionTime + "\n");
                }
            }
        }

        private void Interference(PrecursorIntense precursorInfo, XRawFileIO raw)
        {
            double[,] spectraData2D;

            raw.GetScanData2D(precursorInfo.PrecursorScanNumber, out spectraData2D, 0, true);

            InterferenceCalculator.Interference(precursorInfo, spectraData2D);
        }

        #region NUnit Tests

        [Test]
        public void DatabaseCheck()
        {
            if (!Run(@"C:\DMS_WorkDir\Step_1_ASCORE"))
            {
                Console.WriteLine("You Fail");
            }
        }

        [Test]
        public void TestSisiData()
        {
            var decon = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\DLS201204031741_Auto822622\Sample_4065_iTRAQ_isos.csv",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\DLS201204031733_Auto822617\Sample_5065_iTRAQ_isos.csv",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\DLS201204031744_Auto822624\Sample_4050_iTRAQ_120330102958_isos.csv"};
            var rawfiles = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\Sample_4065_iTRAQ.raw",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\Sample_5065_iTRAQ.raw",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\Sample_4050_iTRAQ_120330102958.raw"};

            var filesToProcess = 1;

            for (var i = 0; i < filesToProcess; i++)
            {

                var lstPrecursorInfo = ParentInfoPass(i + 1, filesToProcess, rawfiles[i], decon[i]);
                if (lstPrecursorInfo == null)
                {
                    Console.WriteLine(rawfiles[i] + " failed to load.  Deleting temp and aborting!");
                    return;
                }
                PrintInterference(lstPrecursorInfo, "number", @"C:\Users\aldr699\Documents\2012\iTRAQ\InterferenceTesting\DataInt" + i + "efz50.txt");
            }
        }

        [Test]
        public void TestInterference()
        {
            //     GetParentInfo parentInfo = new GetParentInfoSequest(@"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\DLS201201111344_Auto783501\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09_isos");
            //List<PrecursorInfo> myInfo = ParentInfoPass(@"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28.raw", parentInfo);
            //Interference(ref myInfo, @"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28.raw");

            //PrintInterference(myInfo, @"C:\Users\aldr699\Documents\2012\InterferenceTesting\here2.txt");
        }

        #endregion
    }
}
