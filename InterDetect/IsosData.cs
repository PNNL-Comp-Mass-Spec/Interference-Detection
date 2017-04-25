
namespace InterDetect
{
    public class IsosData
    {
        public double Abundance {get; private set; }
        public double Mz {get; private set; }
        public int ScanNum {get; private set; }
        public int Charge {get; private set; }

        public IsosData(int scan, double mz, double abundance, int charge)
        {
            ScanNum = scan;
            Mz = mz;
            Charge = charge;
            Abundance = abundance;
        }

        public override string ToString()
        {
            return string.Format("Charge {0}, {1:F4} m/z, intensity {2:F0}", Charge, Mz, Abundance);
        }
    }
}
