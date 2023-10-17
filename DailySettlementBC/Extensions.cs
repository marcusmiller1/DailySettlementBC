using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DailySettlementBC
{
    public static class Extensions
    {
        public static decimal FromImpliedDecimalString(this string value)
        {
            int x = 0;

            if (Int32.TryParse(value, out x))
            {
                if (x > 0)
                    return x / 100.00M;
            }

            return 0;
        }
        public static string CleanAndTrim(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\"", "").Trim();
        }
    }
}
