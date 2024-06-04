﻿using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using BepInEx;
using System.Collections.ObjectModel;
// using Amrv.ConfigurableCompany;
// using Amrv.ConfigurableCompany.API;
using LethalAPI.LibTerminal;
using LethalAPI.LibTerminal.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements.Collections;

namespace APLC;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInProcess("Lethal Company.exe")]
//[BepInDependency(ConfigAPI.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)] 
public class Plugin : BaseUnityPlugin
{
    //Instance of the plugin for other classes to access
    public static Plugin _instance;
    public static float carryWeight;
    public static float initialWeight;
    // public static CConfig url;
    // public static CConfig port;
    // public static CConfig slot;
    // public static CConfig password;
    
    /**
     * Patches the game on startup, injecting the code into the game.
     */
    private void Awake()
    {
        if (_instance == null) _instance = this;
        
        Patches.Patch();
        TerminalCommands.Patch();
    
        LogInfo("Plugin APLC Loaded");
    }

    /**
     * Gets the terminal object for editing(needs to be here because only monobehaviors can findobjectoftype)
     */
    public Terminal getTerminal()
    {
        return FindObjectOfType<Terminal>();
    }

    /**
     * Logs a warning to the console
     */
    public void LogWarning(string message)
    {
        Logger.LogWarning(message);
    }
    
    /**
     * Logs info to the console
     */
    public void LogInfo(string message)
    {
        Logger.LogInfo(message);
    }
    
    /**
     * Logs an error to the console
     */
    public void LogError(string message)
    {
        Logger.LogError(message);
    }

    public string GetGameLogicString()
    {
        Terminal t = getTerminal();
        /*
         * {
         *      moons: [],
         *      logs: [
         *          {
         *              log_name: name,
         *              moons: [moon1, moon2]
         *          }
         *      ],
         *      bestiary: [
         *          {
         *              monster_name: name,
         *              moons: [
         *                  {
         *                      moon_name: moon
         *                      spawn_prob: prob
         *                  }
         *              ]
         *          }
         *      ],
         *      store: [
         *          item1
         *          item2
         *      ],
         *      scrap: [
         *          {
         *              scrap_name: name,
         *              moons: [
         *                  {
         *                      moon_name: moon
         *                      spawn_prob: prob
         *                  }
         *              ]
         *          }
         *      ]
         * }
         */
        var store = t.buyableItemsList;
        string json = @"{
    ""moons"": [
";
        var moons = StartOfRound.Instance.levels;
        
        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;
            json += "        \"" + moon.PlanetName + "\",\n";
        }
        
        json += @"    ],
    ""store"": [
";
        foreach (Item item in store)
        {
            json += "        \"" + item.itemName + "\",\n";
        }
        
        json += @"    ],
    ""scrap"": [
";

        var scrapMap = new Dictionary<string, Collection<Tuple<string, double>>>();
        scrapMap.Add("Apparatus", new Collection<Tuple<string, double>>());
        scrapMap.Add("Shotgun", new Collection<Tuple<string, double>>());
        scrapMap.Add("Knife", new Collection<Tuple<string, double>>());
        scrapMap.Add("Hive", new Collection<Tuple<string, double>>());

        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.spawnableScrap;
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {

                scrapMap.TryAdd(item.spawnableItem.itemName, new Collection<Tuple<string, double>>());
                var checkMoons = scrapMap.Get(item.spawnableItem.itemName);
                bool existsAlready = false;
                
                for (var index = 0; index < checkMoons.Count; index++)
                {
                    var entry = checkMoons[index];
                    if (entry.Item1 == moon.PlanetName)
                    {
                        checkMoons[index] = new Tuple<string, double>(entry.Item1,
                            entry.Item2 + (double)item.rarity / totalRarity);
                        existsAlready = true;
                    }
                }

                if (!existsAlready)
                {
                    scrapMap.Get(item.spawnableItem.itemName)
                        .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                }
            }

            int totalIntRarity = 0;
            int facilityRarity = 0;
            foreach (var interior in moon.dungeonFlowTypes)
            {
                totalIntRarity += interior.rarity;
                if (interior.id == 0)
                {
                    facilityRarity = interior.rarity;
                }
            }

            if (Double.IsNaN((double)facilityRarity / totalIntRarity))
            {
                totalIntRarity = 1;
                facilityRarity = 1;
            }
            scrapMap.Get("Apparatus").Add(new Tuple<string, double>(moon.PlanetName, (double)facilityRarity/totalIntRarity));
        }
        
        var bestiaryMap = new Dictionary<string, Collection<Tuple<string, double>>>();
        
        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.DaytimeEnemies;
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {
                if (!item.enemyType.enemyName.Contains("Lasso"))
                {
                    try
                    {
                        string name = t.enemyFiles[
                                item.enemyType.enemyPrefab.GetComponentInChildren<ScanNodeProperties>()
                                    .creatureScanID]
                            .creatureName;
                        if (name[name.Length - 1] == 's')
                        {
                            name = name.Substring(0, name.Length - 1);
                        }
                        bestiaryMap.TryAdd(name, new Collection<Tuple<string, double>>());
                        
                        var checkMoons = bestiaryMap.Get(name);
                        bool existsAlready = false;
                        
                        for (var index = 0; index < checkMoons.Count; index++)
                        {
                            var entry = checkMoons[index];
                            if (entry.Item1 == moon.PlanetName)
                            {
                                checkMoons[index] = new Tuple<string, double>(entry.Item1,
                                    entry.Item2 + (double)item.rarity / totalRarity);
                                existsAlready = true;
                            }
                        }

                        if (!existsAlready)
                        {
                            bestiaryMap.Get(name)
                                .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore exception
                    }

                    if (item.enemyType.enemyName.Contains("Nutcracker"))
                    {
                        scrapMap.Get("Shotgun").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Butler"))
                    {
                        scrapMap.Get("Knife").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Red Locust"))
                    {
                        scrapMap.Get("Hive").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                }
            }
        }
        
        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.OutsideEnemies;
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {
                if (!item.enemyType.enemyName.Contains("Lasso"))
                {
                    try
                    {
                        string name = t.enemyFiles[
                                item.enemyType.enemyPrefab.GetComponentInChildren<ScanNodeProperties>()
                                    .creatureScanID]
                            .creatureName;
                        if (name[name.Length - 1] == 's')
                        {
                            name = name.Substring(0, name.Length - 1);
                        }
                        bestiaryMap.TryAdd(name, new Collection<Tuple<string, double>>());
                        
                        var checkMoons = bestiaryMap.Get(name);
                        bool existsAlready = false;
                        
                        for (var index = 0; index < checkMoons.Count; index++)
                        {
                            var entry = checkMoons[index];
                            if (entry.Item1 == moon.PlanetName)
                            {
                                checkMoons[index] = new Tuple<string, double>(entry.Item1,
                                    entry.Item2 + (double)item.rarity / totalRarity);
                                existsAlready = true;
                            }
                        }

                        if (!existsAlready)
                        {
                            bestiaryMap.Get(name)
                                .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore exception
                    }

                    if (item.enemyType.enemyName.Contains("Nutcracker"))
                    {
                        scrapMap.Get("Shotgun").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Butler"))
                    {
                        scrapMap.Get("Knife").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Red Locust"))
                    {
                        scrapMap.Get("Hive").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                }
            }
        }
        
        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.Enemies;
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {
                if (!item.enemyType.enemyName.Contains("Lasso"))
                {
                    try
                    {
                        string name = t.enemyFiles[
                                item.enemyType.enemyPrefab.GetComponentInChildren<ScanNodeProperties>()
                                    .creatureScanID]
                            .creatureName;
                        if (name[name.Length - 1] == 's')
                        {
                            name = name.Substring(0, name.Length - 1);
                        }
                        bestiaryMap.TryAdd(name, new Collection<Tuple<string, double>>());
                        
                        var checkMoons = bestiaryMap.Get(name);
                        bool existsAlready = false;
                        
                        for (var index = 0; index < checkMoons.Count; index++)
                        {
                            var entry = checkMoons[index];
                            if (entry.Item1 == moon.PlanetName)
                            {
                                checkMoons[index] = new Tuple<string, double>(entry.Item1,
                                    entry.Item2 + (double)item.rarity / totalRarity);
                                existsAlready = true;
                            }
                        }

                        if (!existsAlready)
                        {
                            bestiaryMap.Get(name)
                                .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore exception
                    }

                    if (item.enemyType.enemyName.Contains("Nutcracker"))
                    {
                        scrapMap.Get("Shotgun").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Butler"))
                    {
                        scrapMap.Get("Knife").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Red Locust"))
                    {
                        scrapMap.Get("Hive").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                }
            }
        }
        
        foreach (string key in scrapMap.Keys)
        {
            json += @$"        {{
            ""{key}"": [
{CreateScrapJSON(scrapMap.Get(key))}
            ]
        }},
";
        }

        json += @"    ],
    ""bestiary"": [
";
        
        foreach (string key in bestiaryMap.Keys)
        {
            json += @$"        {{
            ""{key}"": [
{CreateScrapJSON(bestiaryMap.Get(key))}
            ]
        }},
";
        }


        json += "    ]";

        return json + "\n}";
    }

    private string CreateScrapJSON(Collection<Tuple<string, double>> scrapData)
    {
        var str = "";
        for (var index = 0; index < scrapData.Count; index++)
        {
            var moon = scrapData[index];
            str += $@"                {{
                    ""moon_name"": ""{moon.Item1}"",
                    ""chance"": {moon.Item2}
                }}";
            if (index < scrapData.Count - 1)
            {
                str += ",";
            }

            str += "\n";
        }

        return str;
    }
    
    public Tuple<Item[], SelectableLevel[], Dictionary<string, Collection<Tuple<string, double>>>, Dictionary<string, Collection<Tuple<string, double>>>> GetGameLogic()
    {
        Terminal t = getTerminal();
        /*
         * {
         *      moons: [],
         *      logs: [
         *          {
         *              log_name: name,
         *              moons: [moon1, moon2]
         *          }
         *      ],
         *      bestiary: [
         *          {
         *              monster_name: name,
         *              moons: [
         *                  {
         *                      moon_name: moon
         *                      spawn_prob: prob
         *                  }
         *              ]
         *          }
         *      ],
         *      store: [
         *          item1
         *          item2
         *      ],
         *      scrap: [
         *          {
         *              scrap_name: name,
         *              moons: [
         *                  {
         *                      moon_name: moon
         *                      spawn_prob: prob
         *                  }
         *              ]
         *          }
         *      ]
         * }
         */
        var store = t.buyableItemsList;
        
        var moons = StartOfRound.Instance.levels;

        var scrapMap = new Dictionary<string, Collection<Tuple<string, double>>>();
        scrapMap.Add("Apparatus", new Collection<Tuple<string, double>>());
        scrapMap.Add("Shotgun", new Collection<Tuple<string, double>>());
        scrapMap.Add("Knife", new Collection<Tuple<string, double>>());
        scrapMap.Add("Hive", new Collection<Tuple<string, double>>());

        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.spawnableScrap;
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {
                scrapMap.TryAdd(item.spawnableItem.itemName, new Collection<Tuple<string, double>>());
                var checkMoons = scrapMap.Get(item.spawnableItem.itemName);
                bool existsAlready = false;
                
                for (var index = 0; index < checkMoons.Count; index++)
                {
                    var entry = checkMoons[index];
                    if (entry.Item1 == moon.PlanetName)
                    {
                        checkMoons[index] = new Tuple<string, double>(entry.Item1,
                            entry.Item2 + (double)item.rarity / totalRarity);
                        existsAlready = true;
                    }
                }

                if (!existsAlready)
                {
                    scrapMap.Get(item.spawnableItem.itemName)
                        .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                }
            }

            int totalIntRarity = 0;
            int facilityRarity = 0;
            foreach (var interior in moon.dungeonFlowTypes)
            {
                totalIntRarity += interior.rarity;
                if (interior.id == 0)
                {
                    facilityRarity = interior.rarity;
                }
            }

            if (Double.IsNaN((double)facilityRarity / totalIntRarity))
            {
                totalIntRarity = 1;
                facilityRarity = 1;
            }
            scrapMap.Get("Apparatus").Add(new Tuple<string, double>(moon.PlanetName, (double)facilityRarity/totalIntRarity));
        }
        
        var bestiaryMap = new Dictionary<string, Collection<Tuple<string, double>>>();
        
        foreach (SelectableLevel moon in moons)
        {
            if (moon.PlanetName.Contains("Gordion") || moon.PlanetName.Contains("Liquidation")) continue;

            var scrap = moon.DaytimeEnemies.Concat(moon.OutsideEnemies).Concat(moon.Enemies);
            int totalRarity = 0;
            foreach (var item in scrap)
            {
                totalRarity += item.rarity;
            }
            foreach (var item in scrap)
            {
                if (!item.enemyType.enemyName.Contains("Lasso"))
                {
                    try
                    {
                        string name = t.enemyFiles[
                                item.enemyType.enemyPrefab.GetComponentInChildren<ScanNodeProperties>()
                                    .creatureScanID]
                            .creatureName;
                        if (name[name.Length - 1] == 's')
                        {
                            name = name.Substring(0, name.Length - 1);
                        }
                        bestiaryMap.TryAdd(name, new Collection<Tuple<string, double>>());
                        bool existsAlready = false;
                        var checkMoons = bestiaryMap.Get(name);
                        for (var index = 0; index < checkMoons.Count; index++)
                        {
                            var entry = checkMoons[index];
                            if (entry.Item1 == moon.PlanetName)
                            {
                                checkMoons[index] = new Tuple<string, double>(entry.Item1,
                                    entry.Item2 + (double)item.rarity / totalRarity);
                                existsAlready = true;
                            }
                        }

                        if (!existsAlready)
                        {
                            bestiaryMap.Get(name)
                                .Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore exception
                    }

                    if (item.enemyType.enemyName.Contains("Nutcracker"))
                    {
                        scrapMap.Get("Shotgun").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Butler"))
                    {
                        scrapMap.Get("Knife").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                    if (item.enemyType.enemyName.Contains("Red Locust"))
                    {
                        scrapMap.Get("Hive").Add(new Tuple<string, double>(moon.PlanetName, (double)item.rarity / totalRarity));
                    }
                }
            }
        }

        return new Tuple<Item[], SelectableLevel[], Dictionary<string, Collection<Tuple<string, double>>>, Dictionary<string, Collection<Tuple<string, double>>>>(store, moons, bestiaryMap, scrapMap);
    }
}