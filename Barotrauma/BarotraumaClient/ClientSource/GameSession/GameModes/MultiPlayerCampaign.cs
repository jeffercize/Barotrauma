﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        public bool SuppressStateSending = false;

        private UInt16 pendingSaveID = 1;
        public UInt16 PendingSaveID
        {
            get 
            {
                return pendingSaveID;
            }
            set
            {
                pendingSaveID = value;
                //pending save ID 0 means "no save received yet"
                //save IDs are always above 0, so we should never be waiting for 0
                if (pendingSaveID == 0) { pendingSaveID++; }
            }
        }


        public static void StartCampaignSetup(IEnumerable<string> saveFiles)
        {
            var parent = GameMain.NetLobbyScreen.CampaignSetupFrame;
            parent.ClearChildren();
            parent.Visible = true;
            GameMain.NetLobbyScreen.HighlightMode(
               GameMain.NetLobbyScreen.ModeList.Content.GetChildIndex(GameMain.NetLobbyScreen.ModeList.Content.GetChildByUserData(GameModePreset.MultiPlayerCampaign)));

            var layout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), layout.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) }, isHorizontal: true)
            {
                RelativeSpacing = 0.02f
            };

            var campaignContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), layout.RectTransform, Anchor.BottomLeft), style: "InnerFrame")
            {
                CanBeFocused = false
            };
            
            var newCampaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.95f), campaignContainer.RectTransform, Anchor.Center), style: null);
            var loadCampaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.95f), campaignContainer.RectTransform, Anchor.Center), style: null);

            GameMain.NetLobbyScreen.CampaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer, null, saveFiles);

            var newCampaignButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), buttonContainer.RectTransform),
                TextManager.Get("NewCampaign"), style: "GUITabButton")
            {
                Selected = true
            };

            var loadCampaignButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.00f), buttonContainer.RectTransform),
                TextManager.Get("LoadCampaign"), style: "GUITabButton");

            newCampaignButton.OnClicked = (btn, obj) =>
            {
                newCampaignButton.Selected = true;
                loadCampaignButton.Selected = false;
                newCampaignContainer.Visible = true;
                loadCampaignContainer.Visible = false;
                return true;
            };
            loadCampaignButton.OnClicked = (btn, obj) =>
            {
                newCampaignButton.Selected = false;
                loadCampaignButton.Selected = true;
                newCampaignContainer.Visible = false;
                loadCampaignContainer.Visible = true;
                return true;
            };
            loadCampaignContainer.Visible = false;

            GUITextBlock.AutoScaleAndNormalize(newCampaignButton.TextBlock, loadCampaignButton.TextBlock);

            GameMain.NetLobbyScreen.CampaignSetupUI.StartNewGame = GameMain.Client.SetupNewCampaign;
            GameMain.NetLobbyScreen.CampaignSetupUI.LoadGame = GameMain.Client.SetupLoadCampaign;
        }

        partial void InitProjSpecific()
        {
            var buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, GUICanvas.Instance),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                CanBeFocused = false
            };
            
            int buttonHeight = (int)(GUI.Scale * 40);
            int buttonWidth = GUI.IntScale(200);

            endRoundButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle((GameMain.GraphicsWidth / 2) - (buttonWidth / 2), HUDLayoutSettings.ButtonAreaTop.Center.Y - (buttonHeight / 2), buttonWidth, buttonHeight), GUICanvas.Instance),
                TextManager.Get("EndRound"), textAlignment: Alignment.Center, style: "EndRoundButton")
            {
                Pulse = true,
                TextBlock =
                {
                    Shadow = true,
                    AutoScaleHorizontal = true
                },
                OnClicked = (btn, userdata) =>
                {
                    var availableTransition = GetAvailableTransition(out _, out _);
                    if (Character.Controlled != null &&
                        availableTransition == TransitionType.ReturnToPreviousLocation && 
                        Character.Controlled?.Submarine == Level.Loaded?.StartOutpost)
                    {
                        GameMain.Client.RequestStartRound();
                    }
                    else if (Character.Controlled != null &&
                        availableTransition == TransitionType.ProgressToNextLocation &&
                        Character.Controlled?.Submarine == Level.Loaded?.EndOutpost)
                    {
                        GameMain.Client.RequestStartRound();
                    }
                    else
                    {
                        ShowCampaignUI = true;
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                    return true;
                }
            };
            buttonContainer.Recalculate();
        }

        private void InitCampaignUI()
        {
            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "InnerGlow", color: Color.Black);
            CampaignUI = new CampaignUI(this, campaignUIContainer)
            {
                StartRound = () =>
                {
                    GameMain.Client.RequestStartRound();
                }
            };
        }

        public override void Start()
        {
            base.Start();
            CoroutineManager.StartCoroutine(DoInitialCameraTransition(), "MultiplayerCampaign.DoInitialCameraTransition");
        }

        protected override void LoadInitialLevel()
        {
            //clients should never call this
            throw new InvalidOperationException("");
        }


        private IEnumerable<object> DoInitialCameraTransition()
        {
            while (GameMain.Instance.LoadingScreenOpen)
            {
                yield return CoroutineStatus.Running;
            }

            if (GameMain.Client.LateCampaignJoin)
            {
                GameMain.Client.LateCampaignJoin = false;
                yield return CoroutineStatus.Success;
            }

            Character prevControlled = Character.Controlled;
            if (prevControlled?.AIController != null)
            {
                prevControlled.AIController.Enabled = false;
            }
            GUI.DisableHUD = true;
            if (IsFirstRound)
            {
                Character.Controlled = null;

                if (prevControlled != null)
                {
                    prevControlled.ClearInputs();
                }

                overlayColor = Color.LightGray;
                overlaySprite = Map.CurrentLocation.Type.GetPortrait(Map.CurrentLocation.PortraitId);
                overlayTextColor = Color.Transparent;
                overlayText = TextManager.GetWithVariables("campaignstart",
                    new string[] { "xxxx", "yyyy" },
                    new string[] { Map.CurrentLocation.Name, TextManager.Get("submarineclass." + Submarine.MainSub.Info.SubmarineClass) });
                float fadeInDuration = 1.0f;
                float textDuration = 10.0f;
                float timer = 0.0f;
                while (timer < textDuration)
                {
                    // Try to grab the controlled here to prevent inputs, assigned late on multiplayer
                    if (Character.Controlled != null)
                    {
                        prevControlled = Character.Controlled;
                        Character.Controlled = null;
                        prevControlled.ClearInputs();
                    }
                    overlayTextColor = Color.Lerp(Color.Transparent, Color.White, (timer - 1.0f) / fadeInDuration);
                    timer = Math.Min(timer + CoroutineManager.DeltaTime, textDuration);
                    yield return CoroutineStatus.Running;
                }
                var transition = new CameraTransition(prevControlled, GameMain.GameScreen.Cam,
                    null, null,
                    fadeOut: false,
                    duration: 5,
                    startZoom: 1.5f, endZoom: 1.0f)
                {
                    AllowInterrupt = true,
                    RemoveControlFromCharacter = false
                };
                fadeInDuration = 1.0f;
                timer = 0.0f;
                overlayTextColor = Color.Transparent;
                overlayText = "";
                while (timer < fadeInDuration)
                {
                    overlayColor = Color.Lerp(Color.LightGray, Color.Transparent, timer / fadeInDuration);
                    timer += CoroutineManager.DeltaTime;
                    yield return CoroutineStatus.Running;
                }
                overlayColor = Color.Transparent;
                while (transition.Running)
                {
                    yield return CoroutineStatus.Running;
                }

                if (prevControlled != null)
                {
                    Character.Controlled = prevControlled;
                }
            }
            else
            {
                var transition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam,
                    null, null,
                    fadeOut: false,
                    duration: 5,
                    startZoom: 0.5f, endZoom: 1.0f)
                {
                    AllowInterrupt = true,
                    RemoveControlFromCharacter = true
                };
                while (transition.Running)
                {
                    yield return CoroutineStatus.Running;
                }
            }

            if (prevControlled != null)
            {
                prevControlled.SelectedConstruction = null;
                if (prevControlled.AIController != null)
                {
                    prevControlled.AIController.Enabled = true;
                }
            }
            GUI.DisableHUD = false;
            yield return CoroutineStatus.Success;
        }

        protected override IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null)
        {
            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> DoLevelTransition()
        {
            SoundPlayer.OverrideMusicType = CrewManager.GetCharacters().Any(c => !c.IsDead) ? "endround" : "crewdead";
            SoundPlayer.OverrideMusicDuration = 18.0f;

            Level prevLevel = Level.Loaded;

            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            crewDead = false;

            var continueButton = GameMain.GameSession.RoundSummary?.ContinueButton;
            if (continueButton != null)
            {
                continueButton.Visible = false;
            }

            Character.Controlled = null;

            yield return new WaitForSeconds(0.1f);

            GameMain.Client.EndCinematic?.Stop();
            var endTransition = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null,
                Alignment.Center,
                fadeOut: false,
                duration: EndTransitionDuration);
            GameMain.Client.EndCinematic = endTransition;

            Location portraitLocation = Map?.SelectedLocation ?? Map?.CurrentLocation ?? Level.Loaded?.StartLocation;
            if (portraitLocation != null)
            {
                overlaySprite = portraitLocation.Type.GetPortrait(portraitLocation.PortraitId);
            }
            float fadeOutDuration = endTransition.Duration;
            float t = 0.0f;
            while (t < fadeOutDuration || endTransition.Running)
            {
                t += CoroutineManager.UnscaledDeltaTime;
                overlayColor = Color.Lerp(Color.Transparent, Color.White, t / fadeOutDuration);
                yield return CoroutineStatus.Running;
            }
            overlayColor = Color.White;
            yield return CoroutineStatus.Running;

            //--------------------------------------

            //wait for the new level to be loaded
            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, seconds: 30);
            while (Level.Loaded == prevLevel || Level.Loaded == null)
            {
                if (DateTime.Now > timeOut || Screen.Selected != GameMain.GameScreen)  { break; }
                yield return CoroutineStatus.Running;
            }

            endTransition.Stop();
            overlayColor = Color.Transparent;

            if (DateTime.Now > timeOut) { GameMain.NetLobbyScreen.Select(); }
            if (!(Screen.Selected is RoundSummaryScreen))
            {
                if (continueButton != null)
                {
                    continueButton.Visible = true;
                }
            }

            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || Level.Loaded == null) { return; }

            if (ShowCampaignUI || ForceMapUI)
            {
                if (CampaignUI == null) { InitCampaignUI(); }
                Character.DisableControls = true;                
            }

            base.Update(deltaTime);

            if (PlayerInput.RightButtonClicked() ||
                PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                ShowCampaignUI = false;
                if (GUIMessageBox.VisibleBox?.UserData is RoundSummary roundSummary &&
                    roundSummary.ContinueButton != null &&
                    roundSummary.ContinueButton.Visible)
                {
                    GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
                }
            }

            if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
            {
                endRoundButton.UpdateManually(deltaTime);
                if (CoroutineManager.IsCoroutineRunning("LevelTransition") || ForceMapUI) { return; }
            }

            if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                if (wasDocked)
                {
                    var connectedSubs = Submarine.MainSub.GetConnectedSubs();
                    bool isDocked = Level.Loaded.StartOutpost != null && connectedSubs.Contains(Level.Loaded.StartOutpost);
                    if (!isDocked)
                    {
                        //undocked from outpost, need to choose a destination
                        ForceMapUI = true; 
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }
                else
                {
                    //wasn't initially docked (sub doesn't have a docking port?)
                    // -> choose a destination when the sub is far enough from the start outpost
                    if (!Submarine.MainSub.AtStartPosition)
                    {
                        ForceMapUI = true;
                        if (CampaignUI == null) { InitCampaignUI(); }
                        CampaignUI.SelectTab(InteractionType.Map);
                    }
                }

                if (CampaignUI == null) { InitCampaignUI(); }
            }
        }
        public override void End(TransitionType transitionType = TransitionType.None)
        {
            base.End(transitionType);
            ForceMapUI = ShowCampaignUI = false;
            UpgradeManager.CanUpgrade = true;
            
            // remove all event dialogue boxes
            GUIMessageBox.MessageBoxes.ForEachMod(mb =>
            {
                if (mb is GUIMessageBox msgBox)
                {
                    if (mb.UserData is Pair<string, ushort> pair && pair.First.Equals("conversationaction", StringComparison.OrdinalIgnoreCase))
                    {
                        msgBox.Close();
                    }
                }
            });

            if (transitionType == TransitionType.End)
            {
                EndCampaign();
            }
            else
            {
                IsFirstRound = false;
                CoroutineManager.StartCoroutine(DoLevelTransition(), "LevelTransition");
            }
        }

        protected override void EndCampaignProjSpecific()
        {
            if (GUIMessageBox.VisibleBox?.UserData is RoundSummary roundSummary)
            {
                GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
            }
            CoroutineManager.StartCoroutine(DoEndCampaignCameraTransition(), "DoEndCampaignCameraTransition");
            GameMain.CampaignEndScreen.OnFinished = () =>
            {
                GameMain.NetLobbyScreen.Select();
                if (GameMain.NetLobbyScreen.ContinueCampaignButton != null) { GameMain.NetLobbyScreen.ContinueCampaignButton.Enabled = false; }
                if (GameMain.NetLobbyScreen.QuitCampaignButton != null) { GameMain.NetLobbyScreen.QuitCampaignButton.Enabled = false; }
            };
        }

        private IEnumerable<object> DoEndCampaignCameraTransition()
        {
            Character controlled = Character.Controlled;
            if (controlled != null)
            {
                controlled.AIController.Enabled = false;
            }

            GUI.DisableHUD = true;
            ISpatialEntity endObject = Level.Loaded.LevelObjectManager.GetAllObjects().FirstOrDefault(obj => obj.Prefab.SpawnPos == LevelObjectPrefab.SpawnPosType.LevelEnd);
            var transition = new CameraTransition(endObject ?? Submarine.MainSub, GameMain.GameScreen.Cam,
                null, Alignment.Center,
                fadeOut: true,
                duration: 10,
                startZoom: null, endZoom: 0.2f);

            while (transition.Running)
            {
                yield return CoroutineStatus.Running;
            }
            GameMain.CampaignEndScreen.Select();
            GUI.DisableHUD = false;

            yield return CoroutineStatus.Success;
        }

        public void ClientWrite(IWriteMessage msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);
            msg.Write(map.SelectedMissionIndex == -1 ? byte.MaxValue : (byte)map.SelectedMissionIndex);
            msg.Write(PurchasedHullRepairs);
            msg.Write(PurchasedItemRepairs);
            msg.Write(PurchasedLostShuttles);

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
<<<<<<< HEAD
=======
            }

            msg.Write((UInt16)CargoManager.SoldItems.Count);
            foreach (SoldItem si in CargoManager.SoldItems)
            {
                msg.Write(si.ItemPrefab.Identifier);
                msg.Write((UInt16)si.ID);
                msg.Write(si.Removed);
                msg.Write(si.SellerID);
            }

            msg.Write((ushort)UpgradeManager.PurchasedUpgrades.Count);
            foreach (var (prefab, category, level) in UpgradeManager.PurchasedUpgrades)
            {
                msg.Write(prefab.Identifier);
                msg.Write(category.Identifier);
                msg.Write((byte)level);
>>>>>>> upstream/master
            }
        }

        //static because we may need to instantiate the campaign if it hasn't been done yet
        public static void ClientRead(IReadMessage msg)
        {
            bool isFirstRound   =  msg.ReadBoolean();
            byte campaignID     = msg.ReadByte();
            UInt16 updateID     = msg.ReadUInt16();
            UInt16 saveID       = msg.ReadUInt16();
            string mapSeed      = msg.ReadString();
            UInt16 currentLocIndex      = msg.ReadUInt16();
            UInt16 selectedLocIndex     = msg.ReadUInt16();
            byte selectedMissionIndex   = msg.ReadByte();
            float? reputation = null;
            if (msg.ReadBoolean()) { reputation = msg.ReadSingle(); }
            
            Dictionary<string, float> factionReps = new Dictionary<string, float>();
            byte factionsCount = msg.ReadByte();
            for (int i = 0; i < factionsCount; i++)
            {
                factionReps.Add(msg.ReadString(), msg.ReadSingle());
            }

            bool forceMapUI = msg.ReadBoolean();

            int money = msg.ReadInt32();
            bool purchasedHullRepairs   = msg.ReadBoolean();
            bool purchasedItemRepairs   = msg.ReadBoolean();
            bool purchasedLostShuttles  = msg.ReadBoolean();

            byte missionCount = msg.ReadByte();
            List<Pair<string, byte>> availableMissions = new List<Pair<string, byte>>();
            for (int i = 0; i < missionCount; i++)
            {
                string missionIdentifier = msg.ReadString();
                byte connectionIndex = msg.ReadByte();
                availableMissions.Add(new Pair<string, byte>(missionIdentifier, connectionIndex));
            }

            UInt16? storeBalance = null;
            if (msg.ReadBoolean())
            {
                storeBalance = msg.ReadUInt16();
            }

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

            ushort pendingUpgradeCount = msg.ReadUInt16();
            List<PurchasedUpgrade> pendingUpgrades = new List<PurchasedUpgrade>();
            for (int i = 0; i < pendingUpgradeCount; i++)
            {
                string upgradeIdentifier = msg.ReadString();
                UpgradePrefab prefab = UpgradePrefab.Find(upgradeIdentifier);
                string categoryIdentifier = msg.ReadString();
                UpgradeCategory category = UpgradeCategory.Find(categoryIdentifier);
                int upgradeLevel = msg.ReadByte();
                if (prefab == null || category == null) { continue; }
                pendingUpgrades.Add(new PurchasedUpgrade(prefab, category, upgradeLevel));
            }

            bool hasCharacterData = msg.ReadBoolean();
            CharacterInfo myCharacterInfo = null;
            if (hasCharacterData)
            {
                myCharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName, msg);
            }

            if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(null, savePath, GameModePreset.MultiPlayerCampaign, mapSeed);
                campaign = (MultiPlayerCampaign)GameMain.GameSession.GameMode;
                campaign.CampaignID = campaignID;
                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            }

            //server has a newer save file
            if (NetIdUtils.IdMoreRecent(saveID, campaign.PendingSaveID))
            {
                campaign.PendingSaveID = saveID;
            }
            
            if (NetIdUtils.IdMoreRecent(updateID, campaign.lastUpdateID))
            {
                campaign.SuppressStateSending = true;
                campaign.IsFirstRound = isFirstRound;

                //we need to have the latest save file to display location/mission/store
                if (campaign.LastSaveID == saveID)
                {
                    campaign.ForceMapUI = forceMapUI;

                    UpgradeStore.WaitForServerUpdate = false;

                    campaign.Map.SetLocation(currentLocIndex == UInt16.MaxValue ? -1 : currentLocIndex);
                    campaign.Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
                    campaign.Map.SelectMission(selectedMissionIndex);
                    campaign.CargoManager.SetItemsInBuyCrate(buyCrateItems);
                    campaign.CargoManager.SetPurchasedItems(purchasedItems);
                    campaign.CargoManager.SetSoldItems(soldItems);
                    if (storeBalance.HasValue) { campaign.Map.CurrentLocation.StoreCurrentBalance = storeBalance.Value; }
                    campaign.UpgradeManager.SetPendingUpgrades(pendingUpgrades);
                    campaign.UpgradeManager.PurchasedUpgrades.Clear();

                    foreach (var (identifier, rep) in factionReps)
                    {
                       Faction faction = campaign.Factions.FirstOrDefault(f => f.Prefab.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                       if (faction?.Reputation != null)
                       {
                           faction.Reputation.Value = rep;
                       }
                       else
                       {
                           DebugConsole.ThrowError($"Received an update for a faction that doesn't exist \"{identifier}\".");
                       }
                    }

                    if (reputation.HasValue)
                    {
                        campaign.Map.CurrentLocation.Reputation.Value = reputation.Value;
                        campaign?.CampaignUI?.UpgradeStore?.RefreshAll();
                    }

                    foreach (var availableMission in availableMissions)
                    {
                        MissionPrefab missionPrefab = MissionPrefab.List.Find(mp => mp.Identifier == availableMission.First);
                        if (missionPrefab == null)
                        {
                            DebugConsole.ThrowError($"Error when receiving campaign data from the server: mission prefab \"{availableMission.First}\" not found.");
                            continue;
                        }
                        if (availableMission.Second < 0 || availableMission.Second >= campaign.Map.CurrentLocation.Connections.Count)
                        {
                            DebugConsole.ThrowError($"Error when receiving campaign data from the server: connection index for mission \"{availableMission.First}\" out of range (index: {availableMission.Second}, current location: {campaign.Map.CurrentLocation.Name}, connections: {campaign.Map.CurrentLocation.Connections.Count}).");
                            continue;
                        }
                        LocationConnection connection = campaign.Map.CurrentLocation.Connections[availableMission.Second];
                        campaign.Map.CurrentLocation.UnlockMission(missionPrefab, connection);
                    }

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                }

                bool shouldRefresh = campaign.Money != money ||
                                     campaign.PurchasedHullRepairs != purchasedHullRepairs ||
                                     campaign.PurchasedItemRepairs != purchasedItemRepairs ||
                                     campaign.PurchasedLostShuttles != purchasedLostShuttles;

                campaign.Money = money;
                campaign.PurchasedHullRepairs = purchasedHullRepairs;
                campaign.PurchasedItemRepairs = purchasedItemRepairs;
                campaign.PurchasedLostShuttles = purchasedLostShuttles;

                if (shouldRefresh)
                {
                    campaign?.CampaignUI?.UpgradeStore?.RefreshAll();
                }

                if (myCharacterInfo != null)
                {
                    GameMain.Client.CharacterInfo = myCharacterInfo;
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(myCharacterInfo);
                }
                else
                {
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                }

                campaign.lastUpdateID = updateID;
                campaign.SuppressStateSending = false;
            }
        }

        public void ClientReadCrew(IReadMessage msg)
        {
            ushort availableHireLength = msg.ReadUInt16();
            List<CharacterInfo> availableHires = new List<CharacterInfo>();
            for (int i = 0; i < availableHireLength; i++)
            {
                CharacterInfo hire = CharacterInfo.ClientRead("human", msg);
                hire.Salary = msg.ReadInt32();
                availableHires.Add(hire);
            }

            ushort pendingHireLength = msg.ReadUInt16();
            List<int> pendingHires = new List<int>();
            for (int i = 0; i < pendingHireLength; i++)
            {
                pendingHires.Add(msg.ReadInt32());
            }
            
            bool validateHires = msg.ReadBoolean();

            bool fireCharacter = msg.ReadBoolean();

            int firedIdentifier = -1;
            if (fireCharacter) { firedIdentifier = msg.ReadInt32(); }

            if (fireCharacter)
            {
                CharacterInfo firedCharacter = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifier() == firedIdentifier);
                // this one might and is allowed to be null since the character is already fired on the original sender's game
                if (firedCharacter != null) { CrewManager.FireCharacter(firedCharacter); }
            }

            if (map?.CurrentLocation?.HireManager != null && CampaignUI?.CrewManagement != null)
            {
                CampaignUI?.CrewManagement?.SetHireables(map.CurrentLocation, availableHires);
                if (validateHires) { CampaignUI?.CrewManagement.ValidatePendingHires(); }
                CampaignUI?.CrewManagement?.SetPendingHires(pendingHires, map?.CurrentLocation);
                if (fireCharacter) { CampaignUI?.CrewManagement.UpdateCrew(); }
            }
        }

        public override void Save(XElement element)
        {
            //do nothing, the clients get the save files from the server
        }

        public void LoadState(string filePath)
        {
            DebugConsole.Log($"Loading save file for an existing game session ({filePath})");
            SaveUtil.DecompressToDirectory(filePath, SaveUtil.TempPath, null);

            string gamesessionDocPath = Path.Combine(SaveUtil.TempPath, "gamesession.xml");
            XDocument doc = XMLExtensions.TryLoadXml(gamesessionDocPath);
            if (doc == null) 
            {
                DebugConsole.ThrowError($"Failed to load the state of a multiplayer campaign. Could not open the file \"{gamesessionDocPath}\".");
                return; 
            }
            Load(doc.Root.Element("MultiPlayerCampaign"));
            SubmarineInfo selectedSub;
            GameMain.GameSession.OwnedSubmarines = SaveUtil.LoadOwnedSubmarines(doc, out selectedSub);
            GameMain.GameSession.SubmarineInfo = selectedSub;
        }
    }
}
