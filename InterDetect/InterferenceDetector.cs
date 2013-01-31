﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using NUnit.Framework;
using System.IO;
using Mage;
using System.Data.SQLite;
using ThermoRawFileReaderDLL;
using log4net;
using ThermoRawFileReaderDLL.FinniganFileIO;

namespace InterDetect
{

	public struct Peak
	{
		public double mz;
		public double abundance;
	};

	public class ProgressInfo : EventArgs
	{
		public float Value { get; set;}
	}

	public class InterferenceDetector
	{
		
		private const string raw_ext = ".raw";
		private const string isos_ext = "_isos.csv";

		protected struct ScanEventIndicesType
		{
			public int chargeState;
			public int mz;
			public int isolationWidth;
            public int agctime;
		};

		public event ProgressChangedHandler ProgressChanged;
		public delegate void ProgressChangedHandler(InterferenceDetector id, ProgressInfo e);

		protected delegate void InterferenceDelegate(ref PrecursorIntense preInfo, ref XRawFileIO raw, ref FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo);

		[Test]
		public void DatabaseCheck()
		{
			if (!Run(@"C:\DMS_WorkDir\Step_1_ASCORE"))
			{
				Console.WriteLine("You Fail");
			}

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

			// Keys in this dictionary are folder paths; values are the name of the .Raw file
			Dictionary<string, string> filepaths;

			Dictionary<string, string> isosPaths;
			bool success = false;
			
			System.IO.DirectoryInfo diDataFolder = new System.IO.DirectoryInfo(databaseFolderPath);
			if (!diDataFolder.Exists)
				throw new System.IO.DirectoryNotFoundException("Database folder not found: " + databaseFolderPath);


			System.IO.FileInfo fiDatabaseFile = new System.IO.FileInfo(System.IO.Path.Combine(diDataFolder.FullName, databaseFileName));
			if (!fiDatabaseFile.Exists)
				throw new System.IO.FileNotFoundException("Database not found: " + fiDatabaseFile.FullName);

			// build Mage pipeline to read contents of 
			// a table in a SQLite database into a data buffer

			// first, make the Mage SQLite reader module
			// and configure it to read the table
			SQLiteReader reader = new SQLiteReader();
			reader.Database = fiDatabaseFile.FullName;

			try
			{
				success = LookupMSMSFiles(reader, out filepaths);
				if (!success)
					return false;
			}
			catch (Exception ex)
			{
				throw new Exception("Error calling LookupMSMSFiles: " + ex.Message, ex);
			}

			try
			{
				success = LookupDeconToolsInfo(reader, out isosPaths);
				if (!success)
					return false;
			}
			catch (Exception ex)
			{
				throw new Exception("Error calling LookupDeconToolsInfo: " + ex.Message, ex);
			}
			
			if (isosPaths.Count != filepaths.Count)
			{
				throw new Exception("Error in InterferenceDetector.Run: isosPaths.count <> filePaths.count");
			}

			try
			{
				success = PerformWork(fiDatabaseFile, filepaths, isosPaths);
			}
			catch (Exception ex)
			{
				throw new Exception("Error calling PerformWork: " + ex.Message, ex);
			}


			return true;

		}

		private bool PerformWork(System.IO.FileInfo fiDatabaseFile, Dictionary<string, string> filepaths, Dictionary<string, string> isosPaths)
		{
			string errorMessage = string.Empty;
			int fileCountCurrent = 0;

			//Calculate the needed info and generate a temporary file, keep adding each dataset to this file
			string tempPrecFilePath = Path.Combine(fiDatabaseFile.DirectoryName, "prec_info_temp.txt");

			foreach (string datasetID in filepaths.Keys)
			{
				if (!isosPaths.ContainsKey(datasetID))
				{
					DeleteFile(tempPrecFilePath);
					throw new Exception("Error in PerformWork: Dataset '" + datasetID + "' not found in isosPaths dictionary");
				}

				++fileCountCurrent;
				Console.WriteLine("Processing file " + fileCountCurrent + " / " + filepaths.Count + ": " + Path.GetFileName(filepaths[datasetID]));

				List<PrecursorIntense> myInfo = ParentInfoPass(fileCountCurrent, filepaths.Count, filepaths[datasetID], isosPaths[datasetID]);
				if (myInfo == null)
				{
					DeleteFile(tempPrecFilePath);
					throw new Exception("Error in PerformWork: ParentInfoPass returned null loading " + filepaths[datasetID]);
				}

				PrintInterference(myInfo, datasetID, tempPrecFilePath);

				Console.WriteLine("Iteration Complete");
			}

			try
			{
				//Create a delimeted file reader and write a new table with this info to database
				DelimitedFileReader delimreader = new DelimitedFileReader();
				delimreader.FilePath = tempPrecFilePath;

				SQLiteWriter writer = new SQLiteWriter();
				string tableName = "t_precursor_interference";
				writer.DbPath = fiDatabaseFile.FullName;
				writer.TableName = tableName;

				ProcessingPipeline.Assemble("ImportToSQLite", delimreader, writer).RunRoot(null);
			}
			catch (Exception ex)
			{
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
			catch
			{
				// Ignore errors here
			}
		}

		private bool LookupMSMSFiles(SQLiteReader reader, out Dictionary<string, string> filepaths)
		{
			reader.SQLText = "SELECT * FROM t_msms_raw_files;";

			// Make a Mage sink module (simple row buffer)
			SimpleSink sink = new SimpleSink();

			// construct and run the Mage pipeline to obtain data from t_msms_raw_files
			ProcessingPipeline.Assemble("Test_Pipeline", reader, sink).RunRoot(null);

			// example of reading the rows in the buffer object
			// (dump folder column values to Console)
			int folderPathIdx = sink.ColumnIndex["Folder"];
			int datasetIDIdx = sink.ColumnIndex["Dataset_ID"];
			int datasetIdx = sink.ColumnIndex["Dataset"];
			filepaths = new Dictionary<string, string>();
			foreach (object[] row in sink.Rows)
			{
				filepaths.Add(row[datasetIDIdx].ToString(), Path.Combine(row[folderPathIdx].ToString(), row[datasetIdx].ToString() + raw_ext));
			}
			if (filepaths.Count == 0)
			{
				throw new Exception("Error in LookupMSMSFiles; no results found using " + reader.SQLText);
			}

			return true;
		}


		private bool LookupDeconToolsInfo(SQLiteReader reader, out Dictionary<string, string> isosPaths)
		{
			// Make a Mage sink module (simple row buffer)
			SimpleSink sink = new SimpleSink();

			//Add rows from other table
			reader.SQLText = "SELECT * FROM t_results_metadata WHERE t_results_metadata.Tool Like 'Decon%'";

			// construct and run the Mage pipeline
			ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);

			int datasetID = sink.ColumnIndex["Dataset_ID"];
			int dataset = sink.ColumnIndex["Dataset"];
			int folder = sink.ColumnIndex["Folder"];
			isosPaths = new Dictionary<string, string>();

			//store the paths indexed by datasetID in isosPaths
			foreach (object[] row in sink.Rows)
			{
				string tempIsosFolder = row[folder].ToString();
				if (Directory.Exists(tempIsosFolder))
				{
					string[] isosFileCandidate = Directory.GetFiles(tempIsosFolder);
					if (isosFileCandidate.Length != 0 && File.Exists(isosFileCandidate[0]))
					{
						isosPaths.Add(row[datasetID].ToString(), Path.Combine(row[folder].ToString(), row[dataset].ToString() + isos_ext));
					}

				}
			}

			return true;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="progress">Progress percent complete (value between 0 and 100)</param>
		protected void OnProgressChanged(float progress)
		{

			if (ProgressChanged != null)
			{
				ProgressInfo e = new ProgressInfo();
				e.Value = progress;
				ProgressChanged(this, e);
			}
		}



		[Test]
		public void TestSisiData()
		{
			string[] decon = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\DLS201204031741_Auto822622\Sample_4065_iTRAQ_isos.csv", 
            @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\DLS201204031733_Auto822617\Sample_5065_iTRAQ_isos.csv",
            @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\DLS201204031744_Auto822624\Sample_4050_iTRAQ_120330102958_isos.csv"};
			string[] rawfiles = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\Sample_4065_iTRAQ.raw", 
            @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\Sample_5065_iTRAQ.raw",
            @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\Sample_4050_iTRAQ_120330102958.raw"};

			int filesToProcess = 1;

			for (int i = 0; i < filesToProcess; i++)
			{

				List<PrecursorIntense> myInfo = ParentInfoPass(i + 1, filesToProcess, rawfiles[i], decon[i]);
				if (myInfo == null)
				{
					Console.WriteLine(rawfiles[i] + " failed to load.  Deleting temp and aborting!");
					return;
				}
				PrintInterference(myInfo, "number", @"C:\Users\aldr699\Documents\2012\iTRAQ\InterferenceTesting\DataInt" + i + "efz50.txt");
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



		/// <summary>
		/// Collects the parent ion information as well as inteference 
		/// </summary>
		/// <param name="fileCountCurrent">Rank order of the current dataset being processed</param>
		/// <param name="fileCountTotal">Total number of dataset files to process</param>
		/// <param name="rawfile">Path to the the .Raw file</param>
		/// <param name="isosfile">Path to the .Isos file</param>
		/// <returns>Precursor info list</returns>
		public List<PrecursorIntense> ParentInfoPass(int fileCountCurrent, int fileCountTotal, string rawfile, string isosfile)
		{
			return ParentInfoPassWork(fileCountCurrent, fileCountTotal, rawfile, isosfile, Interference);
		}


		/// <summary>
		/// Collects the parent ion information as well as inteference 
		/// </summary>
		/// <param name="fileCountCurrent">Rank order of the current dataset being processed</param>
		/// <param name="fileCountTotal">Total number of dataset files to process</param>
		/// <param name="rawfile">Path to the the .Raw file</param>
		/// <param name="isosfile">Path to the .Isos file</param>
		/// <param name="delegInterference">Function name to use for Interference Detection (either Interference or Interference2)</param>
		/// <returns>Precursor info list</returns>
		protected List<PrecursorIntense> ParentInfoPassWork(int fileCountCurrent, int fileCountTotal, string rawfile, string isosfile, InterferenceDelegate delegInterference)
		{
			bool worked;
			XRawFileIO myRaw = new XRawFileIO();

			worked = myRaw.OpenRawFile(rawfile);
			if (!worked)
			{
				throw new Exception("File failed to open .Raw file in ParentInfoPass: " + rawfile);
			}


			IsosHandler isos = new IsosHandler(isosfile);

			List<PrecursorIntense> preInfo = new List<PrecursorIntense>();
			int numSpectra = myRaw.GetNumScans();

			List<string> lstScanEventNames = new List<string>();
			
			//TODO: Add error code for 0 spectra
			int currPrecScan = 0;
			//Go into each scan and collect precursor info.
			double sr = 0.0;
			for (int scanNumber = 1; scanNumber <= numSpectra; scanNumber++)
			{
				if (scanNumber / (double)numSpectra > sr)
				{
					if (sr > 0)
						Console.WriteLine("  " + sr * 100 + "% completed");

					float percentCompleteOverall = (fileCountCurrent - 1) / (float)fileCountTotal + (float)sr / (float)fileCountTotal;
					OnProgressChanged(percentCompleteOverall * 100);

					sr += .10;
				}

				int msorder = 2;
				if (isos.IsParentScan(scanNumber))
					msorder = 1;

				FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo = new FinniganFileReaderBaseClass.udtScanHeaderInfoType();
				myRaw.GetScanInfo(scanNumber, out scanInfo);


				if (msorder > 1)
				{
					ScanEventIndicesType scanEventIndices;
					string errorMessage;
					if (!ParseScanEventNames(scanInfo, out scanEventIndices, out errorMessage))
					{
						Console.WriteLine("Skipping scan " + scanNumber + " since " + errorMessage);
						continue;
					}

					int chargeState = Convert.ToInt32(scanInfo.ScanEventValues[scanEventIndices.chargeState]);
					double mz;
					if (scanInfo.ParentIonMZ == 0.0)
					{
						mz = Convert.ToDouble(scanInfo.ScanEventValues[scanEventIndices.mz]);
					}
					else
					{
						mz = scanInfo.ParentIonMZ;
					}
					double isolationWidth = Convert.ToDouble(scanInfo.ScanEventValues[scanEventIndices.isolationWidth]);
					if (chargeState == 0)
					{
						if (!isos.GetChargeState(currPrecScan, mz, ref chargeState))
						{
							// Unable to determine the charge state; skip this scan							
							continue;
						}
					}

					PrecursorIntense info = new PrecursorIntense();
					info.dIsoloationMass = mz;
					info.nScanNumber = scanNumber;
					info.preScanNumber = currPrecScan;
					info.nChargeState = chargeState;
					info.isolationwidth = isolationWidth;
                    info.ionCollectionTime = Convert.ToDouble(scanInfo.ScanEventValues[scanEventIndices.agctime]);
					delegInterference(ref info, ref myRaw, ref scanInfo);
					preInfo.Add(info);


				}
				else if (msorder == 1)
				{
					currPrecScan = scanNumber;
				}

			}
			myRaw.CloseRawFile();
			return preInfo;
		}

		private bool ParseScanEventNames(FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo, out ScanEventIndicesType scanEventIndices, out string errorMessage)
		{
			scanEventIndices = new ScanEventIndicesType();
			errorMessage = string.Empty;

			Dictionary<string, int> dctScanEventNames = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

			for (int i = 0; i < scanInfo.ScanEventNames.Length; i++)
			{
				dctScanEventNames.Add(scanInfo.ScanEventNames[i].TrimEnd(new char[] { ':', ' ' }), i);
			}

			if (!ParseScanEventNameLookupIndex(dctScanEventNames, "Charge State", out scanEventIndices.chargeState, out errorMessage))
				return false;

			if (!ParseScanEventNameLookupIndex(dctScanEventNames, "Monoisotopic M/Z", out scanEventIndices.mz, out errorMessage))
				return false;

			if (!ParseScanEventNameLookupIndex(dctScanEventNames, "MS2 Isolation Width", out scanEventIndices.isolationWidth, out errorMessage))
				return false;

            if (!ParseScanEventNameLookupIndex(dctScanEventNames, "Ion Injection Time (ms)", out scanEventIndices.agctime, out errorMessage))
                return false;

			return true;
		}

		protected bool ParseScanEventNameLookupIndex(Dictionary<string, int> dctScanEventNames, string scanEventName, out int scanEventIndex, out string errorMessage)
		{
			errorMessage = string.Empty;

			if (!dctScanEventNames.TryGetValue(scanEventName, out scanEventIndex))
			{
				errorMessage = "Scan event '" + scanEventName + "' not found";
				return false;
			}

			return true;
		}

		/// <summary>
		/// Print our table to a temporary file
		/// </summary>
		/// <param name="preinfo"></param>
		/// <param name="datasetID">Id number is a string because thats what sql gives me and there
		/// is no point in switching it back and forth</param>
		/// <param name="filepath"></param>
        private void PrintInterference(List<PrecursorIntense> preinfo, string datasetID, string filepath)
		{
            bool fieldExistance = File.Exists(filepath);
            using (StreamWriter sw = new StreamWriter(filepath, fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\tPreIntensity\tIonCollectionTime\n");
                }

                foreach (PrecursorIntense info in preinfo)
                {
                    sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
                        info.dIsoloationMass + "\t" + info.nChargeState + "\t" +
                        info.isolationwidth + "\t" + info.interference + "\t" +
                        info.dPrecursorIntensity + "\t" + info.ionCollectionTime + "\n");
                }
            }
		}


		private void Interference(ref PrecursorIntense preInfo, ref XRawFileIO raw, ref FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo)
		{
			double[] mzlist = null;
			double[] abulist = null;

			raw.GetScanData(preInfo.preScanNumber, ref mzlist, ref abulist, ref scanInfo);

            double lowt = preInfo.dIsoloationMass - (preInfo.isolationwidth);
            double hight = preInfo.dIsoloationMass + (preInfo.isolationwidth);
            double low = preInfo.dIsoloationMass - (preInfo.isolationwidth / 2);
            double high = preInfo.dIsoloationMass + (preInfo.isolationwidth / 2);
            int lowind = -1;
            int highind = -1;

            int a = 0;
            int b = mzlist.Length;
            int c = 0;

            lowind = BinarySearch(ref mzlist, a, b, c, lowt);
            highind = BinarySearch(ref mzlist, lowind, b, c, hight);
            
            //capture all peaks in isowidth+buffer
			List<Peak> peaks = ConvertToPeaks(ref mzlist, ref abulist, lowind, highind);

            //remove peaks lying outside of range
            OutsideOfIsoWindow(low, high, ref peaks);
            //find target peak for use as precursor to find interference
            ClosestToTarget(preInfo, peaks);
            //perform the calculation
            InterferenceCalculation(preInfo, lowind, highind, peaks);
		}


        /// <summary>
        /// Calculates interference with the precursor ion
        /// </summary>
        /// <param name="preInfo"></param>
        /// <param name="lowind"></param>
        /// <param name="highind"></param>
        /// <param name="peaks"></param>
        private static void InterferenceCalculation(PrecursorIntense preInfo, int lowind, int highind, List<Peak> peaks)
        {
            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            double PreErrorAllowed = 10.0;
            double MaxPreInt = 0;
            double MaxInterfereInt = 0;
            //  int p = peaks.GetUpperBound(1);
            double OverallInterference = 0;
            if (lowind != -1 && highind != -1)
            {
                for (int j = 0; j < peaks.Count; j++)
                {
                    double difference = (peaks[j].mz - preInfo.dActualMass) * preInfo.nChargeState;
                    double difference_Rounded = Math.Round(difference);
                    double expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
                    double Difference_ppm = Math.Abs((expected_difference - difference) /
                        (preInfo.dIsoloationMass * preInfo.nChargeState)) * 1000000;

                    if (Difference_ppm < PreErrorAllowed)
                    {
                        MaxPreInt += peaks[j].abundance;
                    }

                    MaxInterfereInt += peaks[j].abundance;

                }
                OverallInterference = MaxPreInt / MaxInterfereInt;
            }
            else
            {
                Console.WriteLine("Did not find the precursor");
            }
            preInfo.interference = OverallInterference;
        }


		private List<Peak> ConvertToPeaks(ref double[] mzlist, ref double[] abulist, int lowind, int highind)
		{

			List<Peak> mzs = new List<Peak>();

			for (int i = lowind + 1; i <= highind - 1; i++)
			{
				if (abulist[i] != 0)
				{
					int j = i;
					double abusum = 0.0;
					while (abulist[i] != 0 && !(abulist[i - 1] > abulist[i] && abulist[i + 1] > abulist[i]))
					{
						abusum += abulist[i];
						i++;
					}
					int end = i;
					i = j;
					double peaksum = 0.0;
					double peakmax = 0.0;
					while (/*abulist[i] != 0 ||*/ i != end)
					{
						//test using maximum of peak
						if (abulist[i] > peakmax)
						{
							peakmax = abulist[i];
						}
						//if (abulist[i] > peaksum)
						//{
						//    peakmax = mzlist[i];
						//    peaksum = abulist[i];
						//}

						peaksum += abulist[i] / abusum * mzlist[i];
						i++;
					}
					Peak centroidPeak = new Peak();
					centroidPeak.mz = peaksum;
					centroidPeak.abundance = peakmax;//abusum;
					mzs.Add(centroidPeak);

				}
			}

			return mzs;

		}

        private static int BinarySearch(ref double[] mzlist, int a, int b, int c, double mzToFind)
        {
            int n = 0;
            int nmax = 100;
            double tol = 0.1;

            while (n < nmax)
            {
                if (Math.Abs(mzlist[c] - mzToFind) < tol || c == (b + a) / 2)
                {
                    break;
                }
                c = (b + a) / 2;
                if (mzlist[c] < mzToFind)
                {
                    a = c;
                }
                if (mzlist[c] > mzToFind)
                {
                    b = c;
                }
            }
            return c;


        }


        /// <summary>
        /// finds the peak closest to the targeted peak to be isolated in raw header and 
        /// gets the intensity info.
        /// </summary>
        /// <param name="preInfo"></param>
        /// <param name="peaks"></param>
        private static void ClosestToTarget(PrecursorIntense preInfo, List<Peak> peaks)
        {
            double closest = 1000.0;
            foreach (Peak p in peaks)
            {
                double temp = Math.Abs(p.mz - preInfo.dIsoloationMass);
                if (temp < closest)
                {
                    preInfo.dActualMass = p.mz;
                    closest = temp;
                    preInfo.dPrecursorIntensity = p.abundance;
                }
            }
        }

        /// <summary>
        /// Removes peaks which were not within the isolation window.
        /// </summary>
        /// <param name="low"></param>
        /// <param name="high"></param>
        /// <param name="peaks"></param>
        private static void OutsideOfIsoWindow(double low, double high, ref List<Peak> peaks)
        {
            var rem_peaks = from peak in peaks
                            where peak.mz < high
                            && peak.mz > low
                            select peak;

            peaks = rem_peaks.ToList();

        }

        private static bool alwaysTrue(Peak input)
        {
            return true;
        }


	}
}
