using System;
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
		};

		public event ProgressChangedHandler ProgressChanged;
		public delegate void ProgressChangedHandler(InterferenceDetector id, ProgressInfo e);

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

				List<PrecursorInfoTest> myInfo = ParentInfoPass2(fileCountCurrent, filepaths.Count, filepaths[datasetID], isosPaths[datasetID]);
				if (myInfo == null)
				{
					DeleteFile(tempPrecFilePath);
					throw new Exception("Error in PerformWork: ParentInfoPass2 returned null loading " + filepaths[datasetID]);
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
		public void TestParentInfoPass1()
		{
			//    GetParentInfo parentInfo = new GetParentInfoSequest(@"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\Seq201111091803_Auto765070\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28_fht.txt");
			List<PrecursorInfo> myInfo = ParentInfoPass(@"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09.raw",
				@"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\DLS201201111344_Auto783501\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09_isos.csv");

			//         PrintInterference(myInfo, @"C:\Users\aldr699\Documents\2012\InterferenceTesting\here2.txt");


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

				List<PrecursorInfoTest> myInfo = ParentInfoPass2(i + 1, filesToProcess, rawfiles[i], decon[i]);
				if (myInfo == null)
				{
					Console.WriteLine(rawfiles[i] + " failed to load.  Deleting temp and aborting!");
					return;
				}
				PrintInterference2(myInfo, "number", @"C:\Users\aldr699\Documents\2012\iTRAQ\InterferenceTesting\DataInt" + i + "efz50.txt");
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
		/// <param name="rawfile"></param>
		/// <param name="isosfile"></param>
		/// <returns>Provides a precursor info list</returns>
		public List<PrecursorInfo> ParentInfoPass(string rawfile, string isosfile)
		{
			bool worked;
			XRawFileIO myRaw = new XRawFileIO();

			worked = myRaw.OpenRawFile(rawfile);
			if (!worked)
			{
				Console.WriteLine("File failed to open .Raw file in ParentInfoPass: " + rawfile);
				return null;
			}


			IsosHandler isos = new IsosHandler(isosfile);


			List<PrecursorInfo> preInfo = new List<PrecursorInfo>();
			int numSpectra = myRaw.GetNumScans();
			//TODO: Add error code for 0 spectra
			int currPrecScan = 0;
			//Go into each scan and collect precursor info.
			double sr = 0.0;
			for (int i = 1; i <= numSpectra; i++)
			{
				if (i / (double)numSpectra > sr)
				{
					if (sr > 0)
						Console.WriteLine("  " + sr * 100.0 + "% complete");

					sr += .10;
				}

				int msorder = 2;
				if (isos.IsParentScan(i))
					msorder = 1;

				FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo = new FinniganFileReaderBaseClass.udtScanHeaderInfoType();


				myRaw.GetScanInfo(i, out scanInfo);
				//      double premass = 0;
				//      premass = scanInfo.ParentIonMZ;
				//      XRawFileIO.GetScanTypeNameFromFinniganScanFilterText(scanInfo.FilterText, ref premass);

				if (msorder > 1)
				{
					int chargeState = Convert.ToInt32(scanInfo.ScanEventValues[11]);
					double mz;
					if (scanInfo.ParentIonMZ == 0.0)
					{
						mz = Convert.ToDouble(scanInfo.ScanEventValues[12]);
					}
					else
					{
						mz = scanInfo.ParentIonMZ;
					}
					// if (mz == 0)
					//{
					//    mz = premass;
					//}
					double isolationWidth = Convert.ToDouble(scanInfo.ScanEventValues[13]);
					if (chargeState == 0)
					{
						if (!isos.GetChargeState(currPrecScan, mz, ref chargeState))
						{
							continue;
						}
					}

					PrecursorInfo info = new PrecursorInfo();
					info.dIsoloationMass = mz;
					info.nScanNumber = i;
					info.preScanNumber = currPrecScan;
					info.nChargeState = chargeState;
					info.isolationwidth = isolationWidth;
					Interference(ref info, ref myRaw, ref scanInfo);
					//       Interference(ref info, isos);
					preInfo.Add(info);


				}
				else if (msorder == 1)
				{
					currPrecScan = i;
				}

			}
			myRaw.CloseRawFile();
			return preInfo;
		}


		public List<PrecursorInfoTest> ParentInfoPass2(int fileCountCurrent, int fileCountTotal, string rawfile, string isosfile)
		{
			bool worked;
			XRawFileIO myRaw = new XRawFileIO();

			worked = myRaw.OpenRawFile(rawfile);
			if (!worked)
			{
				throw new Exception("File failed to open .Raw file in ParentInfoPass2: " + rawfile);
			}


			IsosHandler isos = new IsosHandler(isosfile);

			List<PrecursorInfoTest> preInfo = new List<PrecursorInfoTest>();
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
				//      double premass = 0;
				//      premass = scanInfo.ParentIonMZ;
				//      XRawFileIO.GetScanTypeNameFromFinniganScanFilterText(scanInfo.FilterText, ref premass);

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
					// if (mz == 0)
					//{
					//    mz = premass;
					//}
					double isolationWidth = Convert.ToDouble(scanInfo.ScanEventValues[scanEventIndices.isolationWidth]);
					if (chargeState == 0)
					{
						if (!isos.GetChargeState(currPrecScan, mz, ref chargeState))
						{
							// Unable to determine the charge state; skip this scan							
							continue;
						}
					}

					PrecursorInfoTest info = new PrecursorInfoTest();
					info.dIsoloationMass = mz;
					info.nScanNumber = scanNumber;
					info.preScanNumber = currPrecScan;
					info.nChargeState = chargeState;
					info.isolationwidth = isolationWidth;
					Interference2(ref info, ref myRaw, ref scanInfo);
					//       Interference(ref info, isos);
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
		private void PrintInterference(List<PrecursorInfo> preinfo, string datasetID, string filepath)
		{
			bool fieldExistance = File.Exists(filepath);
			using (StreamWriter sw = new StreamWriter(filepath, fieldExistance))
			{
				if (!fieldExistance)
				{
					sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\n");
				}

				foreach (PrecursorInfo info in preinfo)
				{
					sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
						info.dIsoloationMass + "\t" + info.nChargeState + "\t" +
						info.isolationwidth + "\t" + info.interference + "\n");
				}
			}
		}

		private void PrintInterference(List<PrecursorInfoTest> preinfo, string datasetID, string filepath)
		{
			bool writeHeaderLine = !File.Exists(filepath);
			using (StreamWriter sw = new StreamWriter(new System.IO.FileStream(filepath,FileMode.Append, FileAccess.Write, FileShare.Read)))
			{
				if (writeHeaderLine)
				{
					sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\n");
				}

				foreach (PrecursorInfo info in preinfo)
				{
					sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
						info.dIsoloationMass + "\t" + info.nChargeState + "\t" +
						info.isolationwidth + "\t" + info.interference + "\n");
				}
			}
		}


		private void PrintInterference2(List<PrecursorInfoTest> preinfo, string datasetID, string filepath)
		{
			bool fieldExistance = File.Exists(filepath);
			using (StreamWriter sw = new StreamWriter(filepath, fieldExistance))
			{
				if (!fieldExistance)
				{
					sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tActualMZ\tChargeState\tIsoWidth\tInterference\n");
				}

				foreach (PrecursorInfoTest info in preinfo)
				{
					sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
						info.dIsoloationMass + "\t" + info.dActualMass + "\t" + info.nChargeState + "\t" +
						info.isolationwidth + "\t" + info.interference + "\n");
				}
			}
		}
		/// <summary>
		/// Finds the interference of the target isotopic profile
		/// </summary>
		/// <param name="preInfo"></param>
		/// <param name="isos"></param>
		private void Interference(ref PrecursorInfo preInfo, IsosHandler isos)
		{

			const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
			double PreErrorAllowed = 10.0;
			double low = preInfo.dIsoloationMass - (preInfo.isolationwidth / 2.0);
			double high = preInfo.dIsoloationMass + (preInfo.isolationwidth / 2.0);

			double[,] peaks = isos.GetPeakList2(preInfo.preScanNumber, low, high);

			double MaxPreInt = 0;
			double MaxInterfereInt = 0;
			//  int p = peaks.GetUpperBound(1);
			double OverallInterference = 0;
			if (peaks != null)
			{
				for (int j = peaks.GetLowerBound(1); j <= peaks.GetUpperBound(1); j++)
				{
					double difference = (peaks[0, j] - preInfo.dIsoloationMass) * preInfo.nChargeState;
					double difference_Rounded = Math.Round(difference);
					double expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
					double Difference_ppm = Math.Abs((expected_difference - difference) /
						(preInfo.dIsoloationMass * preInfo.nChargeState)) * 1000000;

					if (Difference_ppm < PreErrorAllowed)
					{
						MaxPreInt += peaks[1, j];
					}

					MaxInterfereInt += peaks[1, j];

				}
				OverallInterference = MaxPreInt / MaxInterfereInt;
			}
			preInfo.interference = OverallInterference;
			//DatasetPrep.Utilities.WriteDataTableToText(dt, fhtFile.Substring(0, fhtFile.Length - 4) + "_int.txt");
		}

		/// <summary>
		/// Finds the interference of the target isotopic profile
		/// </summary>
		/// <param name="preInfo"></param>
		/// <param name="isos"></param>
		private void Interference(ref PrecursorInfo preInfo, ref XRawFileIO raw, ref FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo)
		{
			double[] mzlist = null;
			double[] abulist = null;




			raw.GetScanData(preInfo.preScanNumber, ref mzlist, ref abulist, ref scanInfo);



			const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
			double PreErrorAllowed = 10.0;
			double lowt = preInfo.dIsoloationMass - (preInfo.isolationwidth);
			double hight = preInfo.dIsoloationMass + (preInfo.isolationwidth);
			double low = preInfo.dIsoloationMass - (preInfo.isolationwidth / 2);
			double high = preInfo.dIsoloationMass + (preInfo.isolationwidth / 2);
			bool lowbool = true;
			int lowind = -1;
			int highind = -1;
			for (int i = 0; i < mzlist.Length; i++)
			{
				if (lowbool && mzlist[i] > lowt && lowbool)
				{
					lowbool = false;
					lowind = i;
				}
				if (mzlist[i] > hight)
				{
					highind = i - 1;
					break;
				}
			}

			List<Peak> peaks = ConvertToPeaks(ref mzlist, ref abulist, lowind, highind);
			for (int l = 0; l < peaks.Count; l++)
			{
				if (peaks[l].mz < low || peaks[l].mz > high)
				{
					peaks.RemoveAt(l);
					l--;
				}
			}

			double MaxPreInt = 0;
			double MaxInterfereInt = 0;
			//  int p = peaks.GetUpperBound(1);
			double OverallInterference = 0;
			if (lowind != -1 && highind != -1)
			{
				for (int j = 0; j < peaks.Count; j++)
				{
					double difference = (peaks[j].mz - preInfo.dIsoloationMass) * preInfo.nChargeState;
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
			//DatasetPrep.Utilities.WriteDataTableToText(dt, fhtFile.Substring(0, fhtFile.Length - 4) + "_int.txt");
		}

		private void Interference2(ref PrecursorInfoTest preInfo, ref XRawFileIO raw, ref FinniganFileReaderBaseClass.udtScanHeaderInfoType scanInfo)
		{
			double[] mzlist = null;
			double[] abulist = null;




			raw.GetScanData(preInfo.preScanNumber, ref mzlist, ref abulist, ref scanInfo);



			const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
			double PreErrorAllowed = 10.0;
			double lowt = preInfo.dIsoloationMass - (preInfo.isolationwidth);
			double hight = preInfo.dIsoloationMass + (preInfo.isolationwidth);
			double low = preInfo.dIsoloationMass - (preInfo.isolationwidth / 2);
			double high = preInfo.dIsoloationMass + (preInfo.isolationwidth / 2);
			bool lowbool = true;
			int lowind = -1;
			int highind = -1;
			for (int i = 0; i < mzlist.Length; i++)
			{
				if (lowbool && mzlist[i] > lowt && lowbool)
				{
					lowbool = false;
					lowind = i;
				}
				if (mzlist[i] > hight)
				{
					highind = i - 1;
					break;
				}
			}

			List<Peak> peaks = ConvertToPeaks(ref mzlist, ref abulist, lowind, highind);

			for (int l = 0; l < peaks.Count; l++)
			{
				if (peaks[l].mz < low || peaks[l].mz > high)
				{
					peaks.RemoveAt(l);
					l--;
				}
			}

			double closest = 1000.0;
			foreach (Peak p in peaks)
			{
				double temp = Math.Abs(p.mz - preInfo.dIsoloationMass);
				if (temp < closest)
				{
					preInfo.dActualMass = p.mz;
					closest = temp;
				}
			}

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
			//DatasetPrep.Utilities.WriteDataTableToText(dt, fhtFile.Substring(0, fhtFile.Length - 4) + "_int.txt");
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



	}
}
