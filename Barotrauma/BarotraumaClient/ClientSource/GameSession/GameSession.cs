﻿using Microsoft.Xna.Framework.Graphics;
<<<<<<< HEAD
using Microsoft.Xna.Framework.Input;
=======
>>>>>>> upstream/master

namespace Barotrauma
{
    partial class GameSession
    {
<<<<<<< HEAD
        public RoundSummary RoundSummary { get; private set; }
        public static bool IsTabMenuOpen => GameMain.GameSession?.tabMenu != null;
        public static TabMenu TabMenuInstance => GameMain.GameSession?.tabMenu;
=======
        public RoundSummary RoundSummary
        {
            get;
            private set;
        }

        public static bool IsTabMenuOpen => GameMain.GameSession?.tabMenu != null;
        public static TabMenu TabMenuInstance => GameMain.GameSession?.tabMenu;

>>>>>>> upstream/master

        private TabMenu tabMenu;

        public bool ToggleTabMenu()
        {
            if (GameMain.NetworkMember != null && GameMain.NetLobbyScreen != null)
            {
                if (GameMain.NetLobbyScreen.HeadSelectionList != null) { GameMain.NetLobbyScreen.HeadSelectionList.Visible = false; }
                if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
            }
            if (tabMenu == null && GameMode is TutorialMode == false)
            {
                tabMenu = new TabMenu();
            }
            else
            {
                tabMenu = null;
                NetLobbyScreen.JobInfoFrame = null;
            }

            return true;
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            GameMode?.AddToGUIUpdateList();
            tabMenu?.AddToGUIUpdateList();

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetLobbyScreen?.HeadSelectionList?.AddToGUIUpdateList();
                GameMain.NetLobbyScreen?.JobSelectionFrame?.AddToGUIUpdateList();
            }
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GUI.DisableHUD) { return; }

<<<<<<< HEAD
            if (GameMode.IsRunning)
            {
                if (tabMenu == null)
                {
                    if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
                    {
                        ToggleTabMenu();
                    }
                }
                else
                {
                    tabMenu.Update();

                    if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
                    {
                        ToggleTabMenu();
                    }
=======
            if (tabMenu == null)
            {
                if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
                {
                    ToggleTabMenu();
>>>>>>> upstream/master
                }
            }
            else
            {
<<<<<<< HEAD
                if (tabMenu != null)
=======
                tabMenu.Update();

                if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
>>>>>>> upstream/master
                {
                    ToggleTabMenu();
                }
            }

            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetLobbyScreen?.HeadSelectionList != null)
                {
                    if (PlayerInput.PrimaryMouseButtonDown() && !GUI.IsMouseOn(GameMain.NetLobbyScreen.HeadSelectionList))
                    {
                        if (GameMain.NetLobbyScreen.HeadSelectionList != null) { GameMain.NetLobbyScreen.HeadSelectionList.Visible = false; }
                    }
                }
                if (GameMain.NetLobbyScreen?.JobSelectionFrame != null)
                {
                    if (PlayerInput.PrimaryMouseButtonDown() && !GUI.IsMouseOn(GameMain.NetLobbyScreen.JobSelectionFrame))
                    {
                        GameMain.NetLobbyScreen.JobList.Deselect();
                        if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
                    }
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
<<<<<<< HEAD
            if (GUI.DisableHUD) return;
=======
>>>>>>> upstream/master
            GameMode?.Draw(spriteBatch);
        }
    }
}
