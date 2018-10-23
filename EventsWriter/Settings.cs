using Microsoft.Extensions.Configuration;
using Neo.Wallets;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Neo.Plugins
{
    internal class Settings
    {
        public string DatabaseConnString { get; }
        public HashSet<string> ContractHashList { get; }

        public static Settings Default { get; }

        static Settings()
        {
            Default = new Settings(Assembly.GetExecutingAssembly().GetConfiguration());
        }

        public Settings(IConfigurationSection section)
        {
            DatabaseConnString = section.GetSection("DatabaseConnString").Value;
            ContractHashList = new HashSet<string>(section.GetSection("ContractHashList").GetChildren().Select(p => p.Value.ToString()));
        }
    }
}
