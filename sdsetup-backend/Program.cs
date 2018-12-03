﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Newtonsoft.Json;

namespace sdsetup_backend {
    public class Program {

        public static Dictionary<string, string> Manifests;

        public static string Temp;
        public static string Files;
        public static string Config;

        public static string[] validChannels;
        public static List<string> uuidLocks = new List<string>();

        public static string latestPackageset = "default";

        private static string _privelegedUUID;
        private static string privelegedUUID {
            get {
                return _privelegedUUID;
            }

            set {
                Console.WriteLine("[WARN] New priveleged UUID: " + value);
                _privelegedUUID = value;
            }
        }

        public static void Main(string[] args) {

            ReloadEverything();

            privelegedUUID = Guid.NewGuid().ToString().Replace("-", "").ToLower();

            IWebHost host = CreateWebHostBuilder(args).Build();
            host.Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();

        public static bool IsUuidPriveleged(string uuid) {
            if (uuid == privelegedUUID) return true;
            return false;
        }

        public static bool SetPrivelegedUUID(string oldUuid, string newUuid) {
            if (oldUuid != privelegedUUID) return false;
            privelegedUUID = newUuid;
            return true;
        }

        public static string ReloadEverything() {
            try {
                //use temporary variables so if anything goes wrong, values wont be out of sync.
                Dictionary<string, string> _Manifests = new Dictionary<string, string>();

                string _Temp = Environment.CurrentDirectory + "\\temp";
                string _Files = Environment.CurrentDirectory + "\\files";
                string _Config = Environment.CurrentDirectory + "\\config";

                if (!Directory.Exists(_Temp)) Directory.CreateDirectory(_Temp);
                if (!Directory.Exists(_Files)) Directory.CreateDirectory(_Files);
                if (!Directory.Exists(_Config)) Directory.CreateDirectory(_Config);
                if (!File.Exists(_Config + "\\latestpackageset.txt")) File.WriteAllText(_Config + "\\latestpackageset.txt", "default");
                if (!File.Exists(_Config + "\\validchannels.txt")) File.WriteAllLines(_Config + "\\validchannels.txt", new string[] { "latest", "nightly" });

                foreach(string n in Directory.EnumerateDirectories(_Files )) {
                    string k = n.Split('\\').Last();
                    if (!File.Exists(_Files + "\\" + k + "\\manifest6.json")) File.WriteAllText(_Files + "\\" + k + "\\manifest6.json", "{}");
                }

                string _latestPackageset = File.ReadAllText(_Config + "\\latestpackageset.txt");
                string[] _validChannels = File.ReadAllLines(_Config + "\\validchannels.txt");

                //look away
                foreach (string n in Directory.EnumerateDirectories(_Files)) {
                    string k = n.Split('\\').Last();
                    Manifest m = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(_Files + "\\" + k + "\\manifest6.json"));
                    foreach (string c in Directory.EnumerateDirectories(_Files + "\\" + k)) {
                        string f = c.Split('\\').Last();
                        Package p = JsonConvert.DeserializeObject<Package>(File.ReadAllText(_Files + "\\" + k + "\\" + f + "\\info.json"));
                        m.Platforms[p.Platform].PackageSections[p.Section].Categories[p.Category].Subcategories[p.Subcategory].Packages[p.ID] = p;
                    }
                    _Manifests[k] = JsonConvert.SerializeObject(m, Formatting.Indented);
                }

                
                //update the real variables
                Temp = _Temp;
                Files = _Files;
                Config = _Config;
                latestPackageset = _latestPackageset;
                validChannels = _validChannels;
                Manifests = _Manifests;

            } catch (Exception e) {
                return "[ERROR] Something went wrong while reloading: \n\n\nMessage:\n   " + e.Message + "\n\nStack Trace:\n" + e.StackTrace + "\n\n\nThe server will continue running and no changes will be saved";
            }
            return "Success";
        }

        public static bool OverridePrivelegedUuid() {
            if (File.Exists(Config + "\\uuidoverride.txt")) {
                privelegedUUID = File.ReadAllText(Config + "\\uuidoverride.txt");
                File.Delete(Config + "\\uuidoverride.txt");
                return true;
            }
            return false;
        }
    }
}