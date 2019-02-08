using Neo.IO.Data.LevelDB;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using SData = System.Data;
using SText = System.Text;
using Npgsql;
using NpgsqlTypes;
using SharpRaven;
using SharpRaven.Data;
using static Neo.Ledger.Blockchain;

namespace Neo.Plugins
{
    internal struct SmartContractEvent
    {
        public uint blockNumber;
        public string transactionHash;
        public string contractHash;
        public uint eventTime;
        public string eventType;
        public JArray eventPayload;
        public uint eventIndex;
    }

    public class EventWriterPlugin : Plugin, IPersistencePlugin
    {
        private List<NpgsqlConnection> conn = new List<NpgsqlConnection>();

        public EventWriterPlugin()
        {
            Console.WriteLine("initializing EventsWriter");

            foreach (string connString in Settings.Default.DatabaseConnString)
            {
                Console.WriteLine(connString);
                conn.Add(new NpgsqlConnection(connString));
            }

            foreach (NpgsqlConnection c in conn)
            {
                c.Open();
            }
        }

        ~EventWriterPlugin()
        {   
            foreach (NpgsqlConnection c in conn)
            {
                using (System)
                {
                    if (c != null && c.State == SData.ConnectionState.Open)
                    {
                        c.Close();
                    }
                }
                
            }
        }

        public override void Configure()
        {
            Settings.Load(GetConfiguration());
        }

        public void OnPersist(Persistence.Snapshot snapshot, List<ApplicationExecuted> applicationExecutedList)
        {
           Console.WriteLine(String.Format("On persist called at blockheight={0}", snapshot.Height.ToString()));
           foreach( var applicationExecuted in applicationExecutedList)
           {
                foreach( var result in applicationExecuted.ExecutionResults)
                {
                    if (result.VMState.HasFlag(VMState.FAULT))
                    {
                        Console.WriteLine("Transaction execution faulted!");
                        continue;
                    }

                    for (uint index = 0; index < result.Notifications.Length; index++)
                    {
                        var notification = result.Notifications[index];
                        var scriptHash = notification.ScriptHash.ToString().Substring(2);

                        if (!Settings.Default.ContractHashList.Contains(scriptHash)) continue;

                        try
                        {
                            var payload = notification.State.ToParameter();
                            var stack = (VM.Types.Array)notification.State;
                            string eventType = "";
                            JArray eventPayload = new JArray();

                            for (int i = 0; i < stack.Count; i++)
                            {
                                var bytes = stack[i].GetByteArray();
                                if (i == 0)
                                {
                                    eventType = SText.Encoding.UTF8.GetString(bytes);
                                }
                                else
                                {
                                    string type = stack[i].GetType().ToString();
                                    switch (type)
                                    {
                                        case "Neo.VM.Types.Boolean":
                                            {
                                                eventPayload.Add(stack[i].GetBoolean());
                                                break;
                                            }
                                        case "Neo.VM.Types.String":
                                            {
                                                eventPayload.Add(stack[i].GetString());
                                                break;
                                            }
                                        case "Neo.VM.Types.Integer":
                                            {
                                                eventPayload.Add(stack[i].GetBigInteger().ToString());
                                                break;
                                            }
                                        case "Neo.VM.Types.ByteArray":
                                            {
                                                if (bytes.Length == 20 || bytes.Length == 32)
                                                {
                                                    string hexString = bytes.Reverse().ToHexString();
                                                    eventPayload.Add(hexString);
                                                }
                                                else
                                                {
                                                    eventPayload.Add(stack[i].GetBigInteger().ToString());
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                string hexString = bytes.Reverse().ToHexString();
                                                eventPayload.Add(hexString);
                                                break;
                                            }

                                    }
                                }
                            }

                            var scEvent = new SmartContractEvent
                            {
                                blockNumber = snapshot.PersistingBlock.Index,
                                transactionHash = applicationExecuted.Transaction.Hash.ToString().Substring(2),
                                contractHash = scriptHash,
                                eventType = eventType,
                                eventPayload = eventPayload,
                                eventTime = snapshot.PersistingBlock.Timestamp,
                                eventIndex = index,
                            };

                            WriteToPsql(scEvent);

                        }
                        catch (Exception ex)
                        {
                            string connString = Settings.Default.SentryUrl;
                            var ravenClient = new RavenClient(connString);
                            ravenClient.Capture(new SentryEvent(ex));
                            PrintErrorLogs(ex);
                            throw ex;
                        }
                    }
                }
            }
        }

        private void WriteToPsql(SmartContractEvent contractEvent)
        {
            foreach (NpgsqlConnection c in conn)
            {
                WriteToEventTable(contractEvent, c);

                switch (contractEvent.eventType)
                {
                    case "created":
                        WriteToOfferTable(contractEvent, c);
                        break;
                    case "filled":
                        WriteToTradeTable(contractEvent, c);
                        break;
                    case "swapCreated":
                        WriteToSwapTable(contractEvent, c);
                        break;
                    default:
                        Console.WriteLine("Event has no type");
                        break;
                }
            }
        }

        private void WriteToEventTable(SmartContractEvent contractEvent, NpgsqlConnection conn)
        {
            Console.WriteLine(String.Format("Inserting {0} event {1}, block height: {2}", contractEvent.eventType, contractEvent.eventPayload, contractEvent.blockNumber));

            try
            {
                using (var cmd = new NpgsqlCommand(
                    "INSERT INTO events (block_number, transaction_hash, contract_hash, event_type, event_payload, event_time, event_index, blockchain, " +
                    "created_at, updated_at) " +
                    "VALUES (@blockNumber, @transactionHash, @contractHash, @eventType, @eventPayload, @eventTime, @eventIndex, @blockchain, " +
                    "current_timestamp, current_timestamp)", conn))
                {
                    cmd.Parameters.AddWithValue("blockchain", "neo");
                    cmd.Parameters.AddWithValue("blockNumber", NpgsqlDbType.Oid, contractEvent.blockNumber);
                    cmd.Parameters.AddWithValue("transactionHash", contractEvent.transactionHash);
                    cmd.Parameters.AddWithValue("contractHash", contractEvent.contractHash);
                    cmd.Parameters.AddWithValue("eventType", contractEvent.eventType);
                    cmd.Parameters.AddWithValue("eventTime", NpgsqlDbType.Timestamp, UnixTimeStampToDateTime(contractEvent.eventTime));
                    cmd.Parameters.AddWithValue("eventIndex", NpgsqlDbType.Oid, contractEvent.eventIndex);
                    cmd.Parameters.AddWithValue("eventPayload", NpgsqlDbType.Jsonb, contractEvent.eventPayload.ToString());

                    int nRows = cmd.ExecuteNonQuery();

                    Console.WriteLine(String.Format("Rows inserted={0}", nRows));
                }
            }
            catch (PostgresException ex)
            {
                if (ex.SqlState == "23505")
                {
                    // this is a unique key violation, which is fine, so do nothing.
                    Console.WriteLine("Event already inserted, ignoring");
                }
                else
                {
                    throw ex;
                }
            }
        }

        private static void WriteToOfferTable(SmartContractEvent contractEvent, NpgsqlConnection conn)
        {
            Console.WriteLine(String.Format("Inserting Offer {0}, block height: {1}", contractEvent.eventPayload, contractEvent.blockNumber));

            try
            {
                var address = contractEvent.eventPayload[0].AsString();
                var offerHash = contractEvent.eventPayload[1].AsString();
                var offerAssetId = contractEvent.eventPayload[2].AsString();
                var offerAmount = contractEvent.eventPayload[3].AsNumber();
                var wantAssetId = contractEvent.eventPayload[4].AsString();
                var wantAmount = contractEvent.eventPayload[5].AsNumber();
                var availableAmount = offerAmount;

                using (var cmd = new NpgsqlCommand(
                "INSERT INTO offers (block_number, transaction_hash, contract_hash, event_time, " +
                "blockchain, address, available_amount, offer_hash, offer_asset_id, offer_amount, want_asset_id, want_amount, " +
                    "created_at, updated_at)" +
                "VALUES (@blockNumber, @transactionHash, @contractHash, @eventTime, @blockchain, @address, " +
                "@availableAmount, @offerHash, @offerAssetId, @offerAmount, @wantAssetId,  @wantAmount, " +
                    "current_timestamp, current_timestamp)", conn))
                {
                    cmd.Parameters.AddWithValue("blockNumber", NpgsqlDbType.Oid, contractEvent.blockNumber);
                    cmd.Parameters.AddWithValue("transactionHash", contractEvent.transactionHash);
                    cmd.Parameters.AddWithValue("contractHash", contractEvent.contractHash);
                    cmd.Parameters.AddWithValue("eventTime", NpgsqlDbType.Timestamp, UnixTimeStampToDateTime(contractEvent.eventTime));
                    cmd.Parameters.AddWithValue("blockchain", "neo");
                    cmd.Parameters.AddWithValue("address", NpgsqlDbType.Varchar, address);
                    cmd.Parameters.AddWithValue("availableAmount", NpgsqlDbType.Numeric, availableAmount);
                    cmd.Parameters.AddWithValue("offerHash", NpgsqlDbType.Varchar, offerHash);
                    cmd.Parameters.AddWithValue("offerAssetId", NpgsqlDbType.Varchar, offerAssetId);
                    cmd.Parameters.AddWithValue("offerAmount", NpgsqlDbType.Numeric, offerAmount);
                    cmd.Parameters.AddWithValue("wantAssetId", NpgsqlDbType.Varchar, wantAssetId);
                    cmd.Parameters.AddWithValue("wantAmount", NpgsqlDbType.Numeric, wantAmount);

                    int nRows = cmd.ExecuteNonQuery();

                    Console.WriteLine(String.Format("Rows inserted={0}", nRows));
                }
            }
            catch (PostgresException ex)
            {
                if (ex.SqlState == "23505")
                {
                    // this is a unique key violation, which is fine, so do nothing.
                    Console.WriteLine("Offer already inserted, ignoring");
                }
                else
                {
                    throw ex;
                }
            }
        }

        private static void WriteToTradeTable(SmartContractEvent contractEvent, NpgsqlConnection conn)
        {
            Console.WriteLine(String.Format("Inserting trade {0}, block height: {1}", contractEvent.eventPayload, contractEvent.blockNumber));

            string connString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            try
            {
                var address = contractEvent.eventPayload[0].AsString();
                var offerHash = contractEvent.eventPayload[1].AsString();
                var filledAmount = contractEvent.eventPayload[2].AsNumber();
                var offerAssetId = contractEvent.eventPayload[3].AsString();
                var offerAmount = contractEvent.eventPayload[4].AsNumber();
                var wantAssetId = contractEvent.eventPayload[5].AsString();
                var wantAmount = contractEvent.eventPayload[6].AsNumber();

                using (var cmd = new NpgsqlCommand(
                "INSERT INTO trades (block_number, transaction_hash, contract_hash, address, offer_hash, filled_amount, " +
                "offer_asset_id, offer_amount, want_asset_id, want_amount, event_time, blockchain, created_at, updated_at)" +
                "VALUES (@blockNumber, @transactionHash, @contractHash, @address, @offerHash, @filledAmount, " +
                "@offerAssetId, @offerAmount, @wantAssetId, @wantAmount, @eventTime, @blockchain, current_timestamp, current_timestamp)", conn))
                {
                    cmd.Parameters.AddWithValue("blockNumber", NpgsqlDbType.Oid, contractEvent.blockNumber);
                    cmd.Parameters.AddWithValue("transactionHash", contractEvent.transactionHash);
                    cmd.Parameters.AddWithValue("contractHash", contractEvent.contractHash);
                    cmd.Parameters.AddWithValue("address", NpgsqlDbType.Varchar, address);
                    cmd.Parameters.AddWithValue("offerHash", NpgsqlDbType.Varchar, offerHash);
                    cmd.Parameters.AddWithValue("filledAmount", NpgsqlDbType.Numeric, filledAmount);
                    cmd.Parameters.AddWithValue("offerAssetId", NpgsqlDbType.Varchar, offerAssetId);
                    cmd.Parameters.AddWithValue("offerAmount", NpgsqlDbType.Numeric, offerAmount);
                    cmd.Parameters.AddWithValue("wantAssetId", NpgsqlDbType.Varchar, wantAssetId);
                    cmd.Parameters.AddWithValue("wantAmount", NpgsqlDbType.Numeric, wantAmount);
                    cmd.Parameters.AddWithValue("eventTime", NpgsqlDbType.Timestamp, UnixTimeStampToDateTime(contractEvent.eventTime));
                    cmd.Parameters.AddWithValue("blockchain", "neo");

                    int nRows = cmd.ExecuteNonQuery();

                    Console.WriteLine(String.Format("Rows inserted={0}", nRows));
                }
            }
            catch (PostgresException ex)
            {
                if (ex.SqlState == "23505")
                {
                    // this is a unique key violation, which is fine, so do nothing.
                    Console.WriteLine("Trade already inserted, ignoring");
                }
                else
                {
                    throw ex;
                }
            }
        }

        private static void WriteToSwapTable(SmartContractEvent contractEvent, NpgsqlConnection conn) {
            Console.WriteLine(String.Format("Inserting swap {0}, block height: {1}", contractEvent.eventPayload, contractEvent.blockNumber));

            string connString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            try
            { 
                // (makerAddress, takerAddress, assetID, amount, hashedSecret, expiryTime, feeAssetID, feeAmount)
                var makerAddress = contractEvent.eventPayload[0].AsString();
                var takerAddress = contractEvent.eventPayload[1].AsString();
                var assetID = contractEvent.eventPayload[2].AsString();
                var amount = contractEvent.eventPayload[3].AsNumber();
                var hashedSecret = contractEvent.eventPayload[4].AsNumber();
                var expiryTime = contractEvent.eventPayload[5].AsNumber();
                var feeAssetID = contractEvent.eventPayload[6].AsString();
                var feeAmount = contractEvent.eventPayload[7].AsNumber();
                
                using (var cmd = new NpgsqlCommand(
                "INSERT INTO swaps (makerAddress, takerAddress, assetID, amount, expiryTime, feeAssetID, " +
                "feeAmount, created_at, updated_at, eventTime, blockNumber, transactionHash, blockChain, " + 
                "contractHash, hashedSecret)" +
                "VALUES (@makerAddress, @takerAddress, @assetID, @amount, @expiryTime, @feeAssetID, " +
                "@feeAmount, current_timestamp, current_timestamp, current_timestamp, @blockNumber, " +
                "@transactionHash, @blockchain, @contractHash, @hashedSecret)", conn))
                {
                    cmd.Parameters.AddWithValue("makerAddress", NpgsqlDbType.Varchar, makerAddress);
                    cmd.Parameters.AddWithValue("takerAddress", NpgsqlDbType.Varchar, takerAddress);
                    cmd.Parameters.AddWithValue("assetID", NpgsqlDbType.Varchar, assetID);
                    cmd.Parameters.AddWithValue("amount", NpgsqlDbType.Numeric, amount);
                    cmd.Parameters.AddWithValue("expiryTime", NpgsqlDbType.Timestamp, expiryTime);
                    cmd.Parameters.AddWithValue("feeAssetID", NpgsqlDbType.Varchar, feeAssetID);
                    cmd.Parameters.AddWithValue("feeAmount", NpgsqlDbType.Numeric, feeAmount);
                    cmd.Parameters.AddWithValue("eventTime", NpgsqlDbType.Timestamp, UnixTimeStampToDateTime(contractEvent.eventTime));
                    cmd.Parameters.AddWithValue("blockNumber", NpgsqlDbType.Oid, contractEvent.blockNumber);
                    cmd.Parameters.AddWithValue("transactionHash", contractEvent.transactionHash);
                    cmd.Parameters.AddWithValue("blockchain", "neo");
                    cmd.Parameters.AddWithValue("contractHash", contractEvent.contractHash);

                    int nRows = cmd.ExecuteNonQuery();

                    Console.WriteLine(String.Format("Rows inserted={0}", nRows));
                }
            }
            catch (PostgresException ex)
            {
                if (ex.SqlState == "23505")
                {
                    // this is a unique key violation, which is fine, so do nothing.
                    Console.WriteLine("Swap already inserted, ignoring");
                }
                else
                {
                    throw ex;
                }
            }
        }

        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp);
            return dtDateTime;
        }

        private void PrintErrorLogs(Exception ex)
        {
            Console.WriteLine(ex.GetType());
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            if (ex is AggregateException ex2)
            {
                foreach (Exception inner in ex2.InnerExceptions)
                {
                    Console.WriteLine();
                    PrintErrorLogs(inner);
                }
            }
            else if (ex.InnerException != null)
            {
                Console.WriteLine();
                PrintErrorLogs(ex.InnerException);
            }
        }
    }
}