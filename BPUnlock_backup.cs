using System;
using UnityEngine;
using Oxide.Core;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("BPUnlock", "Death", "1.0.6")]
    class BPUnlock : RustPlugin
    {
        #region Declarations
        Dictionary<string, ItemCategory> Categories = new Dictionary<string, ItemCategory>();
        List<BasePlayer> Queue = new List<BasePlayer>();

        const string perm = "bpunlock.admin";
        const string usage = "Usage: bpunlock <unlock or lock> <group> <item shortname or category name>";

        ItemCategory category;
        Timer queuedBPs;
        #endregion

        #region Hooks
        void OnServerInitialized()
        {
            LoadData();
            permission.RegisterPermission(perm, this);

            // Grab groups to populate data file.
            foreach (var group in permission.GetGroups())
            {
                if (data != null && !data.BlueprintData.ContainsKey(group))
                {
                    data.BlueprintData.Add(group, new List<string>());
                }
            }
            //

            // Cache ItemCategories alongside an index for later use.
            foreach (var cat in Enum.GetValues(typeof(ItemCategory)))
            {
            if(cat != null)
                Categories.Add(cat.ToString().ToLower(), (ItemCategory)cat);
            }
            //
        }

        // Sync BPs automatically when player is moved into a new user group.
        void OnUserGroupAdded(string id, string groupName)
        {
            if (!Categories.ContainsKey(groupName))
            {
                return;
            }

            UpdatePlayerBlueprints(BasePlayer.Find(id));
        }
        //

        void Unload()
        {
            SaveData();
        }

        void OnPlayerConnected(BasePlayer player)
        {
if(player == null) return;
            AddQueueManager(player);
        }
        #endregion

        #region Functions

        #region Commands
        [ConsoleCommand("bpunlock")]
        void ConsoleCommand(ConsoleSystem.Arg arg, ItemDefinition item)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, perm))
            {
                return;
            }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith(usage);
                return;
            }

            // Extra switch for special commands. Will update this method later to be more dynamic.
            switch (arg.Args[0])
            {
                // Only needed if bps are added to a group
                case "update":
                    UpdateOnlinePlayers();
                    arg.ReplyWith("Blueprints have been synced for online players.");
                    return;

                // Only needed if you made changes to the data file manually
                case "reload":
                    LoadData();
                    arg.ReplyWith("Data file changes have been applied.");
                    return;
            }
            //

            if (arg.Args.Length != 3)
            {
                arg.ReplyWith(usage);
                return;
            }

            var group = arg.Args[1];

            if (!data.BlueprintData.ContainsKey(group))
            {
                arg.ReplyWith($"Error: {group} is not a registered group!");
                return;
            }

            item = ItemManager.FindItemDefinition(arg.Args[2]);

            if (item == null && !Categories.TryGetValue(arg.Args[2], out category))
            {
                arg.ReplyWith("Error: Provided item or category is invalid!");
                return;
            }

            var IsItem = item != null;

            switch (arg.Args[0])
            {
                case "unlock":
                    if (IsItem)
                    {
                        if (data != null && data.BlueprintData[group].Contains(item.shortname))                         {
                            arg.ReplyWith($"{group} already has {item.displayName.english} unlocked.");
                            return;
                        }

                        data.BlueprintData[group].Add(item.shortname);
                        arg.ReplyWith($"{group} now has {item.displayName.english} unlocked.");
                    }
                    else
                    {
                        foreach (var bp in ItemManager.GetBlueprints())
                        {
                            if (bp.targetItem.category == category || category == ItemCategory.All)
                            {
                                if (data.BlueprintData[group].Contains(bp.targetItem.shortname))
                                {
                                    continue;
                                }

                                data.BlueprintData[group].Add(bp.targetItem.shortname);
                            }
                        }

                        arg.ReplyWith($"{group} now has all {category} blueprints unlocked.");
                    }
                    break;

                case "lock":
                    if (IsItem)
                    {
                        if (!data.BlueprintData[group].Contains(item.shortname))
                        {
                            arg.ReplyWith($"{group} does not have {item.displayName.english} unlocked.");
                            return;
                        }

                        data.BlueprintData[group].Add(item.shortname);
                    }
                    else
                    {
                        foreach (var bp in ItemManager.GetBlueprints())
                        {
                            if (bp.targetItem.category == category || category == ItemCategory.All)
                            {
                                if (!data.BlueprintData[group].Contains(bp.targetItem.shortname))
                                {
                                    continue;
                                }

                                data.BlueprintData[group].Remove(bp.targetItem.shortname);
                            }
                        }

                        arg.ReplyWith($"{group} now has all {category} blueprints locked.");
                    }
                    break;

                default:
                    arg.ReplyWith($"Error: {arg.Args[0]} is not a valid argument!");
                    return;
            }

            SaveData();
        }
        #endregion

        // Invokes AddQueueManager from OnPlayerConnected to add player to queue for BP updates
        void UpdateOnlinePlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
if(player == null) return;
                OnPlayerConnected(player);
            }
        }

        // Invokes from QueueManager to update a players BPs
        void UpdatePlayerBlueprints(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            // Optimized method for unlocking BPs without writing to database each iteriation.
            var playerInfo = player.PersistantPlayerInfo;

            foreach (var dir in data.BlueprintData)
            {
    if (!permission.UserHasGroup(player.UserIDString, dir.Key))
                {
                continue;
                }

                foreach (var bluePrint in data.BlueprintData[dir.Key])
                {
                    var bp = ItemManager.FindItemDefinition(bluePrint);

if(bp == null) return;

                    if (playerInfo.unlockedItems.Contains(bp.itemid))
                    {
                        continue;
                    }

                    playerInfo.unlockedItems.Add(bp.itemid);
                    player.ClientRPCPlayer(null, player, "UnlockedBlueprint", bp.itemid);

                    player.stats.Add("blueprint_studied", 1);
                }
            }

            ServerMgr.Instance.persistance.SetPlayerInfo(player.userID, playerInfo);
            player.SendNetworkUpdateImmediate();
        }

        // Queue players for BP updates for better performance
        void AddQueueManager(BasePlayer player)
        {
            if (player == null || Queue.Contains(player))
            {
                return;
            }

            Queue.Add(player);

            if (queuedBPs == null)
            {
                queuedBPs = timer.Every(0.5f, () =>
                {
                    if (Queue.Count <= 0)
                    {
                        queuedBPs.Destroy();
                        queuedBPs = null;

                        return;
                    }

                    UpdatePlayerBlueprints(Queue[0]);
                    Queue.RemoveAt(0);
                });
            }
        }

        #region Data
        Data data;

        class Data
        {
            public Dictionary<string, List<string>> BlueprintData = new Dictionary<string, List<string>>();
        }

        void LoadData()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<Data>("bpunlock");
        }

        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("bpunlock", data);
        }
        #endregion

        #endregion
    }
}
