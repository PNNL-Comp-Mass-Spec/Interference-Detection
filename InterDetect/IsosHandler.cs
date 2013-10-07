using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace InterDetect
{
    public class IsosHandler
    {

		Dictionary<int, List<IsosData>> mParentScans;


        public IsosHandler(string filepath)
        {	        
			var lstIsosData = TextFileToData(filepath);

            GetParentScanNumbers(lstIsosData);
        }

        /// <summary>
        /// Find out if a parent scan
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsParentScan(int i)
        {
            return mParentScans.ContainsKey(i);
        }



        /// <summary>
        /// In some cases the raw file fails to provide a charge state, if that is the case
        /// we check the isos file to see if decon2ls could figure it out.
        /// </summary>
        /// <param name="scan"></param>
        /// <param name="mz"></param>
        /// <param name="charge"></param>
        /// <returns></returns>
        public bool GetChargeState(int scan, double mz, ref int charge)
        {
            if (!IsParentScan(scan))
            {
                return false;
            }

			var isosMatch = (from parent in mParentScans[scan].AsEnumerable()
                             where parent.Mz > mz - 0.005 &&
                                   parent.Mz < mz + 0.005
                             select parent).ToList();


			if (isosMatch.Count > 0)
            {
				charge = isosMatch.First().Charge;
            }

            return (charge != 0);

        }

		// Unused function
		//
		///// <summary>
		///// Get a peak list for this range and scan number.
		///// </summary>
		///// <param name="scan">scannumber</param>
		///// <param name="low">low mz value</param>
		///// <param name="high">high mz value</param>
		///// <returns></returns>
		//public double[,] GetPeakList(int scan, double low, double high)
		//{

		//    //change this to questioning a SortedDictionary<int, List<int>>();
		//    DataRow[] foundRows = mData.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
		//        "' AND [mz] < '" + high + "'");

		//    int count = foundRows.Length;

		//    if (count > 0)
		//    {
		//        var data = new double[2, count];
		//        for (int i = 0; i < count; i++)
		//        {
		//            data[0, i] = (double)foundRows[i]["mz"];
		//            data[1, i] = (double)foundRows[i]["abundance"];
		//        }
		//        return data;

		//    }
		//    return null;
		//}

		// Unused function
		//
		///// <summary>
		///// More efficient version of the getpeaklist.  
		///// Get a peak list for this range and scan number.
		///// </summary>
		///// <param name="scan">scannumber</param>
		///// <param name="low">low mz value</param>
		///// <param name="high">high mz value</param>
		///// <returns></returns>
		//public double[,] GetPeakList2(int scan, double low, double high)
		//{

		//    //change this to questioning a SortedDictionary<int, List<int>>();
		//    //DataRow[] foundRows = dt.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
		//    //    "' AND [mz] < '" + high + "'");

		//    DataRow[] foundRows = (from parent in parentScans[scan].AsEnumerable()
		//                                  where parent.Field<double>("mz") > low &&
		//                                  parent.Field<double>("mz") < high
		//                                  select parent).ToArray();

		//    int count = foundRows.Length;

		//    if (count > 0)
		//    {
		//        var data = new double[2, count];
		//        for (int i = 0; i < count; i++)
		//        {
		//            data[0, i] = (double)foundRows[i]["mz"];
		//            data[1, i] = (double)foundRows[i]["abundance"];
		//        }
		//        return data;

		//    }
		//    return null;
		//}

        /// <summary>
        /// Generates a list of parent scan numbers
        /// </summary>
        private void GetParentScanNumbers(IEnumerable<IsosData> lstIsosData)
        {
			mParentScans = new Dictionary<int, List<IsosData>>();   
            foreach (var isosEntry in lstIsosData)
            {
				if (!mParentScans.ContainsKey(isosEntry.ScanNum))
                {
					mParentScans.Add(isosEntry.ScanNum, new List<IsosData> { isosEntry });
                }
                else
                {
					mParentScans[isosEntry.ScanNum].Add(isosEntry);
                }
            }
        }

		// Unused function
		//
		///// <summary>
		///// Writes a datatable to text file
		///// </summary>
		///// <param name="dt"></param>
		///// <param name="filePath"></param>
		//public static void WriteDataTableToText(DataTable dt, string filePath)
		//{
		//    using (var sw = new StreamWriter(filePath))
		//    {
		//        string s = dt.Columns[0].ColumnName;
		//        for (int i = 1; i < dt.Columns.Count; i++)
		//        {
		//            s += "\t" + dt.Columns[i].ColumnName;
		//        }
		//        sw.WriteLine(s);

		//        foreach (DataRow row in dt.Rows)
		//        {
		//            s = "" + row[0];
		//            for (int i = 1; i < dt.Columns.Count; i++)
		//            {
		//                s += "\t" + row[i];
		//            }
		//            sw.WriteLine(s);
		//        }


		//    }
		//}

		/// <summary>
		/// Converts a text file to a datatable
		/// </summary>
		/// <param name="fileName"></param>
		/// <returns></returns>
		protected List<IsosData> TextFileToData(string fileName)
		{
			try
			{
				var splitChars = new[] { '\t', ',' };

				var lstIsosData = new List<IsosData>();

				using (var sr = new StreamReader(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read)))
				{
					// first line has headers   
					var myFields = new List<int>();
					string line;
					int abundanceColIndex = -1;
					int mzColIndex = -1;
					int scanColIndex = -1;
					int chargeColndex = -1;

					if ((line = sr.ReadLine()) != null)
					{
						var headerCols = line.Split(splitChars);

						for (int i = 0; i < headerCols.Length; i++)
						{
							switch (headerCols[i].ToLower())
							{
								case "abundance":
									abundanceColIndex=i; 
									break;
								case "mz":
									mzColIndex = i; 
									break;
								case "scan_num":
									scanColIndex = i; 
									break;
								case "charge":
									chargeColndex = i; 
									break;
							}

						}

					}
					else
					{
						// it's empty, that's an error   
						throw new ApplicationException("The data provided is not in a valid format.");
					}

					if (abundanceColIndex < 0) throw new ApplicationException("Isos files does not have column: abundance");
					if (mzColIndex < 0) throw new ApplicationException("Isos files does not have column: mz");
					if (scanColIndex < 0) throw new ApplicationException("Isos files does not have column: scan_num");
					if (chargeColndex < 0) throw new ApplicationException("Isos files does not have column: charge");

					// fill the rest of the table
					while ((line = sr.ReadLine()) != null)
					{

						var dataValues = line.Split(splitChars);
						var isosEntry = new IsosData
						{
							Abundance = GetValueDbl(dataValues, abundanceColIndex, 0),
							Mz = GetValueDbl(dataValues, mzColIndex, 0),
							ScanNum = GetValueInt(dataValues, scanColIndex, 0),
							Charge = GetValueInt(dataValues, chargeColndex, 0)
						};

						lstIsosData.Add(isosEntry);
					}
				}
				return lstIsosData;
			}
			catch (Exception ex)
			{
				Console.WriteLine("The file failed to load" + ex.Message);
				return null;
			}
		}

		protected double GetValueDbl(string[] dataValues, int colIndex, double valueIfMissing)
	    {
		    double value;
			if (double.TryParse(dataValues[colIndex], out value))
				return value;
			
			return valueIfMissing;
	    }

		protected int GetValueInt(string[] dataValues, int colIndex, int valueIfMissing)
	    {
		    int value;
			if (int.TryParse(dataValues[colIndex], out value))
				return value;
			
			return valueIfMissing;
	    }
    }
}
