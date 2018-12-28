using Microsoft.Extensions.Configuration;
using Neo.Wallets;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Neo.Plugins
{
    internal class Settings
    {
        public HashSet<string> DatabaseConnString { get; }
        public string SentryUrl { get; }
        public HashSet<string> ContractHashList { get; }

        public static Settings Default { get; private set; }

        private Settings(IConfigurationSection section)
        {
            this.DatabaseConnString = new HashSet<string>(section.GetSection("DatabaseConnString").GetChildren().Select(p => p.Value.ToString()));
            this.SentryUrl = section.GetSection("SentryUrl").Value;
            this.ContractHashList = new HashSet<string>(section.GetSection("ContractHashList").GetChildren().Select(p => p.Value.ToString()));
        }

        public static void Load(IConfigurationSection section)
        {
            Default = new Settings(section);
        }
    }
}
