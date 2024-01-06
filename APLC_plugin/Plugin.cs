﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using BepInEx;
using HarmonyLib;
using System.Collections.ObjectModel;
using System.Text;
using System.Timers;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Helpers;
using BepInEx.Configuration;
using GameNetcodeStuff;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements.Collections;
using Object = System.Object;
using Random = System.Random;

namespace APLC
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("Lethal Company.exe")]
    public class Plugin : BaseUnityPlugin
    {
        //Death link service for when death link is enabled
        private DeathLinkService dlService;
        
        //Harmony for patching
        private readonly Harmony _harmony = new(PluginInfo.PLUGIN_GUID);

        //Instance of the plugin for other classes to access
        public static Plugin _instance;

        //Maps the item names to a array of three integers
        //  0: The index in the array the item is(for example, 0 for walkie-talkie)
        //  1: The initial price of the item(since we overwrite it we want to store it somewhere so we can replace the insanely high price with this when the item is unlocked
        //  2: The type of item it is. 0 for shop items, 1 for ship upgrades, 2 for moons
        private Dictionary<String, int[]> itemMap = new Dictionary<string, int[]>();

        //Maps the name of the moon to its number because the terminal nodes that refer to moons are named based off of number and not their actual name 
        private Dictionary<String, String> moonNameMap = new Dictionary<String, String>();

        //Some code should only run once, like defining the itemMap, this makes that happen.
        private bool firstTimeSetup = true;

        //You only want to use things if they exist, this makes sure every object we need exists before messing with them
        private bool gameStarted = false;

        //Checks if an item has already been received, if it hasn't we set its index to true and do whatever is required to receive it
        private bool[] received = new bool[100];
        //Checks if a bestiary entry has already been checked so we don't spam the server with useless info.
        private bool[] checkedMonsters = new bool[17];
        //Same as above but for logs
        private bool[] checkedLogs = new bool[7];

        //The archipelago sesion
        private ArchipelagoSession session;
        
        //Sometimes we aren't ready to receive certain items, so we mark them as received and que them by incrementing these variables
        private int moneyItemsWaiting = 0;
        private int hauntItemsWaiting = 0;
        private int brackenItemsWaiting = 0;

        //Useful for when a client disconnects, then rejoins the APworld. We don't want them to receive all of the money items again because that would essentially lead to an infinite money glitch. 
        private int totalMoneyItems = 0;

        //Bool that checks if the player has successfully connected to the server
        private bool successfullyConnected = true;

        //Checks how many money items were received, if this ever gets higher than totalMoneyItems than 
        private int receivedMoneyItems = 0;

        //The amount of unlocked inventory slots
        private int invSlots = 1;

        //The total received quota so far
        private int totalQuota = 0;

        //The number of quota checks that have been met
        private int quotaChecksMet = 0;

        //These store settings from the yaml file that are stored in the slot.
        private bool inventoryLock = false;
        private bool deathLink = false;
        private int moneyPerQuotaCheck = 1000;
        private int numQuota = 20;
        private int checksPerMoon = 3;

        //The slot name as set from the .cfg
        private string slotName = "";

        //Stores all received items
        private Collection<string> newItems = new Collection<string>();

        //Stores how many times each moon has been completed on a B or higher
        private int[] moonChecks = new[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        
        /// <summary>
        /// Adds spaces before captial letters in a string
        /// </summary>
        /// <param name="text">The string to add spaces to</param>
        /// <returns>The modified string</returns>
        string AddSpacesToSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            StringBuilder newText = new StringBuilder(text.Length * 2);
            newText.Append(text[0]);
            for (int i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]))
                    if ((text[i - 1] != ' ' && !char.IsUpper(text[i - 1])))
                        newText.Append(' ');
                newText.Append(text[i]);
            }
            return newText.ToString();
        }

        /// <summary>
        /// Completes an archipelago location
        /// </summary>
        /// <param name="lName">The name of the location to complete</param>
        private void CompleteLocation(string lName)
        {
            long id = _instance.session.Locations.GetLocationIdFromName("Lethal Company", lName);
            Logger.LogWarning($"Location {lName} complete, id is {id}");
            _instance.session.Locations.CompleteLocationChecks(id);
        }

        /// <summary>
        /// Handles the reception of items from AP
        /// </summary>
        /// <param name="helper">The helped</param>
        private void ReceivedItem(ReceivedItemsHelper helper)
        {
            newItems.Add(helper.PeekItemName());
            CheckItems();
            helper.DequeueItem();
        }
        
        /// <summary>
        /// Probably should split this into two functions, as of right now what it does is it checks if certain locations have been completed and if certain checks have been received, then processes them.
        /// </summary>
        private void CheckItems()
        {
            if (!gameStarted || !successfullyConnected)
            {
                return;
            }
            try
            {
                for (int i = 0; i < newItems.Count; i++)
                {
                    if (newItems[i] == "Inventory Slot" && !received[i])
                    {
                        received[i] = true;
                        invSlots++;
                        GameNetworkManager.Instance.localPlayerController.ItemSlots = new GrabbableObject[invSlots];
                    }
                }

                if (!GameNetworkManager.Instance.isHostingGame)
                {
                    return;
                }
            }
            catch (Exception err)
            {
                Logger.LogError(err.StackTrace);
                return;
            }

            try
            {
                Terminal t = FindObjectOfType<Terminal>();

                Logger.LogWarning("Checking items");

                foreach (int mID in t.scannedEnemyIDs)
                {
                    if (!checkedMonsters[mID])
                    {
                        string eName = t.enemyFiles[mID].name;
                        Logger.LogWarning(eName);
                        eName = eName.Substring(0, eName.Length - 4);
                        Logger.LogWarning(eName);
                        string formatted;
                        if (eName == "CoilHead")
                        {
                            formatted = "Coil-Head";
                        }
                        else if (eName == "Snareflea")
                        {
                            formatted = "Snare Flea";
                        }
                        else if (eName == "Locust")
                        {
                            formatted = "Roaming Locust";
                        }
                        else if (eName == "Puffer")
                        {
                            formatted = "Spore Lizard";
                        }
                        else
                        {
                            formatted = AddSpacesToSentence(eName);
                        }

                        Logger.LogWarning($"Formatted name: {formatted}");
                        Logger.LogWarning($"Actual name: {eName}");
                        CompleteLocation($"Bestiary Entry - {formatted}");
                        checkedMonsters[mID] = true;
                    }
                }

                foreach (int mID in t.unlockedStoryLogs)
                {
                    if (!checkedLogs[mID])
                    {

                        string logName = "";

                        switch (mID)
                        {
                            case 0:
                                continue;
                            case 1:
                                logName = "Smells Here!";
                                break;
                            case 2:
                                logName = "Swing of Things";
                                break;
                            case 3:
                                logName = "Shady";
                                break;
                            case 4:
                                logName = "Sound Behind the Wall";
                                break;
                            case 5:
                                logName = "Screams";
                                break;
                            case 6:
                                logName = "Nonsense";
                                break;
                        }

                        Logger.LogWarning($"Log name: {logName}");

                        if (logName == "")
                        {
                            checkedLogs[mID] = true;
                            continue;
                        }

                        CompleteLocation($"Log - {logName}");

                        checkedLogs[mID] = true;
                    }
                }

                for (int i = 0; i < newItems.Count; i++)
                {
                    if (!received[i])
                    {
                        received[i] = true;
                        string itemName = newItems[i];
                        Logger.LogWarning($"Received item {itemName}");
                        if (itemName == "Money")
                        {
                            if (t != null && totalMoneyItems <= receivedMoneyItems)
                            {
                                t.groupCredits += new Random().Next(100, 1000);
                            }
                            else
                            {
                                if (totalMoneyItems <= receivedMoneyItems)
                                {
                                    moneyItemsWaiting++;
                                }
                            }

                            if (totalMoneyItems >= receivedMoneyItems)
                            {
                                totalMoneyItems++;
                            }

                            receivedMoneyItems++;
                            session.DataStorage["moneyChecksReceived"] = totalMoneyItems;
                        }

                        if (itemName == "HauntTrap")
                        {
                            hauntItemsWaiting++;
                        }

                        if (itemName == "BrackenTrap")
                        {
                            brackenItemsWaiting++;
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Logger.LogError(err.StackTrace);
            }
        }

        /// <summary>
        /// Runs when the game first boots up, connects to AP and sets up all the variables that need setup
        /// </summary>
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            for (int i = 0; i < received.Length; i++)
            {
                received[i] = false;
            }

            for (int i = 0; i < checkedMonsters.Length; i++)
            {
                checkedMonsters[i] = false;
            }
            
            for (int i = 0; i < checkedLogs.Length; i++)
            {
                checkedLogs[i] = false;
            }
            moonNameMap.Add("Experimentation","41");
            moonNameMap.Add("Assurance","220");
            moonNameMap.Add("Vow","56");
            moonNameMap.Add("Offense","21");
            moonNameMap.Add("March","61");
            moonNameMap.Add("Rend","85");
            moonNameMap.Add("Dine","7");
            moonNameMap.Add("Titan","8");
            
            APLCConfig.InitConfig();
            try
            {
                session = ArchipelagoSessionFactory.CreateSession(APLCConfig.AP_URL.Value, APLCConfig.AP_PORT.Value);
                slotName = APLCConfig.AP_SLOT.Value;
                string password = APLCConfig.AP_PASSWORD.Value;
                if (password == "")
                {
                    password = null;
                }

                LoginResult result =
                    session.TryConnectAndLogin("Lethal Company", slotName, ItemsHandlingFlags.AllItems,
                        new Version(0, 4, 4), new[] { "Death Link" }, password: password);

                if (!result.Successful)
                {
                    LoginFailure failure = (LoginFailure)result;
                    string errorMessage =
                        $"Failed to Connect to {APLCConfig.AP_URL.Value + ":" + APLCConfig.AP_PORT.Value} as {slotName}:";
                    foreach (string error in failure.Errors)
                    {
                        errorMessage += $"\n    {error}";
                    }

                    foreach (ConnectionRefusedError error in failure.ErrorCodes)
                    {
                        errorMessage += $"\n    {error}";
                    }

                    Logger.LogWarning(errorMessage);
                }

                LoginSuccessful successful = (LoginSuccessful)result;

                inventoryLock = Int32.Parse(successful.SlotData["inventorySlot"].ToString()) == 1;
                moneyPerQuotaCheck = Int32.Parse(successful.SlotData["moneyPerQuotaCheck"].ToString());
                numQuota = Int32.Parse(successful.SlotData["numQuota"].ToString());
                checksPerMoon = Int32.Parse(successful.SlotData["checksPerMoon"].ToString());
                deathLink = Int32.Parse(successful.SlotData["deathLink"].ToString()) == 1;
                Logger.LogWarning("Successfully collected settings");
            }
            catch (Exception err)
            {
                successfullyConnected = false;
                Logger.LogError("Couldn't connect to Archipelago. Are you sure your config is correct?");
                Logger.LogError(err.StackTrace);
                return;
            }

            if (deathLink)
            {
                dlService = session.CreateDeathLinkService();
                dlService.EnableDeathLink();
                dlService.OnDeathLinkReceived += link =>
                {
                    Random rng = new Random((int)link.Timestamp.Ticks);
                    int selected = rng.Next(0, GameNetworkManager.Instance.connectedPlayers);
                    if (GameNetworkManager.Instance.steamIdsInLobby[selected].Value ==
                        GameNetworkManager.Instance.localPlayerController.playerSteamId)
                    {
                        GameNetworkManager.Instance.localPlayerController.health = 0;
                    }
                };
            }
            
            session.DataStorage["moonChecks"].Initialize(new JArray(moonChecks));
            session.DataStorage["totalQuota"].Initialize(totalQuota);
            session.DataStorage["quotaChecksMet"].Initialize(quotaChecksMet);
            session.DataStorage["moneyChecksReceived"].Initialize(totalMoneyItems);

            moonChecks = session.DataStorage["moonChecks"];
            totalQuota = session.DataStorage["totalQuota"];
            quotaChecksMet = session.DataStorage["quotaChecksMet"];
            totalMoneyItems = session.DataStorage["moneyChecksReceived"];

            session.Items.ItemReceived += ReceivedItem;

            _harmony.PatchAll(typeof(Plugin));
            
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Terminal), "BeginUsingTerminal")]
        static void TerminalStartPrefix(Terminal __instance)
        {
            if (!_instance.successfullyConnected)
            {
                return;
            }
            _instance.Setup(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HUDManager), "FillEndGameStats")]
        static void GradingPostfix()
        {
            string grade = HUDManager.Instance.statsUIElements.gradeLetter.text;
            bool dead = StartOfRound.Instance.allPlayersDead;
            _instance.Logger.LogWarning(dead);
            if (dead)
            {
                _instance.dlService.SendDeathLink(new DeathLink(_instance.slotName, "failed the company."));
            }
            _instance.Logger.LogWarning($"Completed planet {StartOfRound.Instance.currentLevel.PlanetName} with grade {grade}");
            if (grade == "S" || grade == "A" || grade == "B")
            {
                string moon = StartOfRound.Instance.currentLevel.PlanetName.Split(" ")[1];
                _instance.Logger.LogWarning(moon);
                int checkNum = 0;
                string[] moonNames = new[]
                    { "Experimentation", "Assurance", "Vow", "Offense", "March", "Rend", "Dine", "Titan" };
                int i = Array.IndexOf(moonNames, moon);
                checkNum = _instance.moonChecks[i];
                if (checkNum < _instance.checksPerMoon)
                {
                    checkNum++;
                    _instance.moonChecks[i]++;
                    _instance.session.DataStorage["moonChecks"] = new JArray(_instance.moonChecks);
                    _instance.CompleteLocation($"{moon} check {checkNum}");
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
        static void LockInventorySlotsExcept1(PlayerControllerB __instance)
        {
            if (_instance.inventoryLock)
            {
                __instance.ItemSlots = new GrabbableObject[1];
            }

            _instance.gameStarted = true;
            _instance.CheckItems();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StartOfRound), "EndOfGame")]
        static void RoundEndPrefix(StartOfRound __instance)
        {
            if (GameNetworkManager.Instance.isHostingGame && _instance.quotaChecksMet < _instance.numQuota)
            {
                if ((float)(TimeOfDay.Instance.profitQuota - TimeOfDay.Instance.quotaFulfilled) <= 0f)
                {
                    _instance.totalQuota += TimeOfDay.Instance.profitQuota;
                    _instance.session.DataStorage["totalQuota"] = _instance.totalQuota;
                    while ((_instance.quotaChecksMet+1) * _instance.moneyPerQuotaCheck < _instance.totalQuota)
                    {
                        _instance.quotaChecksMet++;
                        _instance.session.DataStorage["quoatChecksMet"] = _instance.quotaChecksMet;
                        _instance.CompleteLocation($"Quota check {_instance.quotaChecksMet}");
                    }
                }
            }
        }
        
        private void Setup(Terminal t)
        {
            CheckItems();
            Logger.LogWarning("Setup ran!");
            Item[] items = t.buyableItemsList;
            
            for(int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (firstTimeSetup && !itemMap.ContainsKey(item.itemName))
                {
                    itemMap.Add(item.itemName, new[] { i, item.creditsWorth, 0 });
                }
                item.creditsWorth = 10000000;
            }

            if (firstTimeSetup)
            {
                ReadOnlyCollection<NetworkItem> apItems = session.Items.AllItemsReceived;
                foreach(NetworkItem item in apItems)
                {
                    newItems.Add(session.Items.GetItemName(item.Item));
                }
                CheckItems();
            }

            CompatibleNoun[] nouns = t.terminalNodes.allKeywords[26].compatibleNouns;
            
            for(int i = 0; i < nouns.Length; i++)
            {
                CompatibleNoun noun = nouns[i];
                if (noun.result.name == "CompanyMoonroute")
                {
                    continue;
                }
                if (firstTimeSetup && !itemMap.ContainsKey(noun.result.name.Substring(0, noun.result.name.Length - 5)))
                {
                    itemMap.Add(noun.result.name.Substring(0, noun.result.name.Length - 5),
                        new[] { i, noun.result.itemCost, 2 });
                }
                noun.result.itemCost = 10000000;
                noun.result.terminalOptions[1].result.itemCost = 10000000;
            }

            nouns = t.terminalNodes.allKeywords[0].compatibleNouns;

            for (int i = 0; i < nouns.Length; i++)
            {
                CompatibleNoun noun = nouns[i];
                int ind = Array.IndexOf(new[] { "SignalTranslatorBuy", "InverseTeleporterBuy" },
                    noun.result.name);
                if(ind != -1)
                {
                    if (firstTimeSetup)
                    {
                        if (!itemMap.ContainsKey(noun.result.name.Substring(0, noun.result.name.Length - 3)))
                        {
                            itemMap.Add(noun.result.name.Substring(0, noun.result.name.Length - 3),
                                new[] { i, noun.result.itemCost, 1 });
                        }
                    }
                    noun.result.itemCost = 10000000;
                }
                ind = Array.IndexOf(new[] { "TeleporterBuy1", "LoudHornBuy1" },
                    noun.result.name);
                if(ind != -1)
                {
                    if (firstTimeSetup && !itemMap.ContainsKey(noun.result.name.Substring(0, noun.result.name.Length - 4)))
                    {
                        itemMap.Add(noun.result.name.Substring(0, noun.result.name.Length - 4),
                            new[] { i, noun.result.itemCost, 1 });
                    }
                    noun.result.itemCost = 10000000;
                }
            }

            firstTimeSetup = false;

            foreach (NetworkItem item in session.Items.AllItemsReceived)
            {
                long itemId = item.Item;
                string itemName = session.Items.GetItemName(itemId);
                if (moonNameMap.ContainsKey(itemName))
                {
                    itemName = moonNameMap.Get(itemName);
                }
                if (itemMap.ContainsKey(itemName))
                {
                    int[] data = itemMap.Get(itemName);
                    if (data[2] == 0)
                    {
                        items[data[0]].creditsWorth = data[1];
                    }

                    if (data[2] == 1)
                    {
                        t.terminalNodes.allKeywords[0].compatibleNouns[data[0]].result.itemCost = data[1];
                    }

                    if (data[2] == 2)
                    {
                        t.terminalNodes.allKeywords[26].compatibleNouns[data[0]].result.itemCost = 0;
                        t.terminalNodes.allKeywords[26].compatibleNouns[data[0]].result.terminalOptions[1].result
                            .itemCost = 0;
                    }
                }
            }
        }
        public void BindConfig<T>(ref ConfigEntry<T> config, string section, string key, T defaultValue, string description = "")
        {
            config = base.Config.Bind<T>(section, key, defaultValue, description);
        }
    }
    
    internal class APLCConfig
	{
		// Token: 0x0600000C RID: 12 RVA: 0x00002408 File Offset: 0x00000608
		public static void InitConfig()
		{
			Plugin._instance.BindConfig<string>(ref APLCConfig.AP_URL, "AP Config", "AP URL", "archipelago.gg", "");
            Plugin._instance.BindConfig<int>(ref APLCConfig.AP_PORT, "AP Config", "AP Port", 31415, "");
            Plugin._instance.BindConfig<string>(ref APLCConfig.AP_SLOT, "AP Config", "AP Slot", "Player1", "");
            Plugin._instance.BindConfig<string>(ref APLCConfig.AP_PASSWORD, "AP Config", "AP Password", "", "");
        }

		public static ConfigEntry<string> AP_URL;

		public static ConfigEntry<int> AP_PORT;

		public static ConfigEntry<string> AP_SLOT;

		public static ConfigEntry<string> AP_PASSWORD;
	}
}
