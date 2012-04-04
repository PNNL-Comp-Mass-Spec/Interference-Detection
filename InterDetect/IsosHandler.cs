using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Data;


namespace InterDetect
{
    public class IsosHandler
    {

        static protected int count;
        private DataTable dt;
        SortedDictionary<int, List<DataRow>> parentScans;


        public IsosHandler(string filepath)
        {
            dt = TextFileToData(filepath);
            GetParentScanNumbers();
        }

        /// <summary>
        /// Find out if a parent scan
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsParentScan(int i)
        {
            return parentScans.ContainsKey(i);
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
            DataRow[] foundRows = (from parent in parentScans[scan].AsEnumerable()
                                   where parent.Field<double>("mz") > mz - 0.005 &&
                                   parent.Field<double>("mz") < mz + 0.005
                                   select parent).ToArray();
            
        
            if (foundRows.Length == 1)
            {
                charge = (int)foundRows[0]["charge"];
            }
            return (charge != 0);

        }

        /// <summary>
        /// Get a peak list for this range and scan number.
        /// </summary>
        /// <param name="scan">scannumber</param>
        /// <param name="low">low mz value</param>
        /// <param name="high">high mz value</param>
        /// <returns></returns>
        public double[,] GetPeakList(int scan, double low, double high)
        {

            //change this to questioning a SortedDictionary<int, List<int>>();
            DataRow[] foundRows = dt.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
                "' AND [mz] < '" + high + "'");
            double[,] data = null;

            int count = foundRows.Length;

            if (count > 0)
            {
                data = new double[2, count];
                for (int i = 0; i < count; i++)
                {
                    data[0, i] = (double)foundRows[i]["mz"];
                    data[1, i] = (double)foundRows[i]["abundance"];
                }
                return data;

            }
            return null;
        }

        /// <summary>
        /// More efficient version of the getpeaklist.  
        /// Get a peak list for this range and scan number.
        /// </summary>
        /// <param name="scan">scannumber</param>
        /// <param name="low">low mz value</param>
        /// <param name="high">high mz value</param>
        /// <returns></returns>
        public double[,] GetPeakList2(int scan, double low, double high)
        {

            //change this to questioning a SortedDictionary<int, List<int>>();
            //DataRow[] foundRows = dt.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
            //    "' AND [mz] < '" + high + "'");

            DataRow[] foundRows = (from parent in parentScans[scan].AsEnumerable()
                                          where parent.Field<double>("mz") > low &&
                                          parent.Field<double>("mz") < high
                                          select parent).ToArray();

            double[,] data = null;
            int count = foundRows.Length;

            if (count > 0)
            {
                data = new double[2, count];
                for (int i = 0; i < count; i++)
                {
                    data[0, i] = (double)foundRows[i]["mz"];
                    data[1, i] = (double)foundRows[i]["abundance"];
                }
                return data;

            }
            return null;
        }

        /// <summary>
        /// Generates a list of parent scan numbers
        /// </summary>
        private void GetParentScanNumbers()
        {
            parentScans = new SortedDictionary<int, List<DataRow>>();   
            foreach (DataRow row in dt.Rows)
            {
                int scan = (int)row["scan_num"];
                if (!parentScans.ContainsKey(scan))
                {
                    parentScans.Add(scan, new List<DataRow>() { row });
                }
                else
                {
                    parentScans[scan].Add(row);
                }
            }
        }
        

        /// <summary>
        /// Writes a datatable to text file
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="filePath"></param>
        public static void WriteDataTableToText(DataTable dt, string filePath)
        {
            using (StreamWriter sw = new StreamWriter(filePath))
            {
                string s = dt.Columns[0].ColumnName;
                for (int i = 1; i < dt.Columns.Count; i++)
                {
                    s += "\t" + dt.Columns[i].ColumnName;
                }
                sw.WriteLine(s);

                s = string.Empty;
                foreach (DataRow row in dt.Rows)
                {
                    s = "" + row[0];
                    for (int i = 1; i < dt.Columns.Count; i++)
                    {
                        s += "\t" + row[i];
                    }
                    sw.WriteLine(s);
                    s = string.Empty;
                }


            }
        }

        /// <summary>
        /// Converts a text file to a datatable
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static DataTable TextFileToData(string fileName)
        {
            string line = "";
            string[] fields = null;
            DataTable dt = new DataTable();
            try
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(fileName))
                {
                    // first line has headers   
                    List<int> myFields = new List<int>();
                    if ((line = sr.ReadLine()) != null)
                    {
                        fields = line.Split(new char[] { '\t', ',' });

                        int mycount = 0;
                        foreach (string s in fields)
                        {
                            if (s == "abundance" || s == "mz")
                            {
                                dt.Columns.Add(s, typeof(double));
                                dt.Columns[s].DefaultValue = 0;
                                myFields.Add(mycount);

                            }
                            else if(s == "scan_num" || s == "charge")
                            {
                                dt.Columns.Add(s, typeof(int));
                                dt.Columns[s].DefaultValue = 0;
                                myFields.Add(mycount);
                            }

                            mycount++;
                            
                        }

                    }
                    else
                    {
                        // it's empty, that's an error   
                        throw new ApplicationException("The data provided is not in a valid format.");
                    }
                    // fill the rest of the table; positional   
                    while ((line = sr.ReadLine()) != null)
                    {
                        DataRow row = dt.NewRow();

                        fields = line.Split(new char[] { '\t', ',' });
                        int i = 0;
                        foreach (int s in myFields)
                        {
                            row[i] = fields[s];
                            i++;
                        }


                        dt.Rows.Add(row);
                    }
                }
                return dt;
            }
            catch (Exception ex)
            {
                Console.WriteLine("The file failed to load" + ex.Message);
                return null;
            }
        }
    }
}
