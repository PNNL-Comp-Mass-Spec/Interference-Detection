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


namespace InterDetect
{

    public struct Peak{
        public double mz;
        public double abundance;
    };

	public class InterferenceDetector
	{

        private const string tempfile = "prec_info_temp.txt";
        private const string raw_ext = ".raw";
        private const string isos_ext = "_isos.csv";

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
            // build Mage pipeline to read contents of 
            // a table in a SQLite database into a data buffer

            // first, make the Mage SQLite reader module
            // and configure it to read the table
            SQLiteReader reader = new SQLiteReader();
            reader.Database = Path.Combine(datapath,"Results.db3");


            reader.SQLText = "SELECT * FROM t_msms_raw_files;";

            // next, make a Mage sink module (simple row buffer)
            SimpleSink sink = new SimpleSink();

            // construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline", reader, sink).RunRoot(null);


            // example of reading the rows in the buffer object
            // (dump folder column values to Console)
            int folderPathIdx = sink.ColumnIndex["Folder"];
            int datasetIDIdx = sink.ColumnIndex["Dataset_ID"];
            int datasetIdx = sink.ColumnIndex["Dataset"];
            Dictionary<string, string> filepaths = new Dictionary<string, string>();
            foreach (object[] row in sink.Rows)
            {
                filepaths.Add(row[datasetIDIdx].ToString(), Path.Combine(row[folderPathIdx].ToString(), row[datasetIdx].ToString() + raw_ext));
            }
            if (filepaths.Count == 0)
            {
                return false;
            }
            //Restart the buffer
            sink = new SimpleSink();

            //Add rows from other table
            reader.SQLText = "SELECT * FROM t_results_metadata WHERE t_results_metadata.Tool Like 'Decon%'";

            // construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);
           
            int datasetID = sink.ColumnIndex["Dataset_ID"];
            int dataset = sink.ColumnIndex["Dataset"];
            int folder = sink.ColumnIndex["Folder"];
            Dictionary<string, string> isosPaths = new Dictionary<string, string>();
            //store the filepaths indexed by datasetID
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
            if (isosPaths.Count != filepaths.Count)
            {
                return false;
            }

            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            string tempPrecFile = Path.Combine(datapath, "prec_info_temp.txt");
            foreach (string files in filepaths.Keys)
            {
                if (!isosPaths.ContainsKey(files))
                {
                    if (File.Exists(tempPrecFile))
                    {
                        File.Delete(tempPrecFile);
                        return false;
                    }
                }
                List<PrecursorInfo> myInfo = ParentInfoPass(filepaths[files], isosPaths[files]);
                if (myInfo == null)
                {
                    Console.WriteLine(filepaths[files] + " failed to load.  Deleting temp and aborting!");
                    if (File.Exists(tempPrecFile))
                    {
                        File.Delete(tempPrecFile);
                    }
                    return false;
                }
                PrintInterference(myInfo, files, tempPrecFile);
                Console.WriteLine("Iteration Complete");
            }

            //Create a delimeted file reader and write a new table with this info to database
            DelimitedFileReader delimreader = new DelimitedFileReader();
            delimreader.FilePath = Path.Combine(datapath, tempfile);

            SQLiteWriter writer = new SQLiteWriter();
            string tableName = "t_precursor_interference";
            writer.DbPath = Path.Combine(datapath, "Results.db3");
            writer.TableName = tableName;

            ProcessingPipeline.Assemble("ImportToSQLite", delimreader, writer).RunRoot(null);


            //cleanup
            File.Delete(Path.Combine(datapath, tempfile));

            return true;

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

            for (int i = 0; i < 3; i++)
            {
                
                List<PrecursorInfo> myInfo = ParentInfoPass(rawfiles[i], decon[i]);
                if (myInfo == null)
                {
                    Console.WriteLine(rawfiles[i] + " failed to load.  Deleting temp and aborting!");
                    return;
                }
                PrintInterference(myInfo, "number", @"C:\Users\aldr699\Documents\2012\iTRAQ\Sisi_Olearia\DataInt" + i + "b.txt");
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
		public static List<PrecursorInfo> ParentInfoPass(string rawfile, string isosfile)
		{
            bool worked;
            ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO myRaw = new ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO();

            worked = myRaw.OpenRawFile(rawfile);
            if (!worked)
            {
                Console.WriteLine("File failed to open");
                return null; ;
            }
            
            if(myRaw.CheckFunctionality())
            {
                string rr= "tell m e";
            }
            IsosHandler isos = new IsosHandler(isosfile);
            //open raw file and set controller

            List<PrecursorInfo> preInfo = new List<PrecursorInfo>();
            int numSpectra = myRaw.GetNumScans();
			//TODO: Add error code for 0 spectra
			int currPrecScan = 0;
            //Go into each scan and collect precursor info.
            double sr = 0.0;
            int tt = 0;
			for (int i = 1; i <= numSpectra; i++)
			{
                if (i / (double)numSpectra > sr)
                {
                    Console.WriteLine(sr * 100.0 + "% completed");
                    sr += .10;
                }
                
                int msorder = 2;
                if (isos.IsParentScan(i))
                    msorder = 1;

                if (i == 16716)
                {
                    string p = "stop";
                    p = "please" + p;
                }

                ThermoRawFileReaderDLL.FinniganFileIO.FinniganFileReaderBaseClass.udtScanHeaderInfoType ude = 
                    new ThermoRawFileReaderDLL.FinniganFileIO.FinniganFileReaderBaseClass.udtScanHeaderInfoType();
                

                myRaw.GetScanInfo(i, ref ude);
          //      double premass = 0;
          //      premass = ude.ParentIonMZ;
          //      ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO.GetScanTypeNameFromFinniganScanFilterText(ude.FilterText, ref premass);
                
				if(msorder > 1)
				{
                    int chargeState = Convert.ToInt32(ude.ScanEventValues[11]);
                    double mz;
                    if (ude.ParentIonMZ == 0.0)
                    {
                        mz = Convert.ToDouble(ude.ScanEventValues[12]);
                    }
                    else
                    {
                        mz = ude.ParentIonMZ;
                    }
                   // if (mz == 0)
                    //{
                    //    mz = premass;
                    //}
                    double isolationWidth = Convert.ToDouble(ude.ScanEventValues[13]);
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
              //      Interference(ref info, ref myRaw, ref ude);
                    Interference(ref info, isos);
                    preInfo.Add(info);
                    
                    
				}
				else if(msorder == 1)
				{
					currPrecScan = i;
				}
				
			}
            myRaw.CloseRawFile();
            return preInfo;
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
            using (StreamWriter sw = new StreamWriter(filepath,fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\n");
                }

                foreach(PrecursorInfo info in preinfo)
                {
                    sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
                        info.dIsoloationMass + "\t" + info.nChargeState + "\t" + 
                        info.isolationwidth + "\t" + info.interference + "\n");
                }
            }
        }

        /// <summary>
        /// Finds the interference of the target isotopic profile
        /// </summary>
        /// <param name="preInfo"></param>
        /// <param name="isos"></param>
        private static void Interference(ref PrecursorInfo preInfo, IsosHandler isos)
        {

            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            double PreErrorAllowed = 10.0;
            double low = preInfo.dIsoloationMass - (preInfo.isolationwidth/2.0);
            double high = preInfo.dIsoloationMass + (preInfo.isolationwidth/2.0);
            
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
        private static void Interference(ref PrecursorInfo preInfo, ref ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO raw,
            ref ThermoRawFileReaderDLL.FinniganFileIO.FinniganFileReaderBaseClass.udtScanHeaderInfoType ude)
        {
            double[] mzlist = null;
            double[] abulist = null;
            



            raw.GetScanData(preInfo.preScanNumber, ref mzlist, ref abulist, ref ude);
            


            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            double PreErrorAllowed = 10.0;
            double lowt = preInfo.dIsoloationMass - (preInfo.isolationwidth);
            double hight = preInfo.dIsoloationMass + (preInfo.isolationwidth);
            double low = preInfo.dIsoloationMass - (preInfo.isolationwidth/2);
            double high = preInfo.dIsoloationMass + (preInfo.isolationwidth/2);
            bool lowbool = true;
            int lowind = -1;
            int highind= -1;
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
                for (int j = 0; j < peaks.Count; j++ )
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

        

        private static List<Peak> ConvertToPeaks(ref double[] mzlist, ref double[] abulist, int lowind, int highind)
        {
            
            List<Peak> mzs = new List<Peak>();

            for (int i = lowind; i <= highind; i++)
            {
                if (abulist[i] != 0)
                {
                    int j = i;
                    double abusum = 0.0;
                    while (abulist[i] != 0)
                    {
                        abusum += abulist[i];
                        i++;
                    }
                    i = j;
                    double peaksum = 0.0;
                    while (abulist[i] != 0)
                    {
                        peaksum += abulist[i] / abusum * mzlist[i];
                        i++;
                    }
                    Peak centroidPeak = new Peak();
                    centroidPeak.mz = peaksum;
                    centroidPeak.abundance = abusum;
                    mzs.Add(centroidPeak);

                }
            }

            return mzs;

        }



	}
}
