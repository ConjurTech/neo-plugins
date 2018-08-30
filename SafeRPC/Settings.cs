using Microsoft.Extensions.Configuration;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Neo.Plugins
{
    internal class Settings
    {
        public static Settings Default { get; }
        public HashSet<UInt160> AllowedWitnesses { get; }
        public String ApiKey { get; }

        static Settings()
        {
            Default = new Settings(Assembly.GetExecutingAssembly().GetConfiguration());
        }

        public Settings(IConfigurationSection section)
        {
            this.AllowedWitnesses = new HashSet<UInt160>(section.GetSection("AllowedWitnesses").GetChildren().Select(p => p.Value.ToScriptHash()));
            this.ApiKey = section.GetSection("ApiKey").Value.ToString();
        }
    }
}
