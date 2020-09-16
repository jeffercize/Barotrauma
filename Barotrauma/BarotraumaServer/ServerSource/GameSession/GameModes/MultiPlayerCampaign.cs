﻿using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        private readonly List<CharacterCampaignData> characterData = new List<CharacterCampaignData>();

        private bool forceMapUI;
        public bool ForceMapUI
        {
            get { return forceMapUI; }
            set
            {
                if (forceMapUI == value) { return; }
                forceMapUI = value;
                LastUpdateID++;
            }
        }

        public bool GameOver { get; private set; }

        public override bool Paused
        {
            get { return ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"); }
        }

        public static void StartNewCampaign(string savePath, string subPath, string seed)
        {
            if (string.IsNullOrWhiteSpace(savePath)) { return; }

            GameMain.GameSession = new GameSession(new SubmarineInfo(subPath), savePath, GameModePreset.MultiPlayerCampaign, seed);
            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            SaveUtil.SaveGame(GameMain.GameSession.SavePath);

            DebugConsole.NewMessage("Campaign started!", Color.Cyan);
            DebugConsole.NewMessage("Current location: " + GameMain.GameSession.Map.CurrentLocation.Name, Color.Cyan);
            ((MultiPlayerCampaign)GameMain.GameSession.GameMode).LoadInitialLevel();
        }

        public static void LoadCampaign(string selectedSave)
        {
            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            SaveUtil.LoadGame(selectedSave);
            ((MultiPlayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
            DebugConsole.NewMessage("Campaign loaded!", Color.Cyan);
            DebugConsole.NewMessage(
                GameMain.GameSession.Map.SelectedLocation == null ?
                GameMain.GameSession.Map.CurrentLocation.Name :
                GameMain.GameSession.Map.CurrentLocation.Name + " -> " + GameMain.GameSession.Map.SelectedLocation.Name, Color.Cyan);
        }

        protected override void LoadInitialLevel()
        {
            NextLevel = map.SelectedConnection?.LevelData ?? map.CurrentLocation.LevelData;
            MirrorLevel = false;
            GameMain.Server.StartGame();
        }

        public static void StartCampaignSetup()
        {
            DebugConsole.NewMessage("********* CAMPAIGN SETUP *********", Color.White);
            DebugConsole.ShowQuestionPrompt("Do you want to start a new campaign? Y/N", (string arg) =>
            {
                if (arg.Equals("y", StringComparison.OrdinalIgnoreCase) || arg.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.ShowQuestionPrompt("Enter a save name for the campaign:", (string saveName) =>
                    {
                        string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                        StartNewCampaign(savePath, GameMain.NetLobbyScreen.SelectedSub.FilePath, GameMain.NetLobbyScreen.LevelSeed);
                    });
                }
                else
                {
                    var saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer, includeInCompatible: false).ToArray();
                    if (saveFiles.Length == 0)
                    {
                        DebugConsole.ThrowError("No save files found.");
                        return;
                    }
                    DebugConsole.NewMessage("Saved campaigns:", Color.White);
                    for (int i = 0; i < saveFiles.Length; i++)
                    {
                        DebugConsole.NewMessage("   " + i + ". " + saveFiles[i], Color.White);
                    }
                    DebugConsole.ShowQuestionPrompt("Select a save file to load (0 - " + (saveFiles.Length - 1) + "):", (string selectedSave) =>
                    {
                        int saveIndex = -1;
                        if (!int.TryParse(selectedSave, out saveIndex)) { return; }

                        if (saveIndex < 0 || saveIndex >= saveFiles.Length)
                        {
                            DebugConsole.ThrowError("Invalid save file index.");
                        }
                        else
                        {
                            LoadCampaign(saveFiles[saveIndex]);
                        }
                    });
                }
            });
        }

        public override void Start()
        {
            base.Start();
            lastUpdateID++;
        }

        private static bool IsOwner(Client client) => client != null && client.Connection == GameMain.Server.OwnerConnection;

        /// <summary>
        /// There is a client-side implementation of the method in <see cref="CampaignMode"/>
        /// </summary>
        public bool AllowedToEndRound(Client client)
        {
            //allow ending the round if the client has permissions, is the owner, the only client in the server,
            //or if no-one has permissions
            return
                client.HasPermission(ClientPermissions.ManageRound) ||
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                GameMain.Server.ConnectedClients.None(c =>
                    c.InGame && (IsOwner(c) || c.HasPermission(ClientPermissions.ManageRound) || c.HasPermission(ClientPermissions.ManageCampaign)));
        }

        /// <summary>
        /// There is a client-side implementation of the method in <see cref="CampaignMode"/>
        /// </summary>
        public bool AllowedToManageCampaign(Client client)
        {
            //allow ending the round if the client has permissions, is the owner, or the only client in the server,
            //or if no-one has management permissions
            return
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                GameMain.Server.ConnectedClients.None(c =>
                    c.InGame && (IsOwner(c) || c.HasPermission(ClientPermissions.ManageCampaign)));
        }

        protected override IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults)
        {
            lastUpdateID++;

            switch (transitionType)
            {
                case TransitionType.None:
                    throw new InvalidOperationException("Level transition failed (no transitions available).");
                case TransitionType.ReturnToPreviousLocation:
                    //deselect destination on map
                    map.SelectLocation(-1);
                    break;
                case TransitionType.ProgressToNextLocation:
                    Map.MoveToNextLocation();
                    Map.ProgressWorld();
                    break;
                case TransitionType.End:
                    EndCampaign();
                    IsFirstRound = true;
                    break;
            }

            bool success = GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);

            GameMain.GameSession.EndRound("", traitorResults, transitionType);
            
            //--------------------------------------

            if (success)
            {
                List<CharacterCampaignData> prevCharacterData = new List<CharacterCampaignData>(characterData);
                //client character has spawned this round -> remove old data (and replace with an up-to-date one if the client still has a character)
                characterData.RemoveAll(cd => cd.HasSpawned);

                //refresh the character data of clients who are still in the server
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (c.Character?.Info == null) { continue; }
                    if (c.Character.IsDead && c.Character.CauseOfDeath?.Type != CauseOfDeathType.Disconnected) { continue; }
                    c.CharacterInfo = c.Character.Info;
                    characterData.RemoveAll(cd => cd.MatchesClient(c));
                    characterData.Add(new CharacterCampaignData(c));                    
                }

                //refresh the character data of clients who aren't in the server anymore
                foreach (CharacterCampaignData data in prevCharacterData)
                {
                    if (data.HasSpawned && !characterData.Any(cd => cd.IsDuplicate(data)))
                    {
                        var character = Character.CharacterList.Find(c => c.Info == data.CharacterInfo);
                        if (character != null && (!character.IsDead || character.CauseOfDeath?.Type == CauseOfDeathType.Disconnected))
                        {
                            data.Refresh(character);
                            characterData.Add(data);
                        }
                    }
                }

                characterData.ForEach(cd => cd.HasSpawned = false);

                //remove all items that are in someone's inventory
                foreach (Character c in Character.CharacterList)
                {
                    if (c.Inventory == null) { continue; }
                    if (Level.Loaded.Type == LevelData.LevelType.Outpost && c.Submarine != Level.Loaded.StartOutpost)
                    {
                        Map.CurrentLocation.RegisterTakenItems(c.Inventory.Items.Where(it => it != null && it.SpawnedInOutpost && it.OriginalModuleIndex > 0).Distinct());
                    }

                    if (c.Info != null && c.IsBot)
                    {
                        if (c.IsDead && c.CauseOfDeath?.Type != CauseOfDeathType.Disconnected) { CrewManager.RemoveCharacterInfo(c.Info); }
                        c.Info.HealthData = new XElement("health");
                        c.CharacterHealth.Save(c.Info.HealthData);
                        c.Info.InventoryData = new XElement("inventory");
                        c.SaveInventory(c.Inventory, c.Info.InventoryData);
                    }
                    
                    c.Inventory.DeleteAllItems();
                }

                yield return CoroutineStatus.Running;

                if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub = leavingSub;
                    GameMain.GameSession.Submarine = leavingSub;
                    var subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }
                NextLevel = newLevel;
                GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
            else
            {
                GameMain.Server.EndGame(TransitionType.None);
                LoadCampaign(GameMain.GameSession.SavePath);
                LastSaveID++;
                LastUpdateID++;
                yield return CoroutineStatus.Success;
            }

            //--------------------------------------

            GameMain.Server.EndGame(transitionType);

            ForceMapUI = false;

            NextLevel = newLevel;
            MirrorLevel = mirror;
            if (PendingSubmarineSwitch != null) 
            {
                SubmarineInfo previousSub = GameMain.GameSession.SubmarineInfo;
                GameMain.GameSession.SubmarineInfo = PendingSubmarineSwitch;
                PendingSubmarineSwitch = null;

                for (int i = 0; i < GameMain.GameSession.OwnedSubmarines.Count; i++)
                {
                    if (GameMain.GameSession.OwnedSubmarines[i].Name == previousSub.Name)
                    {
                        GameMain.GameSession.OwnedSubmarines[i] = previousSub;
                    }
                }

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                LastSaveID++;
            }

            //give clients time to play the end cinematic before starting the next round
            if (transitionType == TransitionType.End)
            {
                yield return new WaitForSeconds(EndCinematicDuration);
            }
            else
            {
                yield return new WaitForSeconds(EndTransitionDuration * 0.5f);
            }

            GameMain.Server.StartGame();

            yield return CoroutineStatus.Success;
        }

        partial void InitProjSpecific()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsInBuyCrateChanged += () => { LastUpdateID++; };
                CargoManager.OnPurchasedItemsChanged += () => { LastUpdateID++; };
                CargoManager.OnSoldItemsChanged += () => { LastUpdateID++; };
                UpgradeManager.OnUpgradesChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
                Map.OnMissionSelected += (loc, mission) => { LastUpdateID++; };
            }
            //increment save ID so clients know they're lacking the most up-to-date save file
            LastSaveID++;
        }

        public void DiscardClientCharacterData(Client client)
        {
            characterData.RemoveAll(cd => cd.MatchesClient(client));
        }

        public CharacterCampaignData GetClientCharacterData(Client client)
        {
            return characterData.Find(cd => cd.MatchesClient(client));
        }

        public CharacterCampaignData SetClientCharacterData(Client client)
        {
            characterData.RemoveAll(cd => cd.MatchesClient(client));
            var data = new CharacterCampaignData(client);
            characterData.Add(data);
            return data;
        }

        public void AssignClientCharacterInfos(IEnumerable<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                if (client.SpectateOnly && GameMain.Server.ServerSettings.AllowSpectating) { continue; }
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) { client.CharacterInfo = matchingData.CharacterInfo; }
            }
        }

        public Dictionary<Client, Job> GetAssignedJobs(IEnumerable<Client> connectedClients)
        {
            var assignedJobs = new Dictionary<Client, Job>();
            foreach (Client client in connectedClients)
            {
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) assignedJobs.Add(client, matchingData.CharacterInfo.Job);
            }
            return assignedJobs;
        }

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition")) { return; }

            base.Update(deltaTime);
            if (Level.Loaded != null)
            {
                if (Level.Loaded.Type == LevelData.LevelType.LocationConnection)
                {
                    var transitionType = GetAvailableTransition(out _, out Submarine leavingSub); 
                    if (transitionType == TransitionType.End)
                    {
                        LoadNewLevel();
                    }
                    else if (transitionType == TransitionType.ProgressToNextLocation && Level.Loaded.EndOutpost != null && Level.Loaded.EndOutpost.DockedTo.Contains(leavingSub))
                    {
                        LoadNewLevel();
                    }
                    else if (transitionType == TransitionType.ReturnToPreviousLocation && Level.Loaded.StartOutpost != null && Level.Loaded.StartOutpost.DockedTo.Contains(leavingSub))
                    {
                        LoadNewLevel();
                    }
                }
                else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
                {
                    KeepCharactersCloseToOutpost(deltaTime);
                }
            }
        }

        public override void End(TransitionType transitionType = TransitionType.None)
        {
            GameOver = !GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
            base.End(transitionType);
        }

        public void ServerWrite(IWriteMessage msg, Client c)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            Reputation reputation = Map?.CurrentLocation?.Reputation;

            msg.Write(IsFirstRound);
            msg.Write(CampaignID);
            msg.Write(lastUpdateID);
            msg.Write(lastSaveID);
            msg.Write(map.Seed);
            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);
            msg.Write(map.SelectedMissionIndex == -1 ? byte.MaxValue : (byte)map.SelectedMissionIndex);
            msg.Write(reputation != null);
            if (reputation != null) { msg.Write(reputation.Value); }

            // hopefully we'll never have more than 128 factions
            msg.Write((byte)Factions.Count);
            foreach (Faction faction in Factions)
            {
                msg.Write(faction.Prefab.Identifier);
                msg.Write(faction.Reputation.Value);
            }

            msg.Write(ForceMapUI);

            msg.Write(Money);
            msg.Write(PurchasedHullRepairs);
            msg.Write(PurchasedItemRepairs);
            msg.Write(PurchasedLostShuttles);

            if (map.CurrentLocation != null)
            {
                msg.Write((byte)map.CurrentLocation?.AvailableMissions.Count());
                foreach (Mission mission in map.CurrentLocation.AvailableMissions)
                {
                    msg.Write(mission.Prefab.Identifier);
                    Location missionDestination = mission.Locations[0] == map.CurrentLocation ? mission.Locations[1] : mission.Locations[0];
                    LocationConnection connection = map.CurrentLocation.Connections.Find(c => c.OtherLocation(map.CurrentLocation) == missionDestination);
                    msg.Write((byte)map.CurrentLocation.Connections.IndexOf(connection));
                }

                // Store balance
                msg.Write(true);
                msg.Write((UInt16)map.CurrentLocation.StoreCurrentBalance);
            }
            else
            {
                msg.Write((byte)0);
                // Store balance
                msg.Write(false);
            }

            msg.Write((UInt16)CargoManager.ItemsInBuyCrate.Count);
            foreach (PurchasedItem pi in CargoManager.ItemsInBuyCrate)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, 100);
            }

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (PurchasedItem pi in CargoManager.PurchasedItems)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, 100);
            }

            msg.Write((UInt16)CargoManager.SoldItems.Count);
            foreach (SoldItem si in CargoManager.SoldItems)
            {
                msg.Write(si.ItemPrefab.Identifier);
                msg.Write((UInt16)si.ID);
                msg.Write(si.Removed);
                msg.Write(si.SellerID);
            }

            msg.Write((ushort)UpgradeManager.PendingUpgrades.Count);
            foreach (var (prefab, category, level) in UpgradeManager.PendingUpgrades)
            {
                msg.Write(prefab.Identifier);
                msg.Write(category.Identifier);
                msg.Write((byte)level);
            }

            var characterData = GetClientCharacterData(c);
            if (characterData?.CharacterInfo == null)
            {
                msg.Write(false);
            }
            else
            {
                msg.Write(true);
                characterData.CharacterInfo.ServerWrite(msg);
            }
        }

        public void ServerRead(IReadMessage msg, Client sender)
        {
            UInt16 currentLocIndex  = msg.ReadUInt16();
            UInt16 selectedLocIndex = msg.ReadUInt16();
            byte selectedMissionIndex = msg.ReadByte();
            bool purchasedHullRepairs = msg.ReadBoolean();
            bool purchasedItemRepairs = msg.ReadBoolean();
            bool purchasedLostShuttles = msg.ReadBoolean();

            UInt16 buyCrateItemCount = msg.ReadUInt16();
            List<PurchasedItem> buyCrateItems = new List<PurchasedItem>();
            for (int i = 0; i < buyCrateItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                buyCrateItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<PurchasedItem> purchasedItems = new List<PurchasedItem>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                purchasedItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 soldItemCount = msg.ReadUInt16();
            List<SoldItem> soldItems = new List<SoldItem>();
            for (int i = 0; i < soldItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                UInt16 id = msg.ReadUInt16();
                bool removed = msg.ReadBoolean();
                byte sellerId = msg.ReadByte();
                soldItems.Add(new SoldItem(ItemPrefab.Prefabs[itemPrefabIdentifier], id, removed, sellerId));
            }

            ushort purchasedUpgradeCount = msg.ReadUInt16();
            List<PurchasedUpgrade> purchasedUpgrades = new List<PurchasedUpgrade>();
            for (int i = 0; i < purchasedUpgradeCount; i++)
            {
                string upgradeIdentifier = msg.ReadString();
                UpgradePrefab prefab = UpgradePrefab.Find(upgradeIdentifier);

                string categoryIdentifier = msg.ReadString();
                UpgradeCategory category = UpgradeCategory.Find(categoryIdentifier);

                int upgradeLevel = msg.ReadByte();

                if (category == null || prefab == null) { continue; }
                purchasedUpgrades.Add(new PurchasedUpgrade(prefab, category, upgradeLevel));
            }

            if (!AllowedToManageCampaign(sender))
            {
                DebugConsole.ThrowError("Client \"" + sender.Name + "\" does not have a permission to manage the campaign");
                return;
            }
            
            Location location = Map.CurrentLocation;
            int hullRepairCost      = location?.GetAdjustedMechanicalCost(HullRepairCost)     ?? HullRepairCost;
            int itemRepairCost      = location?.GetAdjustedMechanicalCost(ItemRepairCost)     ?? ItemRepairCost;
            int shuttleRetrieveCost = location?.GetAdjustedMechanicalCost(ShuttleReplaceCost) ?? ShuttleReplaceCost;

            if (purchasedHullRepairs != this.PurchasedHullRepairs)
            {
                if (purchasedHullRepairs && Money >= hullRepairCost)
                {
                    this.PurchasedHullRepairs = true;
                    Money -= hullRepairCost;
                }
                else if (!purchasedHullRepairs)
                {
                    this.PurchasedHullRepairs = false;
                    Money += hullRepairCost;
                }
            }
            if (purchasedItemRepairs != this.PurchasedItemRepairs)
            {
                if (purchasedItemRepairs && Money >= itemRepairCost)
                {
                    this.PurchasedItemRepairs = true;
                    Money -= itemRepairCost;
                }
                else if (!purchasedItemRepairs)
                {
                    this.PurchasedItemRepairs = false;
                    Money += itemRepairCost;
                }
            }
            if (purchasedLostShuttles != this.PurchasedLostShuttles)
            {
                if (GameMain.GameSession?.SubmarineInfo != null &&
                    GameMain.GameSession.SubmarineInfo.LeftBehindSubDockingPortOccupied)
                {
                    GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("ReplaceShuttleDockingPortOccupied"), sender, ChatMessageType.MessageBox);
                }
                else if (purchasedLostShuttles && Money >= shuttleRetrieveCost)
                {
                    this.PurchasedLostShuttles = true;
                    Money -= shuttleRetrieveCost;
                }
                else if (!purchasedItemRepairs)
                {
                    this.PurchasedLostShuttles = false;
                    Money += shuttleRetrieveCost;
                }
            }

#if DEBUG
            if (currentLocIndex < Map.Locations.Count)
            {
                Map.SetLocation(currentLocIndex);
            }
#endif

            Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
            if (Map.SelectedLocation == null) { Map.SelectRandomLocation(preferUndiscovered: true); }
            if (Map.SelectedConnection != null) { Map.SelectMission(selectedMissionIndex); }

            List<PurchasedItem> currentBuyCrateItems = new List<PurchasedItem>(CargoManager.ItemsInBuyCrate);
            currentBuyCrateItems.ForEach(i => CargoManager.ModifyItemQuantityInBuyCrate(i.ItemPrefab, -i.Quantity));
            buyCrateItems.ForEach(i => CargoManager.ModifyItemQuantityInBuyCrate(i.ItemPrefab, i.Quantity));

            CargoManager.SellBackPurchasedItems(new List<PurchasedItem>(CargoManager.PurchasedItems));
            CargoManager.PurchaseItems(purchasedItems, false);

            // for some reason CargoManager.SoldItem is never cleared by the server, I've added a check to SellItems that ignores all
            // sold items that are removed so they should be discarded on the next message
            CargoManager.BuyBackSoldItems(new List<SoldItem>(CargoManager.SoldItems));
            CargoManager.SellItems(soldItems);

            foreach (var (prefab, category, _) in purchasedUpgrades)
            {
                UpgradeManager.PurchaseUpgrade(prefab, category);

                // unstable logging
                int price = prefab.Price.GetBuyprice(UpgradeManager.GetUpgradeLevel(prefab, category), Map?.CurrentLocation);
                int level = UpgradeManager.GetUpgradeLevel(prefab, category);
                GameServer.Log($"SERVER: Purchased level {level} {category.Identifier}.{prefab.Identifier} for {price}", ServerLog.MessageType.ServerMessage);
            }
        }

        public void ServerReadCrew(IReadMessage msg, Client sender)
        {
            int[] pendingHires = null;

            bool updatePending = msg.ReadBoolean();
            if (updatePending)
            {
                ushort pendingHireLength = msg.ReadUInt16();
                pendingHires = new int[pendingHireLength];
                for (int i = 0; i < pendingHireLength; i++)
                {
                    pendingHires[i] = msg.ReadInt32();
                }
            }

            bool validateHires = msg.ReadBoolean();
            bool fireCharacter = msg.ReadBoolean();

            int firedIdentifier = -1;
            if (fireCharacter) { firedIdentifier = msg.ReadInt32(); }

            Location location = map?.CurrentLocation;

            CharacterInfo firedCharacter = null;

            if (location != null && AllowedToManageCampaign(sender))
            {
                if (fireCharacter)
                {
                    firedCharacter = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifier() == firedIdentifier);
                    if (firedCharacter != null && (firedCharacter.Character?.IsBot ?? true))
                    {
                        CrewManager.FireCharacter(firedCharacter);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Tried to fire an invalid character ({firedIdentifier})");
                    }
                }

                if (location.HireManager != null)
                {
                    if (validateHires)
                    {
                        foreach (CharacterInfo hireInfo in location.HireManager.PendingHires)
                        {
                            TryHireCharacter(location, hireInfo);
                        }
                    }
                    
                    if (updatePending)
                    {
                        List<CharacterInfo> pendingHireInfos = new List<CharacterInfo>();
                        foreach (int identifier in pendingHires)
                        {
                            CharacterInfo match = location.GetHireableCharacters().FirstOrDefault(info => info.GetIdentifier() == identifier);
                            if (match == null)
                            {
                                DebugConsole.ThrowError($"Tried to hire a character that doesn't exist ({identifier})");
                                continue;
                            }

                            pendingHireInfos.Add(match);
                        }
                        location.HireManager.PendingHires = pendingHireInfos;
                    }
                }
            }

            // bounce back
            SendCrewState(validateHires, firedCharacter);
        }

        /// <summary>
        /// Notifies the clients of the current bot situation like syncing pending and available hires
        /// available hires are also synced
        /// </summary>
        /// <param name="validateHires">When set to true notifies the clients that the hires have been validated.</param>
        /// <param name="firedCharacter">When not null will inform the clients that his character has been fired.</param>
        /// <remarks>
        /// It might be obsolete to sync available hires. I found that the available hires are always the same between
        /// the client and the server when there's only one person on the server but when a second person joins both of
        /// their available hires are different from the server.
        /// </remarks>
        public void SendCrewState(bool validateHires, CharacterInfo firedCharacter)
        {
            List<CharacterInfo> availableHires = new List<CharacterInfo>();
            List<CharacterInfo> pendingHires = new List<CharacterInfo>();

            if (map.CurrentLocation != null && map.CurrentLocation.Type.HasHireableCharacters)
            {
                availableHires = map.CurrentLocation.GetHireableCharacters().ToList();
                pendingHires = map.CurrentLocation?.HireManager.PendingHires;
            }

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.CREW);

                msg.Write((ushort)availableHires.Count);
                foreach (CharacterInfo hire in availableHires)
                {
                    hire.ServerWrite(msg);
                    msg.Write(hire.Salary);
                }
            
                msg.Write((ushort)pendingHires.Count);
                foreach (CharacterInfo pendingHire in pendingHires)
                {
                    msg.Write(pendingHire.GetIdentifier());
                }
                
                msg.Write(validateHires);

                msg.Write(firedCharacter != null);
                if (firedCharacter != null) { msg.Write(firedCharacter.GetIdentifier()); }

                GameMain.Server.ServerPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        public override void Save(XElement element)
        {
            element.Add(new XAttribute("campaignid", CampaignID));
            XElement modeElement = new XElement("MultiPlayerCampaign",
                new XAttribute("money", Money),
                new XAttribute("cheatsenabled", CheatsEnabled));
            CampaignMetadata?.Save(modeElement);
            Map.Save(modeElement);
            CargoManager?.SavePurchasedItems(modeElement);
            UpgradeManager?.SavePendingUpgrades(modeElement, UpgradeManager?.PendingUpgrades);

            // save bots
            CrewManager.SaveMultiplayer(modeElement);

            // save available submarines
            XElement availableSubsElement = new XElement("AvailableSubs");
            for (int i = 0; i < GameMain.NetLobbyScreen.CampaignSubmarines.Count; i++)
            {
                availableSubsElement.Add(new XElement("Sub", new XAttribute("name", GameMain.NetLobbyScreen.CampaignSubmarines[i].Name)));
            }
            modeElement.Add(availableSubsElement);

            element.Add(modeElement);

            //save character data to a separate file
            string characterDataPath = GetCharacterDataSavePath();
            XDocument characterDataDoc = new XDocument(new XElement("CharacterData"));
            foreach (CharacterCampaignData cd in characterData)
            {
                characterDataDoc.Root.Add(cd.Save());
            }
            try
            {
                characterDataDoc.SaveSafe(characterDataPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving multiplayer campaign characters to \"" + characterDataPath + "\" failed!", e);
            }

            lastSaveID++;
            DebugConsole.Log("Campaign saved, save ID " + lastSaveID);
        }
    }
}
