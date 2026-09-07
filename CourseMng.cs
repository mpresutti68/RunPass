//using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Micros.DataStore;
using Micros.Ops;
using Micros.Ops.Extensibility;
using Micros.Ops.Input;
using Micros.PosCore.Extensibility;
using Micros.PosCore.Extensibility.DataStore;
using Micros.PosCore.Extensibility.Ops;
using Micros.PosCore.Extensibility.Printing;
using Micros.PosCore.Printing;
using Microsoft.PointOfService;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using static System.Net.Mime.MediaTypeNames;
using static Mysqlx.Expect.Open.Types.Condition.Types;


namespace CourseMng
{
    public interface IDbConnectionFactory
    {
        int TipoDB { get; }
        DbConnection CreateConnection();
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        public int TipoDB { get; }
        private readonly string _connectionString;

        public DbConnectionFactory(int tipoDB, string connectionString)
        {
            TipoDB = tipoDB;
            _connectionString = connectionString;
        }

        public DbConnection CreateConnection()
        {
            DbConnection connection;

            if (TipoDB == 0)
            {
                connection = new SqlConnection(_connectionString);
            }
            else if (TipoDB == 2)
            {
                connection = new MySqlConnection(_connectionString);
            }
            else
            {
                throw new NotSupportedException($"Tipo di database {TipoDB} non supportato.");
            }

            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            return connection;
        }
    }

    public static class myLog
    {
        private static readonly object _lock = new object();
        private static string _logFolderPath;
        public static int _verbosity = 0;

        static myLog()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logFolderPath = Path.Combine(baseDir, "Logs", "CustomExtension");

                if (!Directory.Exists(_logFolderPath))
                {
                    Directory.CreateDirectory(_logFolderPath);
                }
            }
            catch
            {
                _logFolderPath = Path.Combine(Path.GetTempPath(), "SimphonyLogs");
                Directory.CreateDirectory(_logFolderPath);
            }
        }

        public enum LogLevel
        {
            DEBUG,
            INFO,
            WARN,
            ERROR
        }

        public static void UpdateVerbosity(int ver)
        {
            _verbosity = ver;
        }

        public static void Info(string message)
        {
            WriteLog(LogLevel.INFO, message);
        }

        public static void Debug(string message)
        {
            if (_verbosity < 3) return;
            WriteLog(LogLevel.DEBUG, message);
        }

        public static void Warn(string message)
        {
            if (_verbosity < 2) return;
            WriteLog(LogLevel.WARN, message);
        }

        public static void Error(string message, Exception ex = null)
        {
            if (_verbosity < 1) return;
            string detailMessage = message;
            if (ex != null)
            {
                detailMessage += $" | Exception: {ex.Message} | StackTrace: {ex.StackTrace}";
            }
            WriteLog(LogLevel.ERROR, detailMessage);
        }

        private static void WriteLog(LogLevel level, string message)
        {
            try
            {
                string fileName = $"SimphonyExt_{DateTime.Now:yyyyMMdd}.log";
                string fullPath = Path.Combine(_logFolderPath, fileName);
                string formattedEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    using (FileStream fs = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(formattedEntry);
                    }
                }
            }
            catch
            {
                // Protezione contro l'interruzione del processo POS in caso di errore I/O
            }
        }
    }


    public class MyConfig

    {
        public bool Develop1 { get; set; }

        public int VerbosityDisplay { get; set; } = 0;

        public long RTT_ID { get; set; }

        public int VerbosityLog { get; set; }

        public int LunghNomeArticoli { get; set; } = 19;

        public List<int> RvcCourseMng { get; set; }

        public List<int> RvcMenu { get; set; }

        public int CorsaIniziale { get; set; } = 1;

        public int CorsaLimite { get; set; } = 5;

        public List<long> CambioCorsa { get; set; }

        public List<List<int>> OrderDeviceMarcia { get; set; }

        public int CodificaStampante { get; set; } = 850;

        public CoppiaValori CorsaPreDessert { get; set; }

        public CoppiaValori CorsaDessert { get; set; }

        public CoppiaValori CorsaPetitFour { get; set; }

        public long Marcia { get; set; }

        public long Marciato { get; set; }

        public string Lbl_Marcia { get; set; }

        public long TM_SendOrder { get; set; }

        public long FamilyMarcia { get; set; }

        public long OverPrintClass { get; set; }

        public int SubLevelNoPrice { get; set; } = 8;

        public List<long> Suite { get; set; }
    }

    public class CoppiaValori
    {
        [JsonProperty("Obj")]
        public long Obj { get; set; }

        [JsonProperty("corsa")]
        public int Corsa { get; set; }
    }

    public class OrderDeviceCache : IEnumerable<OrderDeviceCache.OrderDeviceInfo>
    {
        //private readonly string _connectionString;
        private readonly Dictionary<string, OrderDeviceInfo> _items;
        private readonly IDbConnectionFactory _dbFactory;
        private readonly string _queryMySql = @"
        WITH Printer as (
            SELECT prn.PrinterID, prn.ConfigurationString, concat(ObjectNumber, ' ', nam.StringText) NomePrinter
            FROM datastore.printer prn
            INNER JOIN datastore.string_table nam on prn.NameID = nam.StringNumberID
            WHERE ConnectionString = 'Micros.Devices.RollPrinter.Ethernet'
        ),
            OrderDevice as (
                SELECT  OrdDvcIndex
                FROM DataStore.WORKSTATION_ORDER_DEVICE
                where IsVisible = 1 and IsDeleted =0
                        and WorkstationID=@WksID
        )
        SELECT  odv.OrdDvcIndex,
		        nam.StringText NomeOrderDevice,
                odv.OptionBits,
                odv.PmcOptionBits,
                odv.VduOptionBits,
                prn.PrinterID PrimaryID,
                SUBSTRING_INDEX(prn.ConfigurationString, ',', 1) IP_Printer,
                SUBSTRING_INDEX(SUBSTRING_INDEX(prn.ConfigurationString, ',', 2), ',', -1) Porta_Printer,
                prn.NomePrinter PrinterName,
                bak.PrinterID SecondaryBackupID,
                SUBSTRING_INDEX(bak.ConfigurationString, ',', 1) IP_Backup,
                SUBSTRING_INDEX(SUBSTRING_INDEX(bak.ConfigurationString, ',', 2), ',', -1) Porta_Backup,
                bak.NomePrinter BackupName,
                hiu.RevCtrID,
                CASE 
                        WHEN LEFT(odv.CustomName,2) = 'MA' THEN 'M'
                        WHEN LEFT(odv.CustomName,2) = 'OD' THEN 'O'
                        ELSE 'N'
                END AS TypeOD
        FROM datastore.order_device odv
        LEFT OUTER JOIN Printer prn on odv.PrimaryID = prn.PrinterID
        LEFT OUTER JOIN Printer bak on odv.SecondaryBackupID = bak.PrinterID
        INNER JOIN datastore.hierarchy_structure his on odv.HierStrucID = his.HierStrucID
        INNER JOIN datastore.hierarchy_unit hiu on his.HierUnitID = hiu.HierUnitID
        INNER JOIN datastore.string_table nam on odv.HeaderID=nam.StringNumberID
        INNER JOIN OrderDevice odact on odv.OrdDvcIndex = odact.OrdDvcIndex
        WHERE hiu.RevCtrID = @RevCtrID;";

        private readonly string _querySqlServer = @"
            WITH PrinterCTE as (
                SELECT prn.PrinterID, prn.ConfigurationString, CONCAT(ObjectNumber, ' ', nam.StringText) NomePrinter
                FROM datastore.dbo.printer prn
                INNER JOIN datastore.dbo.string_table nam on prn.NameID = nam.StringNumberID
                WHERE ConnectionString = 'Micros.Devices.RollPrinter.Ethernet'
            ),
                OrderDevice as (
                SELECT  [OrdDvcIndex]
                FROM [DataStore].[dbo].[WORKSTATION_ORDER_DEVICE]
                where IsVisible = 1 and IsDeleted =0
                        and WorkstationID=@WksID --65848
            )
            SELECT  odv.OrdDvcIndex,
		            nam.StringText NomeOrderDevice,
                    odv.OptionBits,
                    odv.PmcOptionBits,
                    odv.VduOptionBits,
                    prn.PrinterID PrimaryID,
                    -- T-SQL: Estrazione IP (Tutto ciò che c'è prima della prima virgola)
                    CASE WHEN CHARINDEX(',', prn.ConfigurationString) > 0 
                         THEN LEFT(prn.ConfigurationString, CHARINDEX(',', prn.ConfigurationString) - 1) 
                         ELSE prn.ConfigurationString END AS IP_Printer,
                   CASE WHEN CHARINDEX(',', prn.ConfigurationString) > 0 

                         THEN SUBSTRING(prn.ConfigurationString, CHARINDEX(',', prn.ConfigurationString) + 1, LEN(prn.ConfigurationString)) 

                         ELSE '' END AS Porta_Printer,
                    prn.NomePrinter PrinterName,
                    bak.PrinterID SecondaryBackupID,
                    -- T-SQL: Estrazione IP Backup
                    CASE WHEN CHARINDEX(',', bak.ConfigurationString) > 0 
                         THEN LEFT(bak.ConfigurationString, CHARINDEX(',', bak.ConfigurationString) - 1) 
                         ELSE bak.ConfigurationString END AS IP_Backup,
                    -- T-SQL: Estrazione Porta Backup
                    CASE WHEN CHARINDEX(',', bak.ConfigurationString) > 0 
                         THEN SUBSTRING(bak.ConfigurationString, CHARINDEX(',', bak.ConfigurationString) + 1, LEN(bak.ConfigurationString)) 
                         ELSE '' END AS Porta_Backup,
                    bak.NomePrinter BackupName,
                    hiu.RevCtrID,
                    CASE 
                        WHEN LEFT(odv.CustomName,2) = 'MA' THEN 'M'
                        WHEN LEFT(odv.CustomName,2) = 'OD' THEN 'O'
                        ELSE 'N'
                    END AS TypeOD
            FROM datastore.dbo.order_device odv
            LEFT OUTER JOIN PrinterCTE prn on odv.PrimaryID = prn.PrinterID
            LEFT OUTER JOIN PrinterCTE bak on odv.SecondaryBackupID = bak.PrinterID
            INNER JOIN datastore.dbo.hierarchy_structure his on odv.HierStrucID = his.HierStrucID
            INNER JOIN datastore.dbo.hierarchy_unit hiu on his.HierUnitID = hiu.HierUnitID
            INNER JOIN datastore.dbo.string_table nam on odv.HeaderID=nam.StringNumberID
            INNER JOIN OrderDevice odact on odv.OrdDvcIndex = odact.OrdDvcIndex
            WHERE hiu.RevCtrID = @RevCtrID; --12847";

        /*public OrderDeviceCache(string connectionString)
        {
            _connectionString = connectionString;
            _items = new Dictionary<string, OrderDeviceInfo>();
        }
        */

        public OrderDeviceCache(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
            _items = new Dictionary<string, OrderDeviceInfo>();
        }

        /*
        public void Load(int typeDb, int revCtrlId, int wksID)
        {
            if (typeDb == 0)
                LoadSqlServer(revCtrlId, wksID);
            else
                LoadMySql(revCtrlId,wksID);
        }
        private void LoadMySql(int revCtrId, int wksID)
        {
            _items.Clear();

            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                using (MySqlCommand cmd = new MySqlCommand(_sql, conn))
                {
                    cmd.Parameters.AddWithValue("@RevCtrID", revCtrId);
                    cmd.Parameters.AddWithValue("@WksID", wksID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        int ordDvcIndexCol = reader.GetOrdinal("OrdDvcIndex");

                        while (reader.Read())
                        {
                            OrderDeviceInfo item = new OrderDeviceInfo();

                            item.OrdDvcIndex = !reader.IsDBNull(ordDvcIndexCol)
                                ? reader.GetInt16(ordDvcIndexCol).ToString()
                                : string.Empty;

                            item.OptionBits = reader["OptionBits"] as string ?? string.Empty;

                            item.NomeOrderDevice = reader["NomeOrderDevice"] as string ?? string.Empty;
                            // FIX: Assegnazione corretta degli ID
                            item.PrimaryID = reader["PrimaryID"] != DBNull.Value ? Convert.ToInt32(reader["PrimaryID"]) : 0;
                            item.BackupID = reader["SecondaryBackupID"] != DBNull.Value ? Convert.ToInt32(reader["SecondaryBackupID"]) : 0;

                            item.IPPrinter = reader["IP_Printer"] as string ?? string.Empty;
                            item.PrinterName = reader["PrinterName"] as string ?? string.Empty;

                            // FIX: Parsing sicuro per la porta
                            //if (reader["Porta_Printer"] != DBNull.Value && int.TryParse(reader["Porta_Printer"].ToString(), out int portaPrn))
                            //    item.PortaPrinter = portaPrn;

                            item.PortaPrinter = 9100;
                            item.RvcID = reader["RevCtrID"] != DBNull.Value ? Convert.ToInt32(reader["RevCtrID"]) : 0;

                            item.IPBackup = reader["IP_Backup"] as string ?? string.Empty;
                            item.BackupName = reader["BackupName"] as string ?? string.Empty;
                            item.TypeOD = reader["TypeOD"] as string ?? string.Empty;
                            // FIX: Parsing sicuro per la porta di backup
                            //if (reader["Porta_Backup"] != DBNull.Value && int.TryParse(reader["Porta_Backup"].ToString(), out int portaBak))
                            //    item.PortaBackup = portaBak;

                            item.PortaBackup = 9100;
                            if (!string.IsNullOrEmpty(item.OrdDvcIndex) && !_items.ContainsKey(item.OrdDvcIndex))
                            {
                                _items.Add(item.OrdDvcIndex, item);
                            }
                        }
                    }
                }
            }
        */
        public void Load(int revCtrId, int wksID)
        {
            _items.Clear();

            string query = _dbFactory.TipoDB == 2 ? _queryMySql : _querySqlServer;

            // Creazione e apertura gestite dalla Factory
            using (DbConnection conn = _dbFactory.CreateConnection())
            {
                using (DbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = query;

                    // Creazione generica dei parametri (sostituisce AddWithValue)
                    DbParameter paramRev = cmd.CreateParameter();
                    paramRev.ParameterName = "@RevCtrID";
                    paramRev.Value = revCtrId;
                    cmd.Parameters.Add(paramRev);

                    DbParameter paramWks = cmd.CreateParameter();
                    paramWks.ParameterName = "@WksID";
                    paramWks.Value = wksID;
                    cmd.Parameters.Add(paramWks);

                    using (DbDataReader reader = cmd.ExecuteReader())
                    {
                        int ordDvcIndexCol = reader.GetOrdinal("OrdDvcIndex");

                        while (reader.Read())
                        {
                            OrderDeviceInfo item = new OrderDeviceInfo();

                            item.OrdDvcIndex = !reader.IsDBNull(ordDvcIndexCol)
                                ? reader.GetInt16(ordDvcIndexCol).ToString()
                                : string.Empty;

                            item.OptionBits = reader["OptionBits"] as string ?? string.Empty;
                            item.NomeOrderDevice = reader["NomeOrderDevice"] as string ?? string.Empty;

                            item.PrimaryID = reader["PrimaryID"] != DBNull.Value ? Convert.ToInt32(reader["PrimaryID"]) : 0;
                            item.BackupID = reader["SecondaryBackupID"] != DBNull.Value ? Convert.ToInt32(reader["SecondaryBackupID"]) : 0;

                            item.IPPrinter = reader["IP_Printer"] as string ?? string.Empty;
                            item.PrinterName = reader["PrinterName"] as string ?? string.Empty;

                            // Porte fisse per ora come da tuo codice
                            item.PortaPrinter = 9100;

                            item.RvcID = reader["RevCtrID"] != DBNull.Value ? Convert.ToInt32(reader["RevCtrID"]) : 0;

                            item.IPBackup = reader["IP_Backup"] as string ?? string.Empty;
                            item.BackupName = reader["BackupName"] as string ?? string.Empty;
                            item.TypeOD = reader["TypeOD"] as string ?? string.Empty;

                            item.PortaBackup = 9100;

                            if (!string.IsNullOrEmpty(item.OrdDvcIndex) && !_items.ContainsKey(item.OrdDvcIndex))
                            {
                                _items.Add(item.OrdDvcIndex, item);
                            }
                        }
                    }
                }
            }
        }

        /*
        private void LoadSqlServer(int revCtrId, int wksID)
        {
            _items.Clear();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (SqlCommand cmd = new SqlCommand(_querySqlServer, conn))
                {
                    cmd.Parameters.AddWithValue("@RevCtrID", revCtrId);
                    cmd.Parameters.AddWithValue("@WksID", wksID);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        int ordDvcIndexCol = reader.GetOrdinal("OrdDvcIndex");

                        while (reader.Read())
                        {
                            OrderDeviceInfo item = new OrderDeviceInfo();

                            item.OrdDvcIndex = !reader.IsDBNull(ordDvcIndexCol)
                                ? reader.GetInt16(ordDvcIndexCol).ToString()
                                : string.Empty;

                            item.OptionBits = reader["OptionBits"] as string ?? string.Empty;

                            item.NomeOrderDevice = reader["NomeOrderDevice"] as string ?? string.Empty;

                            item.PrimaryID = reader["PrimaryID"] != DBNull.Value ? Convert.ToInt32(reader["PrimaryID"]) : 0;
                            item.BackupID = reader["SecondaryBackupID"] != DBNull.Value ? Convert.ToInt32(reader["SecondaryBackupID"]) : 0;

                            item.IPPrinter = reader["IP_Printer"] as string ?? string.Empty;
                            item.PrinterName = reader["PrinterName"] as string ?? string.Empty;

                            // Parsing della porta con sicurezza per gestire eventuali terzi parametri ignorati
                            // string portaPrnStr = reader["Porta_Printer"] as string ?? string.Empty;
                            // if (portaPrnStr.Contains(",")) portaPrnStr = portaPrnStr.Split(',')[0];
                            // if (int.TryParse(portaPrnStr, out int portaPrn))
                            //    item.PortaPrinter = portaPrn;
                            item.PortaPrinter = 9100;

                            item.RvcID = reader["RevCtrID"] != DBNull.Value ? Convert.ToInt32(reader["RevCtrID"]) : 0;

                            item.IPBackup = reader["IP_Backup"] as string ?? string.Empty;
                            item.BackupName = reader["BackupName"] as string ?? string.Empty;
                            item.TypeOD = reader["TypeOD"] as string ?? string.Empty;
                            // string portaBakStr = reader["Porta_Backup"] as string ?? string.Empty;
                            // if (portaBakStr.Contains(",")) portaBakStr = portaBakStr.Split(',')[0];
                            // if (int.TryParse(portaBakStr, out int portaBak))
                            //    item.PortaBackup = portaBak;
                            item.PortaBackup = 9100;

                            if (!string.IsNullOrEmpty(item.OrdDvcIndex) && !_items.ContainsKey(item.OrdDvcIndex))
                            {
                                _items.Add(item.OrdDvcIndex, item);
                            }
                        }
                    }
                }
            }
        }
        */

        // Mantenuto un solo metodo di recupero per evitare duplicazioni
        public OrderDeviceInfo GetDevice(int ordDvcIndex)
        {
            if (_items.TryGetValue(ordDvcIndex.ToString(), out OrderDeviceInfo device))
            {
                return device;
            }

            return null;
        }

        public int Count()
        {
            return _items.Count;
        }

        public IEnumerator<OrderDeviceInfo> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }

        // Ritorna l'enumeratore non generico (richiesto dall'interfaccia base IEnumerable)
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Rinominato per riflettere il vero contenuto dell'oggetto
        public class OrderDeviceInfo
        {
            public string OrdDvcIndex { get; set; }
            public string NomeOrderDevice { get; set; }
            public string OptionBits { get; set; }
            public int PrimaryID { get; set; }
            public int BackupID { get; set; }
            public string IPPrinter { get; set; }
            public int PortaPrinter { get; set; }
            public string PrinterName { get; set; }
            public string IPBackup { get; set; }
            public int PortaBackup { get; set; }
            public int RvcID { get; set; }
            public string BackupName { get; set; }
            public string TypeOD { get; set; }
        }
    }

    [Export(typeof(OpsExtensibilityApplication))]

    public class CondimentPrint
    {
        public string quant { get; set; }
        public string nome { get; set; }
        public string reference { get; set; }
        public string prezzo { get; set; }
        public string peso { get; set; }
    }

    public class MenuItemPrint
    {
        public long miobjnum { get; set; }
        public string quant { get; set; }
        public string nome { get; set; }
        public string reference { get; set; }
        public string prezzo { get; set; }
        public string peso { get; set; }
        public int numcorsa { get; set; }
        public string corsa { get; set; }
        public string seat { get; set; }
        public int ODIndex { get; set; }
        public List<CondimentPrint> condiments { get; set; }
    }

    public class TestWork
    {
        public static long LeggiInt(string codice8Cifre, long chiaveMolt = 7398213, long chiaveAdd = 59283714)
        {
            long modulo = 100000000;

            if (!long.TryParse(codice8Cifre, out long cifratoInt))
            {
                throw new ArgumentException("Il codice fornito non è valido o non è numerico.");
            }

            long inversoMoltiplicativo = PosInt(chiaveMolt, modulo);

            long differenza = (cifratoInt - chiaveAdd) % modulo;

            if (differenza < 0)
            {
                differenza += modulo;
            }

            long numeroBase8 = (differenza * inversoMoltiplicativo) % modulo;

            long numero5Cifre = numeroBase8 / 1000;

            return numero5Cifre;
        }


        private static long PosInt(long a, long m)
        {
            long m0 = m;
            long y = 0, x = 1;

            if (m == 1) return 0;

            while (a > 1)
            {
                long q = a / m;
                long t = m;
                m = a % m;
                a = t;
                t = y;
                y = x - q * y;
                x = t;
            }

            if (x < 0)
            {
                x += m0;
            }

            return x;
        }
    }

    public class DatiComanda
    {
        public string Tavolo { get; set; }
        public string CheckNumber { get; set; }
        public string Utente { get; set; }
        public string CheckID { get; set; }
        public string Rvc_name { get; set; }
        public int MarciaCorrente { get; set; }
    }

    public class NomiCorse
    {
        [JsonProperty("CorsaNum")]
        public int CorsaNum { get; set; }
        [JsonProperty("CorsaNome")]
        public string CorsaNome { get; set; }

    }
    public class AggiornaDatiComandaEventArgs : EventArgs
    {
        public DatiComanda DatiComanda { get; private set; }
        public AggiornaDatiComandaEventArgs(DatiComanda datiComanda)
        {
            DatiComanda = datiComanda;
        }
    }
    public class GestioneCorse2
    {
        public event EventHandler AggiornaDatiComanda;
        private int corsainiziale { get; set; }
        private int corsalimite { get; set; }
        private int corsaattuale { get; set; }
        private HashSet<int> corsemarciate { get; set; }
        private HashSet<int> corseusate { get; set; }
        private bool inviatastampa { get; set; } = false;
        private int corsapredessert { get; set; }
        private int corsadessert { get; set; }
        private int corsapetitfour { get; set; }

        // COSTRUTTORE
        public GestioneCorse2(int corsainiz, int corsalim, int predessert, int dessert, int petitfour)
        {
            corseusate = new HashSet<int>();
            corsemarciate = new HashSet<int>();
            corsainiziale = corsainiz;
            corsalimite = corsalim;
            corsaattuale = corsainiz;
            corsapredessert = predessert;
            corsadessert = dessert;
            corsapetitfour = petitfour;
            OnAggiornaDatiComanda();
        }
        // PROPRIETÀ PRIVATE
        private enum TipoCorsaEnum
        {
            NoCourse = 0,
            Base = 1,
            AltraCorsa = 2,
            NonGestita = 3
        }
        private TipoCorsaEnum TipoCorsa(int corsa)
        {
            if (corsa == 0) return TipoCorsaEnum.NoCourse;
            else if (corsa >= corsainiziale && corsa <= corsalimite) return TipoCorsaEnum.Base;
            else if (corsa >= corsalimite && corsa < 20) return TipoCorsaEnum.AltraCorsa;
            return TipoCorsaEnum.NonGestita;
        }
        private bool SeCorsaMarciabile(int corsa)  //Se appartiene alle base o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base || TipoCorsa(corsa) == TipoCorsaEnum.AltraCorsa; }
        private bool SeCorsaUsabile(int corsa) //Se appartiene alle base o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base; }
        private bool SeCorsaIterabile(int corsa)  //Se appaartiene alle iterabile o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base; }
        private int UltimaCorsaUsata()
        {
            if (!corseusate.Any())
                return 0;
            var corseIterabili = corseusate.Where(c => SeCorsaIterabile (c));
            if (!corseIterabili.Any())
                return 0;
            else
                return corseIterabili.Max();
        }
        private bool SeCorsaMarciata(int corsa)
        { return corsemarciate.Contains(corsa); }
        private bool SeTutteMarciate()
        { return corseusate.IsSubsetOf(corsemarciate); }
        private void ImpostaCorsa()
        {
            if (UltimaCorsaMarciata() > 0)
                corsaattuale = UltimaCorsaMarciata();
            else
                corsaattuale = corsainiziale;
        }
        public string OttieniStringaMarciate()
        {
            //HashSet<int> valori = _gestioneCorse.GetCorseMarciate();
            // Controllo di sicurezza nel caso l'HashSet sia nullo
            if (corsemarciate == null || corsemarciate.Count == 0)
            {
                return "Nessuna Marciata"; // Lista CorseMarciate Vuota
            }

            if (SeTutteMarciate() && SeCorsaUsata(corsaattuale)) // verifica che tutte le corse usate sono marciate e che quella corrente non è usata
            {
                return "Tutte Marciate";
            }

            var valoriModificati = corsemarciate.Select(v =>
            {
                if (TipoCorsa(v) == TipoCorsaEnum.AltraCorsa) return NomeAltraCorsa(v);

                return v.ToString(); // Mantiene il numero originale per tutti gli altri
            });

            // Unisce i valori dell'HashSet separandoli con una virgola e uno spazio
            string elencoValori = string.Join(", ", valoriModificati);

            // Costruisce la stringa finale
            return $"Marciate: {elencoValori}";
        }
        private string NomeAltraCorsa(int numeroCorsa)
        {
            if (corsapredessert == numeroCorsa)
                return "Pre-Dessert";
            else if (corsadessert == numeroCorsa)
                return "Dessert";
            else if (corsapetitfour == numeroCorsa)
                return "petitFour";
            else
                return null;
        }

        // PROPRIETÀ PUBBLICHE
        public int UltimaCorsaMarciata()
        {
            if (!corsemarciate.Any())
                return 0;
            return corsemarciate.Where(c => SeCorsaIterabile(c)).Max();
        }
        public bool SeCorsaUsata(int corsa)
        { return corseusate.Contains(corsa); }
        public int CorsaAttuale()
        { return corsaattuale; }
        public bool SeImmediata(int corsa)
        { return SeCorsaMarciata(corsa); }
        public bool SeCorsaAggiuntaAComandaFinita(int corsa) //Rispone true se si tratta di na corsa valida, nuova e tutte le altre sono state marciate
        { return (SeTutteMarciate() && SeCorsaUsabile(corsa) && !SeCorsaUsata(corsa)); }
        public int NextMarcia()
        {
            if (!corseusate.Any())
                return 0;
            var prossimecorsedamarciare = corseusate.Where(c => c > UltimaCorsaMarciata());
            if (!prossimecorsedamarciare.Any())
                return 0;
            return prossimecorsedamarciare.Min();
        }
        public List<string> Intestazione()
        {
            List<string> intestazione = new List<string>();
            intestazione.Add(testoMessaggio(string.Format("Corsa {0}", CorsaAttuale()), 40));
            intestazione.Add(testoMessaggio(OttieniStringaMarciate(), 40));
            return intestazione;    
        }
        //METODI PUBLICI
        public void SetCorse(HashSet<int> parcorseusate, HashSet<int> parcorsemarciate)
        {
            corsemarciate = parcorsemarciate;
            corseusate = parcorseusate;
            OnAggiornaDatiComanda();
        }
        public void SetCorsa(int corsa)
        {
            if (SeCorsaUsabile(corsa)) 
            { 
                corsaattuale = corsa; 
                OnAggiornaDatiComanda();
            }
        }
        public void SetNextCorsa()
        {
            List<int> numeri = Enumerable.Range(1, corsalimite).ToList();
            var corseIterabili = new List<int>();
            int ultimaCorsaMarciata = UltimaCorsaMarciata();
            if (corseusate.Any())
            {
                corseIterabili = numeri.Where(c => c > corsaattuale && c >= ultimaCorsaMarciata && c <= UltimaCorsaUsata() + 1 && c <= corsalimite).ToList();

                if (corseIterabili.Any())
                    corsaattuale = corseIterabili[0];
                else
                    corsaattuale = ultimaCorsaMarciata;
                OnAggiornaDatiComanda();
            }
        }
        public void AddMarciata(int corsa)
        {
            if (SeCorsaMarciabile(corsa)) // Modifica il parametro solo se la corsa è marciablie
            {
                corsemarciate.Add(corsa);
                OnAggiornaDatiComanda();
            }
        }
        public void AddUsata(int corsa)
        { 
            if (SeCorsaUsabile(corsa)) corsemarciate.Add(corsa);//Modifica il parametro solo se la corsa è usabile
            if (corseusate.Count == 1) // Se la corsa usata è la prima, la aggiungiamo anche alle corse marciate
            {
                AddMarciata(corsa);
            }
            OnAggiornaDatiComanda();
        }  
        public void Stampata()
        { inviatastampa = true; }
        //METODI EVENTI
        protected virtual void OnAggiornaDatiComanda()
        {
            AggiornaDatiComanda?.Invoke(this, EventArgs.Empty);
        }
        //METODI PRIVATI
        private string testoMessaggio(string input, int lunghezzaTotale)
        {
            {
                int lunginput = input.Length;
                int lunglati = (lunghezzaTotale - lunginput) / 2;
                string spaziSX = new string(' ', lunglati - 1);
                string spaziDX = new string(' ', lunglati - 1);

                return $"-{spaziSX}{input}{spaziDX}-";
            }
        }
    }

    /*public class GestioneCorse
    {
        public int CorsaIniziale { get; set; }
        public int CorsaLimite { get; set; }
        public int CorsaAttuale { get; set; }
        private HashSet<int> CorseMarciate { get; set; }
        private HashSet<int> CorseUsate { get; set; }
        private bool InviataStampa { get; set; }
        public GestioneCorse(int corsainiz, int corsalim, bool inviataStampa = false)
        {
            CorseMarciate = new HashSet<int>();
            CorseUsate = new HashSet<int>();
            CorsaIniziale = corsainiz;
            CorsaLimite = corsalim;
            CorsaAttuale = corsainiz;
            InviataStampa = inviataStampa;
            //AggiungiCorsaMarciata(corsainiz);
        }
        public enum TipoCorsaEnum
        {
            NoCourse = 0,
            Base = 1,
            AltraCorsa = 2,
            NonGestita = 3
        }
        private TipoCorsaEnum TipoCorsa(int corsa)
        {
            if (corsa == 0)  return TipoCorsaEnum.NoCourse; 
            else if (corsa >= CorsaIniziale && corsa <= CorsaLimite) return TipoCorsaEnum.Base; 
            else if (corsa >= CorsaLimite && corsa < 20) return TipoCorsaEnum.AltraCorsa;
            return TipoCorsaEnum.NonGestita;
        }
        private bool GetifCorsaMarciabile(int corsa)  //Se appartiene alle base o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base || TipoCorsa(corsa)== TipoCorsaEnum.AltraCorsa; }
        private bool GetifCorsaUsabile(int corsa) //Se appartiene alle base o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base || TipoCorsa(corsa) == TipoCorsaEnum.AltraCorsa; }
        private bool GetIfCorsaIterabile(int corsa)  //Se appaartiene alle iterabile o alle altre corse
        { return TipoCorsa(corsa) == TipoCorsaEnum.Base; }
        private bool GetIfUsabilexAttuale(int corsa, bool extend)  // se fa parte delle iterabili o se fa parte delle altre corse e extend è true
        { return (GetifCorsaUsabile(corsa) || (GetifCorsaMarciabile(corsa) && extend)); }
        // --- METODI PER L'INSERIMENTO DATI ---

        public int GetCorsaAttuale()
        {  return CorsaAttuale; }

        public bool GetIfCorsaAttualeUsata()
        { return GetIfCorsaUsata(GetCorsaAttuale()); }

        public int GetNumeroCorseUsate()
        { return CorseUsate.Count; }

        public bool GetIfIsOnlyCourse(int corsa)
        { return (GetIfCorsaUsata(corsa) && (GetNumeroCorseUsate() == 1)); }

        public void AggiungiCorsaMarciata(int corsa)
        { if (GetifCorsaMarciabile(corsa))  CorseMarciate.Add(corsa); }  //Modifica il parametro solo se la corsa è marciablie

        public void Stampata()
        { InviataStampa = true; }
            
        public HashSet<int> GetCorseMarciate()
        { return CorseMarciate; }

        public void AggiungiCorsaUsata(int corsa)
        {
            if (GetifCorsaUsabile(corsa)) //Modifica il parametro solo se la corsa è usabile
            {
                CorseUsate.Add(corsa);
                if (CorseUsate.Count == 1) // Se la corsa usata è la prima, la aggiungiamo anche alle corse marciate
                {
                    AggiungiCorsaMarciata(corsa);
                }
            }
        }

        public bool GetIfCorsaAttualeMarciata()
        { return (CorseMarciate.Contains(CorsaAttuale)); }

        public void PulisciTutto()
        {
            CorseMarciate.Clear();
            CorseUsate.Clear();
        }

        public void ImpostaCorsaAttuale(int? corsa = null, bool limited = true) //Se limitato valgono sole le prime 14 corse
        {
            if (corsa == null || !GetIfUsabilexAttuale(corsa.Value, !limited))  // Corsa non viene passato imposto ultima corsa marciata
            { CorsaAttuale = UltimaCorsaMarciataValida(limited); }
            //else if (TipoCorsa(corsa.Value) == TipoCorsaEnum.NoCourse) // Se NoCourse imposto la corsa iniziale
            //{ CorsaAttuale = CorsaIniziale; }
            //else if (!GetIfUsabilexAttuale(corsa.Value, !limited)) // Se fuori dai limiti seleziona l'ultima marciata
            //{
            //    //int corsaultmmarc = GetUltimaCorsaMarciata();
            //    CorsaAttuale = UltimaCorsaMarciataValida(limited);
           // }
            else if (GetIfCorsaUsata(corsa.Value) || (corsa.Value == GetUltimaCorsaUsata() + 1)) // Se la corsa è già usata o è la prossima da usare, la imposto come attuale
            { CorsaAttuale = corsa.Value; }
            //altrimenti non faccio nulla e mantengo la corsa attuale
        }

        private int UltimaCorsaMarciataValida(bool limited = true)
        {
            int corsaultmmarc = GetUltimaCorsaMarciata();
            if (!GetIfUsabilexAttuale(corsaultmmarc, !limited))
                return CorsaIniziale;
            else
                return corsaultmmarc;
        }

        public int? GetCorsaSuccessivaMarciata(int corsaAttuale)
        {
            if (!CorseMarciate.Any())
                return null;

            var corseMaggiori = CorseMarciate.Where(c => c > corsaAttuale);

            if (!corseMaggiori.Any())
                return null;

            return corseMaggiori.Min();
        }

        public int? GetProssimaCorsaDaMarciare()
        {
            if (!CorseUsate.Any())
                return null;
            var prossimecorsedamarciare = CorseUsate.Where(c => c > GetUltimaCorsaMarciata());
            if (!prossimecorsedamarciare.Any())
                return null; 
            return prossimecorsedamarciare.Min();
        }

        public int GetUltimaCorsaMarciata()
        {
            if (!CorseMarciate.Any())
                return 0;
           return CorseMarciate.Where(c => GetIfCorsaIterabile(c)).Max();
        }

        public bool GetNessunaMarciata()
        { return !CorseMarciate.Any(); }

        public int GetUltimaCorsaUsata()
        {
            if (!CorseUsate.Any())
                return 0;
            var corseIterabili = CorseUsate.Where(c => GetIfCorsaIterabile(c));
            if (!corseIterabili.Any())
                return 0;
            else
                return corseIterabili.Max();
        }

        public int GetPrimaCorsaUsata()
        {
            if (!CorseUsate.Any())
                return 0;
            return CorseUsate.Min();
        }
        public bool GetIfTutteCorseMarciate()
        { return CorseUsate.IsSubsetOf(CorseMarciate); }

        public bool GetIfCorsaMarciata(int corsa)
        { return CorseMarciate.Contains(corsa); }

        public bool GetIfImmediata(int corsa)
        {

            return (CorseMarciate.Contains(corsa) || (GetifCorsaMarciabile(corsa)));
        }

        public bool GetIfCorsaUsata(int corsa)
        { return CorseUsate.Contains(corsa); }

        public int? GetCorsaSuccessivaUsata(int corsaAttuale)
        {
            if (!CorseUsate.Any())
                return null;

            var corseMaggiori = CorseUsate.Where(c => c > corsaAttuale);

            if (!corseMaggiori.Any())
                return null;

            return corseMaggiori.Min();
        }

        public void ImpostaCorsaSuccessivaSelezionabile()
        {
            List<int> numeri = Enumerable.Range(1, CorsaLimite).ToList();
            var corseIterabili = new List<int>();
            int ultimaCorsaMarciata = GetUltimaCorsaMarciata();
            if (CorseUsate.Any())
            {
                corseIterabili = numeri.Where(c => c > CorsaAttuale && c >= ultimaCorsaMarciata && c <= GetUltimaCorsaUsata() + 1 && c <= CorsaLimite).ToList();

                if (corseIterabili.Any())
                    CorsaAttuale = corseIterabili[0];
                else
                    CorsaAttuale = ultimaCorsaMarciata;
            }
        }
    }*/

    public interface ICorseRepository
    {
        Task<List<NomiCorse>> OttieniNomiCorseAsync();
    }

    public class CorseRepository : ICorseRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        // Qui incollerai per intero la tua query MySQL (con WITH RECURSIVE)
        private readonly string _queryMySql = @"
            WITH RECURSIVE albero AS
            (
                SELECT hs.HierStrucID, hs.HierUnitID, 0 AS Livello
                FROM datastore.hierarchy_structure hs
                WHERE hs.ParentHierStrucID IS NULL

                UNION ALL

                SELECT hs.HierStrucID, hs.HierUnitID, a.Livello + 1 AS Livello
                FROM datastore.hierarchy_structure hs
                INNER JOIN albero a 
                    ON hs.ParentHierStrucID = a.HierStrucID
            ),
            Livelli AS
            (
                SELECT 
                    a.HierStrucID,
                    nam.StringText AS Nome,
                    MAX(a.Livello) OVER (PARTITION BY a.HierUnitID) AS Livello
                FROM albero a
                INNER JOIN datastore.hierarchy_unit hu 
                    ON a.HierUnitID = hu.HierUnitID
                INNER JOIN datastore.string_table nam 
                    ON hu.NameID = nam.StringNumberID
            ),
            Course AS
            (
                SELECT cou.ObjectNumber CorsaNum,
                       nam.StringText CorsaNome,
                       lvl.HierStrucID,
                       lvl.Nome,
                       lvl.Livello
                FROM datastore.dining_course cou
                INNER JOIN datastore.string_table nam ON cou.NameID=nam.StringNumberID
                LEFT JOIN Livelli lvl 
                    ON cou.HierStrucID = lvl.HierStrucID
            ),
            CuorsePriceMax AS
            (
                SELECT *,
                       ROW_NUMBER() OVER
                       (
                           PARTITION BY CorsaNum
                           ORDER BY Livello DESC
                       ) AS rn
                FROM Course
            )
            SELECT 0 as CorsaNum, 'No Corsa' as CorsaNome
            UNION ALL
            SELECT CorsaNum, CorsaNome
            FROM CuorsePriceMax
            WHERE rn=1";

        // Qui incollerai per intero la tua query SQL Server (senza RECURSIVE)
        private readonly string _querySqlServer = @"
            WITH albero AS
            (
                SELECT hs.HierStrucID, hs.HierUnitID, 0 AS Livello
                FROM datastore.dbo.hierarchy_structure hs
                WHERE hs.ParentHierStrucID IS NULL
                UNION ALL
                SELECT hs.HierStrucID, hs.HierUnitID, a.Livello + 1 AS Livello
                FROM datastore.dbo.hierarchy_structure hs
                INNER JOIN albero a ON hs.ParentHierStrucID = a.HierStrucID
            ),
            Livelli AS (
                SELECT a.HierStrucID, nam.StringText AS Nome,
                       MAX(a.Livello) OVER (PARTITION BY a.HierUnitID) AS Livello
                FROM albero a
                INNER JOIN datastore.dbo.hierarchy_unit hu ON a.HierUnitID = hu.HierUnitID
                INNER JOIN datastore.dbo.string_table nam ON hu.NameID = nam.StringNumberID
            ),
            Course AS (
                SELECT cou.ObjectNumber CorsaNum, nam.StringText CorsaNome,
                       lvl.HierStrucID, lvl.Nome, lvl.Livello
                FROM datastore.dbo.dining_course cou
                INNER JOIN datastore.dbo.string_table nam ON cou.NameID=nam.StringNumberID
                LEFT JOIN Livelli lvl ON cou.HierStrucID = lvl.HierStrucID
            ),
            CuorsePriceMax AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY CorsaNum ORDER BY Livello DESC) AS rn
                FROM Course
            )
            SELECT 0 as CorsaNum, 'No Corsa' as CorsaNome
            UNION ALL
            SELECT CorsaNum, CorsaNome FROM CuorsePriceMax WHERE rn=1";

        public CorseRepository(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // Firma corretta che rispetta l'interfaccia!
        public async Task<List<NomiCorse>> OttieniNomiCorseAsync()
        {
            var risultati = new List<NomiCorse>();

            // La factory ci dice che DB stiamo usando, scegliamo la query corretta
            string query = _dbFactory.TipoDB == 2 ? _queryMySql : _querySqlServer;

            // Usiamo DbConnection per avere accesso ai metodi asincroni
            using (DbConnection connection = _dbFactory.CreateConnection())
            {
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = query;

                    // Ora possiamo usare ExecuteReaderAsync() senza problemi
                    using (DbDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            risultati.Add(new NomiCorse
                            {
                                CorsaNum = Convert.ToInt32(reader["CorsaNum"]),
                                CorsaNome = reader["CorsaNome"] != DBNull.Value ? reader["CorsaNome"].ToString() : null
                            });
                        }
                    }
                }
            }
            return risultati;
        }
    }

    public static class EscPos
    {
        // --- STILE TESTO ---
        public static readonly byte[] TextNormal = { 0x1D, 0x21, 0x00 };
        public static readonly byte[] TextDoubleSize = { 0x1D, 0x21, 0x11 };
        public static readonly byte[] TextDoubleHeight = { 0x1D, 0x21, 0x01 };
        public static readonly byte[] TextDoubleWidth = { 0x1D, 0x21, 0x10 };
        public static readonly byte[] TextTripleSize = { 0x1D, 0x21, 0x22 };

        public static readonly byte[] AlignLeft = { 0x1B, 0x61, 0x00 };
        public static readonly byte[] AlignCenter = { 0x1B, 0x61, 0x01 };
        public static readonly byte[] AlignRight = { 0x1B, 0x61, 0x02 };

        public static readonly byte[] NoUnderline = { 0x1B, 0x2D, 0x00 };
        public static readonly byte[] Underline1 = { 0x1B, 0x2D, 0x01 };
        public static readonly byte[] Underline2 = { 0x1B, 0x2D, 0x02 };

        public static readonly byte[] NoColorInv = { 0x1D, 0x42, 0x00 };
        public static readonly byte[] ColorInv = { 0x1D, 0x42, 0x01 };

        public static readonly byte[] TextBoldOff = { 0x1B, 0x45, 0x00 };
        public static readonly byte[] TextBoldOn = { 0x1B, 0x45, 0x01 };

        // ----- TABULAZIONI ------
        public static readonly byte[] Tab = { 0x09 };

        public static byte[] SetTabs(params byte[] columns)
        {
            var command = new List<byte> { 0x1B, 0x44 }; // ESC D
            command.AddRange(columns);                   // Aggiunge le colonne (es. 10, 25)
            command.Add(0x00);                           // NUL - Termina il comando
            return command.ToArray();
        }

        // --- AZIONI ---
        public static readonly byte[] Initialize = { 0x1B, 0x40 }; // Resetta la stampante (ESC @)
        public static readonly byte[] impostaCodePage850 = { 27, 116, 2 }; // Imposta il CodePage a 850
        public static readonly byte[] PartCutPaper = { 0x1D, 0x56, 0x31 }; // Taglio parziale
        public static readonly byte[] LineFeed = { 0x1B, 0x64, 1 };
        public static readonly byte[] LineFeed2 = { 0x1B, 0x64, 2 };
        public static readonly byte[] LineFeed3 = { 0x1B, 0x64, 3 };
        // --- LOGHI ---
        public static readonly byte[] PrintLogo1 = { 0x1C, 0x70, 0x01, 0x00 }; // Stampa immagine NV #1

        // --- METODI HELPER ---
        public static byte[] GetFeedLines(byte lines)
        {
            return new byte[] { 0x1B, 0x64, lines };
        }
    }

    public class PrinterStatus
    {
        public bool IsOnline { get; set; }
        public bool HasPaper { get; set; }
        public bool IsCoverOpen { get; set; }
        public bool IsReady => IsOnline && HasPaper && !IsCoverOpen;
    }

    public class MenuRoot
    {
        [JsonProperty("Menu")]
        public List<MenuTemplate> ListaMenu { get; set; }
    }

    public class MenuTemplate
    {
        [JsonProperty("MenuName")]
        public string MenuName { get; set; } = string.Empty;

        [JsonProperty("MenuID")]
        public int MenuID { get; set; } = 0;

        [JsonProperty("Course1")]
        public List<int> Course1 { get; set; } = new List<int>();

        [JsonProperty("Course2")]
        public List<int> Course2 { get; set; } = new List<int>();

        [JsonProperty("Course3")]
        public List<int> Course3 { get; set; } = new List<int>();

        [JsonProperty("Course4")]
        public List<int> Course4 { get; set; } = new List<int>();

        [JsonProperty("Course5")]
        public List<int> Course5 { get; set; } = new List<int>();

        [JsonProperty("Course6")]
        public List<int> Course6 { get; set; } = new List<int>();

        [JsonProperty("Course7")]
        public List<int> Course7 { get; set; } = new List<int>();

        [JsonProperty("Course8")]
        public List<int> Course8 { get; set; } = new List<int>();

        [JsonProperty("Course9")]
        public List<int> Course9 { get; set; } = new List<int>();

        [JsonProperty("Course10")]
        public List<int> Course10 { get; set; } = new List<int>();

        [JsonProperty("Course11")]
        public List<int> Course11 { get; set; } = new List<int>();

        [JsonProperty("Course12")]
        public List<int> Course12 { get; set; } = new List<int>();

        [JsonProperty("Course13")]
        public List<int> Course13 { get; set; } = new List<int>();

        [JsonProperty("Course14")]
        public List<int> Course14 { get; set; } = new List<int>();

        [JsonProperty("Course15")]
        public List<int> Course15 { get; set; } = new List<int>();

        [JsonProperty("Course16")]
        public List<int> Course16 { get; set; } = new List<int>();

        [JsonProperty("Course17")]
        public List<int> Course17 { get; set; } = new List<int>();

        [JsonProperty("Course18")]
        public List<int> Course18 { get; set; } = new List<int>();

        [JsonProperty("Course19")]
        public List<int> Course19 { get; set; } = new List<int>();

        [JsonProperty("Course20")]
        public List<int> Course20 { get; set; } = new List<int>();

        [JsonProperty("PreDessert")]
        public List<int> PreDessert { get; set; } = new List<int>();

        [JsonProperty("Dessert")]
        public List<int> Dessert { get; set; } = new List<int>();

        [JsonProperty("Petit-Four")]
        public List<int> PetitFour { get; set; } = new List<int>();

        [JsonIgnore]
        public Dictionary<string, List<int>> AllCourses
        {
            get
            {
                var courses = new Dictionary<string, List<int>>
            {
                { "Course1", Course1 },
                { "Course2", Course2 },
                { "Course3", Course3 },
                { "Course4", Course4 },
                { "Course5", Course5 },
                { "Course6", Course6 },
                { "Course7", Course7 },
                { "Course8", Course8 },
                { "Course9", Course9 },
                { "Course10", Course10 },
                { "Course11", Course11 },
                { "Course12", Course12 },
                { "Course13", Course13 },
                { "Course14", Course14 },
                { "Course15", Course15 },
                { "Course16", Course16 },
                { "Course17", Course17 },
                { "Course18", Course18 },
                { "Course19", Course19 },
                { "Course20", Course20 },
                { "PreDessert", PreDessert },
                { "Dessert", Dessert },
                { "PetitFour", PetitFour }
            };

                // Filtra solo le portate che hanno effettivamente dei piatti inseriti
                return courses.Where(c => c.Value != null && c.Value.Any())
                              .ToDictionary(c => c.Key, c => c.Value);
            }
        }
    }
   
    public class Application : OpsExtensibilityApplication
    {
        private string LeggiLaMiaVariabile(string chiaveDaCercare)
        {
            if (OpsContext.Check == null) return null;

            var risultati = OpsContext.Check.ExtensibilityDetail.Find(this.ApplicationName, chiaveDaCercare);

            if (risultati != null && risultati.Count > 0)
            {
                try
                {
                    dynamic miaVariabile = risultati[0];
                    return miaVariabile.StringData;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }
        private MyConfig _config; 
        private List<MenuTemplate> _listaMenu;
        private bool _coursemng_enable = true;
        private bool _extensionEnbled = true;
        private int _tipoDB = 0;
        private bool _menu_enable;
        private int _actual_Rvc;
        private OrderDeviceCache _device;
        private string _connStringMySql = "Server=127.0.0.1;Database=datastore;";
        private string _connStringSqlServer = "Server=LocalHost\\SQlExpress;Database=datastore;";
        private string _account = "Uid=chkrdy;Pwd=chkrdy1$;";
        private string _connString = "";
        private GestioneCorse2 _gestioneCorse;
        private IDbConnectionFactory _dbFactory;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _processedPrints =
                new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();
        private Encoding _codificaStampante;

        [ImportingConstructor]
        public Application(IExecutionContext context)
             : base(context)
        {

            this.OpsSignInEvent += GesioneSignIn;
            this.OpsBeginCheckEvent += GestisciNewCheck;
            this.OpsPickUpCheckEvent += GestisciPickupCheck;
            this.OpsMiPreviewEvent += GestisciPreviewMI;
            this.OpsMiEvent += GestisciAggiuntaArticolo;
            this.OpsItemSelectedEvent += ItemSelected;
            this.OpsSelectedItemCompleteQueryEvent += SelectedItemCompleteQuery;
            this.OpsCustomOrderDeviceEventArgs += GestOpsCustomOrderDeviceEventArgs;
            this.OpsPrinterDataEvent += GestPrinterDataEvent;
            this.OpsFinalTenderEvent += GestFinalTenderEvent;
            this.OpsInitEvent += GestInitEvent;
            
        }

        private EventProcessingInstruction GestInitEvent(object sender, OpsInitEventArgs args)
        {
            myLog.Info("Estensione Simphony avviata con successo.");

            //var dataStore = this.DataStore;

            if (_config is null)
            {
                string json = DataStore.ReadExtensionApplicationContentTextByNameKey(OpsContext.RvcID, ApplicationName, "Config");
                _config = JsonConvert.DeserializeObject<MyConfig>(json);
                myLog.UpdateVerbosity(_config.VerbosityLog);
            }

            long codreq = TestWork.LeggiInt(_config.RTT_ID.ToString());

            _codificaStampante = Encoding.GetEncoding(_config.CodificaStampante);

            if (OpsContext.PropHierStrucID != codreq)
            {
                OpsContext.ShowMessage(messageSow("Codice non valido, extension disabilitata"));
                myLog.Warn("Codice non valido, extension disabilitata");
                _menu_enable = false;
                _coursemng_enable = false;
                _extensionEnbled = false;
            }
            else
                _extensionEnbled = true;

            _actual_Rvc = OpsContext.RvcID;

            if (_config.VerbosityDisplay > 0)
            {
                OpsContext.ShowMessage(messageSow("OpsInitEvent"));
                OpsContext.ShowMessage(messageSow(string.Format("Versione Assemby {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString())));
            }
            myLog.Debug("OpsInitEvent");
            myLog.Debug(string.Format("Versione Assemby {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

#pragma warning disable 0618
            var dataStoreold = OpsContext.DataStore;
#pragma warning restore 0618           
            
            var dbSettingsField = dataStoreold.GetType().GetField("_dbSettings", BindingFlags.NonPublic | BindingFlags.Instance);

            if (dbSettingsField != null)
            {
                // Otteniamo l'istanza reale di SimphonyUtilities.Settings.DatabaseSettings
                var dbSettingsObj = dbSettingsField.GetValue(dataStoreold);

                if (dbSettingsObj != null)
                {
                    // 3. Recupero della proprietà DatabaseType (potrebbe essere pubblica, ma per sicurezza cerchiamo ovunque)
                    var dbTypeProp = dbSettingsObj.GetType().GetProperty("DatabaseType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (dbTypeProp != null)
                    {
                        // Otteniamo il valore vero e proprio
                        var databaseType = dbTypeProp.GetValue(dbSettingsObj);

                        // Dato che DatabaseType è un Enum, spesso è comodo farne il cast a int per i controlli.
                        // Come si nota nel sorgente decompilato: (int)_dbSettings.DatabaseType != 1
                        _tipoDB = (int)databaseType;

                        // Esempio di utilizzo:
                        if (_tipoDB == 0)
                        {
                            _connString = string.Format("{0}{1}", _connStringSqlServer, _account);
                        }
                        
                        else if (_tipoDB == 2) 
                        {
                            // Logica MS SQL
                            _connString = string.Format("{0}{1}", _connStringMySql, _account);
                        }
                        else //if (_tipoDB == 1)
                        {
                            // Errore Oracle non supportato
                            OpsContext.ShowMessage(messageSow("Database non supportato Extension disabilitata "));
                            myLog.Error("30F2188A - Database non supportato Extension disabilitata ");
                            _extensionEnbled = false;
                        }
                    }
                }
            }
            if (_extensionEnbled)
            {
                _dbFactory = new DbConnectionFactory(_tipoDB, _connString);
            }
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GestFinalTenderEvent(object sender, OpsTmedEventArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("OpsFinalTenderEvent"));
            myLog.Debug("OpsFinalTenderEvent");
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GestPrinterDataEvent(object sender, OpsPrinterDataArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("OpsPrinterDataEvent"));
            myLog.Debug("OpsPrinterDataEvent");
            return EventProcessingInstruction.Continue;
        }

        private DatiComanda PreparaDatiComanda()
        {
            DatiComanda dati = new DatiComanda();
            dati.CheckNumber = OpsContext.CheckNumber.ToString();
            dati.CheckID = OpsContext.CheckName;
            dati.Tavolo = OpsContext.CheckTableName;
            dati.Utente = OpsContext.TransEmployeeCheckName;
            dati.Rvc_name = OpsContext.RvcName;
            //dati.MarciaCorrente = marciacorr;
            return dati;
        }

        private List<MenuItemPrint> PreparaListaArticoliComanda(OpsCustomOrderDeviceEventArgs args, List<NomiCorse> corse)
        {
            //_gestioneCorse.VerificaPrimaCorsaMarciata();  //Verifica che ci sia almeno una corsa marciata, se non c'è la imposta alla prima corsa usata
            _gestioneCorse.Stampata(); // Imposta la variabile InviataStampa a true, in modo da sapere se è già stata stampata almeno una volta
            List<MenuItemPrint> ListaArticoli = new List<MenuItemPrint> { };
            try
            {
                foreach (var item in args.Detail)
                {
                    string nomeArt = "";
                    string quant = "";
                    string price = "";
                    string corsaart = "";
                    int corsanum = 0;
                    string referenceArt = "";
                    string seatart;
                    string pesoart = "";
                    long miobjnum = 0;
                    List<CondimentPrint> ListaCondiment = new List<CondimentPrint> { };

                    miobjnum = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).MiObjNum;
                    nomeArt = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).Name.ToString();
                    corsanum = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).KdsCourseNum;
                    corsaart = corse[corsanum].CorsaNome;
                    quant = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).SalesCount.ToString();
                    price = (((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).Total / ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).SalesCount).ToString();
                    seatart = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).Seat.ToString();
                    if (((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).WeighedItem)
                        pesoart = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).NetWeight.ToString("0.000", new CultureInfo("it-IT"));
                    if (item.DetailItem.ReferenceEntries.Count > 0) referenceArt = item.DetailItem.ReferenceEntries[0].Descriptor;

                    var conds = item.DetailItem;
                    var listcond = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)conds).Condiments;

                    foreach (var cond in listcond)
                    {
                        string nomeCond = "";
                        string quantCond = "";
                        string priceCond = "";
                        string referenceCond = "";
                        string pesocond = "";

                        nomeCond = cond.ExpandedName;
                        quantCond = cond.SalesCount.ToString();
                        if (cond.ReferenceEntries.Count > 0)
                            referenceCond = cond.ReferenceEntries[0].Descriptor;
                        priceCond = (cond.Total / (cond.SalesCount)).ToString();

                        CondimentPrint Condiments = new CondimentPrint { nome = nomeCond, prezzo = priceCond, quant = quantCond, reference = referenceCond, peso = pesocond };
                        ListaCondiment.Add(Condiments);
                    }

                    bool newart =true;
                    if (ListaCondiment == null || ListaCondiment.Count == 0)
                    { 
                        MenuItemPrint risultato = ListaArticoli.FirstOrDefault(artic => artic.miobjnum == miobjnum && (artic.numcorsa == corsanum) && (artic.condiments == null || artic.condiments.Count == 0));
                        if (risultato != null)
                        {
                            risultato.quant = (int.Parse(risultato.quant) + int.Parse(quant)).ToString();
                            newart = false;
                        }
                    }
                    if (newart)
                    {
                        MenuItemPrint Articolo = new MenuItemPrint
                        { quant = quant, nome = nomeArt, prezzo = price, corsa = corsaart, condiments = ListaCondiment, numcorsa = corsanum, reference = referenceArt, peso = pesoart, seat = seatart, miobjnum = miobjnum };
                        ListaArticoli.Add(Articolo);
                    }
                }
            }
            catch (Exception ex)
            {
                {
                    OpsContext.ShowMessage(messageSow("Errore Preparazione Articoli Comanda"));
                    OpsContext.ShowMessage(messageSow(ex.Message));
                    myLog.Error("551BA846 - Errore Preparazione Articoli Comanda", ex);
                }
            }
            return ListaArticoli;
        }

        private DatiComanda PreparaDatiComadaTest()
        {
            DatiComanda dati = new DatiComanda();
            dati.CheckNumber = "00900";
            dati.CheckID = "check di test";
            dati.Tavolo = "100";
            dati.Utente = "Teststampa";
            dati.Rvc_name = "Ristorante Test";
            dati.MarciaCorrente = 1;

            return dati;
        }

        private List<MenuItemPrint> PreparaListaArticoliComandaTest(List<NomiCorse> corse)
        {
            List<MenuItemPrint> ListaArticoli = new List<MenuItemPrint> { };
            try
            {
                
                string nomeArt = "";
                string quant = "";
                string price = "";
                string corsaart = "";
                int corsanum = 0;
                string referenceArt = "";
                string seatart;
                string pesoart = "";

                // asticolo 1
                nomeArt = "Articolo 1";
                corsanum = 1;
                corsaart = corse[corsanum - 1].CorsaNome;
                quant = "1";
                price = "11.00";
                seatart = "1";
                referenceArt = "refer Articolo 1";
                pesoart = "1.100";

                //var conds = item.DetailItem;
                //var listcond = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)conds).Condiments;


                string nomeCond = "";
                string quantCond = "";
                string priceCond = "";
                string referenceCond = "";
                string pesocond = "";

                nomeCond = "Condimento 1 di 1";
                quantCond = "1";
                referenceCond = "refer cond 1 di 1";
                priceCond = "1.0";

                CondimentPrint Condiments = new CondimentPrint { nome = nomeCond, prezzo = priceCond, quant = quantCond, reference = referenceCond, peso = pesocond };
                List<CondimentPrint> ListaCondiment = new List<CondimentPrint> { };
                ListaCondiment.Add(Condiments);

                MenuItemPrint Articolo = new MenuItemPrint
                { quant = quant, nome = nomeArt, prezzo = price, corsa = corsaart, condiments = ListaCondiment, numcorsa = corsanum, reference = referenceArt, peso = pesoart, seat = seatart };
                ListaArticoli.Add(Articolo);

                // asticolo 2
                nomeArt = "Articolo 2";
                corsanum = 2;
                corsaart = corse[corsanum - 1].CorsaNome;
                quant = "1";
                price = "12.00";
                seatart = "1";
                referenceArt = "refer Articolo 2";
                pesoart = "";
                //var conds = item.DetailItem;
                //var listcond = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)conds).Condiments;


                nomeCond = "Condimento 1 di 2";
                quantCond = "1";
                referenceCond = "refer cond 1 di 2";
                priceCond = "2.0";

                Condiments = new CondimentPrint { nome = nomeCond, prezzo = priceCond, quant = quantCond, reference = referenceCond, peso = pesocond };
                ListaCondiment = new List<CondimentPrint> { };
                ListaCondiment.Add(Condiments);

                Articolo = new MenuItemPrint
                { quant = quant, nome = nomeArt, prezzo = price, corsa = corsaart, condiments = ListaCondiment, numcorsa = corsanum, reference = referenceArt, peso = pesoart, seat = seatart };
                ListaArticoli.Add(Articolo);
            }
            catch (Exception ex)
            {
                {
                    OpsContext.ShowMessage(messageSow("Errore Preparazione Articoli Comanda"));
                    OpsContext.ShowMessage(messageSow(ex.Message));
                    myLog.Error("551BA846 - Errore Preparazione Articoli Comanda", ex);
                }
            }
            return ListaArticoli;
        }

        private string messageSow(string origMessage)
        {
            return $"CourseMng:\r\n{origMessage}";
        }

        private EventProcessingInstruction GestOpsCustomOrderDeviceEventArgs(object sender, OpsCustomOrderDeviceEventArgs args)
        {

            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("OpsCustomOrderDeviceEventArgs"));
            myLog.Debug("OpsCustomOrderDeviceEventArgs");
            if (!_extensionEnbled || !_coursemng_enable)
            {
                myLog.Warn("E56B63C0 - Estensione Non abilitata");
                return EventProcessingInstruction.Continue;
            }
            var fingerprintLines = args.Detail
                .Where(d => d != null && !string.IsNullOrEmpty(d.MiName))
                .Select(d => d.MiName.Trim())
                .Take(5);
            string printContent = string.Join("|", fingerprintLines);
            string cacheKey = $"{args.OrderDeviceIndex}_{printContent.GetHashCode()}";
            CleanUpCache();
            if (_processedPrints.TryGetValue(cacheKey, out DateTime lastProcessed))
            {
                // Se la stessa comanda è passata negli ultimi 3 secondi, è sicuramente 
                // l'evento di backup sollevato dal Print Controller. Interrompiamo l'esecuzione.
                if ((DateTime.Now - lastProcessed).TotalSeconds < 3)
                {
                    return EventProcessingInstruction.Continue;
                }
            }
            _processedPrints[cacheKey] = DateTime.Now;

            List<MenuItemPrint> ListaArticoli = new List<MenuItemPrint> { };
            //List<NomiCorse> corse = Task.Run(() => LeggiNomiCorse()).GetAwaiter().GetResult();
            List<NomiCorse> corse = Task.Run(() => LeggiNomiCorse()).GetAwaiter().GetResult();

            //DatiComanda datiComanda = PreparaDatiComanda(_CorseMarciate.Max());
            DatiComanda datiComanda = PreparaDatiComanda();

            List<byte> payload = new List<byte>();
            byte[] data = payload.ToArray();

            if (args.CustomName.Substring(0, 2) == "OD")
            {
                ListaArticoli = PreparaListaArticoliComanda(args, corse);
                if (args.CustomName == "OD_100_00")
                {
                    payload = Comanda_OD_100_00(args.OrderDeviceIndex, datiComanda, ListaArticoli);
                    OrderDeviceCache.OrderDeviceInfo device = _device.GetDevice(args.OrderDeviceIndex);
                    stampaComanda(payload.ToArray(), device);
                }
            }

            if (args.CustomName.Substring(0, 2) == "MA")
            {
                int corsaatt = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)args.Detail[0].DetailItem).KdsCourseNum;
                ListaArticoli = PreparaListaArticoliMarcia(args, corsaatt);

                if (args.CustomName == "MA_100_00")
                {
                    List<List<MenuItemPrint>> listaDiListe = ListaArticoli
                    .GroupBy(item => item.ODIndex)
                    .Select(gruppo => gruppo.ToList())
                    .ToList();

                    foreach (List<MenuItemPrint> sottoLista in listaDiListe)
                    {
                        // L'ODIndex sarà uguale per tutti gli elementi di questa sottoLista
                        int indiceCorrente = sottoLista.First().ODIndex;

                        OrderDeviceCache.OrderDeviceInfo device = _device.GetDevice(indiceCorrente);

                        if (device.TypeOD != "M")
                        {
                            payload = Comanda_MA_100_00(indiceCorrente, datiComanda, sottoLista);
                            stampaComanda(payload.ToArray(), device);
                        }
                    }
            }
        }
        return EventProcessingInstruction.Continue;

     }

        private void stampaComanda(byte[] data, OrderDeviceCache.OrderDeviceInfo device, bool test = false)
        {
            bool primoTent = false;
            if (device.IPBackup != null) primoTent = true;
            if (TestConnessioneStampante(device.IPPrinter, device.PortaPrinter, test, primoTent))
            {
                /*using (TcpClient client = new TcpClient())
                {

                    try
                    {

                        client.Connect(device.IPPrinter, device.PortaPrinter);


                        using (NetworkStream stream = client.GetStream())
                        {
                            //OpsContext.ShowMessage(data.ToString());
                            stream.Write(data, 0, data.Length);
                            stream.Flush();

                        }
                        if (test)
                        {
                            Console.WriteLine($"Comanda Stampata");
                            myLog.Debug("Comanda Stampata");
                        }
                    }
                    catch (SocketException ex)
                    {
                        // Gestione degli errori di rete (es. stampante spenta o IP errato)
                        Console.WriteLine($"Errore di rete durante la connessione alla stampante: {device.IPPrinter}:{device.PortaPrinter}");
                        myLog.Error($"9E11D85E - Errore stampante: {device.IPPrinter}:{device.PortaPrinter}", ex);
                    }
                    catch (Exception ex)
                    {
                        // Gestione degli errori generici
                        Console.WriteLine($"Errore stampa");
                        myLog.Error($"C2DF02C8 - Errore stampa: {device.IPPrinter}:{device.PortaPrinter}", ex);
                    }
                }*/
                stampaScontrino(data, device.IPPrinter, device.PortaPrinter, test);
            }
            else
            {
                if (TestConnessioneStampante(device.IPBackup, device.PortaBackup, test))
                {
                    stampaScontrino(data, device.IPBackup, device.PortaBackup, test);
                }
            }
        }

        private void stampaScontrino(byte[] data, string IPPrinter, int PortaPrinter, bool test)
        {
            using (TcpClient client = new TcpClient())
            {

                try
                {

                    client.Connect(IPPrinter, PortaPrinter);


                    using (NetworkStream stream = client.GetStream())
                    {
                        //OpsContext.ShowMessage(data.ToString());
                        stream.Write(data, 0, data.Length);
                        stream.Flush();

                    }
                    if (test)
                    {
                        Console.WriteLine($"Comanda Stampata");
                        myLog.Debug("Comanda Stampata");
                    }
                }
                catch (SocketException ex)
                {
                    // Gestione degli errori di rete (es. stampante spenta o IP errato)
                    Console.WriteLine($"Errore di rete durante la connessione alla stampante: {IPPrinter}:{PortaPrinter}");
                    myLog.Error($"9E11D85E - Errore stampante: {IPPrinter}:{PortaPrinter}", ex);
                }
                catch (Exception ex)
                {
                    // Gestione degli errori generici
                    Console.WriteLine($"Errore stampa");
                    myLog.Error($"C2DF02C8 - Errore stampa: {IPPrinter}:{PortaPrinter}", ex);
                }
            }
        }

        public bool TestConnessioneStampante(string ip, int porta, bool test = false, bool primotent = false, int timeoutMillisecondi = 3000)
        {
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    // Avvia la richiesta di connessione in modo asincrono
                    Task connectTask = client.ConnectAsync(ip, porta);

                    // Wait() attende il completamento del task. 
                    // Restituisce false se scade il timeout prima che la connessione avvenga.
                    bool completato = connectTask.Wait(timeoutMillisecondi);

                    if (!completato)
                    {
                        // Il timeout è scaduto: la stampante è lenta, irraggiungibile o spenta
                        if (!primotent)
                        {
                            OpsContext.ShowMessage($"Errore di rete durante la connessione alla stampante: {ip}:{porta}");
                            myLog.Error($"F2AA9C53 - Errore stampante: {ip}:{porta}");
                        }
                        else
                        {
                            myLog.Info("Stampante primaria non disponibile, invio backup");
                        }
                        return false;
                    }
                    else
                    {
                        if (test) OpsContext.ShowMessage($"Connessione stampante: {ip}:{porta} OK");
                    }
                    // Se arriviamo qui, il task è completato. Verifichiamo se siamo connessi.
                    return client.Connected;
                }
                catch (AggregateException ex)
                {
                    // ConnectAsync genera un'AggregateException (che contiene una SocketException) 
                    // se la connessione viene esplicitamente rifiutata (es. porta sbagliata)
                    //Console.WriteLine($"Errore di rete durante la connessione alla stampante: {ip}:{porta}");
                    if (!primotent)
                        myLog.Error($"68CCC233 - Errore stampante: {ip}:{porta}", ex);
                    else
                        myLog.Info("Stampante primaria non disponibile, invio backup");
                    return false;
                }
                catch (Exception ex)
                {
                    if (!primotent)
                        myLog.Error($"2F89FCC5 - Errore impreviso: {ip}:{porta}", ex);
                    else
                        myLog.Info("Stampante primaria non disponibile, invio backup");
                    return false;
                }
            } // Il blocco using chiuderà il TcpClient di test liberando le risorse
        }

        List<byte> Comanda_OD_100_00(int ODIndex, DatiComanda datiComanda, List<MenuItemPrint> listaArticoli)
        {
            List<byte> payload = new List<byte>();

            // Inizializza stampante
            payload.AddRange(EscPos.Initialize);
            payload.AddRange(EscPos.impostaCodePage850);
            payload.AddRange(EscPos.AlignCenter);

            //Header
            payload.AddRange(Header_000(ODIndex, datiComanda));

            // Corpo
            payload.AddRange(Corpo_000(datiComanda, listaArticoli));

            //Footer
            payload.AddRange(Footer_000(datiComanda ));

            // Taglio carta
            payload.AddRange(EscPos.LineFeed3);
            payload.AddRange(EscPos.PartCutPaper);
            //payload.AddRange(EscPos.LineFeed);

            return payload;

        }

        List<byte> Header_000(int ODIndex, DatiComanda datiComanda)
        {
            List<byte> intestazione = new List<byte>();
            string riga = "";

            // Nome OrderDevice -- testo normale --
            intestazione.AddRange(EscPos.TextNormal);
            riga = string.Format("=== {0} ===\r\n\r\n", _device.GetDevice(ODIndex).NomeOrderDevice);
            intestazione.AddRange(_codificaStampante.GetBytes(riga));

            //Revenue Center -- testo doppio --
            intestazione.AddRange(EscPos.TextDoubleSize);
            riga = datiComanda.Rvc_name;
            intestazione.AddRange(_codificaStampante.GetBytes(riga));

            // Tavolo -- testo triplo, grassetto e centrato --
            intestazione.AddRange(EscPos.TextTripleSize);
            intestazione.AddRange(EscPos.TextBoldOn);
            if (datiComanda.Tavolo != "") riga = string.Format("\r\nTav. {0}\r\n", datiComanda.Tavolo);
            else riga = string.Format("\r\nCheck {0} \r\n", datiComanda.CheckNumber);
            intestazione.AddRange(_codificaStampante.GetBytes(riga));

            //ID Conto -- testo doppio
            intestazione.AddRange(EscPos.TextDoubleSize);
            if (datiComanda.CheckID != "")
            {
                riga = string.Format("{0}\r\n", datiComanda.CheckID);
                intestazione.AddRange(_codificaStampante.GetBytes(riga));
            }

            // Riga -- testo doppio centrato --
            intestazione.AddRange(EscPos.TextDoubleSize);
            intestazione.AddRange(EscPos.TextBoldOff);
            riga = string.Format("-------------------\r\n");
            intestazione.AddRange(_codificaStampante.GetBytes(riga));

            return intestazione;
        }

        List<byte> Corpo_000(DatiComanda datiComanda, List<MenuItemPrint> listaArticoli)
        {
            List<byte> corpo = new List<byte>();
            string riga = "";

            var raggruppatiPerCorsa = listaArticoli.GroupBy(m => m.corsa);
            int corsacorr = 0;

            foreach (MenuItemPrint articolo in listaArticoli)
            {

                int numcorsa = articolo.numcorsa;
                if (numcorsa > corsacorr)
                {
                    // Corsa -- testo grande, grassetto e invertito (se la corsa è già marciata),  centrato --
                    corpo.AddRange(EscPos.AlignCenter);

                    if (_gestioneCorse.SeImmediata(numcorsa))
                    {
                        corpo.AddRange(EscPos.TextNormal);
                        corpo.AddRange(EscPos.NoColorInv);
                        riga = string.Format("\r\n{0}\r\n\r\n", articolo.corsa);
                    }
                    else
                    {
                        corpo.AddRange(EscPos.TextDoubleHeight);
                        corpo.AddRange(EscPos.ColorInv);
                        riga = string.Format("\r\nSegue {0}\r\n\r\n", articolo.corsa);
                    }
                    //corpo.AddRange(_codificaStampante.GetBytes(riga));
                    corpo.AddRange(_codificaStampante.GetBytes(riga));
                    corsacorr = numcorsa;
                }

                // Articolo -- testo doppio  -- 
                corpo.AddRange(EscPos.AlignLeft);
                corpo.AddRange(EscPos.TextDoubleSize);
                corpo.AddRange(EscPos.NoColorInv);
                string artprint = articolo.nome;
                string refprint = articolo.reference;
                if (articolo.nome.Length > _config.LunghNomeArticoli) artprint = string.Format("{0}.", articolo.nome.Substring(0, _config.LunghNomeArticoli - 1));
                if (articolo.reference.Length > _config.LunghNomeArticoli) refprint = string.Format("{0}.", articolo.reference.Substring(0, _config.LunghNomeArticoli - 1));
                StringBuilder art = new StringBuilder();
                art.Append(articolo.quant);
                //art.Append(" x ");
                art.Append(" ");
                if ((articolo.nome == "" || articolo.nome.StartsWith("#")) && articolo.reference != "") art.Append(refprint);
                else art.Append(artprint);
                art.Append("\r\n");
                if (articolo.nome != "" && articolo.reference != "")
                {
                    art.Append("  ");
                    art.Append(articolo.reference);
                    art.Append("\r\n");
                }
                if (articolo.peso != null && articolo.peso != "")
                {
                    art.Append("  Peso: ");
                    art.Append(articolo.peso);
                    art.Append("\r\n");
                }
                //payload.AddRange(EscPos.TextDoubleHeight);
                corpo.AddRange(_codificaStampante.GetBytes(art.ToString()));
                foreach (CondimentPrint condimento in articolo.condiments)
                {
                    // Condimento -- testo doppia altezza sottolineato
                    corpo.AddRange(EscPos.AlignLeft);
                    corpo.AddRange(EscPos.TextDoubleHeight);
                    StringBuilder cond = new StringBuilder();
                    cond.Append("     ");
                    //payload.AddRange(EscPos.Underline2);
                    if (condimento.quant != "1")
                    {
                        cond.Append(condimento.quant);
                        //cond.Append(" x "); 
                        cond.Append(" ");
                    }

                    if ((condimento.nome == "" || condimento.nome.StartsWith("#")) && condimento.reference != "") cond.Append(condimento.reference);
                    else cond.Append(condimento.nome);
                    //payload.AddRange(EscPos.NoUnderline);
                    //cond.Append("\r\n");

                    //payload.AddRange(_codificaStampante.GetBytes(cond.ToString()));
                    if (!(condimento.nome == "" || condimento.nome.StartsWith("#")) && condimento.reference != "")
                    {
                        cond.Append("\r\n     ");
                        cond.Append(condimento.reference);
                    }
                    corpo.AddRange(_codificaStampante.GetBytes(cond.ToString()));
                    corpo.AddRange(EscPos.NoUnderline);
                    corpo.AddRange(_codificaStampante.GetBytes("\r\n"));
                }
            }
            return corpo;
        }

        List<byte> Footer_000(DatiComanda datiComanda)
        {
            List<byte> footer = new List<byte>();
            string riga = "";

            footer.AddRange(EscPos.AlignCenter);
            footer.AddRange(EscPos.TextDoubleSize);
            footer.AddRange(EscPos.NoColorInv);
            riga = string.Format("-------------------\r\n");
            footer.AddRange(_codificaStampante.GetBytes(riga));

            footer.AddRange(EscPos.TextNormal);
            footer.AddRange(EscPos.AlignLeft);
            riga = string.Format("{0} -- {1}\r\n\r\n", datiComanda.Utente, DateTime.Now.ToString());
            footer.AddRange(_codificaStampante.GetBytes(riga));

            return footer; 
        }

        List<byte> Marcia_000(List<MenuItemPrint> listaArticoli)
        {
            List<byte> marcia = new List<byte>();
            string riga = "";

            // Marcia -- testo doppio  -- 
            string numcorsa = listaArticoli[0].numcorsa.ToString();
            if (listaArticoli[0].numcorsa > 14)
                numcorsa = NomeAltraCorsa(listaArticoli[0].numcorsa);
            marcia.AddRange(EscPos.AlignCenter);
            marcia.AddRange(EscPos.TextTripleSize);
            marcia.AddRange(EscPos.NoColorInv);
            riga = string.Format(string.Format("\r\n{0} {1}\r\n", _config.Lbl_Marcia, numcorsa));
            marcia.AddRange(_codificaStampante.GetBytes(riga));

            foreach (MenuItemPrint articolo in listaArticoli)
            {

                // Articolo -- testo doppio  -- 
                marcia.AddRange(EscPos.AlignLeft);
                marcia.AddRange(EscPos.TextDoubleSize);
                marcia.AddRange(EscPos.NoColorInv);
                string artprint = articolo.nome;
                string refprint = articolo.reference;
                if (articolo.nome.Length > _config.LunghNomeArticoli) artprint = string.Format("{0}.", articolo.nome.Substring(0, _config.LunghNomeArticoli - 1));
                if (articolo.reference.Length > _config.LunghNomeArticoli) refprint = string.Format("{0}.", articolo.reference.Substring(0, _config.LunghNomeArticoli - 1));
                StringBuilder art = new StringBuilder();
                art.Append(articolo.quant);
                art.Append(" ");
                if ((articolo.nome == "" || articolo.nome.StartsWith("#")) && articolo.reference != "") art.Append(refprint);
                else art.Append(artprint);
                art.Append("\r\n");
                if (articolo.nome != "" && articolo.reference != "")
                {
                    art.Append("  ");
                    art.Append(articolo.reference);
                    art.Append("\r\n");
                }
                marcia.AddRange(_codificaStampante.GetBytes(art.ToString()));
                foreach (CondimentPrint condimento in articolo.condiments)
                {
                    // Condimento -- testo doppia altezza sottolineato
                    marcia.AddRange(EscPos.AlignLeft);
                    marcia.AddRange(EscPos.TextDoubleHeight);
                    StringBuilder cond = new StringBuilder();
                    cond.Append("     ");
                    if (condimento.quant != "1")
                    {
                        cond.Append(condimento.quant);
                        cond.Append(" ");
                    }

                    if ((condimento.nome == "" || condimento.nome.StartsWith("#")) && condimento.reference != "") cond.Append(condimento.reference);
                    else cond.Append(condimento.nome);

                    if (!(condimento.nome == "" || condimento.nome.StartsWith("#")) && condimento.reference != "")
                    {
                        cond.Append("     ");
                        cond.Append(condimento.reference);
                    }
                    marcia.AddRange(_codificaStampante.GetBytes(cond.ToString()));
                    marcia.AddRange(EscPos.NoUnderline);
                    marcia.AddRange(_codificaStampante.GetBytes("\r\n"));
                }
            }

            return marcia;
        }

        List<byte> Comanda_MA_100_00(int ODIndex, DatiComanda datiComanda, List<MenuItemPrint> listaArticoli)
        {

            List<byte> payload = new List<byte>();

            // Inizializza stampante
            payload.AddRange(EscPos.Initialize);
            payload.AddRange(EscPos.impostaCodePage850);
            payload.AddRange(EscPos.AlignCenter);

            //Header
            payload.AddRange(Header_000(ODIndex, datiComanda));

            //Corpo
            payload.AddRange(Marcia_000(listaArticoli));
            
            //Footer
            payload.AddRange(Footer_000(datiComanda));

            // Taglio carta
            payload.AddRange(EscPos.LineFeed3);
            payload.AddRange(EscPos.PartCutPaper);


            return payload;
        }
                   
        public async Task<List<NomiCorse>> LeggiNomiCorse()
        {
            // _dbFactory è già stato istanziato in GestInitEvent
            ICorseRepository repository = new CorseRepository(_dbFactory);

            try
            {
                List<NomiCorse> corse = await repository.OttieniNomiCorseAsync();
                return corse;
            }
            catch (Exception ex)
            {
                myLog.Error("Errore lettura corse", ex);
                return new List<NomiCorse>();
            }
        }

        private List<MenuItemPrint> PreparaListaArticoliMarcia(OpsCustomOrderDeviceEventArgs args, int corsacorr)
        {
            List<MenuItemPrint> ListaArticoli = new List<MenuItemPrint> { };

            foreach (var item in args.AllDetail)
            {
                string nomeArt = "";
                string quant = "";
                //string price = "";
                //string corsaart = "";
                int corsanum = 0;
                string referenceArt = "";
                //string seatart;
                string pesoart = "";
                string printopt = "";
                //int ODIndex=0;

                List <CondimentPrint> ListaCondiment = new List<CondimentPrint> { };

                corsanum = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).KdsCourseNum;
                if (corsanum == corsacorr)
                {
                    nomeArt = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).Name.ToString();
                    quant = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)item.DetailItem).SalesCount.ToString();
                    printopt = item.DetailItem.PrintOptionBits;
                    if (item.DetailItem.ReferenceEntries.Count > 0) referenceArt = item.DetailItem.ReferenceEntries[0].Descriptor;

                    var conds = item.DetailItem;
                    var listcond = ((Micros.PosCore.DataStore.DbRecords.DbMenuItemDetail)conds).Condiments;

                    foreach (var cond in listcond)
                    {
                        string nomeCond = "";
                        string quantCond = "";
                        string referenceCond = "";
                        string pesocond = "";

                        nomeCond = cond.ExpandedName;
                        quantCond = cond.SalesCount.ToString();
                        if (cond.ReferenceEntries.Count > 0)
                            referenceCond = cond.ReferenceEntries[0].Descriptor;

                        CondimentPrint Condiments = new CondimentPrint { nome = nomeCond, quant = quantCond, reference = referenceCond, peso = pesocond };
                        ListaCondiment.Add(Condiments);
                    }
                    //nt numdev = _device.Count();
                    //OrderDeviceCache.OrderDeviceInfo device;
                    //for (int odn = 1; odn < numdev; odn++)
                    foreach (OrderDeviceCache.OrderDeviceInfo device in _device) 
                    { 
                        //for (int od = 1; od < 29; od++)
                        //device = _device.GetDevice(odn);
                        int od = int.Parse(device.OrdDvcIndex);
                        if (printopt[7 + od] == '1')
                        {
                                MenuItemPrint Articolo = new MenuItemPrint
                                { quant = quant, nome = nomeArt, condiments = ListaCondiment, numcorsa = corsanum, reference = referenceArt, peso = pesoart, ODIndex = od };
                                ListaArticoli.Add(Articolo);
                        }
                      }

                    
                }
            }
            return ListaArticoli;
        }
      
        private void CleanUpCache()
        {
            // Rimuove gli elementi più vecchi di 10 secondi per evitare memory leak
            var keysToRemove = _processedPrints
                .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > 10)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _processedPrints.TryRemove(key, out _);
            }
        }

        private EventProcessingInstruction SelectedItemCompleteQuery(object sender, OpsSelectedItemCompleteQueryEventArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("SelectedItemComplete"));
            myLog.Debug("SelectedItemComplete");
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction ItemSelected(object sender, OpsItemSelectedEventArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("SelectedItem"));
            myLog.Debug("SelectedItem");
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GestisciPreviewMI(object sender, OpsMenuItemEventArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("OpsMiPreviewEvent"));
            myLog.Debug("OpsMiPreviewEvent");
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GesioneSignIn(object sender, OpsSignInPreviewEventArgs args)
        {
            if (_config.VerbosityDisplay > 0) OpsContext.ShowMessage(messageSow("EventSignIn"));
            myLog.Debug("EventSignIn");
            
            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GestisciPickupCheck(object sender, OpsPickUpCheckEventArgs args)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                myLog.Warn("C6A6EEA8 - Estensione Non abilitata");
                return EventProcessingInstruction.Continue;
            }

            VerificaExtension(false);

            return EventProcessingInstruction.Continue;
        }

        private void VerificaExtension(bool NewCheck = true)
        { 
            
            _coursemng_enable = _config.RvcCourseMng.Contains(OpsContext.RvcNumber) ;
            _menu_enable = _config.RvcMenu.Contains(OpsContext.RvcNumber) && _config.Suite.Count >0; // il menu è attivo se il revenue center fa parte della lista e l'elenco dei suite non è vuoto
            _gestioneCorse = new GestioneCorse2(_config.CorsaIniziale,_config.CorsaLimite, _config.CorsaPreDessert.Corsa, _config.CorsaDessert.Corsa, _config.CorsaPetitFour.Corsa); //Inizializza la classe GestioneCorse con la corsa iniziale, la corsa limite e il flag per l'apertura conto (per indicare se è già stata stampata)
            _gestioneCorse.AggiornaDatiComanda += AggiornaCorsa;
            if ((_device is null || _actual_Rvc != OpsContext.RvcID))
            {
                try
                {
                    _device = new OrderDeviceCache(_dbFactory);
                    _device.Load(OpsContext.RvcID, OpsContext.WorkstationID);
                }
                catch (Exception ex)
                {
                    OpsContext.ShowMessage(messageSow("Errore Lettura Device"));
                    OpsContext.ShowMessage(messageSow(ex.Message));
                    myLog.Error("FDC7F8AF - Errore Lettura Articoli", ex);
                }
            }
            
            if (OpsContext.Check != null && _coursemng_enable && !NewCheck)
            {
                HashSet<int> corsemarciate = new HashSet<int>();
                HashSet<int> corseusate = new HashSet<int>();
                if (_config.VerbosityDisplay > 3) OpsContext.ShowMessage(messageSow("Cerca Marcia"));
                foreach (CheckDetailItem riga in OpsContext.CheckDetail)
                {
                    if (riga is MenuItemDetail articolo)
                    {
                        if (_config.VerbosityDisplay > 3) OpsContext.ShowMessage(messageSow(string.Format("Articolo {0}", riga.Name)));
                        if (articolo.MiObjNum == _config.Marcia || articolo.MiObjNum == _config.Marciato)
                        {
                            corsemarciate.Add(articolo.KdsCourseNum);
                        }
                        //_CorseUsate.Add(articolo.KdsCourseNum);
                        corseusate.Add(articolo.KdsCourseNum);
                    }
                }
                //_gestioneCorse.ImpostaCorsaAttuale();
                //AggiornaCorsa();
                _gestioneCorse.SetCorse(corseusate, corsemarciate);
            }
            // else if (NewCheck)
            //{
            //    AggiornaCorsa();
            //}

            /*if (OpsContext.Check != null && _coursemng_enable)
            {
                try
                {
                    ImpostaCorsa();
                }
                catch (Exception ex)
                {
                    OpsContext.ShowMessage(messageSow("Errore apertura conto: " + ex.Message));
                    myLog.Error("A4C31445 - Errore apertura conto", ex);
                }
            }*/

            if (OpsContext.Check != null && _menu_enable && (_listaMenu is null | _actual_Rvc != OpsContext.RvcID))
            {
                var allContentData = DataStore.ReadAllContent(OpsContext.RvcID);
                var elementoCercato = allContentData.FirstOrDefault(c => c.Name == "CorseMng_Menu");

                if (elementoCercato != null)
                {
                    string menu = Encoding.Unicode.GetString(elementoCercato.ContentData.DataBlob);
                    MenuRoot radice = JsonConvert.DeserializeObject<MenuRoot>(menu);
                    _listaMenu = radice.ListaMenu;
                }
                else _menu_enable = false;
            }
        }

        private EventProcessingInstruction GestisciNewCheck(object sender, OpsBeginCheckEventArgs args)
        {
            if (!_extensionEnbled )
            {
                myLog.Warn("5979ADED - Estensione Non abilitata");
                return EventProcessingInstruction.Continue;
            }
            
            VerificaExtension(true);

            //corse = Task.Run(() => LeggiNomiCorse()).GetAwaiter().GetResult();

            return EventProcessingInstruction.Continue;
        }

        private EventProcessingInstruction GestisciAggiuntaArticolo(object sender, OpsMenuItemEventArgs args)
        {
            if (_extensionEnbled)
            {
                if (!args.MiClass.OptionBits.CheckBit(2) && args.MiClass.OptionBits.CheckBit(45) && _coursemng_enable && _extensionEnbled)
                    try
                    {
                        int valoreCorsa = _gestioneCorse.CorsaAttuale(); 
                        int valore;

                        dynamic argsDinamico = args;
                        try
                        {
                            if (OpsContext.CheckContext == null || OpsContext.CheckContext.CheckDetail == null)
                                return EventProcessingInstruction.Continue;

                            dynamic articolo = OpsContext.CheckContext.CheckDetail[OpsContext.CheckContext.CheckDetail.Count - 1];
                                
                            //    OpsCommand cmdCondimento = new OpsCommand(OpsCommandType.MenuItem);
                            if (articolo != null)
                            {
                                valore = articolo.KdsCourseNum;
                                if (valore == 0)  // L'articolo non ha una corsa, gli viene assegnata quella corrente
                                {
                                    cmdCambioCorsa(valoreCorsa);
                                }
                                else
                                { valoreCorsa=valore; } //se l'articolo ha una corsa, la variabile valoreCorsa viene aggiornata con il valore della corsa dell'articolo
                                if (_gestioneCorse.SeCorsaAggiuntaAComandaFinita(valoreCorsa))   //verifica se si tratta di una corsa 'aggiuntiva' e che non sia già presente e che siano tutte marciate
                                    {
                                        
                                        bool risposta = OpsContext.AskQuestion("Tutte le altre corse sono marciate. Procedo anche con questa?");
                                        if (risposta)
                                        {
                                            // Procedo con la preparazione immediata
                                            EseguiMarcia(valore, true);
                                            //_gestioneCorse.AggiungiCorsaMarciata(valore);
                                        }                                            
                                    }
                                _gestioneCorse.AddUsata(valoreCorsa);
                                //_gestioneCorse.ImpostaCorsaAttuale();
                                //AggiornaCorsa();
                            }
                        }
                        catch (Exception ex)
                        {
                            OpsContext.ShowMessage(messageSow("Errore!! "));
                            myLog.Error("9B426026 Errore aggiunta articolo", ex);
                        }
                        
                    }
                    catch (Exception ex)
                    {
                        OpsContext.ShowMessage(messageSow("Errore2!! "));
                        myLog.Error("3A347EE0 - Errore aggiunta articolo", ex);
                    }
                if (!args.MiClass.OptionBits.CheckBit(2) && _menu_enable && _extensionEnbled)
                    try
                    {
                        {

                            MenuTemplate menuTrovato = _listaMenu.FirstOrDefault(menu => menu.MenuID == args.MiMaster.ObjectNumber);

                            if (menuTrovato != null && _coursemng_enable)
                            {
                                InserisciMenuWithCourse(menuTrovato, Convert.ToInt32(args.Count));
                            }
                            else if (menuTrovato != null && !_coursemng_enable)
                            {
                                InserisciMenuNoCourse(menuTrovato, Convert.ToInt32(args.Count));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OpsContext.ShowMessage(messageSow("Errore3!! "));
                        myLog.Error("CFEDA03B - Errore aggiunta articolo", ex);
                    }

            }
            else
            {
                OpsContext.ShowMessage(messageSow("Estensione Disabilitata"));
                myLog.Warn("2F336424 - Estensione Disabilitata");
            }
            return EventProcessingInstruction.Continue;
        }

        private void InserisciMenuNoCourse(MenuTemplate MenuTrovato, int numeroMenu)
        {
            if (MenuTrovato.Course1.Count > 0)
            {
                ImpostaCorsa(1);
                foreach (int articolo in MenuTrovato.Course1)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course2.Count > 0)
            {
                ImpostaCorsa(2);
                foreach (int articolo in MenuTrovato.Course2)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course3.Count > 0)
            {
                ImpostaCorsa(3);
                foreach (int articolo in MenuTrovato.Course3)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course4.Count > 0)
            {
                ImpostaCorsa(4);
                foreach (int articolo in MenuTrovato.Course4)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course5.Count > 0)
            {
                ImpostaCorsa(5);
                foreach (int articolo in MenuTrovato.Course5)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course6.Count > 0)
            {
                ImpostaCorsa(6);
                foreach (int articolo in MenuTrovato.Course6)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course7.Count > 0)
            {
                ImpostaCorsa(7);
                foreach (int articolo in MenuTrovato.Course7)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course8.Count > 0)
            {
                ImpostaCorsa(8);
                foreach (int articolo in MenuTrovato.Course8)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course9.Count > 0)
            {
                ImpostaCorsa(9);
                foreach (int articolo in MenuTrovato.Course9)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }

            if (MenuTrovato.Course10.Count > 0)
            {
                ImpostaCorsa(10);
                foreach (int articolo in MenuTrovato.Course10)
                {
                    insertArticolo(articolo, numeroMenu, true, true);
                }
            }
        }

        private void InserisciMenuWithCourse(MenuTemplate MenuTrovato, int numeroMenu)
        {

            //insertArticolo(MenuTrovato.MenuID, numeroMenu);
            for (int c = 1; c<21;c++)
            {
                //if (MenuTrovato.AllCourses[$"Course{c}"].Count > 0)
                if (MenuTrovato.AllCourses.TryGetValue($"Course{c}", out List<int> courseList))
                    { 
                    ImpostaCorsa(c);
                    foreach (int articolo in courseList)
                    {
                        insertArticolo(articolo, numeroMenu, true, true);
                    }
                }
            }
        }

        private void insertArticolo(int IDarticolo, int numeroMenu, bool noPrice = false, bool noPrint = false, object newPrice = null)
        {
            //  Creazione Comandi
            OpsCommand cmdArticolo = new OpsCommand(OpsCommandType.MenuItem);
            OpsCommand cmdnoPrint = new OpsCommand(OpsCommandType.MenuItem);
            OpsCommand cmdSublevelNP = new OpsCommand(OpsCommandType.SubMenuLevel);
            OpsCommand cmdPrezzo = new OpsCommand(OpsCommandType.AsciiData);
            OpsCommand cmdEnter = new OpsCommand(OpsCommandType.EnterKey);
            OpsCommand cmdStorno = new OpsCommand(OpsCommandType.Void);

            // Configurazione Comandi
            cmdArticolo.Number = IDarticolo;
            if (newPrice != null)
            {
                decimal dprice = (decimal)(newPrice);
                string sprice = ((int)(dprice * 100)).ToString();
                cmdPrezzo.Text = sprice;
            }
            cmdnoPrint.Number = _config.OverPrintClass;
            cmdSublevelNP.Number = _config.SubLevelNoPrice;

            // Esecuzione Comandi
            if (noPrice)
            {
                OpsContext.ProcessCommand(cmdSublevelNP);
            }

            if (newPrice != null)
            {
                OpsContext.ProcessCommand(cmdPrezzo);
            }
            OpsContext.ProcessCommand(cmdArticolo);

            if (noPrint)
            {
                OpsContext.ProcessCommand(cmdnoPrint);
                OpsContext.ProcessCommand(cmdStorno);
            }
        }

        public void ImpostaCorsa(object numCorsa = null, bool aggCorsa = true)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("C63AF98A - Estensione Non abilitata");
                return;
            }

            if (OpsContext.Check == null)
                return;

            
            if (numCorsa != null)
                _gestioneCorse.SetCorsa((int)numCorsa);

            //if (aggCorsa)
            //    AggiornaCorsa();

        }

        /**/

        private void AggiornaCorsa(object sender, EventArgs e)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("83C1AE53 - Estensione Non abilitata");
                return;
            }
            if (OpsContext.Check == null)
                return;

            OpsContext.Check.ExtensibilityDetail.RemoveAll(this.ApplicationName);  //Pulisce schermo
            List<string> intestazione = _gestioneCorse.Intestazione();


            //Scrive Label Corsa attuale
            //ExtensibilityDataInfo CorsaCorrente = new ExtensibilityDataInfo(testoMessaggio(string.Format("Corsa {0}", _gestioneCorse.GetCorsaAttuale()), 40), "CorsaCorrente", "");
            ExtensibilityDataInfo CorsaCorrente = new ExtensibilityDataInfo(intestazione[0], "CorsaCorrente", "");
            CorsaCorrente.SetPrintOnDisplayOnly();
            OpsContext.Check.AddExtensibilityData(CorsaCorrente);

            //Scrive label corse marciate
            //ExtensibilityDataInfo CorseMarciate = new ExtensibilityDataInfo(testoMessaggio(OttieniStringaMarciate(), 40), "CorseMarciate", "");
            ExtensibilityDataInfo CorseMarciate = new ExtensibilityDataInfo(intestazione[1], "CorseMarciate", "");
            CorseMarciate.SetPrintOnDisplayOnly();
            OpsContext.Check.AddExtensibilityData(CorseMarciate);

            //Se immediata scrive label
            //bool immediata = _gestioneCorse.GetIfCorsaAttualeMarciata() || _gestioneCorse.GetNessunaMarciata(); //Se la corsa attuale è marciata oppure nessuna corsa è marcia è Immediata
            if (intestazione.Count==3)
            {
                //ExtensibilityDataInfo infoAseguire = new ExtensibilityDataInfo(testoMessaggio("A seguire", 40), "NoteCorsa", "");
                ExtensibilityDataInfo infoAseguire = new ExtensibilityDataInfo(intestazione[2], "NoteCorsa", "");
                infoAseguire.SetPrintOnDisplayOnly();
                OpsContext.Check.AddExtensibilityData(infoAseguire);
            }
            OpsContext.ProcessCommand(new OpsCommand(OpsCommandType.Refresh));
        }
        
        

        private long? CondAltraCorsa(int numeroCorsa)
        {
            if (_config.CorsaPreDessert.Corsa == numeroCorsa)
                return _config.CorsaPreDessert.Obj;
            else if (_config.CorsaDessert.Corsa == numeroCorsa)
                return _config.CorsaDessert.Obj;
            else if (_config.CorsaPetitFour.Corsa == numeroCorsa)
                return _config.CorsaPetitFour.Obj;
            else
                return null;
        }

        private string NomeAltraCorsa(int numeroCorsa)
        {
            if (_config.CorsaPreDessert.Corsa == numeroCorsa)
                return "Pre-Dessert";
            else if (_config.CorsaDessert.Corsa == numeroCorsa)
                return "Dessert";
            else if (_config.CorsaPetitFour.Corsa == numeroCorsa)
                return "petitFour";
            else
                return null;
        }

        private void EseguiMarcia(int numCorsa, bool onlyMarker = false)
        {
            try
            {
                if (OpsContext.Check == null || !_coursemng_enable || Convert.ToInt32(numCorsa) == 1 || !_gestioneCorse.SeCorsaUsata(numCorsa))
                    return;
                
                if (_config.VerbosityDisplay > 2)
                {
                    OpsContext.ShowMessage(messageSow(string.Format("Marcia {0}", numCorsa)));
                }
                myLog.Debug("Marcia {0}");

                if (!onlyMarker) //Se la marcia non deve essere stampata manda anche un send order
                {
                    OpsCommand cmdMarcia = new OpsCommand(OpsCommandType.MenuItem);
                    cmdMarcia.Number = _config.Marcia;
                    OpsContext.ProcessCommand(cmdMarcia);
                    cmdCambioCorsa(numCorsa);
                    if (_config.VerbosityDisplay > 1)
                    {
                        OpsContext.ShowMessage(messageSow("Send Order"));
                    }
                    myLog.Debug("Send Order");
                    OpsCommand cmdSend = new OpsCommand(OpsCommandType.TenderMedia);
                    cmdSend.Number = _config.TM_SendOrder;
                    OpsContext.ProcessCommand(cmdSend);
                }
                else
                {
                    OpsCommand cmdMarciato = new OpsCommand(OpsCommandType.MenuItem);
                    cmdMarciato.Number = _config.Marciato;
                    OpsContext.ProcessCommand(cmdMarciato);
                    cmdCambioCorsa(numCorsa);
                }
                _gestioneCorse.AddMarciata(Convert.ToInt32(numCorsa));
            }
            catch (Exception ex)
            {
                OpsContext.ShowMessage(messageSow("Errore Marcia"));
                myLog.Error("EAA72F2E - Errore Marcia", ex);
            }
        }

        private void cmdNoPrint()
        {
            OpsCommand cmdnoPrint = new OpsCommand(OpsCommandType.MenuItem);
            cmdnoPrint.Number = _config.OverPrintClass;
            OpsContext.ProcessCommand(cmdnoPrint);
            OpsContext.ProcessCommand(new OpsCommand(OpsCommandType.Void));
            
        }

        private void cmdCambioCorsa(int numCorsa)
        {
            OpsCommand cmdCondimento = new OpsCommand(OpsCommandType.MenuItem);
            
            if (numCorsa <= _config.CambioCorsa.Count())
            {
                cmdCondimento.Number = _config.CambioCorsa[numCorsa - 1];
            }
            else
            {
                long? condaltracorsa = CondAltraCorsa(numCorsa);
                if (condaltracorsa != null)
                    cmdCondimento.Number = condaltracorsa.Value;
            }
            if (cmdCondimento.Number != 0)
            {
                OpsContext.ProcessCommand(cmdCondimento);
                OpsContext.ProcessCommand(new OpsCommand(OpsCommandType.Void));
                //_gestioneCorse.AggiungiCorsaUsata(numCorsa);
            }
        }

        [ExtensibilityMethod]
        public void ViewCurrentCourse()
        {
            if (OpsContext.Check != null)
            {
                string valore = LeggiLaMiaVariabile("CorsaCorrente");
                if (_config.VerbosityDisplay > 3)
                {
                    OpsContext.ShowMessage(messageSow(string.Format("Corsa corrente è {0}", valore)));
                    //myLog.Debug("Corsa corrente è {0}");
                }
            }
            else
            {
                OpsContext.ShowMessage(messageSow("Devi prima aprire un check!"));
            }
        }

        [ExtensibilityMethod]
        public void AumentaCorsa()
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("9C9C306D - Estensione Non abilitata");
                return;
            }

            if (OpsContext.Check != null)
            {
                _gestioneCorse.SetNextCorsa();
                //ImpostaCorsa();
                //AggiornaCorsa();
            }
            else
            {
                OpsContext.ShowMessage(messageSow("Apri prima un conto!"));
            }
        }

        [ExtensibilityMethod]
        public void CambiaCorsa(object numCorsa)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("45DEDD32 - Estensione Non abilitata");
                return;
            }

            if (OpsContext.Check != null)
            {
                _gestioneCorse.SetCorsa(Convert.ToInt32(numCorsa));
                //AggiornaCorsa();
            }
            else
            {
                OpsContext.ShowMessage(messageSow("Apri prima un conto!"));
            }
        }

        [ExtensibilityMethod]
        public void SpostaCorsa(object numCorsa)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("527174B5 - Estensione Non abilitata");
                return;
            }
            foreach (CheckDetailItem riga in OpsContext.CheckContext.CheckDetail)
            {
                if (riga.DetailType == Micros.PosCore.Extensibility.Ops.CheckDetailType.DtlTypeMi)
                    if (((Micros.PosCore.Extensibility.Ops.OpsMenuItemDetail)riga).DetailLink == OpsContext.CurrentParentItem)  //Trovato articolo selezionato  
                    {                      
                        if (riga != null)
                        { cmdCambioCorsa(Convert.ToInt32(numCorsa)); }
                    }
            }
        }

        [ExtensibilityMethod]
        public void VisCode()
        {
            OpsContext.ShowMessage(messageSow(OpsContext.PropHierStrucID.ToString()));
        }
        
        [ExtensibilityMethod]
        public void TestComanda(object OrderDevice)
        {
            if ((_device is null || _actual_Rvc != OpsContext.RvcID))
            {
                try
                {
                    _device = new OrderDeviceCache(_dbFactory);
                    _device.Load(OpsContext.RvcID, OpsContext.WorkstationID);
                }
                catch (Exception ex)
                {
                    OpsContext.ShowMessage(messageSow("Errore Lettura Device"));
                    OpsContext.ShowMessage(messageSow(ex.Message));
                    myLog.Error("FDC6E8AF - Errore Lettura Articoli", ex);
                }
            }
            myLog.Debug($"Inzio Stampa Comanda device {OrderDevice}");
            DatiComanda datiComanda = PreparaDatiComadaTest();
            //List<NomiCorse> corse = Task.Run(() => LeggiNomiCorse()).GetAwaiter().GetResult();
            List<NomiCorse> corse = Task.Run(() => LeggiNomiCorse()).GetAwaiter().GetResult();
            List<MenuItemPrint> ListaArticoli = PreparaListaArticoliComandaTest(corse);
            List<byte> payload = new List<byte>();
            byte[] data = payload.ToArray();
            payload = Comanda_OD_100_00(Convert.ToInt32(OrderDevice), datiComanda, ListaArticoli);
            OrderDeviceCache.OrderDeviceInfo device = _device.GetDevice(Convert.ToInt32(OrderDevice));
            stampaComanda(payload.ToArray(), device, true);
            myLog.Debug($"Fine Stampa Comanda device {OrderDevice}");
        }

        [ExtensibilityMethod]
        public void MarciaNext()
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("AC35B43F - Estensione Non abilitata");
                return;
            }
            int? successiva = _gestioneCorse.NextMarcia();

            if (successiva != null)
               EseguiMarcia(successiva.Value);
            else
               OpsContext.ShowMessage("Le corse sono tutte marciate");
        }
                
        [ExtensibilityMethod]
        public void RepeatMarcia()
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("AC35B43F - Estensione Non abilitata");
                return;
            }
            //EseguiMarcia(_CorseMarciate.Max());
            EseguiMarcia(_gestioneCorse.UltimaCorsaMarciata());
        }

        [ExtensibilityMethod]
        public void Marcia(object numCorsa)
        {
            if (!_extensionEnbled || !_coursemng_enable)
            {
                OpsContext.ShowMessage(messageSow("Estensione Non abilitata"));
                myLog.Warn("E409237A - Estensione Non abilitata");
                return;
            }

            EseguiMarcia(Convert.ToInt32(numCorsa));

        }

        [ExtensibilityMethod]
        public void Status()
        {
            if (!_extensionEnbled) OpsContext.ShowMessage(messageSow("Estensione Disabilitata"));
            else
            { OpsContext.ShowMessage(messageSow($"Versione Estesione{Assembly.GetExecutingAssembly().GetName().Version.ToString()}\nGestione Corse abilitata {_coursemng_enable}\nGestione Menu abilitata {_menu_enable}")); }
        }

        public class ApplicationFactory : IExtensibilityAssemblyFactory
        {
            public ExtensibilityAssemblyBase Create(IExecutionContext context)
            {
                return new Application(context);
            }

            public void Destroy(ExtensibilityAssemblyBase app)
            {
                app.Destroy();
            }
        }
    }
}