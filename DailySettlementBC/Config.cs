using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DailySettlementBC
{
    internal class Config
    {
        public static IConfiguration _config;

        public Config(IConfiguration config) { _config = config; }
        public Config()
        {
            if (_config == null)
            {
                ConfigurationBuilder b = new();
                b.AddJsonFile("appsettings.secrets.json", optional: false, reloadOnChange: true);
                _config = b.Build();
            }
        }
        public static string GetSetting(string key)
        {
            return _config.GetSection("AppSettings")[key];
        }
        public static IConfigurationSection GetSection(string key)
        {
            return _config.GetSection(key);
        }
    }
}
