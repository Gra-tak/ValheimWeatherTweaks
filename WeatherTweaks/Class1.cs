using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;



namespace BepInEx.Configuration
{  
    public static class ConfigExtension
    {
        public static T GetFieldValue<T>(this object obj, string name)
        {
            var field = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField | BindingFlags.GetProperty);
            return (T)field?.GetValue(obj);
        }
        public static void SaveShort(this ConfigFile f)
        {
            lock (f.GetFieldValue<object>("_ioLock"))
            {
                string directoryName = Path.GetDirectoryName(f.ConfigFilePath);
                if (directoryName != null)
                {
                    Directory.CreateDirectory(directoryName);
                }

                using (StreamWriter streamWriter = new StreamWriter(f.ConfigFilePath, append: false, Utility.UTF8NoBom))
                {
                    var _ownerMetadata = f.GetFieldValue<BepInPlugin>("_ownerMetadata");
                    if (_ownerMetadata != null)
                    {
                        streamWriter.WriteLine($"## Settings file was created by plugin {_ownerMetadata.Name} v{_ownerMetadata.Version}");
                        streamWriter.WriteLine("## Plugin GUID: " + _ownerMetadata.GUID);
                        streamWriter.WriteLine();
                    }

                    var repeats = new HashSet<string>();
                    Dictionary<ConfigDefinition, ConfigEntryBase> Entries =new Dictionary<ConfigDefinition, ConfigEntryBase> () ;
                    using (var itemEnum = f.GetEnumerator())
                    {
                        while (itemEnum.MoveNext())
                        {
                            Entries.Add(itemEnum.Current.Key, itemEnum.Current.Value);
                        }
                    }
                    foreach (var item in from x in Entries.Select((KeyValuePair<ConfigDefinition, ConfigEntryBase> x) => new
                    {
                        Key = x.Key,
                        entry = x.Value,
                        value = x.Value.GetSerializedValue()
                    })
                                         group x by x.Key.Section into x
                                         //orderby x.Key
                                         select x)
                    {
                        streamWriter.WriteLine("[" + item.Key + "]");
                        foreach (var item2 in item)
                        {
                            if (repeats.Contains(item2.Key.Key))
                            {
                                streamWriter.WriteLine(item2.Key.Key + " = " + item2.value);
                                if (item2.entry != null)
                                    streamWriter.WriteLine("# Default value: " + TomlTypeConverter.ConvertToString(item2.entry.DefaultValue, item2.entry.SettingType));
                            }
                            else
                            {
                                item2.entry?.WriteDescription(streamWriter);
                                repeats.Add(item2.Key.Key);
                                streamWriter.WriteLine(item2.Key.Key + " = " + item2.value);
                            }
                            
                            streamWriter.WriteLine();
                        }

                        //streamWriter.WriteLine();
                    }
                }
            }
        }
    }
}

namespace WeatherTweaks
{
    [BepInPlugin(PluginId, "Weather Tweaks", "1.1.0")]
    public class WeatherTweaks : BaseUnityPlugin
    {

        public ConfigFile Config2;
        public const string PluginId = "WeatherTweaks";
        private static WeatherTweaks _instance;
        private static ConfigEntry<bool> _loggingEnabled;
        public static ConfigEntry<float> _fogMultiplier;
        public static ConfigEntry<bool> _mistland_very_clear_after_queen_defeated;
        public static ConfigEntry<string> _extraWeathers;

        //reset on reload (EnvMan awake):
        public static bool QueenDefeated = false;
        public static bool QueenWasAliveAtStart = false;
        public static List<ConfigEntry<float>> configs = new List<ConfigEntry<float>>();
        public static List<ConfigEntry<bool>> configsBool = new List<ConfigEntry<bool>>();


        private Harmony _harmony;

        public static void Log(string message)
        {
            if (_loggingEnabled.Value)
                _instance.Logger.LogInfo(message);
        }

        public static void LogWarning(string message)
        {
            if (_loggingEnabled.Value)
                _instance.Logger.LogWarning(message);
        }

        public static void LogError(string message)
        {
            //if (_loggingEnabled.Value)
            _instance.Logger.LogError(message);
        }

        private void Awake()
        {
            _instance = this;
            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginId);
            Init();
        }

        public void Init()
        {
            _instance.Config.SaveOnConfigSet = false;
            _loggingEnabled = Config.Bind("Logging", "Logging Enabled", false, "Enable logging.");
            _fogMultiplier = Config.Bind("Global", "Fog multiplier", 1f, "Multiplier applied to all fog settings in the game (should also effect non-biome environments). Multiplicative with all fog settngs done by this mod.");
            _mistland_very_clear_after_queen_defeated = Config.Bind("Global", "Partly fogless Mistland after defeated boss", false, "Enables a forth weather in the mistlands once its boss is defeated that has no extra mist, more brightness and no clouds with weight 1.5 (the same weight as the normal dry mistland weather). If set to false, the forth weather is enabled at any time as long as the weight in Mistland_very_clear is non-zero (it is zero by default).");
            _extraWeathers = Config.Bind("Global", "Extra Weather", "Mistlands:Mistlands_very_clear", "Add an additional weather type to a biome. This will start as a copy of the first weather of this biome, but can be configured by the user once created. Formatting: '<Biome>:<Name of new weather type>,<Biome2>:<Name of further new weather type>'. Example: 'Mistlands:Mistlands_very_clear, Swamp:Swamp_Dry'.");
        }

        private void OnDestroy()
        {
            _instance = null;
            _harmony?.UnpatchSelf();
        }

        [HarmonyPatch(typeof(EnvMan), "Awake")]
        private class WeatherPatchLoadInit
        {
            private static void Postfix()
            {
                QueenDefeated = false;
                QueenWasAliveAtStart = false;
                configs.Clear();
                configsBool.Clear();
            }
        }



        [HarmonyPatch(typeof(EnvMan), "InitializeBiomeEnvSetup")]
        private class WeatherPatch
        {
            private static void SetConfig(BiomeEnvSetup biome, EnvEntry env, string varName, ref float var, string desc)
            {
                var c = _instance.Config.Bind(biome.m_name + "_" + env.m_environment, varName, var, desc);
                configs.Add(c);
                var = c.Value;
            }
            private static void SetConfigBool(BiomeEnvSetup biome, EnvEntry env, string varName, ref bool var, string desc)
            {
                var c = _instance.Config.Bind(biome.m_name + "_" + env.m_environment, varName, var, desc);
                configsBool.Add(c);
                var = c.Value;
            }


            public static HashSet<string> repeats = new HashSet<string>();
            private static void Postfix(BiomeEnvSetup biome, EnvMan __instance)
            {
                Log("Doing configs for biome : " + biome.m_name);
                List<string> newWeather = _extraWeathers.Value.Split(',').ToList().ConvertAll(x => x.Trim(' '));
                foreach (var s in newWeather)
                {
                    List<string> biomeAndName = s.Split(':').ToList().ConvertAll(x => x.Trim(' '));
                    if (biomeAndName[0] == biome.m_name)
                    {
                        Log("Adding env entry : " + biomeAndName[1]);
                        EnvEntry newEntry = new EnvEntry
                        {
                            m_environment = biomeAndName[1],
                            m_env = biome.m_environments[0].m_env.Clone()
                        };
                        newEntry.m_env.m_name = biomeAndName[1];
                        biome.m_environments.Add(newEntry);
                        __instance.m_environments.Add(newEntry.m_env);
                        if (biome.m_name == "Mistlands" && biomeAndName[1] == "Mistlands_very_clear") //defaults for this biome that is added by default
                        {
                            newEntry.m_env.m_lightIntensityDay = 1.6f;
                            newEntry.m_env.m_lightIntensityNight = 0.8f;
                            newEntry.m_env.m_rainCloudAlpha = 0f;
                            newEntry.m_weight = 0f;
                        }
                    }
                }
                //if (biome.m_name=="Mistlands" && ZoneSystem.instance.GetGlobalKey("defeated_queen"))
                //{
                //    Log("Adding env entry");
                //    EnvEntry newEntry = new EnvEntry
                //    {
                //        m_environment = "Mistlands_very_clear",
                //        m_env = biome.m_environments[0].m_env.Clone()
                //    };
                //    newEntry.m_env.m_name = "Mistlands_very_clear";
                //    biome.m_environments.Add(newEntry);
                //    __instance.m_environments.Add(newEntry.m_env);
                //    QueenDefeated = true;
                //}
                foreach (var e in biome.m_environments)
                {
                    //make sure this is also the same members used as in ReApply
                    SetConfig(biome, e, "m_weight", ref e.m_weight, "Weight of this enviroment.");
                    if (!repeats.Contains(e.m_environment))
                    {
                        SetConfig(biome, e, "m_fogDensityDay", ref e.m_env.m_fogDensityDay, "Fog density during the day.");
                        SetConfig(biome, e, "m_fogDensityEvening", ref e.m_env.m_fogDensityEvening, "Fog density during the evening.");
                        SetConfig(biome, e, "m_fogDensityMorning", ref e.m_env.m_fogDensityMorning, "Fog density during the morning.");
                        SetConfig(biome, e, "m_fogDensityNight", ref e.m_env.m_fogDensityNight, "Fog density during the night.");
                        SetConfig(biome, e, "m_lightIntensityDay", ref e.m_env.m_lightIntensityDay, "Light intensity during the day.");
                        SetConfig(biome, e, "m_lightIntensityNight", ref e.m_env.m_lightIntensityNight, "Light intensity during the night.");
                        SetConfig(biome, e, "m_rainCloudAlpha", ref e.m_env.m_rainCloudAlpha, "");
                        SetConfig(biome, e, "m_sunAngle", ref e.m_env.m_sunAngle, "");
                        SetConfig(biome, e, "m_windMin", ref e.m_env.m_windMin, "");
                        SetConfig(biome, e, "m_windMax", ref e.m_env.m_windMax, "");
                        SetConfigBool(biome, e, "m_alwaysDark", ref e.m_env.m_alwaysDark, "");
                        //SetConfigBool(biome, e, "m_default", ref e.m_env.m_default, "");
                        SetConfigBool(biome, e, "m_isCold", ref e.m_env.m_isCold, "");
                        SetConfigBool(biome, e, "m_isColdAtNight", ref e.m_env.m_isColdAtNight, "");
                        SetConfigBool(biome, e, "m_isFreezing", ref e.m_env.m_isFreezing, "");
                        SetConfigBool(biome, e, "m_isFreezingAtNight", ref e.m_env.m_isFreezingAtNight, "");
                        SetConfigBool(biome, e, "m_isWet", ref e.m_env.m_isWet, "");
                        SetConfigBool(biome, e, "m_psystemsOutsideOnly", ref e.m_env.m_psystemsOutsideOnly, "");

                        //foreach (var gameObject in e.m_env.m_psystems)
                        //{
                        //    ParticleSystem[] componentsInChildren = gameObject.GetComponentsInChildren<ParticleSystem>();
                        //    for (int j = 0; j < componentsInChildren.Length; j++)
                        //    {
                        //        Log(e.m_environment+componentsInChildren[j].name);
                        //        Log(e.m_environment + "startSizeMultiplier: " + componentsInChildren[j].main.startSizeMultiplier);
                        //        Log(e.m_environment + "maxParticles: " + componentsInChildren[j].main.maxParticles);
                        //    }
                        //    MistEmitter componentInChildren = gameObject.GetComponentInChildren<MistEmitter>();
                        //    if (componentInChildren)
                        //    {
                        //        //Log(e.m_environment+ "rays: " + componentInChildren.m_rays);
                        //        //Log(e.m_environment + "rays: " + componentInChildren.m_interval);
                        //        //Log(e.m_environment + "rays: " + componentInChildren.m_totalRadius);
                        //        //Log(e.m_environment + "rays: " + componentInChildren.m_testRadius);
                        //        //Log(e.m_environment + "rays: " + componentInChildren.m_placeOffset);
                        //    }
                        //}
                        repeats.Add(e.m_environment);
                    }
                }
                _instance.Config.SaveShort();
                //Following doesn't seem to work here as the global key isn't done yet
                //if (ZoneSystem.instance.GetGlobalKey("defeated_queen"))
                //    QueenDefeated = true;
                //if (_mistland_very_clear_after_queen_defeated.Value)
                //    if (biome.m_name == "Mistlands")
                //    {
                //        if (!QueenDefeated) //disable very clear weather if queen is not defeated yet without killing the config. Not needed on the default config but if people change the default is it
                //        {
                //            Log("Queen not defeated. Disabling very clear mistlands");
                //            foreach (var e in biome.m_environments)
                //                if (e.m_environment == "Mistlands_very_clear")
                //                    e.m_weight = 0f;
                //        }
                //        else
                //        {
                //            Log("Queen already defeated. Enabling very clear mistlands");
                //            foreach (var e in biome.m_environments) //setting weight no non-zero even with the config default of zero as descriped in "Partly fogless Mistland after defeated boss"
                //                if (e.m_environment == "Mistlands_very_clear")
                //                    if (e.m_weight == 0f )
                //                        e.m_weight = 1.5f;
                //        }
                //    }
            }

            private static void ApplyConfig<T>(ref T f, ref List<ConfigEntry<T>>.Enumerator it)
            {
                if (it.MoveNext())
                {
                    Log(it.Current.Definition.ToString()+": Before: " + f + ". After: " + it.Current.Value);
                    f = it.Current.Value;
                }
                else
                    LogError("Unable to reapply config");
            }


            public static void ReApply()
            {
                Log("Reapplying. Number of configs: " + configs.Count);
                var it = configs.GetEnumerator();
                var itBool = configsBool.GetEnumerator();
                repeats.Clear();
                foreach (var biome in EnvMan.instance.m_biomes)
                {
                    Log(biome.m_name + "starting");
                    foreach (var e in biome.m_environments)
                    {
                        if (biome.m_name == "Ocean" && e.m_env.m_name == "Ashlands_SeaStorm")
                            continue; //seems to be dynamically added later and messes up my config... Probably copies config from ashland oceans
                        //make sure this is also the same members used as in Postfix
                        ApplyConfig(ref e.m_weight, ref it);
                        if (!repeats.Contains(e.m_environment))
                        {
                            ApplyConfig(ref e.m_env.m_fogDensityDay,ref it);
                            ApplyConfig(ref e.m_env.m_fogDensityEvening,ref it);
                            ApplyConfig(ref e.m_env.m_fogDensityMorning,ref it);
                            ApplyConfig(ref e.m_env.m_fogDensityNight,ref it);
                            ApplyConfig(ref e.m_env.m_lightIntensityDay,ref it);
                            ApplyConfig(ref e.m_env.m_lightIntensityNight,ref it);
                            ApplyConfig(ref e.m_env.m_rainCloudAlpha, ref it);
                            ApplyConfig(ref e.m_env.m_sunAngle, ref it);
                            ApplyConfig(ref e.m_env.m_windMin, ref it);
                            ApplyConfig(ref e.m_env.m_windMax, ref it);
                            ApplyConfig(ref e.m_env.m_alwaysDark, ref itBool);
                            ApplyConfig(ref e.m_env.m_isCold, ref itBool);
                            ApplyConfig(ref e.m_env.m_isColdAtNight, ref itBool);
                            ApplyConfig(ref e.m_env.m_isFreezing, ref itBool);
                            ApplyConfig(ref e.m_env.m_isFreezingAtNight, ref itBool);
                            ApplyConfig(ref e.m_env.m_isWet, ref itBool);
                            ApplyConfig(ref e.m_env.m_psystemsOutsideOnly, ref itBool);
                            Log(biome.m_name + " applied " + e.m_env.m_name);
                            repeats.Add(e.m_environment);
                        }
                        else
                            Log(biome.m_name + " repeat skipped " + e.m_env.m_name);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(EnvMan), "SetEnv")]
        public static class Fog_Mutliplier
        {
            private static void Postfix()
            {
                RenderSettings.fogDensity *= _fogMultiplier.Value;
            }
        }


        [HarmonyPatch(typeof(Mister), "GetDemistersSorted")] //yeah. This name is incorrect in Valheim. Those are the Misters, not the Demisters. Expect this to break in future patches...
        public static class WeatherTweakRemoveMist
        {
            public static void Postfix(Vector3 refPoint, ref List<Mister> __result)
            {
                if (EnvMan.instance.GetCurrentEnvironment().m_name == "Mistlands_very_clear")
                    __result=new List<Mister>();
            }
        }

        [HarmonyPatch(typeof(EnvMan), "Update")] //yeah. This name is incorrect in Valheim. Those are the Misters, not the Demisters. Expect this to break in future patches...
        public static class WeatherTweakQueenDefeated
        {
            public static void Postfix(EnvMan __instance)
            {
                if (!QueenDefeated && !QueenWasAliveAtStart && !ZoneSystem.instance.GetGlobalKey("defeated_queen"))
                {
                    QueenWasAliveAtStart = true;
                    if (_mistland_very_clear_after_queen_defeated.Value)
                        foreach (BiomeEnvSetup biome in __instance.m_biomes)
                            if (biome.m_name == "Mistlands")
                            {
                                foreach (var e in biome.m_environments)
                                    if (e.m_environment == "Mistlands_very_clear")
                                    {
                                        e.m_weight = 0f;
                                        Log("Queen not defeated. Disabling very clear mistlands");
                                    }
                            }
                }
                if (!QueenDefeated && ZoneSystem.instance.GetGlobalKey("defeated_queen"))
                {
                    QueenDefeated = true;
                    if (_mistland_very_clear_after_queen_defeated.Value)
                        foreach (BiomeEnvSetup biome in __instance.m_biomes)
                            if (biome.m_name == "Mistlands")
                            {
                                foreach (var e in biome.m_environments) //setting weight no non-zero even with the config default of zero as descriped in "Partly fogless Mistland after defeated boss"
                                    if (e.m_environment == "Mistlands_very_clear")
                                    {
                                        ConfigEntry<float> c;
                                        if (_instance.Config.TryGetEntry<float>(new ConfigDefinition(biome.m_name + "_" + e.m_environment, "m_weight"),out c))
                                            e.m_weight = c.Value;
                                        //WeatherPatch.SetConfig(biome, e, "m_weight", ref e.m_weight, "Weight of this enviroment.");
                                        if (e.m_weight == 0f)
                                            e.m_weight = 1.5f;
                                        if (QueenWasAliveAtStart) //"force" enabling the new environment (just until the next weather change)
                                            __instance.m_currentEnv = e.m_env;
                                        Log("Queen defeated. Enabling very clear mistlands");
                                    }
                            }

                    //foreach (BiomeEnvSetup biome in __instance.m_biomes)
                    //{
                    //    if (biome.m_name == "Mistlands")
                    //    {
                    //        Log("Trying to add env entry");
                    //        EnvEntry newEntry = new EnvEntry
                    //        {
                    //            m_environment = "Mistlands_very_clear",
                    //            m_env = biome.m_environments[0].m_env.Clone()
                    //        };
                    //        newEntry.m_env.m_name = "Mistlands_very_clear";
                    //        biome.m_environments.Add(newEntry);
                    //        __instance.m_environments.Add(newEntry.m_env);
                    //        QueenDefeated = true;
                    //        WeatherPatch.ReApply();
                    //        __instance.m_currentEnv = newEntry.m_env;
                    //    }
                    //}
                }
            }
        }

        //[HarmonyPatch(typeof(EnvMan), "SetEnv")]
        //private static class Test2
        //{
        //    private static void Postfix(EnvSetup env)
        //    {
        //        Log("Env name"+env.m_name);
        //    }
        //}


        [HarmonyPatch(typeof(Terminal), "InputText")]
        private static class InputText_Patch
        {
            // Token: 0x06000005 RID: 5 RVA: 0x000021B0 File Offset: 0x000003B0
            private static bool Prefix(Terminal __instance)
            {
                string text = __instance.m_input.text;
                if (text.ToLower().Equals("weathertweaks reload"))
                {
                    _instance.Config.Reload();
                    WeatherPatch.ReApply();
                    Traverse.Create(__instance).Method("AddString", new object[]
                    {
                        text
                    }).GetValue();
                    Traverse.Create(__instance).Method("AddString", new object[]
                    {
                        "Weather Tweaks config reloaded"
                    }).GetValue();
                    return false;
                }
                else if (text.ToLower().Equals("weathertweaks apply"))
                {
                    WeatherPatch.ReApply();
                    _instance.Config.SaveShort();
                    Traverse.Create(__instance).Method("AddString", new object[]
                    {
                        text
                    }).GetValue();
                    Traverse.Create(__instance).Method("AddString", new object[]
                    {
                        "Weather Tweaks config applied"
                    }).GetValue();
                    return false;
                }
                return true;
            }
        }
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        public static class TerminalInitConsole_Patch
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("weathertweaks", "with keyword 'reload': Reload config of WeatherTweaks. With keyword 'apply': Apply changes done in-game (Configuration Manager)", null);
            }
        }

    }

}
