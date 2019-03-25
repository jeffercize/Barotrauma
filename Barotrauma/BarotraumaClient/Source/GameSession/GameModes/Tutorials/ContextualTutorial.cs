using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public static bool Selected = false;
        public static bool ContentRunning = false;
        public static bool Initialized = false;

        private enum ContentTypes { None = 0, Video = 1, TextOnly = 2 };

        private TutorialSegment activeSegment;
        private List<TutorialSegment> segments;
        
        private VideoPlayer videoPlayer;

        private Steering navConsole;
        private Reactor reactor;
        private Sonar sonar;
        private Vector2 subStartingPosition;
        private List<Character> crew;
        private Character mechanic;
        private Character engineer;
        private Character injuredMember = null;

        private List<Pair<Character, float>> characterTimeOnSonar;
        private float requiredTimeOnSonar = 5f;

        private bool started = false;
        private string playableContentPath;

        private float tutorialTimer;

        private bool disableTutorialOnDeficiencyFound = true;

        private GUIFrame holderFrame, objectiveFrame;
        //private GUIButton toggleButton;
        //private bool objectivesOpen = false;
        //private float openState = 0f;
        private List<TutorialSegment> activeObjectives = new List<TutorialSegment>();
        private string objectiveTranslated;
        //private Point objectiveBaseOffset = Point.Zero;

        private class TutorialSegment
        {
            public string Id;
            public string Objective;
            public ContentTypes ContentType;
            public XElement TextContent;
            public XElement VideoContent;
            public bool IsTriggered;
            public GUIButton ReplayButton;
            public GUITextBlock LinkedTitle, LinkedText;

            public TutorialSegment(XElement config)
            {
                Id = config.GetAttributeString("id", "Missing ID");
                Objective = TextManager.Get(config.GetAttributeString("objective", string.Empty), true);
                Enum.TryParse(config.GetAttributeString("contenttype", "None"), true, out ContentType);
                IsTriggered = config.GetAttributeBool("istriggered", false);

                switch (ContentType)
                {
                    case ContentTypes.None:
                        break;
                    case ContentTypes.Video:
                        VideoContent = config.Element("Video");
                        TextContent = config.Element("Text");
                        break;
                    case ContentTypes.TextOnly:
                        TextContent = config.Element("Text");
                        break;
                }
            }
        }

        public ContextualTutorial(XElement element) : base(element)
        {
            playableContentPath = element.GetAttributeString("playablecontentpath", "");
            segments = new List<TutorialSegment>();

            foreach (var segment in element.Elements("Segment"))
            {
                segments.Add(new TutorialSegment(segment));
            }

            Name = "ContextualTutorial";
        }

        public override void Initialize()
        {
            if (Initialized) return;
            Initialized = true;

            base.Initialize();
            videoPlayer = new VideoPlayer();
            characterTimeOnSonar = new List<Pair<Character, float>>();
            //objectivesOpen = true;

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].IsTriggered = false;
            }
        }

        public void LoadPartiallyComplete(XElement element)
        {
            int[] completedSegments = element.GetAttributeIntArray("completedsegments", null);

            if (completedSegments == null || completedSegments.Length == 0)
            {
                return;
            }

            if (completedSegments.Length == segments.Count) // Completed all segments
            {
                Stop();
                return;
            }

            for (int i = 0; i < completedSegments.Length; i++)
            {
                segments[completedSegments[i]].IsTriggered = true;
            }
        }

        private void PreloadVideoContent() // Have to see if needed with videos
        {
            /*for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].ContentType != ContentTypes.Video || segments[i].IsTriggered) continue;
                spriteSheetPlayer.PreloadContent(playableContentPath, "tutorial", segments[i].Id, segments[i].VideoContent);
            }*/
        }

        public void SavePartiallyComplete(XElement element)
        {
            XElement tutorialElement = new XElement("contextualtutorial");
            tutorialElement.Add(new XAttribute("completedsegments", GetCompletedSegments()));
            element.Add(tutorialElement);
        }

        private string GetCompletedSegments()
        {
            string completedSegments = string.Empty;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered)
                {
                    completedSegments += i + ",";
                }
            }

            if (completedSegments.Length > 0)
            {
                completedSegments = completedSegments.TrimEnd(',');
            }

            return completedSegments;
        }

        public override void Start()
        {
            if (!Initialized) return;

            PreloadVideoContent();

            base.Start();

            activeObjectives.Clear();
            objectiveTranslated = TextManager.Get("Objective");
            CreateObjectiveFrame();
            activeSegment = null;
            tutorialTimer = 0.0f;
            subStartingPosition = Vector2.Zero;
            characterTimeOnSonar.Clear();

            subStartingPosition = Submarine.MainSub.WorldPosition;
            navConsole = Item.ItemList.Find(i => i.HasTag("command"))?.GetComponent<Steering>();
            sonar = navConsole?.Item.GetComponent<Sonar>();
            reactor = Item.ItemList.Find(i => i.HasTag("reactor"))?.GetComponent<Reactor>();

#if DEBUG
            if (reactor == null || navConsole == null || sonar == null)
            {
                infoBox = CreateInfoFrame("Submarine not compatible with the tutorial:"
                    + "\nReactor - " + (reactor != null ? "OK" : "Tag 'reactor' not found")
                    + "\nNavigation Console - " + (navConsole != null ? "OK" : "Tag 'command' not found")
                    + "\nSonar - " + (sonar != null ? "OK" : "Not found under Navigation Console"), true);
                CoroutineManager.StartCoroutine(WaitForErrorClosed());
                return;
            }
#endif
            if (disableTutorialOnDeficiencyFound)
            {
                if (reactor == null || navConsole == null || sonar == null)
                {
                    Stop();
                    return;
                }
            }
            else
            {
                if (navConsole == null) segments[2].IsTriggered = true; // Disable navigation console usage tutorial
                if (reactor == null) segments[5].IsTriggered = true; // Disable reactor usage tutorial
                if (sonar == null) segments[6].IsTriggered = true; // Disable enemy on sonar tutorial
            }

            crew = GameMain.GameSession.CrewManager.GetCharacters().ToList();
            mechanic = CrewMemberWithJob("mechanic");
            engineer = CrewMemberWithJob("engineer");

            Completed = true; // Trigger completed at start to prevent the contextual tutorial from automatically activating on starting new campaigns after this one
            started = true;
        }

#if DEBUG
        private IEnumerable<object> WaitForErrorClosed()
        {
            while (infoBox != null) yield return null;
            Stop();
        }
#endif

        public void Stop()
        {
            started = ContentRunning = Initialized = false;
            videoPlayer.Remove();
            videoPlayer = null;
            characterTimeOnSonar = null;
        }

        private void CreateObjectiveFrame()
        {
            holderFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center));
            objectiveFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ObjectiveAnchor, holderFrame.RectTransform), style: null);

            /*int toggleButtonWidth = (int)(30 * GUI.Scale);
            int toggleButtonHeight = (int)(40 * GUI.Scale);
            toggleButton = new GUIButton(new RectTransform(new Point(toggleButtonWidth, toggleButtonHeight), objectiveFrame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(0, 20) }, style: "UIToggleButton");
            toggleButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                objectivesOpen = !objectivesOpen;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = objectivesOpen ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                return true;
            };

            objectiveBaseOffset = new Point((int)(-2.5f * toggleButton.Rect.Width), 0);*/
        }

        public override void AddToGUIUpdateList()
        {
            if (objectiveFrame != null && activeObjectives.Count > 0)
            {
                objectiveFrame.AddToGUIUpdateList(order: -1);
            }
            base.AddToGUIUpdateList();
            if (videoPlayer != null)
            {
                videoPlayer.AddToGUIUpdateList();
            }
        }

        public override void Update(float deltaTime)
        {
            if (videoPlayer != null)
            {
                videoPlayer.Update();
            }

            if (!started || ContentRunning) return;

            deltaTime *= 0.5f;
                       
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered || activeObjectives.Contains(segments[i])) continue;
                if (CheckContextualTutorials(i, deltaTime)) // Found a relevant tutorial, halt finding new ones
                {
                    break;
                }
            }

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                CheckActiveObjectives(activeObjectives[i], deltaTime);
            }

            /*if (activeObjectives.Count > 0)
            {
                if (objectivesOpen)
                {
                    openState -= deltaTime * 5.0f;
                }
                else
                {
                    openState += deltaTime * 5.0f;
                }

                openState = MathHelper.Clamp(openState, 0.0f, 1.0f);

                float widestObjective = 0f;
                for (int i = 0; i < activeObjectives.Count; i++)
                {
                    if (activeObjectives[i].ReplayButton.Rect.Width > widestObjective)
                    {
                        widestObjective = activeObjectives[i].ReplayButton.Rect.Width;
                    }
                }

                float objectivesHiddenOffset = widestObjective + toggleButton.Rect.Width + 100f;

                for (int i = 0; i < activeObjectives.Count; i++)
                {
                    activeObjectives[i].ReplayButton.RectTransform.AbsoluteOffset = objectiveBaseOffset + new Point((int)MathHelper.SmoothStep(0, objectivesHiddenOffset, openState), 0);
                }
            }*/
        }

        private void ClosePreTextAndTriggerVideoCallback()
        {
            videoPlayer.LoadContentWithObjective(playableContentPath, new VideoPlayer.VideoSettings(activeSegment.VideoContent), new VideoPlayer.TextSettings(activeSegment.VideoContent), activeSegment.Id, true, activeSegment.Objective, CurrentSegmentStopCallback);
        }

        private void CurrentSegmentStopCallback()
        {
            if (!string.IsNullOrEmpty(activeSegment.Objective))
            {
                AddNewObjective(activeSegment);
            }

            activeSegment = null;
            ContentRunning = false;
        }

        private void AddNewObjective(TutorialSegment segment)
        {
            activeObjectives.Add(segment);

            Point replayButtonSize = new Point((int)GUI.ObjectiveNameFont.MeasureString(segment.Objective).X, (int)(GUI.ObjectiveNameFont.MeasureString(segment.Objective).Y * 1.45f));

            segment.ReplayButton = new GUIButton(new RectTransform(replayButtonSize, objectiveFrame.RectTransform, Anchor.TopRight, Pivot.TopRight) { AbsoluteOffset = new Point(/*(int)(-2.5f * toggleButton.Rect.Width)*/0, (replayButtonSize.Y + 20) * (activeObjectives.Count - 1)) }, style: null);
            segment.ReplayButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                ReplaySegmentVideo(segment);
                return true;
            };

            int yOffset = (int)(GUI.ObjectiveNameFont.MeasureString(objectiveTranslated).Y / 2f) + 5;
            segment.LinkedTitle = new GUITextBlock(new RectTransform(new Point(replayButtonSize.X, yOffset), segment.ReplayButton.RectTransform, Anchor.Center, Pivot.BottomCenter) { AbsoluteOffset = new Point(10, 0) }, objectiveTranslated, textColor: Color.White, font: GUI.ObjectiveTitleFont, textAlignment: Alignment.CenterRight);
            segment.LinkedText = new GUITextBlock(new RectTransform(new Point(replayButtonSize.X, yOffset), segment.ReplayButton.RectTransform, Anchor.Center, Pivot.TopCenter) { AbsoluteOffset = new Point(10, 0) }, segment.Objective, textColor: new Color(4, 180, 108), font: GUI.ObjectiveNameFont, textAlignment: Alignment.CenterRight);

            segment.LinkedTitle.Color = segment.LinkedTitle.HoverColor = segment.LinkedTitle.PressedColor = segment.LinkedTitle.SelectedColor = Color.Transparent;
            segment.LinkedText.Color = segment.LinkedText.HoverColor = segment.LinkedText.PressedColor = segment.LinkedText.SelectedColor = Color.Transparent;
            segment.ReplayButton.Color = segment.ReplayButton.HoverColor = segment.ReplayButton.PressedColor = segment.ReplayButton.SelectedColor = Color.Transparent;
        }

        private void RemoveCompletedObjective(TutorialSegment objective)
        {
            objectiveFrame.RemoveChild(objective.ReplayButton);
            activeObjectives.Remove(objective);
            objective.IsTriggered = true;

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                activeObjectives[i].ReplayButton.RectTransform.AbsoluteOffset = new Point(0, (activeObjectives[i].ReplayButton.Rect.Height + 20) * i);
            }
        }

        private bool CheckContextualTutorials(int index, float deltaTime)
        {
            switch (index)
            {
                case 0: // Welcome: Game Start [Text]
                    if (tutorialTimer < 0.5f)
                    {
                        tutorialTimer += deltaTime;
                        return false;
                    }
                    break;
                case 1: // Command Reactor: 2 seconds after 'Welcome' dismissed and only if no command given to start reactor [Video]
                    if (tutorialTimer < 2.5f)
                    {
                        tutorialTimer += deltaTime;

                        if (HasOrder("operatereactor"))
                        {
                            segments[index].IsTriggered = true;
                            tutorialTimer = 2.5f;
                        }
                        return false;
                    }
                    break;
                case 2: // Nav Console: 2 seconds after 'Command Reactor' dismissed or if nav console is activated [Video]
                    if (!segments[1].IsTriggered || !IsReactorPoweredUp()) return false; // Do not advance tutorial based on this segment if reactor has not been powered up
                    if (Character.Controlled?.SelectedConstruction != navConsole.Item)
                    {                       
                        if (tutorialTimer < 4.5f)
                        {
                            tutorialTimer += deltaTime;
                            return false;
                        }
                    }
                    else
                    {
                        tutorialTimer = 4.5f;
                    }

                    TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                    return true;
                case 3: // Objective: Travel ~150 meters and while sub is not flooding [Text]
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 8000f || IsFlooding())
                    {
                        return false;
                    }
                    else // Called earlier than others due to requiring specific args
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 4: // Flood: Hull is breached and sub is taking on water [Video]
                    if (!IsFlooding())
                    {
                        return false;
                    }
                    break;
                case 5: // Reactor: Player uses reactor for the first time [Video]
                    if (Character.Controlled?.SelectedConstruction != reactor.Item)
                    {
                        return false;
                    }
                    break;
                case 6: // Enemy on Sonar:  Player witnesses creature signal on sonar for 5 seconds [Video]
                    if (!HasEnemyOnSonarForDuration(deltaTime))
                    {
                        return false;
                    }
                    break;
                case 7: // Degrading1: Any equipment degrades to 50% health or less and player has not assigned any crew to perform maintenance [Text]
                    if ((mechanic == null || mechanic.IsDead) && (engineer == null || engineer.IsDead)) // Both engineer and mechanic are dead or do not exist -> do not display
                    {
                        return false;
                    }

                    bool degradedEquipmentFound = false;

                    foreach (Item item in Item.ItemList)
                    {
                        if (!item.Repairables.Any() || item.Condition > 50.0f) continue;
                        degradedEquipmentFound = true;
                        break;
                    }

                    if (degradedEquipmentFound)
                    {
                        if (HasOrder("repairsystems", "jobspecific"))
                        {
                            segments[index].IsTriggered = true;
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                    break;
                case 8: // Medical: Crewmember is injured but not killed [Video]
                    for (int i = 0; i < crew.Count; i++)
                    {
                        Character member = crew[i];
                        if (member.Vitality < member.MaxVitality && !member.IsDead)
                        {
                            injuredMember = member;
                            TriggerTutorialSegment(index, new string[] { member.Info.DisplayName,
                                (member.Info.Gender == Gender.Male) ? TextManager.Get("PronounPossessiveMale").ToLower() : TextManager.Get("PronounPossessiveFemale").ToLower() });
                            return true;
                        }
                    }

                    if (injuredMember == null) return false;
                    break;
                case 9: // Approach1: Destination is within ~100m [Video]
                    if (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 8000f)
                    {
                        return false;
                    }
                    else
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 10: // Approach2: Sub is docked [Text]
                    if (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0)
                    {
                        return false;
                    }
                    break;
            }

            TriggerTutorialSegment(index);
            return true;
        }

        private bool HasObjective(string objectiveName)
        {
            for (int i = 0; i < activeObjectives.Count; i++)
            {
                if (activeObjectives[i].Id == objectiveName) return true;
            }

            return false;
        }

        private void CheckActiveObjectives(TutorialSegment objective, float deltaTime)
        {
            switch(objective.Id)
            {
                case "ReactorCommand": // Reactor commanded
                    if (!HasOrder("operatereactor")) return;
                    break;
                case "NavConsole": // traveled 50 meters
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 4000f)
                    {
                        return;
                    }
                    break;
                case "Flood": // Hull breaches repaired
                    if (IsFlooding()) return;
                    break;
                case "Medical":
                    if (injuredMember != null && !injuredMember.IsDead)
                    {
                        if (injuredMember.CharacterHealth.DroppedItem == null) return;
                    }
                    break;
                case "EnemyOnSonar": // Enemy dispatched
                    if (HasEnemyOnSonarForDuration(deltaTime))
                    {
                        return;
                    }
                    break;
                case "Degrading": // Fixed
                    if (mechanic != null && !mechanic.IsDead)
                    {
                        HumanAIController humanAI = mechanic.AIController as HumanAIController;
                        if (mechanic.CurrentOrder?.AITag != "repairsystems" || humanAI.CurrentOrderOption != "jobspecific")
                        {
                            return;
                        }
                    }

                    if (engineer != null && !engineer.IsDead)
                    {
                        HumanAIController humanAI = engineer.AIController as HumanAIController;
                        if (engineer.CurrentOrder?.AITag != "repairsystems" || humanAI.CurrentOrderOption != "jobspecific")
                        {
                            return;
                        }
                    }

                    break;
                case "Approach1": // Wait until docked
                    if (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0)
                    {
                        return;
                    }
                    break;
            }

            RemoveCompletedObjective(objective);
        }

        private bool IsReactorPoweredUp()
        {
            float load = 0.0f;
            List<Connection> connections = reactor.Item.Connections;
            if (connections != null && connections.Count > 0)
            {
                foreach (Connection connection in connections)
                {
                    if (!connection.IsPower) continue;
                    foreach (Connection recipient in connection.Recipients)
                    {
                        if (!(recipient.Item is Item it)) continue;

                        PowerTransfer pt = it.GetComponent<PowerTransfer>();
                        if (pt == null) continue;

                        load = Math.Max(load, pt.PowerLoad);
                    }
                }
            }

            return Math.Abs(load + reactor.CurrPowerConsumption) < 10;
        }

        private Character CrewMemberWithJob(string job)
        {
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i].Info.Job.Name == job) return crew[i];
            }

            return null;
        }

        private bool HasOrder(string aiTag, string option = null)
        {
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i].CurrentOrder?.AITag == aiTag)
                {
                    if (option == null)
                    {
                        return true;
                    }
                    else
                    {
                        HumanAIController humanAI = crew[i].AIController as HumanAIController;
                        return humanAI.CurrentOrderOption == option;
                    }
                }
            }

            return false;
        }

        private bool IsFlooding()
        {
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedWall == null) continue;
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) continue;
                if (gap.Submarine == null) continue;
                if (gap.Submarine != Submarine.MainSub) continue;
                return true;
            }

            return false;
        }

        private bool HasEnemyOnSonarForDuration(float deltaTime)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled || !(c.AIController is EnemyAIController)) continue;
                if (sonar.DetectSubmarineWalls && c.AnimController.CurrentHull == null && sonar.Item.CurrentHull != null) continue;
                if (Vector2.DistanceSquared(c.WorldPosition, sonar.Item.WorldPosition) > sonar.Range * sonar.Range)
                {
                    for (int i = 0; i < characterTimeOnSonar.Count; i++)
                    {
                        if (characterTimeOnSonar[i].First == c)
                        {
                            characterTimeOnSonar.RemoveAt(i);
                            break;
                        }
                    }

                    continue;
                }

                Pair<Character, float> pair = characterTimeOnSonar.Find(ct => ct.First == c);
                if (pair != null)
                {
                    pair.Second += deltaTime;
                }
                else
                {
                    characterTimeOnSonar.Add(new Pair<Character, float>(c, deltaTime));
                }
            }

            return characterTimeOnSonar.Find(ct => ct.Second >= requiredTimeOnSonar && !ct.First.IsDead) != null;
        }

        private void TriggerTutorialSegment(int index, params object[] args)
        {
            ContentRunning = true;
            activeSegment = segments[index];

            string tutorialText = TextManager.GetFormatted(activeSegment.TextContent.GetAttributeString("tag", ""), true, args);
            string objectiveText = string.Empty;

            if (!string.IsNullOrEmpty(activeSegment.Objective))
            {
                if (args.Length == 0)
                {
                    objectiveText = activeSegment.Objective;
                }
                else
                {
                    objectiveText = string.Format(activeSegment.Objective, args);
                }

                activeSegment.Objective = objectiveText;
            }
            else
            {
                activeSegment.IsTriggered = true; // Complete at this stage only if no related objective
            }

            switch (activeSegment.ContentType)
            {
                case ContentTypes.None:
                    break;
                case ContentTypes.Video:
                    infoBox = CreateInfoFrame(TextManager.Get(activeSegment.Id), tutorialText,
                          activeSegment.TextContent.GetAttributeInt("width", 300),
                          activeSegment.TextContent.GetAttributeInt("height", 80),
                          activeSegment.TextContent.GetAttributeString("anchor", "Center"), true, ClosePreTextAndTriggerVideoCallback);                    
                    break;
                case ContentTypes.TextOnly:
                    infoBox = CreateInfoFrame(TextManager.Get(activeSegment.Id), tutorialText,
                                              activeSegment.TextContent.GetAttributeInt("width", 300),
                                              activeSegment.TextContent.GetAttributeInt("height", 80),
                                              activeSegment.TextContent.GetAttributeString("anchor", "Center"), true, CurrentSegmentStopCallback);
                    break;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (!segments[i].IsTriggered) return;
            }

            CoroutineManager.StartCoroutine(WaitToStop()); // Completed
        }

        private void ReplaySegmentVideo(TutorialSegment segment)
        {
            if (ContentRunning) return;
            ContentRunning = true;
            videoPlayer.LoadContent(playableContentPath, new VideoPlayer.VideoSettings(segment.VideoContent), new VideoPlayer.TextSettings(segment.VideoContent), segment.Id, true, () => ContentRunning = false);
        }

        private IEnumerable<object> WaitToStop()
        {
            while (ContentRunning) yield return null;
            Stop();
        }
    }
}
