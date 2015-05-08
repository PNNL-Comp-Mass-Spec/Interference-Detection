using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using System.IO;
using Mage;
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
		/// <summary>
		/// Overall percent complete (value between 0 and 100)
		/// </summary>
		public float Value { get; set;}

		/// <summary>
		/// Percent complete for the current file
		/// </summary>
		public float ProgressCurrentFile { get; set; }
	}

	public class InterferenceDetector
	{
		
		private const string raw_ext = ".raw";
		private const string isos_ext = "_isos.csv";

		// Auto-properties
		public bool ShowProgressAtConsole { get; set; }
		public string WorkDir { get; set; }

		protected struct ScanEventIndicesType
		{
			public int chargeState;
			public int mz;
			public int isolationWidth;
            public int agctime;
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
			string tempPrecFilePath = outpath;

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
			int fileCountCurrent = 0;

			//Calculate the needed info and generate a temporary file, keep adding each dataset to this file			
			Debug.Assert(fiDatabaseFile.DirectoryName != null, "fiDatabaseFile.DirectoryName != null");
			string tempPrecFilePath = Path.Combine(fiDatabaseFile.DirectoryName, "prec_info_temp.txt");

			foreach (string datasetID in dctRawFiles.Keys)
			{
				if (!dctIsosFiles.ContainsKey(datasetID))
				{
					DeleteFile(tempPrecFilePath);
					throw new Exception("Error in PerformWork: Dataset '" + datasetID + "' not found in isosPaths dictionary");
				}

				++fileCountCurrent;
				Console.WriteLine("Processing file " + fileCountCurrent + " / " + dctRawFiles.Count + ": " + Path.GetFileName(dctRawFiles[datasetID]));

				List<PrecursorIntense> lstPrecursorInfo = ParentInfoPass(fileCountCurrent, dctRawFiles.Count, dctRawFiles[datasetID], dctIsosFiles[datasetID]);
				if (lstPrecursorInfo == null)
				{
					DeleteFile(tempPrecFilePath);
					throw new Exception("Error in PerformWork: ParentInfoPass returned null loading " + dctRawFiles[datasetID]);
				}

				PrintInterference(lstPrecursorInfo, datasetID, tempPrecFilePath);

				if (ShowProgressAtConsole)
					Console.WriteLine("Iteration Complete");
			}

			try
			{
				//Create a delimited file reader and write a new table with this info to database
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
			int folderPathIdx = sink.ColumnIndex["Folder"];
			int datasetIDIdx = sink.ColumnIndex["Dataset_ID"];
			int datasetIdx = sink.ColumnIndex["Dataset"];
			filepaths = new Dictionary<string, string>();
			foreach (string[] row in sink.Rows)
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
			// Make a Mage sink module (simple row buffer)
			var sink = new SimpleSink();

			//Add rows from other table
			reader.SQLText = "SELECT * FROM t_results_metadata WHERE t_results_metadata.Tool Like 'Decon%'";

			// construct and run the Mage pipeline
			ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);

			int datasetID = sink.ColumnIndex["Dataset_ID"];
			int dataset = sink.ColumnIndex["Dataset"];
			int folder = sink.ColumnIndex["Folder"];
			isosPaths = new Dictionary<string, string>();

			//store the paths indexed by datasetID in isosPaths
			foreach (string[] row in sink.Rows)
			{
				string tempIsosFolder = row[folder];
				if (Directory.Exists(tempIsosFolder))
				{
					string[] isosFileCandidate = Directory.GetFiles(tempIsosFolder);
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

				List<PrecursorIntense> lstPrecursorInfo = ParentInfoPass(i + 1, filesToProcess, rawfiles[i], decon[i]);
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

			bool worked = rawFileReader.OpenRawFile(rawFilePathLocal);
			if (!worked)
			{
				throw new Exception("File failed to open .Raw file in ParentInfoPass: " + rawFilePathLocal);
			}

			var isos = new IsosHandler(isosFilePathLocal);

			var lstPrecursorInfo = new List<PrecursorIntense>();
			int numSpectra = rawFileReader.GetNumScans();
			
			//TODO: Add error code for 0 spectra
			int currPrecScan = 0;
			//Go into each scan and collect precursor info.
			double sr = 0.0;

			const int scanStart = 1;
			int scanEnd = numSpectra;

			for (int scanNumber = 1; scanNumber <= scanEnd; scanNumber++)
			{
				if (scanEnd > scanStart && (scanNumber - scanStart) / (double)(scanEnd - scanStart) > sr)
				{
					if (sr > 0 && ShowProgressAtConsole)
						Console.WriteLine("  " + sr * 100 + "% completed");

					float percentCompleteCurrentFile = (float)sr * 100;
					float percentCompleteOverall = ((fileCountCurrent - 1) / (float)fileCountTotal + (float)sr / fileCountTotal) * 100;

					OnProgressChanged(percentCompleteOverall, percentCompleteCurrentFile);

					sr += .05;
				}

				int msorder = 2;
				if (isos.IsParentScan(scanNumber))
					msorder = 1;

				FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo;
				rawFileReader.GetScanInfo(scanNumber, out scanInfo);


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
					if (Math.Abs(scanInfo.ParentIonMZ) < 1e-6)
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

					var precursorInfo = new PrecursorIntense
					{
						dIsoloationMass = mz,
						nScanNumber = scanNumber,
						preScanNumber = currPrecScan,
						nChargeState = chargeState,
						isolationwidth = isolationWidth,
						ionCollectionTime = Convert.ToDouble(scanInfo.ScanEventValues[scanEventIndices.agctime])
					};

					Interference(ref precursorInfo, ref rawFileReader);
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

		private bool ParseScanEventNames(FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo, out ScanEventIndicesType scanEventIndices, out string errorMessage)
		{
			scanEventIndices = new ScanEventIndicesType();
			errorMessage = string.Empty;

			var dctScanEventNames = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

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
		/// <param name="lstPrecursorInfo"></param>
		/// <param name="datasetID">Id number is a string because thats what sql gives me and there
		/// is no point in switching it back and forth</param>
		/// <param name="filepath"></param>
		private void PrintInterference(List<PrecursorIntense> lstPrecursorInfo, string datasetID, string filepath)
		{
            bool fieldExistance = File.Exists(filepath);
            using (var sw = new StreamWriter(filepath, fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\tPreIntensity\tIonCollectionTime\n");
                }

				foreach (PrecursorIntense info in lstPrecursorInfo)
                {
                    sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
                        info.dIsoloationMass + "\t" + info.nChargeState + "\t" +
                        info.isolationwidth + "\t" + info.interference + "\t" +
                        info.dPrecursorIntensity + "\t" + info.ionCollectionTime + "\n");
                }
            }
		}


		private void Interference(ref PrecursorIntense precursorInfo, ref XRawFileIO raw)
		{
			double[,] spectraData2D;

			raw.GetScanData2D(precursorInfo.preScanNumber, out spectraData2D, intMaxNumberOfPeaks: 0);

            double mzToFindLow = precursorInfo.dIsoloationMass - (precursorInfo.isolationwidth);
            double mzToFindHigh = precursorInfo.dIsoloationMass + (precursorInfo.isolationwidth);

            int a = 0;
			int b = spectraData2D.GetUpperBound(1) + 1;
            int c = 0;

			int lowInd = BinarySearch(ref spectraData2D, a, b, c, mzToFindLow);
			int highInd = BinarySearch(ref spectraData2D, lowInd, b, c, mzToFindHigh);
            
            //capture all peaks in isowidth+buffer
			List<Peak> peaks = ConvertToPeaks(ref spectraData2D, lowInd, highInd);

			double mzWindowLow = precursorInfo.dIsoloationMass - (precursorInfo.isolationwidth / 2);
			double mzWindowHigh = precursorInfo.dIsoloationMass + (precursorInfo.isolationwidth / 2);

            // Narrow the range of peaks to the final tolerances
			peaks = FilterPeaksByMZ(mzWindowLow, mzWindowHigh, peaks);

            //find target peak for use as precursor to find interference
            ClosestToTarget(precursorInfo, peaks);

            //perform the calculation
			InterferenceCalculation(precursorInfo, peaks);
		}


        /// <summary>
        /// Calculates interference with the precursor ion
        /// </summary>
		/// <param name="precursorInfo"></param>
        /// <param name="lowind"></param>
        /// <param name="highind"></param>
        /// <param name="peaks"></param>
		private void InterferenceCalculation(PrecursorIntense precursorInfo, List<Peak> peaks)
        {
            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            const double PreErrorAllowed = 10.0;
            double MaxPreInt = 0;
            double MaxInterfereInt = 0;            
            double OverallInterference = 0;

			if (peaks.Count > 0)
            {
                for (int j = 0; j < peaks.Count; j++)
                {
					double difference = (peaks[j].mz - precursorInfo.dActualMass) * precursorInfo.nChargeState;
                    double difference_Rounded = Math.Round(difference);
                    double expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
                    double Difference_ppm = Math.Abs((expected_difference - difference) /
						(precursorInfo.dIsoloationMass * precursorInfo.nChargeState)) * 1000000;

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
				Console.WriteLine("Did not find the precursor for " + precursorInfo.dIsoloationMass + " in scan " + precursorInfo.nScanNumber);
            }

			precursorInfo.interference = OverallInterference;
        }


		private List<Peak> ConvertToPeaks(ref double[,] spectraData2D, int lowind, int highind)
		{

			var mzs = new List<Peak>();

			for (int i = lowind + 1; i <= highind - 1; i++)
			{
				if (spectraData2D[1,i] > 0)
				{
					int j = i;
					double abusum = 0.0;
					while (spectraData2D[1,i] > 0 && !(spectraData2D[1,i - 1] > spectraData2D[1,i] && spectraData2D[1,i + 1] > spectraData2D[1,i]))
					{
						abusum += spectraData2D[1,i];
						i++;
					}
					int end = i;
					i = j;
					double peaksum = 0.0;
					double peakmax = 0.0;
					while (i != end)
					{
						//test using maximum of peak
						if (spectraData2D[1,i] > peakmax)
						{
							peakmax = spectraData2D[1,i];
						}

						peaksum += spectraData2D[1, i] / abusum * spectraData2D[0,i];
						i++;
					}
					var centroidPeak = new Peak
					{
						mz = peaksum,
						abundance = peakmax
					};
					mzs.Add(centroidPeak);

				}
			}

			return mzs;

		}

		private int BinarySearch(ref double[,] spectraData2D, int a, int b, int c, double mzToFind)
        {
            const double tol = 0.1;

            while (true)
            {
				if (Math.Abs(spectraData2D[0,c] - mzToFind) < tol || c == (b + a) / 2)
                {
                    break;
                }
                c = (b + a) / 2;
				if (spectraData2D[0,c] < mzToFind)
                {
                    a = c;
                }
				if (spectraData2D[0,c] > mzToFind)
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
		/// <param name="precursorInfo"></param>
        /// <param name="peaks"></param>
		private void ClosestToTarget(PrecursorIntense precursorInfo, IEnumerable<Peak> peaks)
        {
            double closest = 100000.0;
            foreach (Peak p in peaks)
            {
				double temp = Math.Abs(p.mz - precursorInfo.dIsoloationMass);
                if (temp < closest)
                {
					precursorInfo.dActualMass = p.mz;
                    closest = temp;
					precursorInfo.dPrecursorIntensity = p.abundance;
                }
            }
        }

        /// <summary>
        /// Filters the peak list to only retain peaks between lowMz and highMz
        /// </summary>
		/// <param name="lowMz"></param>
		/// <param name="highMz"></param>
        /// <param name="peaks"></param>
		private List<Peak> FilterPeaksByMZ(double lowMz, double highMz, IEnumerable<Peak> peaks)
        {
            var peaksFiltered = from peak in peaks
							where peak.mz < highMz
							&& peak.mz > lowMz
							orderby peak.mz
                            select peak;

			return peaksFiltered.ToList();

        }

	}
}
