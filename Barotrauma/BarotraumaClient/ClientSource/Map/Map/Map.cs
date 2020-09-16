﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    partial class Map
    {
        public bool AllowDebugTeleport;

        class MapAnim
        {
            public Location StartLocation;
            public Location EndLocation;
            public string StartMessage;
            public string EndMessage;

            /// <summary>
            /// Initial zoom (0 - 1, from min zoom to max)
            /// </summary>
            public float? StartZoom;
            /// <summary>
            /// Initial zoom (0 - 1, from min zoom to max)
            /// </summary>
            public float? EndZoom;

            private float startDelay;
            public float StartDelay
            {
                get { return startDelay; }
                set
                {
                    startDelay = value;
                    Timer = -startDelay;
                }
            }

            public Vector2? StartPos;

            public float Duration;
            public float Timer;

            public bool Finished;
        }

        private readonly Queue<MapAnim> mapAnimQueue = new Queue<MapAnim>();

        public Location HighlightedLocation { get; private set; }

        private static Sprite noiseOverlay;

        public Vector2 DrawOffset;
        private Vector2 drawOffsetNoise;

        private Vector2 currLocationIndicatorPos;

        private float zoom = 3.0f;
        private float targetZoom;

        private Rectangle borders;
        
        private Sprite[,] mapTiles;
        private bool[,] tileDiscovered;

#if DEBUG
        private GUIComponent editor;

        private void CreateEditor()
        {
            editor = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), GUI.Canvas, Anchor.TopRight, minSize: new Point(400, 0)));
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), editor.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f,
                CanBeFocused = false
            };

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.95f), paddedFrame.RectTransform, Anchor.Center));
            new SerializableEntityEditor(listBox.Content.RectTransform, generationParams, false, true);

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform), "Generate")
            {
                OnClicked = (btn, userData) =>
                {
                    Rand.SetSyncedSeed(ToolBox.StringToInt(this.Seed));
                    Generate();
                    InitProjectSpecific();
                    return true;
                }
            };
        }
#endif
        public Location CurrentDisplayLocation
        {
            get 
            {
                return GameMain.GameSession.Campaign.CurrentDisplayLocation;
            }
        }

        partial void InitProjectSpecific()
        {
            noiseOverlay ??= new Sprite("Content/UI/noise.png", Vector2.Zero);

            OnLocationChanged = LocationChanged;

            borders = new Rectangle(
                (int)Locations.Min(l => l.MapPosition.X),
                (int)Locations.Min(l => l.MapPosition.Y),
                (int)Locations.Max(l => l.MapPosition.X),
                (int)Locations.Max(l => l.MapPosition.Y));
            borders.Width -= borders.X;
            borders.Height -= borders.Y;

            if (CurrentLocation != null)
            {
                DrawOffset = -CurrentLocation.MapPosition;
            }


            Vector2 tileSize = generationParams.MapTiles.Values.First().First().size * generationParams.MapTileScale;
            int tilesX = (int)Math.Ceiling(Width / tileSize.X);
            int tilesY = (int)Math.Ceiling(Height / tileSize.Y);
            mapTiles = new Sprite[tilesX, tilesY];
            tileDiscovered = new bool[tilesX, tilesY];
            for (int x = 0; x < tilesX; x++)
            {
                for (int y = 0; y < tilesY; y++)
                {
                    var biome = GetBiome(x * tileSize.X);
                    var tileList = generationParams.MapTiles.ContainsKey(biome.Identifier) ?
                        generationParams.MapTiles[biome.Identifier] :
                        generationParams.MapTiles.Values.First();
                    mapTiles[x, y] = tileList[x % tileList.Count];                    
                }
            }

            RemoveFogOfWar(StartLocation);

            GenerateLocationConnectionVisuals();
        }

        partial void GenerateLocationConnectionVisuals()
        {
            foreach (LocationConnection connection in Connections)
            {
                Vector2 connectionStart = connection.Locations[0].MapPosition;
                Vector2 connectionEnd = connection.Locations[1].MapPosition;
                float connectionLength = Vector2.Distance(connectionStart, connectionEnd);
                int iterations = Math.Min((int)Math.Sqrt(connectionLength * generationParams.ConnectionIndicatorIterationMultiplier), 5);
                connection.CrackSegments.Clear();
                connection.CrackSegments.AddRange(MathUtils.GenerateJaggedLine(
                    connectionStart, connectionEnd,
                    iterations, connectionLength * generationParams.ConnectionIndicatorDisplacementMultiplier));
            }
        }

        private void LocationChanged(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) return;
            //focus on starting location
            if (prevLocation != null)
            {
                mapAnimQueue.Enqueue(new MapAnim()
                {
                    EndZoom = 1.0f,
                    EndLocation = prevLocation,
                    Duration = MathHelper.Clamp(Vector2.Distance(-DrawOffset, prevLocation.MapPosition) / 1000.0f, 0.1f, 0.5f)
                });
                mapAnimQueue.Enqueue(new MapAnim()
                {
                    EndZoom = 0.5f,
                    StartLocation = prevLocation,
                    EndLocation = newLocation,
                    Duration = 2.0f,
                    StartDelay = 0.5f
                });
            }
            else
            {
                currLocationIndicatorPos = CurrentLocation.MapPosition;
            }

            RemoveFogOfWar(newLocation);
        }

        private void RemoveFogOfWar(Location location, bool removeFromAdjacentLocations = true)
        {
            if (location == null) { return; }
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;
            int startX = (int)Math.Max(Math.Floor(location.MapPosition.X / mapTileSize.X - 0.25f), 0);
            int startY = (int)Math.Max(Math.Floor(location.MapPosition.Y / mapTileSize.Y - 0.25f), 0);
            int endX = (int)Math.Min(Math.Floor(location.MapPosition.X / mapTileSize.X + 0.25f), mapTiles.GetLength(0));
            int endY = (int)Math.Min(Math.Floor(location.MapPosition.Y / mapTileSize.Y + 0.25f), mapTiles.GetLength(1));
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    tileDiscovered[x, y] = true;
                }
            }
            if (removeFromAdjacentLocations)
            {
                foreach (LocationConnection c in location.Connections)
                {
                    var otherLocation = c.OtherLocation(location);
                    RemoveFogOfWar(otherLocation, removeFromAdjacentLocations: false);
                }
            }
        }

        private bool IsInFogOfWar(Location location)
        {
            if (GameMain.DebugDraw) { return false; }
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;
            int x = (int)Math.Floor(location.MapPosition.X / mapTileSize.X);
            int y = (int)Math.Floor(location.MapPosition.Y / mapTileSize.Y);

            return !tileDiscovered[MathHelper.Clamp(x, 0, tileDiscovered.Length), MathHelper.Clamp(y, 0, tileDiscovered.Length)];
        }

        partial void ChangeLocationType(Location location, string prevName, LocationTypeChange change)
        {
            if (change.Messages.Any())
            {
                string msg = change.Messages[Rand.Range(0, change.Messages.Count)]
                    .Replace("[previousname]", prevName)
                    .Replace("[name]", location.Name);
                location.LastTypeChangeMessage = msg;
                if (GameMain.Client != null)
                {
                    GameMain.Client.AddChatMessage(msg, Networking.ChatMessageType.Default, TextManager.Get("RadioAnnouncerName"));
                }
                else
                {
                    GameMain.GameSession?.GameMode.CrewManager.AddSinglePlayerChatMessage(
                        TextManager.Get("RadioAnnouncerName"),
                        msg,
                        Networking.ChatMessageType.Default,
                        sender: null);
                }
            }         
        }

        partial void ClearAnimQueue()
        {
            mapAnimQueue.Clear();
        }

        public void Update(float deltaTime, GUICustomComponent mapContainer)
        {
            Rectangle rect = mapContainer.Rect;

            if (CurrentDisplayLocation != null)
            {
                if (!CurrentDisplayLocation.Discovered)
                {
                    RemoveFogOfWar(CurrentDisplayLocation);
                    CurrentDisplayLocation.Discovered = true;
                    if (CurrentDisplayLocation.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = CurrentDisplayLocation;
                    }
                }
            }

            currLocationIndicatorPos = Vector2.Lerp(currLocationIndicatorPos, CurrentDisplayLocation.MapPosition, deltaTime);
#if DEBUG
            if (GameMain.DebugDraw)
            {
                if (editor == null) CreateEditor();
                editor.AddToGUIUpdateList(order: 1);
            }
#endif

            if (mapAnimQueue.Count > 0)
            {
                hudVisibility = Math.Max(hudVisibility - deltaTime, 0.0f);
                UpdateMapAnim(mapAnimQueue.Peek(), deltaTime);
                if (mapAnimQueue.Peek().Finished)
                {
                    mapAnimQueue.Dequeue();
                }
                return;
            }

            hudVisibility = Math.Min(hudVisibility + deltaTime, 0.75f + (float)Math.Sin(Timing.TotalTime * 3.0f) * 0.25f);
            
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
            Vector2 viewOffset = DrawOffset + drawOffsetNoise;

            float closestDist = 0.0f;
            HighlightedLocation = null;
            if (GUI.MouseOn == null || GUI.MouseOn == mapContainer)
            {
                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    if (IsInFogOfWar(location) && !(CurrentDisplayLocation?.Connections.Any(c => c.Locations.Contains(location)) ?? false) && !GameMain.DebugDraw) { continue; }

                    Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;
                    if (!rect.Contains(pos)) { continue; }

                    float iconScale = generationParams.LocationIconSize / location.Type.Sprite.size.X;
                    if (location == CurrentDisplayLocation) { iconScale *= 1.2f; }

                    Rectangle drawRect = location.Type.Sprite.SourceRect;
                    drawRect.Width = (int)(drawRect.Width * iconScale * zoom * 1.4f);
                    drawRect.Height = (int)(drawRect.Height * iconScale * zoom * 1.4f);
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!drawRect.Contains(PlayerInput.MousePosition)) { continue; }

                    float dist = Vector2.Distance(PlayerInput.MousePosition, pos);
                    if (HighlightedLocation == null || dist < closestDist)
                    {
                        closestDist = dist;
                        HighlightedLocation = location;
                    }
                }
            }

            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                float moveSpeed = 1000.0f;
                Vector2 moveAmount = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { moveAmount += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { moveAmount -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { moveAmount += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { moveAmount -= Vector2.UnitY; }
                DrawOffset += moveAmount * moveSpeed / zoom * deltaTime;
            }

            targetZoom = MathHelper.Clamp(targetZoom, generationParams.MinZoom, generationParams.MaxZoom);
            zoom = MathHelper.Lerp(zoom, targetZoom, 0.1f);

            if (GUI.MouseOn == mapContainer)
            {
                foreach (LocationConnection connection in Connections)
                {
                    if (HighlightedLocation != CurrentDisplayLocation &&
                        connection.Locations.Contains(HighlightedLocation) && connection.Locations.Contains(CurrentDisplayLocation))
                    {
                        if (PlayerInput.PrimaryMouseButtonClicked() &&
                            SelectedLocation != HighlightedLocation && HighlightedLocation != null)
                        {
                            //clients aren't allowed to select the location without a permission
                            if ((GameMain.GameSession?.GameMode as CampaignMode)?.AllowedToManageCampaign() ?? false)
                            {
                                SelectedConnection = connection;
                                SelectedLocation = HighlightedLocation;

                                OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
                                GameMain.Client?.SendCampaignState();
                            }
                        }
                    }
                }            

                targetZoom += PlayerInput.ScrollWheelSpeed / 500.0f;

                if (PlayerInput.MidButtonHeld() || (HighlightedLocation == null && PlayerInput.PrimaryMouseButtonHeld()))
                {
                    DrawOffset += PlayerInput.MouseSpeed / zoom;
                }
                if (AllowDebugTeleport)
                {
                    if (PlayerInput.DoubleClicked() && HighlightedLocation != null)
                    {
                        var passedConnection = CurrentDisplayLocation.Connections.Find(c => c.OtherLocation(CurrentDisplayLocation) == HighlightedLocation);
                        if (passedConnection != null)
                        {
                            passedConnection.Passed = true;
                        }

                        Location prevLocation = CurrentDisplayLocation;
                        CurrentLocation = HighlightedLocation;
                        Level.Loaded.DebugSetStartLocation(CurrentLocation);

                        CurrentLocation.Discovered = true;
                        CurrentLocation.CreateStore();
                        OnLocationChanged?.Invoke(prevLocation, CurrentLocation);
                        SelectLocation(-1);
                        if (GameMain.Client == null)
                        {
                            ProgressWorld();
                        }
                        else
                        {
                            GameMain.Client.SendCampaignState();
                        }
                    }

                    if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) && PlayerInput.PrimaryMouseButtonClicked() && HighlightedLocation != null)
                    {
                        int distance = DistanceToClosestLocationWithOutpost(HighlightedLocation, out Location foundLocation);
                        DebugConsole.NewMessage($"Distance to closest outpost from {HighlightedLocation.Name} to {foundLocation?.Name} is {distance}");
                    }

                    if (PlayerInput.PrimaryMouseButtonClicked() && HighlightedLocation == null)
                    {
                        SelectLocation(-1);
                    }
                }
            }
        }
        
        public void Draw(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            Rectangle rect = mapContainer.Rect;

            Vector2 viewSize = new Vector2(rect.Width / zoom, rect.Height / zoom);
            Vector2 edgeBuffer = rect.Size.ToVector2() / 2;
            DrawOffset.X = MathHelper.Clamp(DrawOffset.X, -Width - edgeBuffer.X + viewSize.X / 2.0f, edgeBuffer.X - viewSize.X / 2.0f);
            DrawOffset.Y = MathHelper.Clamp(DrawOffset.Y, -Height - edgeBuffer.Y + viewSize.Y / 2.0f, edgeBuffer.Y - viewSize.Y / 2.0f);

            drawOffsetNoise = new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.1f % 255, Timing.TotalTime * 0.1f % 255, 0) - 0.5f, 
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.2f % 255, Timing.TotalTime * 0.2f % 255, 0.5f) - 0.5f) * 10.0f;

            Vector2 viewOffset = DrawOffset + drawOffsetNoise;

            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);

            Rectangle prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, rect);
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);

            Vector2 topLeft = rectCenter + viewOffset;
            Vector2 bottomRight = rectCenter + (viewOffset + new Vector2(Width, Height));
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;

            int startX = (int)Math.Floor(-topLeft.X / mapTileSize.X) - 1;
            int startY = (int)Math.Floor(-topLeft.Y / mapTileSize.Y) - 1;
            int endX = (int)Math.Ceiling((-topLeft.X + rect.Width) / mapTileSize.X);
            int endY = (int)Math.Ceiling((-topLeft.Y + rect.Height) / mapTileSize.Y);

            float noiseT = (float)(Timing.TotalTime * 0.01f);
            cameraNoiseStrength = (float)PerlinNoise.CalculatePerlin(noiseT, noiseT * 0.5f, noiseT * 0.2f);
            float noiseScale = (float)PerlinNoise.CalculatePerlin(noiseT * 5.0f, noiseT * 2.0f, 0) * 5.0f;

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    int tileX = Math.Abs(x) % mapTiles.GetLength(0);
                    int tileY = Math.Abs(y) % mapTiles.GetLength(1);
                    Vector2 tilePos = rectCenter + (viewOffset + new Vector2(x, y) * mapTileSize) * zoom;
                    mapTiles[tileX, tileY].Draw(spriteBatch, tilePos, Color.White, origin: Vector2.Zero, scale: generationParams.MapTileScale * zoom);

                    if (GameMain.DebugDraw) { continue; }
                    if (!tileDiscovered[tileX, tileY] || x < 0 || y < 0 || x >= tileDiscovered.GetLength(0) || y >= tileDiscovered.GetLength(1))
                    {
                        generationParams.FogOfWarSprite?.Draw(spriteBatch, tilePos, Color.White * cameraNoiseStrength, origin: Vector2.Zero, scale: generationParams.MapTileScale * zoom);
                        noiseOverlay.DrawTiled(spriteBatch, tilePos, mapTileSize * zoom,
                            startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                            color: Color.White * cameraNoiseStrength * 0.2f,
                            textureScale: Vector2.One * noiseScale);
                    }
                }
            }

            if (GameMain.DebugDraw)
            {
                if (topLeft.X > rect.X)
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, (int)(topLeft.X - rect.X), rect.Height), Color.Black * 0.5f, true);
                if (topLeft.Y > rect.Y)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, rect.Y, (int)(bottomRight.X - topLeft.X), (int)(topLeft.Y - rect.Y)), Color.Black * 0.5f, true);
                if (bottomRight.X < rect.Right)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)bottomRight.X, rect.Y, (int)(rect.Right - bottomRight.X), rect.Height), Color.Black * 0.5f, true);
                if (bottomRight.Y < rect.Bottom)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, (int)bottomRight.Y, (int)(bottomRight.X - topLeft.X), (int)(rect.Bottom - bottomRight.Y)), Color.Black * 0.5f, true);
            }

            float rawNoiseScale = 1.0f + PerlinNoise.GetPerlin((int)(Timing.TotalTime * 1 - 1), (int)(Timing.TotalTime * 1 - 1));
            cameraNoiseStrength = PerlinNoise.GetPerlin((int)(Timing.TotalTime * 1 - 1), (int)(Timing.TotalTime * 1 - 1));

            noiseOverlay.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(), 
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color : Color.White * cameraNoiseStrength * 0.1f,
                textureScale: Vector2.One * rawNoiseScale);

            noiseOverlay.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(),
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color: new Color(20,20,20,50),
                textureScale: Vector2.One * rawNoiseScale * 2);

            noiseOverlay.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color: Color.White * cameraNoiseStrength * 0.1f,
                textureScale: Vector2.One * noiseScale);

            Pair<Rectangle, string> tooltip = null;
            if (generationParams.ShowLocations)
            {
                foreach (LocationConnection connection in Connections)
                {
                    if (IsInFogOfWar(connection.Locations[0]) && IsInFogOfWar(connection.Locations[1])) { continue; }
                    DrawConnection(spriteBatch, connection, rect, viewOffset);
                }
                
                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    if (IsInFogOfWar(location)) { continue; }
                    Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;
                    
                    Rectangle drawRect = location.Type.Sprite.SourceRect;
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!rect.Intersects(drawRect)) { continue; }

                    if (location == CurrentDisplayLocation )
                    {
                        generationParams.CurrentLocationIndicator.Draw(spriteBatch,
                            rectCenter + (currLocationIndicatorPos + viewOffset) * zoom,
                            generationParams.IndicatorColor,
                            generationParams.CurrentLocationIndicator.Origin, 0, Vector2.One * (generationParams.LocationIconSize / generationParams.CurrentLocationIndicator.size.X) * 1.7f * zoom);
                    }

                    if (location == SelectedLocation)
                    {
                        generationParams.SelectedLocationIndicator.Draw(spriteBatch,
                            rectCenter + (location.MapPosition + viewOffset) * zoom,
                            generationParams.IndicatorColor,
                            generationParams.SelectedLocationIndicator.Origin, 0, Vector2.One * (generationParams.LocationIconSize / generationParams.SelectedLocationIndicator.size.X) * 1.7f * zoom);
                    }

                    Color color = location.Type.SpriteColor;
                    if (!location.Discovered) { color = Color.White; }
                    if (location.Connections.Find(c => c.Locations.Contains(CurrentDisplayLocation)) == null)
                    {
                        color *= 0.5f;
                    }

                    float iconScale = location == CurrentDisplayLocation ? 1.2f : 1.0f;
                    if (location == HighlightedLocation)
                    {
                        iconScale *= 1.2f;
                    }

                    location.Type.Sprite.Draw(spriteBatch, pos, color, 
                        scale: generationParams.LocationIconSize / location.Type.Sprite.size.X * iconScale * zoom);
                    if (location.TypeChangeTimer <= 0 && !string.IsNullOrEmpty(location.LastTypeChangeMessage) && generationParams.TypeChangeIcon != null)
                    {
                        Vector2 typeChangeIconPos = pos + new Vector2(1.35f, -0.35f) * generationParams.LocationIconSize * 0.5f * zoom;
                        float typeChangeIconScale = 18.0f / generationParams.TypeChangeIcon.SourceRect.Width;
                        generationParams.TypeChangeIcon.Draw(spriteBatch, typeChangeIconPos, GUI.Style.Red, scale: typeChangeIconScale * zoom);
                        if (Vector2.Distance(PlayerInput.MousePosition, typeChangeIconPos) < generationParams.TypeChangeIcon.SourceRect.Width * zoom)
                        {
                            tooltip = new Pair<Rectangle, string>(
                                new Rectangle(typeChangeIconPos.ToPoint(), new Point(30)), 
                                location.LastTypeChangeMessage);
                        }
                    }
                    if (location != CurrentLocation && CurrentLocation.AvailableMissions.Any(m => m.Locations.Contains(location)) && generationParams.MissionIcon != null)
                    {
                        Vector2 missionIconPos = pos + new Vector2(1.35f, 0.35f) * generationParams.LocationIconSize * 0.5f * zoom;
                        float missionIconScale = 18.0f / generationParams.MissionIcon.SourceRect.Width;
                        generationParams.MissionIcon.Draw(spriteBatch, missionIconPos, generationParams.IndicatorColor, scale: missionIconScale * zoom);
                        if (Vector2.Distance(PlayerInput.MousePosition, missionIconPos) < generationParams.MissionIcon.SourceRect.Width * zoom)
                        {
                            var availableMissions = CurrentLocation.AvailableMissions.Where(m => m.Locations.Contains(location));
                            tooltip = new Pair<Rectangle, string>(
                                new Rectangle(missionIconPos.ToPoint(), new Point(30)), 
                                TextManager.Get("mission") + '\n'+ string.Join('\n', availableMissions.Select(m => "- " + m.Name)));
                        }
                    }

                    if (GameMain.DebugDraw && location == HighlightedLocation)
                    {
                        if (location.Reputation != null)
                        {
                            Vector2 dPos = pos;
                            dPos.Y += 48;
                            string name = $"Reputation: {location.Name}";
                            Vector2 nameSize = GUI.SmallFont.MeasureString(name);
                            GUI.DrawString(spriteBatch, dPos, name, Color.White, Color.Black * 0.8f, 4, font: GUI.SmallFont);
                            dPos.Y += nameSize.Y + 16;

                            Rectangle bgRect = new Rectangle((int)dPos.X, (int)dPos.Y, 256, 32);
                            bgRect.Inflate(8,8);
                            Color barColor = ToolBox.GradientLerp(location.Reputation.NormalizedValue, Color.Red, Color.Yellow, Color.LightGreen);
                            GUI.DrawRectangle(spriteBatch, bgRect, Color.Black * 0.8f, isFilled: true);
                            GUI.DrawRectangle(spriteBatch, new Rectangle((int)dPos.X, (int)dPos.Y, (int)(location.Reputation.NormalizedValue * 255), 32), barColor, isFilled: true);
                            string reputationValue = location.Reputation.Value.ToString(CultureInfo.InvariantCulture);
                            Vector2 repValueSize = GUI.SubHeadingFont.MeasureString(reputationValue);
                            GUI.DrawString(spriteBatch, dPos + (new Vector2(256, 32) / 2) - (repValueSize / 2), reputationValue, Color.White, Color.Black, font: GUI.SubHeadingFont);
                            GUI.DrawRectangle(spriteBatch, new Rectangle((int)dPos.X, (int)dPos.Y, 256, 32), Color.White);
                        }
                    }
                }
            }

            DrawDecorativeHUD(spriteBatch, rect);

            if (HighlightedLocation != null)
            {
                Vector2 pos = rectCenter + (HighlightedLocation.MapPosition + viewOffset) * zoom;
                pos.X += 50 * zoom;
                Vector2 nameSize = GUI.LargeFont.MeasureString(HighlightedLocation.Name);
                Vector2 typeSize = GUI.Font.MeasureString(HighlightedLocation.Type.Name);
                Vector2 size = new Vector2(Math.Max(nameSize.X, typeSize.X), nameSize.Y + typeSize.Y);
                GUI.Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
                    spriteBatch, new Rectangle((int)(pos.X - 60 * GUI.Scale), (int)(pos.Y - size.Y), (int)(size.X + 120 * GUI.Scale), (int)(size.Y * 2.2f)), Color.Black * hudVisibility);
                GUI.DrawString(spriteBatch, pos - new Vector2(0.0f, size.Y / 2),
                    HighlightedLocation.Name, GUI.Style.TextColor * hudVisibility * 1.5f, font: GUI.LargeFont);
                GUI.DrawString(spriteBatch, pos + new Vector2(0.0f, size.Y / 2 - GUI.Font.MeasureString(HighlightedLocation.Type.Name).Y),
                    HighlightedLocation.Type.Name, GUI.Style.TextColor * hudVisibility * 1.5f);
            }
            if (tooltip != null)
            {
                GUIComponent.DrawToolTip(spriteBatch, tooltip.Second, tooltip.First);
            }
            spriteBatch.End();
            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
        }

        private void DrawConnection(SpriteBatch spriteBatch, LocationConnection connection, Rectangle viewArea, Vector2 viewOffset, Color? overrideColor = null)
        {
            Color connectionColor;
            if (GameMain.DebugDraw)
            {
                float sizeFactor = MathUtils.InverseLerp(
                   generationParams.SmallLevelConnectionLength,
                   generationParams.LargeLevelConnectionLength,
                   connection.Length);
                connectionColor = ToolBox.GradientLerp(sizeFactor, Color.LightGreen, GUI.Style.Orange, GUI.Style.Red);
            }
            else if (overrideColor.HasValue)
            {
                connectionColor = overrideColor.Value;
            }
            else
            {
                connectionColor = connection.Passed ? generationParams.ConnectionColor : generationParams.UnvisitedConnectionColor;
            }

            int width = (int)(generationParams.LocationConnectionWidth * zoom);

            if (Level.Loaded?.LevelData == connection.LevelData)
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width = (int)(width * 1.5f);
            }
            if (SelectedLocation != CurrentDisplayLocation &&
                (connection.Locations.Contains(SelectedLocation) && connection.Locations.Contains(CurrentDisplayLocation)))
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width *= 2;
            }
            else if (HighlightedLocation != CurrentDisplayLocation &&
                    (connection.Locations.Contains(HighlightedLocation) && connection.Locations.Contains(CurrentDisplayLocation)))
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width *= 2;
            }

            Vector2 rectCenter = viewArea.Center.ToVector2();

            int startIndex = connection.CrackSegments.Count > 2 ? 1 : 0;
            int endIndex = connection.CrackSegments.Count > 2 ? connection.CrackSegments.Count - 1 : connection.CrackSegments.Count;

            for (int i = startIndex; i < endIndex; i++)
            {
                var segment = connection.CrackSegments[i];

                Vector2 start = rectCenter + (segment[0] + viewOffset) * zoom;
                Vector2 end = rectCenter + (segment[1] + viewOffset) * zoom;

                if (!viewArea.Contains(start) && !viewArea.Contains(end))
                {
                    continue;
                }
                else
                {
                    if (MathUtils.GetLineRectangleIntersection(start, end, new Rectangle(viewArea.X, viewArea.Y + viewArea.Height, viewArea.Width, viewArea.Height), out Vector2 intersection))
                    {
                        if (!viewArea.Contains(start))
                        {
                            start = intersection;
                        }
                        else
                        {
                            end = intersection;
                        }
                    }
                }

                float a = 1.0f;
                if (!connection.Locations[0].Discovered && !connection.Locations[1].Discovered)
                {
                    if (IsInFogOfWar(connection.Locations[0]))
                    {
                        a = (float)i / connection.CrackSegments.Count;
                    }
                    else if (IsInFogOfWar(connection.Locations[1]))
                    {
                        a = 1.0f - (float)i / connection.CrackSegments.Count;
                    }
                }
                float dist = Vector2.Distance(start, end);
                var connectionSprite = connection.Passed ? generationParams.PassedConnectionSprite : generationParams.ConnectionSprite;
                spriteBatch.Draw(connectionSprite.Texture,
                    new Rectangle((int)start.X, (int)start.Y, (int)(dist - 1 * zoom), width),
                    connectionSprite.SourceRect, connectionColor * a, MathUtils.VectorToAngle(end - start),
                    new Vector2(0, connectionSprite.size.Y / 2), SpriteEffects.None, 0.01f);
            }

            if (GameMain.DebugDraw && zoom > 1.0f && generationParams.ShowLevelTypeNames)
            {
                Vector2 center = rectCenter + (connection.CenterPos + viewOffset) * zoom;
                if (viewArea.Contains(center) && connection.Biome != null)
                {
                    GUI.DrawString(spriteBatch, center, connection.Biome.Identifier + " (" + connection.Difficulty + ")", Color.White);
                }
            }
        }

        private float hudVisibility;
        private float cameraNoiseStrength;

        private void DrawDecorativeHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            generationParams.DecorativeGraphSprite.Draw(spriteBatch, (int)((Timing.TotalTime * 5.0f) % generationParams.DecorativeGraphSprite.FrameCount),
                new Vector2(rect.Left, rect.Top), Color.White, Vector2.Zero, 0, Vector2.One * GUI.Scale);

            GUI.DrawString(spriteBatch,
                new Vector2(rect.Right - GUI.IntScale(170), rect.Y + GUI.IntScale(5)),
                "JOVIAN FLUX " + ((cameraNoiseStrength + Rand.Range(-0.02f, 0.02f)) * 500), generationParams.IndicatorColor * hudVisibility, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch,
                new Vector2(rect.X + GUI.IntScale(15), rect.Bottom - GUI.IntScale(25)),
                "LAT " + (-DrawOffset.Y / 100.0f) + "   LON " + (-DrawOffset.X / 100.0f), generationParams.IndicatorColor * hudVisibility, font: GUI.SmallFont);
        }

        private void UpdateMapAnim(MapAnim anim, float deltaTime)
        {
            //pause animation while there are messageboxes on screen
            if (GUIMessageBox.MessageBoxes.Count > 0) return;

            if (!string.IsNullOrEmpty(anim.StartMessage))
            {
                new GUIMessageBox("", anim.StartMessage);
                anim.StartMessage = null;
                return;
            }

            if (anim.StartZoom == null) { anim.StartZoom = MathUtils.InverseLerp(generationParams.MinZoom, generationParams.MaxZoom, zoom); }
            if (anim.EndZoom == null) { anim.EndZoom = MathUtils.InverseLerp(generationParams.MinZoom, generationParams.MaxZoom, zoom); }

            anim.StartPos = (anim.StartLocation == null) ? -DrawOffset : anim.StartLocation.MapPosition;

            anim.Timer = Math.Min(anim.Timer + deltaTime, anim.Duration);
            float t = anim.Duration <= 0.0f ? 1.0f : Math.Max(anim.Timer / anim.Duration, 0.0f);
            DrawOffset = -Vector2.SmoothStep(anim.StartPos.Value, anim.EndLocation.MapPosition, t);
            DrawOffset += new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.3f % 255, Timing.TotalTime * 0.4f % 255, 0) - 0.5f,
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.4f % 255, Timing.TotalTime * 0.3f % 255, 0.5f) - 0.5f) * 50.0f * (float)Math.Sin(t * MathHelper.Pi);

            zoom =
                MathHelper.Lerp(generationParams.MinZoom, generationParams.MaxZoom,
                    MathHelper.SmoothStep(anim.StartZoom.Value, anim.EndZoom.Value, t));

            if (anim.Timer >= anim.Duration)
            {
                if (!string.IsNullOrEmpty(anim.EndMessage))
                {
                    new GUIMessageBox("", anim.EndMessage);
                    anim.EndMessage = null;
                    return;
                }
                anim.Finished = true;
            }
        }

        partial void RemoveProjSpecific()
        {
            noiseOverlay?.Remove();
            noiseOverlay = null;
        }
    }
}
