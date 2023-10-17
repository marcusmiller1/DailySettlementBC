using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DailySettlementBC
{
    class ReportItem
    {
        public string TransactionType { get; set; }
        public decimal TransactionAmount { get; set; }
        public string ResponseCode { get; set; }
        public DateTime TransactionTime { get; set; }
        public DateTime SettledDateTime { get; set; }
        public string MillersAccount { get; set; } = "";
        public string MerchantOrderId { get; set; }

        //internal removes it from .ToCSV()
        internal string OrderNumber { get; set; }

        internal decimal OrderAmount { get; set; }
        public string ReportGroup { get; set; }
        public string CardType { get; set; }
        public string CardNumber { get; set; }
        public Guid CustomerId { get; set; }

        public decimal TxnOrderDiff { get; set; }
        public string TxnEventCode { get; set; }
        public string ProcessorId { get; set; }
        public string Processor { get; set; }
        public string ProcessorStatus { get; set; }
        public int OrderIdProduction { get; set; }
        internal List<Order> OrderInfo { get; set; } = new();
        public bool FlaggedForReview { get; set; } = false;
        //internal List<Invoice> InvoiceInfo { get; set; } = new();

        public decimal OrderTotal { get; set; } = 0;
        //public decimal InvoiceTotal { get; set; } = 0;

        public bool Voided { get; set; } = false;

        public string OrderIds { get; set; } = "";
        //public List<OrderInfo> ProductionOrders { get; set; } = new List<OrderInfo>();
        public static ReportItem FromCsv(string line, string reportGroup)
        {
            var values = line.Split(new char[] { ',' });
            var ri = new ReportItem();
            ri.CardType = values[0];
            ri.CardNumber = values[1];
            ri.TransactionType = values[2];
            ri.TransactionAmount = decimal.Parse(values[3].Replace("$", ""));
            ri.ResponseCode = values[4];
            ri.TransactionTime = DateTime.Parse(values[5]);
            ri.SettledDateTime = DateTime.Parse(values[6]);
            ri.MerchantOrderId = values[7];
            //ri.OrderNumber = ordernumber;
            ri.ReportGroup = reportGroup;

            ri.TxnOrderDiff = 0;
            ri.Processor = "chase";
            ri.ProcessorStatus = "";
            return ri;
        }
        public static ReportItem FromPayPalCSV(string line, string reportGroup)
        {
            var ri = new ReportItem();
            var values = line.Split(new char[] { ',' });
            ri.CardType = "";
            ri.CardNumber = "";
            var respcode = values[5].CleanAndTrim();

            if (values[0].CleanAndTrim() == "SB")
            {

                if (respcode == "T0006")
                    ri.TransactionType = "Sale";
                else if (respcode == "T1107")
                    ri.TransactionType = "Credit";
                else if (respcode == "T1111" || respcode == "T1110" || respcode == "T1106")
                    ri.TransactionType = "ChargeBack";
                else
                    ri.TransactionType = "";

                ri.TransactionAmount = values[9].FromImpliedDecimalString(); // decimal.Parse(values[9].Replace("$", "")) / 100.00M;
                ri.ResponseCode = respcode; // values[5].CleanAndTrim();
                ri.TransactionTime = DateTime.Parse(values[6]);
                ri.SettledDateTime = DateTime.Parse(values[7]);
                ri.MerchantOrderId = values[2].CleanAndTrim();

                ri.ReportGroup = reportGroup;
                ri.TxnOrderDiff = 0;
                ri.TxnEventCode = respcode;
                ri.ProcessorId = values[1];
                ri.Processor = "paypal";
                ri.ProcessorStatus = "";

            }

            return ri;
        }
        public static ReportItem FromBraintreeCSV(string line)
        {
            
            var values = line.Split(new char[] { ',' });

            var ri = new ReportItem
            {
                TransactionType = values[0],
                TransactionAmount = decimal.Parse(values[1].Replace("$", "")),
                ResponseCode = values[2],
                TransactionTime = DateTime.Parse(values[3]),
                SettledDateTime = DateTime.Parse(values[4]),
                MerchantOrderId = values[6],
                ReportGroup = values[7],
                CardType = values[8],
                CardNumber = values[9],
                ProcessorId = values[13],
                Processor = values[14],
                ProcessorStatus = values[15],
                TxnOrderDiff = 0
            };

            return ri;
        }
        public static ReportItem FromPayPalTrans(Braintree.Transaction t)
        {
            var ri = new ReportItem
            {
                TransactionType = t.Type.ToString(),
                TransactionAmount = t.Amount.Value,
                ResponseCode = t.ProcessorResponseText,
                TransactionTime = TimeZoneInfo.ConvertTimeFromUtc(t.CreatedAt.Value, TimeZoneInfo.Local),
                SettledDateTime = t.CreatedAt.Value,
                MerchantOrderId = t.OrderId,
                TxnOrderDiff = 0,
                Processor = "paypal",
                ProcessorStatus = t.Status.ToString().ToLower(),
                ReportGroup = "Mpix",
                CardNumber = "",
                CardType = "",
                ProcessorId = t.Id ?? string.Empty
            };

            return ri;
        }
        public static ReportItem FromPreparedReportCSV(string line)
        {
            var values = line.Split(new char[] { ',' });
            var ri = new ReportItem();
            ri.CardType = values[10];
            ri.CardNumber = values[11];
            ri.TransactionType = values[0];
            ri.TransactionAmount = decimal.Parse(values[1].Replace("$", ""));
            ri.ResponseCode = values[2];
            ri.TransactionTime = DateTime.Parse(values[3]);
            ri.SettledDateTime = DateTime.Parse(values[4]);
            ri.MillersAccount = values[5];
            ri.MerchantOrderId = values[6];
            ri.OrderNumber = values[7];
            ri.OrderAmount = decimal.Parse(values[8]);
            ri.ReportGroup = values[9];
            ri.CustomerId = Guid.Parse(values[12]);
            ri.TxnOrderDiff = decimal.Parse(values[13]);
            ri.Processor = "braintree";
            ri.ProcessorStatus = values[17];
            ri.OrderIdProduction = int.Parse(values[18]);
            ri.ProcessorId = values[15];

            return ri;
        }
        public static ReportItem FromBraintreeTrans(Braintree.Transaction t)
        {
            //var values = line.Split(new char[] { ',' });
            var ri = new ReportItem
            {
                CardType = t.CreditCard.CardType.ToString(),
                CardNumber = t.CreditCard.LastFour.PadLeft(12, 'x'),
                TransactionType = t.Type.ToString(),
                TransactionAmount = t.Amount.Value,
                ResponseCode = t.ProcessorResponseText,
                TransactionTime = TimeZoneInfo.ConvertTimeFromUtc(t.CreatedAt.Value, TimeZoneInfo.Local),
                SettledDateTime = t.CreatedAt.Value,
                MerchantOrderId = t.OrderId,
                TxnOrderDiff = 0,
                Processor = "braintree",
                ProcessorStatus = t.Status.ToString().ToLower(),
                ProcessorId = t.Id ?? string.Empty
            };
            switch (t.MerchantAccountId.ToLower())
            {
                case "millersproimaging_instant":
                    ri.ReportGroup = "Millers";
                    break;
                case "mpix_instant":
                    ri.ReportGroup = "Mpix";
                    break;
                case "mpixpro_instant":
                    ri.ReportGroup = "MpixPro";
                    break;
                case "thirty9_instant":
                    ri.ReportGroup = "thirty9";
                    break;

            }
            return ri;
        }
        //public static ReportItem FromTransactionDetail(Reporting.Braintree t)
        //{
        //    var ri = new ReportItem
        //    {
        //        CardType = t.CardType,
        //        TransactionAmount = t.TransactionAmount.Value,
        //        CardNumber = t.CardNumber,
        //        CustomerId = t.CustomerId.Value,
        //        MerchantOrderId = t.MerchantOrderId,
        //        OrderIdProduction = t.OrderIdProduction.Value,
        //        Processor = "braintree",
        //        ProcessorId = t.ProcessorId,
        //        ReportGroup = t.ReportGroup,
        //        ProcessorStatus = t.ProcessorStatus.ToString(),
        //        ResponseCode = t.ResponseCode.ToString(),
        //        SettledDateTime = t.SettledDateTime.Value,
        //        TransactionTime = t.TransactionTime.Value,
        //        TransactionType = t.TransactionType.ToString(),
        //        TxnOrderDiff = t.TxnOrderDiff.Value

        //    };

        //    return ri;
        //}
        //public static ReportItem FromTransactionDetail(Reporting.PayPal t)
        //{
        //    var ri = new ReportItem
        //    {
        //        CardType = t.CardType,
        //        TransactionAmount = t.TransactionAmount.Value,
        //        CardNumber = t.CardNumber,
        //        CustomerId = t.CustomerId.Value,
        //        MerchantOrderId = t.MerchantOrderId,
        //        OrderIdProduction = t.OrderIdProduction.Value,
        //        Processor = "braintree",
        //        ProcessorId = t.ProcessorId,
        //        ReportGroup = t.ReportGroup,
        //        ResponseCode = t.ResponseCode.ToString(),
        //        SettledDateTime = t.SettledDateTime.Value,
        //        TransactionTime = t.TransactionTime.Value,
        //        TransactionType = t.TransactionType.ToString(),
        //        TxnEventCode = t.TxnEventCode.ToString(),
        //        TxnOrderDiff = t.TxnOrderDiff.Value

        //    };
        //    return ri;
        //}
        public string ToCSVHeader()
        {
            var ret = "";
            Type t = this.GetType();
            var props = t.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);

            ret = string.Join(",", props.Select(p => p.Name));

            return ret;
        }
        public string ToCSV()
        {
            var ret = "";
            Type t = this.GetType();

            var props = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            ret = string.Join(",", props.Select(p => p.GetValue(this, null)));

            return ret;

        }
    }
}
