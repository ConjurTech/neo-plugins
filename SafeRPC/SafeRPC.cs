using Microsoft.AspNetCore.Http;
using Neo.IO.Data.LevelDB;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Network.RPC;
using Neo.SmartContract;
using System.Linq;

namespace Neo.Plugins
{
    public class SafeRPC : Plugin, IRpcPlugin
    {
        public SafeRPC()
        {

        }

        public JObject OnProcess(HttpContext context, string method, JArray _params)
        {
            if (method == "getrawmempool")
            {
                if (_params[0].AsString() == Settings.Default.ApiKey) return null;
                throw new RpcException(-400, "Access denied");
            }

            if (method == "sendrawtransaction")
            {
                Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
                if (tx.Witnesses.Any(witness => Settings.Default.AllowedWitnesses.Contains(witness.ScriptHash))) return null;
                throw new RpcException(-400, "Access denied");
            }

            return null;
        }
    }
}
