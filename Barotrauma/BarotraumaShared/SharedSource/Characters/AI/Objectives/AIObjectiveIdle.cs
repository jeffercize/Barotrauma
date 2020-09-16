﻿using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveIdle : AIObjective
    {
        public override string DebugTag => "idle";
        public override bool UnequipItems => true;
        public override bool AllowOutsideSubmarine => true;

        private BehaviorType behavior;
        public BehaviorType Behavior
        {
            get { return behavior; }
            set 
            { 
                behavior = value;
                switch (behavior)
                {
                    case BehaviorType.Active:
                        newTargetIntervalMin = 10;
                        newTargetIntervalMax = 20;
                        standStillMin = 2;
                        standStillMax = 10;
                        walkDurationMin = 5;
                        walkDurationMax = 10;
                        break;
                    case BehaviorType.Passive:
                        newTargetIntervalMin = 60;
                        newTargetIntervalMax = 120;
                        standStillMin = 30;
                        standStillMax = 60;
                        walkDurationMin = 5;
                        walkDurationMax = 10;
                        break;
                }
            }
        }

        private float newTargetIntervalMin;
        private float newTargetIntervalMax;
        private float standStillMin;
        private float standStillMax;
        private float walkDurationMin;
        private float walkDurationMax;

        public enum BehaviorType
        {
            Active,
            Passive,
            StayInHull
        }

        private Hull currentTarget;
        private float newTargetTimer;

        private bool searchingNewHull;

        private float standStillTimer;
        private float walkDuration;

        private Character tooCloseCharacter;

        const float chairCheckInterval = 5.0f;
        private float chairCheckTimer;

        private readonly List<Hull> targetHulls = new List<Hull>(20);
        private readonly List<float> hullWeights = new List<float>(20);

        public AIObjectiveIdle(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            Behavior = BehaviorType.Passive;
            standStillTimer = Rand.Range(-10.0f, 10.0f);
            walkDuration = Rand.Range(0.0f, 10.0f);
            chairCheckTimer = Rand.Range(0.0f, chairCheckInterval);
            CalculatePriority();
        }

        protected override bool Check() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace); }

        public readonly HashSet<string> PreferredOutpostModuleTypes = new HashSet<string>();

        private bool IsInWrongSub() => 
            character.Submarine == null ||
            currentTarget != null && currentTarget.Submarine != character.Submarine ||
            character.TeamID == Character.TeamType.FriendlyNPC && character.Submarine.TeamID != character.TeamID;

        public void CalculatePriority(float max = 0)
        {
            //Random = Rand.Range(0.5f, 1.5f);
            //randomTimer = randomUpdateInterval;
            //max = max > 0 ? max : Math.Min(Math.Min(AIObjectiveManager.RunPriority, AIObjectiveManager.OrderPriority) - 1, 100);
            //float initiative = character.GetSkillLevel("initiative");
            //Priority = MathHelper.Lerp(1, max, MathUtils.InverseLerp(100, 0, initiative * Random));
            Priority = 1;
        }

        public override float GetPriority() => Priority;

        public override void Update(float deltaTime)
        {
            //if (objectiveManager.CurrentObjective == this)
            //{
            //    if (randomTimer > 0)
            //    {
            //        randomTimer -= deltaTime;
            //    }
            //    else
            //    {
            //        CalculatePriority();
            //    }
            //}
        }

        private float timerMargin;

        private void SetTargetTimerLow()
        {
            // Increases the margin each time the method is called -> takes longer between the path finding calls.
            // The intention behind this is to reduce unnecessary path finding calls in cases where the bot can't find a path.
            timerMargin += 0.5f;
            timerMargin = Math.Min(timerMargin, newTargetIntervalMin);
            newTargetTimer = Math.Min(newTargetTimer, timerMargin);
        }

        private void SetTargetTimerHigh()
        {
            // This method is used to the timer between the current value and the min so that it never reaches 0.
            // Prevents pathfinder calls.
            newTargetTimer = Math.Max(newTargetTimer, newTargetIntervalMin);
            timerMargin = 0;
        }

        private void SetTargetTimerNormal()
        {
            newTargetTimer = currentTarget != null && character.AnimController.InWater ? newTargetIntervalMin : Rand.Range(newTargetIntervalMin, newTargetIntervalMax);
            timerMargin = 0;
        }

        protected override void Act(float deltaTime)
        {
            if (PathSteering == null) { return; }

            //don't keep dragging others when idling
            if (character.SelectedCharacter != null)
            {
                character.DeselectCharacter();
            }

            if (behavior != BehaviorType.StayInHull)
            {
                bool currentTargetIsInvalid = currentTarget == null || IsForbidden(currentTarget) || 
                    (PathSteering.CurrentPath != null && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull)));

                bool IsSteeringFinished() => PathSteering.CurrentPath != null && PathSteering.CurrentPath.Finished;

                if (currentTargetIsInvalid || currentTarget == null || IsSteeringFinished() && (IsForbidden(character.CurrentHull) || IsInWrongSub()))
                {
                    //don't reset to zero, otherwise the character will keep calling FindTargetHulls 
                    //almost constantly when there's a small number of potential hulls to move to
                    SetTargetTimerLow();
                }
                else if (character.IsClimbing)
                {
                    if (currentTarget == null)
                    {
                        SetTargetTimerLow();
                    }
                    else if (Math.Abs(character.AnimController.TargetMovement.Y) > 0.9f)
                    {
                        // Don't allow new targets when climbing straight up or down
                        SetTargetTimerHigh();
                    }
                }
                else if (character.AnimController.InWater)
                {
                    if (currentTarget == null)
                    {
                        SetTargetTimerLow();
                    }
                }
                if (newTargetTimer <= 0.0f)
                {
                    if (!searchingNewHull)
                    {
                        //find all available hulls first
                        FindTargetHulls();
                        searchingNewHull = true;
                        return;
                    }
                    else if (targetHulls.Count > 0)
                    {
                        //choose a random available hull
                        currentTarget = ToolBox.SelectWeightedRandom(targetHulls, hullWeights, Rand.RandSync.Unsynced);
                        bool isCurrentHullAllowed = !IsInWrongSub() && !IsForbidden(character.CurrentHull);
                        var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, errorMsgStr: $"AIObjectiveIdle {character.DisplayName}", nodeFilter: node =>
                        {
                            if (node.Waypoint.CurrentHull == null) { return false; }
                            // Check that there is no unsafe or forbidden hulls on the way to the target
                            if (node.Waypoint.CurrentHull != character.CurrentHull && HumanAIController.UnsafeHulls.Contains(node.Waypoint.CurrentHull)) { return false; }
                            if (isCurrentHullAllowed && IsForbidden(node.Waypoint.CurrentHull)) { return false; }
                            return true;
                        });
                        if (path.Unreachable)
                        {
                            //can't go to this room, remove it from the list and try another room next frame
                            int index = targetHulls.IndexOf(currentTarget);
                            targetHulls.RemoveAt(index);
                            hullWeights.RemoveAt(index);
                            PathSteering.Reset();
                            currentTarget = null;
                            return;
                        }
                        searchingNewHull = false;
                    }
                    else
                    {
                        // Couldn't find a target for some reason -> reset
                        SetTargetTimerHigh();
                        searchingNewHull = false;
                    }

                    if (currentTarget != null)
                    {
                        character.AIController.SelectTarget(currentTarget.AiTarget);
                        string errorMsg = null;
    #if DEBUG
                        bool isRoomNameFound = currentTarget.DisplayName != null;
                        errorMsg = "(Character " + character.Name + " idling, target " + (isRoomNameFound ? currentTarget.DisplayName : currentTarget.ToString()) + ")";
    #endif
                        var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, errorMsgStr: errorMsg, nodeFilter: node => node.Waypoint.CurrentHull != null);
                        PathSteering.SetPath(path);
                    }
                    SetTargetTimerNormal();
                }            
                newTargetTimer -= deltaTime;
            }

            //wander randomly 
            // - if reached the end of the path 
            // - if the target is unreachable
            // - if the path requires going outside
            if (!character.IsClimbing)
            {
                if (behavior == BehaviorType.StayInHull || SteeringManager != PathSteering || (PathSteering.CurrentPath != null &&
                    (PathSteering.CurrentPath.Finished || PathSteering.CurrentPath.Unreachable || PathSteering.CurrentPath.HasOutdoorsNodes)))
                {
                    Wander(deltaTime);
                    return;
                }
                character.SelectedConstruction = null;
            }

            if (currentTarget != null)
            {
                if (SteeringManager == PathSteering)
                {
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(currentTarget), weight: 1, nodeFilter: node => node.Waypoint.CurrentHull != null);
                }
                else
                {
                    character.AIController.SteeringManager.SteeringSeek(character.GetRelativeSimPosition(currentTarget));
                }
            }
            else
            {
                Wander(deltaTime);
            }
        }

        public void Wander(float deltaTime)
        {
            if (character.IsClimbing) { return; }
            if (!character.AnimController.InWater)
            {
                standStillTimer -= deltaTime;
                if (standStillTimer > 0.0f)
                {
                    walkDuration = Rand.Range(walkDurationMin, walkDurationMax);

                    if (character.CurrentHull != null && character.CurrentHull.Rect.Width > 150 && tooCloseCharacter == null)
                    {
                        foreach (Character c in Character.CharacterList)
                        {
                            if (c == character || !c.IsBot || c.CurrentHull != character.CurrentHull || !(c.AIController is HumanAIController humanAI)) { continue; }
                            if (Vector2.DistanceSquared(c.WorldPosition, character.WorldPosition) > 60.0f * 60.0f) { continue; }
                            if ((humanAI.ObjectiveManager.CurrentObjective is AIObjectiveIdle idleObjective && idleObjective.standStillTimer > 0.0f) ||
                                (humanAI.ObjectiveManager.CurrentObjective is AIObjectiveGoTo gotoObjective && gotoObjective.IsCloseEnough))
                            {
                                //if there are characters too close on both sides, don't try to steer away from them
                                //because it'll cause the character to spaz out trying to avoid both
                                if (tooCloseCharacter != null && 
                                    Math.Sign(tooCloseCharacter.WorldPosition.X - character.WorldPosition.X) != Math.Sign(c.WorldPosition.X - character.WorldPosition.X))
                                {
                                    tooCloseCharacter = null;
                                    break;
                                }
                                tooCloseCharacter = c; 
                            }
                            HumanAIController.FaceTarget(c);                            
                        }
                    }

                    if (tooCloseCharacter != null && !tooCloseCharacter.Removed && Vector2.DistanceSquared(tooCloseCharacter.WorldPosition, character.WorldPosition) < 50.0f * 50.0f)
                    {
                        Vector2 diff = character.WorldPosition - tooCloseCharacter.WorldPosition;
                        if (diff.LengthSquared() < 0.0001f) { diff = Rand.Vector(1.0f); }
                        if (diff.X > 0 && character.WorldPosition.X > character.CurrentHull.WorldRect.Right - 50) { diff.X = -diff.X; }
                        if (diff.X < 0 && character.WorldPosition.X < character.CurrentHull.WorldRect.X + 50) { diff.X = -diff.X; }
                        PathSteering.SteeringManual(deltaTime, Vector2.Normalize(diff));
                        return;
                    }
                    else
                    {
                        PathSteering.Reset();
                        tooCloseCharacter = null;
                    }

                    chairCheckTimer -= deltaTime;
                    if (chairCheckTimer <= 0.0f && character.SelectedConstruction == null)
                    {
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.CurrentHull != character.CurrentHull || !item.HasTag("chair")) { continue; }
                            var controller = item.GetComponent<Controller>();
                            if (controller == null || controller.User != null) { continue; }
                            item.TryInteract(character, forceSelectKey: true);
                        }
                        chairCheckTimer = chairCheckInterval;
                    }

                    return;
                }
                if (standStillTimer < -walkDuration)
                {
                    standStillTimer = Rand.Range(standStillMin, standStillMax);
                }
            }

            PathSteering.Wander(deltaTime);
        }

        private void FindTargetHulls()
        {
            targetHulls.Clear();
            hullWeights.Clear();
            foreach (var hull in Hull.hullList)
            {
                if (HumanAIController.UnsafeHulls.Contains(hull)) { continue; }
                if (hull.Submarine == null) { continue; }
                if (character.Submarine == null) { break; }
                if (character.TeamID == Character.TeamType.FriendlyNPC)
                {
                    if (hull.Submarine.TeamID != character.TeamID)
                    {
                        // Don't allow npcs to idle in a sub that's not in their team (like the player sub)
                        continue;
                    }
                }
                else
                {
                    if (hull.Submarine.TeamID != character.Submarine.TeamID)
                    {
                        // Don't allow to idle in the subs that are not in the same team as the current sub
                        // -> the crew ai bots can't change the sub from outpost to main sub or vice versa on their own
                        continue;
                    }
                }
                if (IsForbidden(hull)) { continue; }
                // Check that the hull is linked
                if (!character.Submarine.GetConnectedSubs().Contains(hull.Submarine)) { continue; }
                // Ignore hulls that are too low to stand inside.
                if (character.AnimController is HumanoidAnimController animController)
                {
                    if (hull.CeilingHeight < ConvertUnits.ToDisplayUnits(animController.HeadPosition.Value))
                    {
                        continue;
                    }
                }
                if (!targetHulls.Contains(hull))
                {
                    targetHulls.Add(hull);
                    float weight = hull.Volume;
                    // Prefer rooms that are closer. Avoid rooms that are not in the same level.
                    float yDist = Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 5 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + yDist;
                    float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 2500, dist));
                    weight *= distanceFactor;
                    hullWeights.Add(weight);
                }
            }

            if (PreferredOutpostModuleTypes.Any() && character.CurrentHull != null)
            {
                for (int i = 0; i < targetHulls.Count; i++)
                {
                    if (targetHulls[i].OutpostModuleTags.Any(t => PreferredOutpostModuleTypes.Contains(t)))
                    {
                        hullWeights[i] *= Rand.Range(10.0f, 100.0f);
                    }
                }
            }
        }

        public static bool IsForbidden(Hull hull)
        {
            if (hull == null) { return true; }
            string hullName = hull.RoomName;
            if (hullName == null) { return false; }
            return hullName.Contains("ballast", StringComparison.OrdinalIgnoreCase) || hullName.Contains("airlock", StringComparison.OrdinalIgnoreCase);
        }
    }
}
