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

        public static Settings Default { get; }

        static Settings()
        {
            Default = new Settings(Assembly.GetExecutingAssembly().GetConfiguration());
        }

        public Settings(IConfigurationSection section)
        {
            DatabaseConnString = new HashSet<string>(section.GetSection("DatabaseConnString").GetChildren().Select(p => p.Value.ToString()));
            SentryUrl = section.GetSection("SentryUrl").Value;
            ContractHashList = new HashSet<string>(section.GetSection("ContractHashList").GetChildren().Select(p => p.Value.ToString()));
        }
    }
}
