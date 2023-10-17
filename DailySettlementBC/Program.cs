using Braintree;
using Dapper;
using NLog;
using Renci.SshNet;
using System.IO;
using System.Text;
using System.Transactions;
using static Dapper.SqlMapper;

namespace DailySettlementBC
{
    internal class Program
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        static Config Config;

        static void Main(string[] args)
        {
            try
            {
                Init();

                StringBuilder sb = new StringBuilder();

                GetBraintreeTransactions();

                var downloadRes = DownloadPayPalFiles();

                if (!downloadRes.Item1)
                    downloadRes = PayPalFilesReady();

                if (downloadRes.Item1)
                    ExtractPayPalReportItems(downloadRes.Item2);

                foreach (var r in ReportItems.Where(x => x.TransactionType != ""))
                {
                    var p = GetOrderInfoP(r.MerchantOrderId, r.ProcessorId);
                    var c = GetOrderInfoC(r.MerchantOrderId, r.ProcessorId);


                    if (p.Any())
                        r.OrderInfo.AddRange(p);
                    if (c.Any())
                        r.OrderInfo.AddRange(c);

                    if (!r.OrderInfo.Any())
                    {
                        //var i = GetInvoice(r.OrderInfo[0].OrderHeaderId);
                        //if (i != null)
                        //    r.InvoiceInfo.AddRange(i);

                        r.FlaggedForReview = true;

                        if (ReportItems.Any(x => x.TransactionType.ToLower() != "sale" && x.MerchantOrderId == r.MerchantOrderId))
                        {
                            r.Voided = true;
                            r.FlaggedForReview = false;
                        }
                    }
                    else
                    {
                        r.MillersAccount = r.OrderInfo[0].MillersAccount;
                        r.CustomerId = r.OrderInfo[0].CustomerId;
                    }


                    var osum = r.OrderInfo.Sum(x => x.TotalAmount);
                    var tsum = r.TransactionAmount;
                    //var isum = r.InvoiceInfo.Sum(x => x.Amount);

                    r.OrderTotal = osum;
                    //r.InvoiceTotal = isum;

                    int res = decimal.Compare(osum, tsum);

                    if (res != 0 || r.OrderInfo.Count == 0 && !r.Voided && r.TransactionType.ToLower() == "sale")
                    {
                        r.FlaggedForReview = true;
                    }

                    if (r.MerchantOrderId.StartsWith("AB"))
                    {
                        var ce = GetCustExpenseOrderID(r.MerchantOrderId);

                        if (ce != null)
                        {
                            r.OrderIds += ce.OrderId;
                            r.MillersAccount = ce.AccountNo;
                            r.FlaggedForReview = false;
                        }
                    }
                    if (r.Processor.ToLower() == "paypal")
                        r.ProcessorId = GetPayPalTransactionId(r.MerchantOrderId);

                    r.OrderIds += string.Join(" ", r.OrderInfo.Select(x => x.OrderId).ToList().Distinct());

                    sb.AppendLine(r.ToCSV());
                }

                if (ReportItems.Any())
                    sb.Insert(0, ReportItems.First().ToCSVHeader() + System.Environment.NewLine);
                else if (PayPalReportItems.Any())
                    sb.Insert(0, PayPalReportItems.First().ToCSVHeader() + System.Environment.NewLine);

                string outputpath = Path.Combine(Config.GetSetting("Output"), $"settlement_{DateTime.Now:MMddyyyy}.csv");
                File.WriteAllText(outputpath, sb.ToString());

                Console.WriteLine("Done");
            }
            catch (Exception ex)
            {
                logger.Error($"Error in DailySettlementBC: {ex}");
            }

        }
        static void Init()
        {
            Config = new Config();

            var paths = new List<string> { @"c:\settlements", @"c:\settlements\logs", @"c:\settlements\braintree", @"c:\settlements\paypal", @"c:\settlements\output" };
            foreach (var p in paths)
            {
                if (!Directory.Exists(p))
                    Directory.CreateDirectory(p);
            }
        }
        private static string GetPayPalTransactionId(string merchantOrderId)
        {
            string res = "";
            try
            {
                using (var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:mpixdb1.millerslab.com;Initial Catalog=Mpix20_Orders;uid=sa;pwd=millers8050; Failover Partner=mpixdb2.millerslab.com;Encrypt=false"))
                {
                    var q = "select TransactionId from OrderFormHeader where order_id = @id";

                    res = conn.QueryFirstOrDefault<string>(q, new { id = StringParam(merchantOrderId) });
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error in GetPayPalTransactionId: {ex}");
                res = ex.Message;
            }
            return res;

        }
        private static CustomerExpense GetCustExpenseOrderID(string merchantOrderId)
        {
            try
            {
                using (var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:pprodsql.millerslab.com;Initial Catalog=Millers_Shipping;uid=sa;pwd=millers8050; Failover Partner=pprodsqlfail.millerslab.com;Encrypt=false"))
                {
                    var q = "select OrderID, AccountNo from CustomerExpense where InvoiceNo = @id";

                    var res = conn.QueryFirstOrDefault<CustomerExpense>(q, new { id = StringParam(merchantOrderId) });
                    if (res != null && !string.IsNullOrEmpty(res.OrderId))
                        res.OrderId += "-SE";

                    return res;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error in GetCustExpenseInfo: {ex}");
            }
            return null;

        }

        static List<Invoice> GetInvoice(Guid headerid)
        {
            try
            {
                using (var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:pprodsql.millerslab.com;Initial Catalog=Millers_Accounting;uid=sa;pwd=millers8050; Failover Partner=pprodsqlfail.millerslab.com;Encrypt=false"))
                {
                    var q = "select OrderId, OrderAmount as Amount, Location, AccountNo  from PartnerInvoice where OrderHeaderId = @id";

                    var res = conn.Query<Invoice>(q, new { id = headerid });

                    return res.ToList();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error in GetInvoice: {ex}");
            }
            return new List<Invoice>();
        }

        static List<Order> GetOrderInfoC(string orderid, string transactionid)
        {
            try
            {
                using (var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:cdata01.millerslab.com;Initial Catalog=Millers_Production;uid=sa;pwd=Millers1712; Failover Partner=cdata02.millerslab.com;Encrypt=false"))
                {
                    var q = "select OrderId, TotalAmount, Entity, OrderHeaderid, AccountNo as MillersAccount, Order_Id_Production as OrderIDProduction, AccountID as CustomerId from OrderInfo where partnerorderid = @orderid or OrderId = @orderid";

                    var res = conn.Query<Order>(q, new { orderid = StringParam(orderid) });

                    return res.ToList();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error in GetOrderInfoC: {ex}");
            }
            return new List<Order>();
        }
        static List<Order> GetOrderInfoP(string orderid, string transactionid)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:pprodsql.millerslab.com;Initial Catalog=Millers_Production;uid=sa;pwd=millers8050; Failover Partner=pprodsqlfail.millerslab.com;Encrypt=false");

                var q = "select OrderId, TotalAmount, Entity, OrderHeaderId, AccountNo as MillersAccount, Order_ID_Production as OrderIDProduction, AccountID as CustomerId from OrderInfo where partnerorderid = @orderid or OrderId = @orderid";

                var res = conn.Query<Order>(q, new { orderid = StringParam(orderid) });

                return res.ToList();
            }
            catch (Exception ex)
            {
                logger.Error($"Error in GetOrderInfoP: {ex}");
            }
            return new List<Order>();
        }
        // static Transaction GetTransaction(string merchid)
        // {

        //     using var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:pdatasvr01;Initial Catalog=PaymentSystem;uid=sa;pwd=Miller$8050; Failover Partner=pdatasvr02;Encrypt=false");
        //     var q = "select top 1 * from transactions where orderid = @orderid order by TxnDate desc";

        //     var res = conn.QueryFirstOrDefault<Transaction>(q, new { orderid = StringParam(merchid) });

        //     //return new Transaction { OrderId = string.Join(",", res.Select(x => x.OrderId)), TotalAmount = res.Sum(x => x.TotalAmount) };
        //     return res;
        // }
        // static List<Transaction> GetTransactionsByDay(DateTime day)
        // {
        //     try
        //     {
        //         using var conn = new Microsoft.Data.SqlClient.SqlConnection("Data Source=tcp:pdatasvr01;Initial Catalog=PaymentSystem;uid=sa;pwd=Miller$8050; Failover Partner=pdatasvr02;Encrypt=false");

        //         var q = "select OrderId, TransactionId, Amount,Entity from Transactions a where a.TxnDate > @start and a.TxnDate < @end and (a.ResponseCode = '000' or a.ResponseText = 'Approved') and (a.TxnType = 'sale' or a.TxnType = 'credit') and OrderID not IN  (select OrderId from Transactions where TxnDate > @start and TxnDate < @end and TxnType = 'void') order by TxnDate";

        //         var res = conn.Query<Transaction>(q, new { start = day.Date, end = day.Date.AddDays(1).AddMinutes(32) });

        //         return res.ToList();
        //     }
        //     catch (Exception ex)
        //     {
        //         logger.Error($"Error in GetTransactionsByDay: {ex}");
        //     }
        //     return new List<Transaction>();

        // }
        public static DbString StringParam(string value, int lentgh = 50, bool fixedLength = false, bool ansi = true)
        {
            return new DbString { Value = value, Length = lentgh, IsAnsi = ansi, IsFixedLength = fixedLength };
        }
        static List<ReportItem> ReportItems = new();
        static List<ReportItem> PayPalReportItems = new();
        static void GetBraintreeTransactions(DateTime? reportDay = null)
        {
            try
            {
                Console.WriteLine("Starting Braintree Transactions download");
                if (ReportItems == null)
                    ReportItems = new List<ReportItem>();

                if (reportDay == null)
                    reportDay = DateTime.Now.Date.AddDays(-1);

                //var local = $@"c:\Braintree\download_{reportDay:MMddyyyy}.csv";
                var local = Path.Combine(Config.GetSetting("Incoming"), "braintree", $"download_{reportDay:MMddyyyy}.csv");

                if (File.Exists(local))
                {
                    ReportItems.AddRange(File.ReadAllLines(local).Select(r => ReportItem.FromBraintreeCSV(r)).ToList());

                    return;
                }

                var sec = Config.GetSection("BraintreeApi");
                string mid = sec["MerchantId"];
                string publickey = sec["PublicKey"];
                string privatekey = sec["PrivateKey"];

                var gateway = new Braintree.BraintreeGateway(Braintree.Environment.PRODUCTION, mid, publickey, privatekey);

                var start = reportDay.Value.Date;
                var end = start.AddHours(23).AddMinutes(59).AddSeconds(59);

                var result = gateway.Transaction.Search(new Braintree.TransactionSearchRequest().CreatedAt.Between(start, end).
                    Status.IncludedIn(Braintree.TransactionStatus.SETTLED, Braintree.TransactionStatus.SUBMITTED_FOR_SETTLEMENT,
                    Braintree.TransactionStatus.SETTLING,
                    Braintree.TransactionStatus.SETTLEMENT_CONFIRMED));


                Console.WriteLine($"Loading {result.MaximumCount} Braintree Transactions");

                foreach (var row in result.Cast<Braintree.Transaction>().ToList())
                    ReportItems.Add(ReportItem.FromBraintreeTrans(row));

                Console.WriteLine("Finished Loading Braintree Transactions");
                StringBuilder sb = new();
                foreach (var r in ReportItems)
                    sb.AppendLine(r.ToCSV());
                
                File.WriteAllText(local, sb.ToString());
            }
            catch (Exception ex)
            {
                logger.Error($"GetBraintreeTransaction Error:{System.Environment.NewLine}{ex}");
            }
        }
        static Tuple<bool, string> PayPalFilesReady(DateTime? reportDay = null)
        {
            bool notReady = true;
            if (reportDay == null)
                reportDay = DateTime.Now;
            Tuple<bool, string> res = Tuple.Create(false, "");

            var root = Path.Combine(Config.GetSetting("Incoming"), "paypal");

            while (notReady)
            {
                var formattedDate = reportDay.Value.ToString("MMddyyyy");

                var ppdir = Path.Combine(root, formattedDate);

                if (Directory.Exists(ppdir) && Directory.GetFiles(ppdir).Length > 0)
                    notReady = false;
                else
                    notReady = true;

                if (notReady)
                {
                    Console.WriteLine("PayPal File not ready. Waiting 5 minutes to retry.");
                    System.Threading.Thread.Sleep(60000 * 5);
                    res = DownloadPayPalFiles(reportDay);
                }
            }
            return res;
        }

        static Tuple<bool, string> DownloadPayPalFiles(DateTime? reportingDay = null)
        {
            bool fileready = false;
            string path = "";

            if (reportingDay == null)
                reportingDay = DateTime.Now;
            //var root = @"c:\settlements\PayPal";
            var root = Path.Combine(Config.GetSetting("Incoming"), "paypal");

            var formattedDate = reportingDay.Value.ToString("MMddyyyy");
            var dailypath = Path.Combine(root, formattedDate);


            try
            {
                if (!Directory.Exists(dailypath))
                    Directory.CreateDirectory(dailypath);

                var fi = new DirectoryInfo(dailypath).GetFiles().Where(x => x.Name.StartsWith("STL") && x.Name.Contains(reportingDay.Value.AddDays(-1).ToString("yyyyMMdd"))).FirstOrDefault();
                if (fi != null)
                {
                    return Tuple.Create<bool, string>(true, fi.FullName);
                }


                Console.WriteLine("Starting Download PayPal Files");
                var sec = Config.GetSection("Sftp");

                var url = sec["PayPalUrl"];
                var u = sec["PayPalUser"];
                var p = sec["PayPalPassword"];  

                using (var sftp = new SftpClient(url, u, p))
                {

                    sftp.Connect();
                    if (sftp.IsConnected)
                    {

                        var list = sftp.ListDirectory("/ppreports/outgoing");
                        foreach (var f in list)
                        {
                            //f.FullName.Dump();
                            var s = reportingDay.Value.AddDays(-1).ToString("yyyyMMdd");
                            if (f.IsRegularFile && f.Name.StartsWith("STL") && f.Name.Contains(reportingDay.Value.AddDays(-1).ToString("yyyyMMdd"))) //grab the settlement file
                            {
                                if (!Directory.Exists(dailypath))
                                    Directory.CreateDirectory(dailypath);

                                using FileStream fs = new(Path.Combine(dailypath, f.Name), FileMode.Create, FileAccess.ReadWrite);
                                Console.WriteLine(f.Name);
                                sftp.DownloadFile(f.FullName, fs);
                                //m.PayPalFilesReady = true;
                                //m.PathToPayPalFile = Path.Combine(dailypath, f.Name);
                                fileready = true;
                                path = Path.Combine(dailypath, f.Name);
                            }

                        }
                        if (!fileready)
                            Console.WriteLine("DailySettlement.Download: PayPal Daily File was not available for download");

                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"UpdateSettlement.DownloadPayPalFiles {System.Environment.NewLine}{ex}");
                Console.WriteLine(ex.Message);
            }
            return Tuple.Create<bool, string>(fileready, path);
        }

        static void ExtractPayPalReportItems(string path)
        {
            Console.WriteLine("Starting PayPal Extract and Combine Report Files");
            //PayPalReportItems = new List<ReportItem>();

            ReportItems.AddRange(File.ReadAllLines(path).Where(x => x.StartsWith("\"SB\"")).Select(r => ReportItem.FromPayPalCSV(r, "Mpix")).ToList());
        }
    }
    class Order
    {
        public string OrderId { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public string Entity { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public string MillersAccount { get; set; } = "";
        public Guid OrderHeaderId { get; set; }
        public Guid CustomerId { get; set; }
    }
    class Invoice
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
    }
    class Transaction
    {
        public long Id { get; set; }
        public string RequestId { get; set; }
        public string OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public string Entity { get; set; }
        public string TxnType { get; set; }
        public decimal Amount { get; set; }
        public string TransactionId { get; set; }
        public DateTime TxnDate { get; set; }
        public string Processor { get; set; }
        public string TokenOrNonce { get; set; }
        public string ResponseCode { get; set; }
        public string ResponseText { get; set; }
        public string ProcessorResponse { get; set; }
    }
    class CustomerExpense
    {
        public string AccountNo { get; set; }
        public string OrderId { get; set; }
    }
}