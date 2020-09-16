﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";

        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;
        public override bool AllowOutsideSubmarine => true;

        private readonly CombatMode initialMode;

        private float seekWeaponsTimer;
        private readonly float seekWeaponsInterval = 1;
        private float ignoreWeaponTimer;
        private readonly float ignoredWeaponsClearTime = 10;

        // Won't (by default) start the offensive with weapons that have lower priority than this
        private readonly float goodWeaponPriority = 30;

        private readonly float arrestHoldFireTime = 8;
        private float holdFireTimer;
        private bool hasAimed;
        private bool isLethalWeapon;

        public Character Enemy { get; private set; }
        public bool HoldPosition { get; set; }

        private Item _weapon;
        private Item Weapon
        {
            get { return _weapon; }
            set
            {
                _weapon = value;
                _weaponComponent = null;
                hasAimed = false;
                RemoveSubObjective(ref seekAmmunition);
            }
        }
        private ItemComponent _weaponComponent;
        private ItemComponent WeaponComponent
        {
            get
            {
                if (Weapon == null) { return null; }
                if (_weaponComponent == null)
                {
                    _weaponComponent =
                        Weapon.GetComponent<RangedWeapon>() as ItemComponent ??
                        Weapon.GetComponent<MeleeWeapon>() as ItemComponent ??
                        Weapon.GetComponent<RepairTool>() as ItemComponent;
                }
                return _weaponComponent;
            }
        }

        public override bool ConcurrentObjectives => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<ItemComponent> weapons = new HashSet<ItemComponent>();
        private readonly HashSet<Item> ignoredWeapons = new HashSet<Item>();

        private AIObjectiveContainItem seekAmmunition;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;

        private Hull retreatTarget;
        private float coolDownTimer;
        private IEnumerable<FarseerPhysics.Dynamics.Body> myBodies;
        private float aimTimer;

        private bool canSeeTarget;
        private float visibilityCheckTimer;
        private readonly float visibilityCheckInterval = 0.2f;

        /// <summary>
        /// Aborts the objective when this condition is true
        /// </summary>
        public Func<bool> abortCondition;

        public bool allowHoldFire;

        /// <summary>
        /// Don't start using a weapon if this condition is true
        /// </summary>
        public Func<bool> holdFireCondition;

        public enum CombatMode
        {
            Defensive,
            Offensive,
            Arrest,
            Retreat
        }

        public CombatMode Mode { get; private set; }

        private bool IsOffensiveOrArrest => initialMode == CombatMode.Offensive || initialMode == CombatMode.Arrest;
        private bool TargetEliminated => Enemy == null || Enemy.Removed || Enemy.IsUnconscious;

        public AIObjectiveCombat(Character character, Character enemy, CombatMode mode, AIObjectiveManager objectiveManager, float priorityModifier = 1, float coolDown = 10.0f) 
            : base(character, objectiveManager, priorityModifier)
        {
            Enemy = enemy;
            coolDownTimer = coolDown;
            findSafety = objectiveManager.GetObjective<AIObjectiveFindSafety>();
            if (findSafety != null)
            {
                findSafety.Priority = 0;
                HumanAIController.UnreachableHulls.Clear();
            }
            Mode = mode;
            initialMode = Mode;
            if (Enemy == null)
            {
                Mode = CombatMode.Retreat;
            }
        }

        public override float GetPriority()
        {
            if (character.TeamID == Character.TeamType.FriendlyNPC && Enemy != null)
            {
                if (Enemy.Submarine == null || (Enemy.Submarine.TeamID != character.TeamID && Enemy.Submarine != character.Submarine))
                {
                    Priority = 0;
                    return Priority;
                }
            }
            float damageFactor = MathUtils.InverseLerp(0.0f, 5.0f, HumanAIController.GetDamageDoneByAttacker(Enemy) / 100.0f);
            Priority = TargetEliminated ? 0 : Math.Min((95 + damageFactor) * PriorityModifier, 100);
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            ignoreWeaponTimer -= deltaTime;
            seekWeaponsTimer -= deltaTime;
            if (ignoreWeaponTimer < 0)
            {
                ignoredWeapons.Clear();
                ignoreWeaponTimer = ignoredWeaponsClearTime;
            }
        }

        protected override bool Check()
        {
            if (IsOffensiveOrArrest && Mode != initialMode)
            {
                Abandon = true;
                SteeringManager.Reset();
                return false;
            }
            return IsEnemyDisabled || (!IsOffensiveOrArrest && coolDownTimer <= 0);
        }

        private bool IsEnemyDisabled => Enemy == null || Enemy.Removed || Enemy.IsDead;

        protected override void Act(float deltaTime)
        {
            if (abortCondition != null && abortCondition())
            {
                Abandon = true;
                SteeringManager.Reset();
                return;
            }
            if (!IsOffensiveOrArrest)
            {
                coolDownTimer -= deltaTime;
            }
            if (seekAmmunition == null)
            {
                if (Mode != CombatMode.Retreat && TryArm() && !IsEnemyDisabled)
                {
                    OperateWeapon(deltaTime);
                }
                if (!HoldPosition)
                {
                    Move(deltaTime);
                }
                switch (Mode)
                {
                    case CombatMode.Offensive:
                        if (TargetEliminated && objectiveManager.IsCurrentOrder<AIObjectiveFightIntruders>())
                        {
                            // TODO: enable
                            //character.Speak(TextManager.Get("DialogTargetDown"), null, 3.0f, "targetdown", 30.0f);
                        }
                        break;
                    case CombatMode.Arrest:
                        if (HumanAIController.HasItem(Enemy, "handlocker", out _, requireEquipped: true))
                        {
                            IsCompleted = true;
                        }
                        break;
                }
            }
        }

        private void Move(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Arrest:
                    Engage();
                    break;
                case CombatMode.Defensive:
                case CombatMode.Retreat:
                    Retreat(deltaTime);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private bool IsLoaded(ItemComponent weapon) => weapon.HasRequiredContainedItems(character, addMessage: false);

        private bool TryArm()
        {
            if (character.LockHands || Enemy == null)
            {
                Weapon = null;
                return false;
            }
            if (seekWeaponsTimer < 0)
            {
                seekWeaponsTimer = seekWeaponsInterval;
                // First go through all weapons and try to reload without seeking ammunition
                var allWeapons = GetAllWeapons();
                while (allWeapons.Any())
                {
                    Weapon = GetWeapon(allWeapons, out _weaponComponent);
                    if (Weapon == null)
                    {
                        // No weapons
                        break;
                    }
                    if (!character.Inventory.Items.Contains(Weapon) || WeaponComponent == null)
                    {
                        // Not in the inventory anymore or cannot find the weapon component
                        allWeapons.Remove(WeaponComponent);
                        Weapon = null;
                        continue;
                    }
                    if (IsLoaded(WeaponComponent))
                    {
                        // All good, the weapon is loaded
                        break;
                    }
                    if (Reload(seekAmmo: false))
                    {
                        // All good, reloading successful
                        break;
                    }
                    else
                    {
                        // No ammo.
                        allWeapons.Remove(WeaponComponent);
                        Weapon = null;
                    }
                }
                if (Weapon == null)
                {
                    // No weapon found with the conditions above. Try again, now let's try to seek ammunition too
                    Weapon = GetWeapon(out _weaponComponent);
                    if (Weapon != null)
                    {
                        if (!CheckWeapon(seekAmmo: true))
                        {
                            if (seekAmmunition != null)
                            {
                                // No loaded weapon, but we are trying to seek ammunition.
                                return false;
                            }
                            else
                            {
                                Weapon = null;
                            }
                        }
                    }
                }
                if (Weapon == null)
                {
                    Mode = CombatMode.Retreat;
                }
            }
            else
            {
                if (!CheckWeapon(seekAmmo: false))
                {
                    Weapon = null;
                }
            }
            return Weapon != null;

            bool CheckWeapon(bool seekAmmo)
            {
                if (!character.Inventory.Items.Contains(Weapon) || WeaponComponent == null)
                {
                    // Not in the inventory anymore or cannot find the weapon component
                    return false;
                }
                if (!IsLoaded(WeaponComponent))
                {
                    // Try reloading (and seek ammo)
                    if (!Reload(seekAmmo))
                    {
                        return false;
                    }
                }
                return true;
            };
        }

        private void OperateWeapon(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Defensive:
                case CombatMode.Arrest:
                    if (Equip())
                    {
                        Attack(deltaTime);
                    }
                    break;
                case CombatMode.Retreat:
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private Item GetWeapon(out ItemComponent weaponComponent) => GetWeapon(GetAllWeapons(), out weaponComponent);

        private Item GetWeapon(IEnumerable<ItemComponent> weaponList, out ItemComponent weaponComponent)
        {
            weaponComponent = null;
            float bestPriority = 0;
            float lethalDmg = -1;
            foreach (var weapon in weaponList)
            {
                // By default, the bots won't go offensive with bad weapons, unless they are close to the enemy or ordered to fight enemies.
                // NPC characters ignore this check.
                if ((initialMode == CombatMode.Offensive || initialMode == CombatMode.Arrest) && character.TeamID != Character.TeamType.FriendlyNPC)
                {
                    if (!objectiveManager.IsCurrentOrder<AIObjectiveFightIntruders>() && !EnemyIsClose())
                    {
                        if (weapon.CombatPriority < goodWeaponPriority)
                        {
                            continue;
                        }
                    }
                }

                float priority = weapon.CombatPriority;
                if (!IsLoaded(weapon))
                {
                    if (weapon is RangedWeapon && EnemyIsClose())
                    {
                        // Close to the enemy. Ignore weapons that don't have any ammunition (-> Don't seek ammo).
                        continue;
                    }
                    else
                    {
                        // Halve the priority for weapons that don't have proper ammunition loaded.
                        priority /= 2;
                    }
                }
                if (Enemy.Stun > 1)
                {
                    // Enemy is stunned, reduce the priority of stunner weapons.
                    Attack attack = GetAttackDefinition(weapon);
                    if (attack != null)
                    {
                        lethalDmg = attack.GetTotalDamage();
                        float max = lethalDmg + 1;
                        if (weapon.Item.HasTag("stunner"))
                        {
                            priority = max;
                        }
                        else
                        {
                            float stunDmg = ApproximateStunDamage(weapon, attack);
                            float diff = stunDmg - lethalDmg;
                            priority = Math.Clamp(priority - Math.Max(diff * 2, 0), min: 1, max);
                        }
                    }
                }
                else if (Mode == CombatMode.Arrest)
                {
                    // Enemy is not stunned, increase the priority of stunner weapons and decrease the priority of lethal weapons.
                    if (weapon.Item.HasTag("stunner"))
                    {
                        priority *= 2;
                    }
                    else
                    {
                        Attack attack = GetAttackDefinition(weapon);
                        if (attack != null)
                        {
                            lethalDmg = attack.GetTotalDamage();
                            float stunDmg = ApproximateStunDamage(weapon, attack);
                            float diff = stunDmg - lethalDmg;
                            if (diff < 0)
                            {
                                priority /= 2;
                            }
                        }
                    }
                }
                if (priority > bestPriority)
                {
                    weaponComponent = weapon;
                    bestPriority = priority;
                }
            }
            if (weaponComponent == null) { return null; }
            if (bestPriority < 1) { return null; }
            if (Mode == CombatMode.Arrest)
            {
                if (weaponComponent.Item.HasTag("stunner"))
                {
                    isLethalWeapon = false;
                }
                else
                {
                    if (lethalDmg < 0)
                    {
                        lethalDmg = GetLethalDamage(weaponComponent);
                    }
                    isLethalWeapon = lethalDmg > 1;
                }
                if (allowHoldFire && !hasAimed && holdFireTimer <= 0)
                {
                    holdFireTimer = arrestHoldFireTime * Rand.Range(0.75f, 1.25f);
                }
            }
            return weaponComponent.Item;

            bool EnemyIsClose() => character.CurrentHull == Enemy.CurrentHull || Vector2.DistanceSquared(character.Position, Enemy.Position) < 500;

            Attack GetAttackDefinition(ItemComponent weapon)
            {
                Attack attack = null;
                if (weapon is MeleeWeapon meleeWeapon)
                {
                    attack = meleeWeapon.Attack;
                }
                else if (weapon is RangedWeapon rangedWeapon)
                {
                    attack = rangedWeapon.FindProjectile(triggerOnUseOnContainers: false)?.Attack;
                }
                return attack;
            }

            float GetLethalDamage(ItemComponent weapon)
            {
                float lethalDmg = 0;
                Attack attack = GetAttackDefinition(weapon);
                if (attack != null)
                {
                    lethalDmg = attack.GetTotalDamage();
                }
                return lethalDmg;
            }

            float ApproximateStunDamage(ItemComponent weapon, Attack attack)
            {
                // Try to reduce the priority using the actual damage values and status effects.
                // This is an approximation, because we can't check the status effect conditions here.
                // The result might be incorrect if there is a high stun effect that's only applied in certain conditions.
                var statusEffects = attack.StatusEffects.Where(se => !se.HasConditions && se.type == ActionType.OnUse && se.HasRequiredItems(character));
                if (weapon.statusEffectLists != null && weapon.statusEffectLists.TryGetValue(ActionType.OnUse, out List<StatusEffect> hitEffects))
                {
                    statusEffects = statusEffects.Concat(hitEffects);
                }
                float afflictionsStun = attack.Afflictions.Keys.Sum(a => a.Identifier == "stun" ? a.Strength : 0);
                float effectsStun = statusEffects.None() ? 0 : statusEffects.Max(se =>
                {
                    float stunAmount = 0;
                    var stunAffliction = se.Afflictions.Find(a => a.Identifier == "stun");
                    if (stunAffliction != null)
                    {
                        stunAmount = stunAffliction.Strength;
                    }
                    return stunAmount;
                });
                return attack.Stun + afflictionsStun + effectsStun;
            }
        }

        private HashSet<ItemComponent> GetAllWeapons()
        {
            weapons.Clear();
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                if (ignoredWeapons.Contains(item)) { continue; }
                SeekWeapons(item, weapons);
                if (item.OwnInventory != null)
                {
                    item.OwnInventory.Items.ForEach(i => SeekWeapons(i, weapons));
                }
            }
            return weapons;
        }

        private void SeekWeapons(Item item, ICollection<ItemComponent> weaponList)
        {
            if (item == null) { return; }
            foreach (var component in item.Components)
            {
                if (component.CombatPriority > 0)
                {
                    weaponList.Add(component);
                }
            }
        }

        private void Unequip()
        {
            if (!character.LockHands && character.SelectedItems.Contains(Weapon))
            {
                if (!Weapon.AllowedSlots.Contains(InvSlotType.Any) || !character.Inventory.TryPutItem(Weapon, character, new List<InvSlotType>() { InvSlotType.Any }))
                {
                    Weapon.Drop(character);
                }
            }
        }

        private bool Equip()
        {
            if (character.LockHands) { return false; }
            if (!WeaponComponent.HasRequiredContainedItems(character, addMessage: false))
            {
                return false;
            }
            if (!character.HasEquippedItem(Weapon))
            {
                Weapon.TryInteract(character, forceSelectKey: true);
                var slots = Weapon.AllowedSlots.FindAll(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    aimTimer = Rand.Range(0.5f, 1f);
                }
                else
                {
                    Weapon = null;
                    Mode = CombatMode.Retreat;
                    return false;
                }
            }
            return true;
        }

        private float findHullTimer;
        private readonly float findHullInterval = 1.0f;

        private void Retreat(float deltaTime)
        {
            RemoveFollowTarget();
            RemoveSubObjective(ref seekAmmunition);
            if (retreatObjective != null && retreatObjective.Target != retreatTarget)
            {
                RemoveSubObjective(ref retreatObjective);
            }
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                if (findHullTimer > 0)
                {
                    findHullTimer -= deltaTime;
                }
                else
                {
                    retreatTarget = findSafety.FindBestHull(HumanAIController.VisibleHulls, allowChangingTheSubmarine: character.TeamID != Character.TeamType.FriendlyNPC);
                    findHullTimer = findHullInterval * Rand.Range(0.9f, 1.1f);
                }
            }
            if (retreatTarget != null && character.CurrentHull != retreatTarget)
            {
                TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true),
                    onAbandon: () =>
                    {
                        if (Enemy != null && HumanAIController.VisibleHulls.Contains(Enemy.CurrentHull))
                        {
                            // If in the same room with an enemy -> don't try to escape because we'd want to fight it
                            SteeringManager.Reset();
                            RemoveSubObjective(ref retreatObjective);
                        }
                        else
                        {
                            // else abandon and fall back to find safety mode
                            Abandon = true;
                        }
                    }, 
                    onCompleted: () => RemoveSubObjective(ref retreatObjective));
            }
        }

        private void Engage()
        {
            if (character.LockHands || Enemy == null)
            {
                Mode = CombatMode.Retreat;
                SteeringManager.Reset();
                return;
            }
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveSubObjective(ref seekAmmunition);
            if (followTargetObjective != null && followTargetObjective.Target != Enemy)
            {
                RemoveFollowTarget();
            }
            TryAddSubObjective(ref followTargetObjective,
                constructor: () => new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true, closeEnough: 50)
                {
                    IgnoreIfTargetDead = true,
                    DialogueIdentifier = "dialogcannotreachtarget",
                    TargetName = Enemy.DisplayName
                },
                onAbandon: () =>
                {
                    Abandon = true;
                    SteeringManager.Reset();
                });
            if (followTargetObjective == null) { return; }
            if (Mode == CombatMode.Arrest && Enemy.Stun > 2)
            {
                if (HumanAIController.HasItem(character, "handlocker", out Item handCuffs))
                {
                    if (!arrestingRegistered)
                    {
                        arrestingRegistered = true;
                        followTargetObjective.Completed += OnArrestTargetReached;
                    }
                    followTargetObjective.CloseEnough = 100;
                }
                else
                {
                    RemoveFollowTarget();
                    SteeringManager.Reset();
                }
            }
            else if (WeaponComponent == null)
            {
                RemoveFollowTarget();
                SteeringManager.Reset();
            }
            else
            {
                followTargetObjective.CloseEnough =
                    WeaponComponent is RangedWeapon ? 1000 :
                    WeaponComponent is MeleeWeapon mw ? mw.Range :
                    WeaponComponent is RepairTool rt ? rt.Range : 50;
            }
        }

        private bool arrestingRegistered;

        private void RemoveFollowTarget()
        {
            if (arrestingRegistered)
            {
                followTargetObjective.Completed -= OnArrestTargetReached;
            }
            RemoveSubObjective(ref followTargetObjective);
            arrestingRegistered = false;
        }

        private void OnArrestTargetReached()
        {
            if (HumanAIController.HasItem(character, "handlocker", out Item handCuffs) && Enemy.Stun > 0 && character.CanInteractWith(Enemy))
            {
                if (HumanAIController.TryToMoveItem(handCuffs, Enemy.Inventory))
                {
                    handCuffs.Equip(Enemy);
                }
                else
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Failed to handcuff the target.", Color.Red);
#endif
                }
                // Confiscate stolen goods.
                foreach (var item in Enemy.Inventory.Items)
                {
                    if (item == null || item == handCuffs) { continue; }
                    if (item.StolenDuringRound)
                    {
                        item.Drop(character);
                        character.Inventory.TryPutItem(item, character, new List<InvSlotType>() { InvSlotType.Any });
                    }
                }
                // TODO: enable
                //character.Speak(TextManager.Get("DialogTargetArrested"), null, 3.0f, "targetarrested", 30.0f);
                IsCompleted = true;
            }
        }

        /// <summary>
        /// Seeks for more ammunition. Creates a new subobjective.
        /// </summary>
        private void SeekAmmunition(string[] ammunitionIdentifiers)
        {
            retreatTarget = null;
            RemoveSubObjective(ref retreatObjective);
            RemoveFollowTarget();
            TryAddSubObjective(ref seekAmmunition,
                constructor: () => new AIObjectiveContainItem(character, ammunitionIdentifiers, Weapon.GetComponent<ItemContainer>(), objectiveManager)
                {
                    targetItemCount = Weapon.GetComponent<ItemContainer>().Capacity,
                    checkInventory = false
                },
                onCompleted: () => RemoveSubObjective(ref seekAmmunition),
                onAbandon: () =>
                {
                    SteeringManager.Reset();
                    RemoveSubObjective(ref seekAmmunition);
                    ignoredWeapons.Add(Weapon);
                    Weapon = null;
                });
        }
        
        /// <summary>
        /// Reloads the ammunition found in the inventory.
        /// If seekAmmo is true, tries to get find the ammo elsewhere.
        /// </summary>
        private bool Reload(bool seekAmmo)
        {
            if (WeaponComponent == null) { return false; }
            if (!WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained)) { return false; }
            var containedItems = Weapon.ContainedItems;
            // Drop empty ammo
            foreach (Item containedItem in containedItems)
            {
                if (containedItem == null) { continue; }
                if (containedItem.Condition <= 0)
                {
                    containedItem.Drop(character);
                }
            }
            RelatedItem item = null;
            Item ammunition = null;
            string[] ammunitionIdentifiers = null;
            foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
            {
                ammunition = containedItems.FirstOrDefault(it => it.Condition > 0 && requiredItem.MatchesItem(it));
                if (ammunition != null)
                {
                    // Ammunition still remaining
                    return true;
                }
                item = requiredItem;
                ammunitionIdentifiers = requiredItem.Identifiers;
            }
            // No ammo
            if (ammunition == null)
            {
                if (ammunitionIdentifiers != null)
                {
                    // Try reload ammunition from inventory
                    ammunition = character.Inventory.FindItem(i => ammunitionIdentifiers.Any(id => id == i.Prefab.Identifier || i.HasTag(id)) && i.Condition > 0, true);
                    if (ammunition != null)
                    {
                        var container = Weapon.GetComponent<ItemContainer>();
                        if (container.Item.ParentInventory == character.Inventory)
                        {
                            if (!container.Inventory.CanBePut(ammunition))
                            {
                                return false;
                            }
                            character.Inventory.RemoveItem(ammunition);
                            if (!container.Inventory.TryPutItem(ammunition, null))
                            {
                                ammunition.Drop(character);
                            }
                        }
                        else
                        {
                            container.Combine(ammunition, character);
                        }
                    }
                }
            }
            if (WeaponComponent.HasRequiredContainedItems(character, addMessage: false))
            {
                return true;
            }
            else if (ammunition == null && !HoldPosition && IsOffensiveOrArrest && seekAmmo && ammunitionIdentifiers != null)
            {
                SeekAmmunition(ammunitionIdentifiers);
            }
            return false;
        }

        private void Attack(float deltaTime)
        {
            character.CursorPosition = Enemy.Position;
            visibilityCheckTimer -= deltaTime;
            if (visibilityCheckTimer <= 0.0f)
            {
                canSeeTarget = character.CanSeeTarget(Enemy);
                visibilityCheckTimer = visibilityCheckInterval;
            }
            if (!canSeeTarget) { return; }
            if (Weapon.RequireAimToUse)
            {
                character.SetInput(InputType.Aim, false, true);
            }
            hasAimed = true;
            if (holdFireTimer > 0)
            {
                holdFireTimer -= deltaTime;
                return;
            }
            if (aimTimer > 0)
            {
                aimTimer -= deltaTime;
                return;
            }
            if (Mode == CombatMode.Arrest && isLethalWeapon && Enemy.Stun > 0) { return; }
            if (holdFireCondition != null && holdFireCondition()) { return; }
            float sqrDist = Vector2.DistanceSquared(character.Position, Enemy.Position);
            if (!character.IsFacing(Enemy.WorldPosition))
            {
                aimTimer = Rand.Range(1f, 1.5f);
                return;
            }
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                float sqrRange = meleeWeapon.Range * meleeWeapon.Range;
                if (character.AnimController.InWater)
                {
                    if (sqrDist > sqrRange) { return; }
                }
                else
                {
                    // It's possible that the center point of the creature is out of reach, but we could still hit the character.
                    float xDiff = Math.Abs(Enemy.WorldPosition.X - character.WorldPosition.X);
                    if (xDiff > meleeWeapon.Range) { return; }
                    float yDiff = Math.Abs(Enemy.WorldPosition.Y - character.WorldPosition.Y);
                    if (yDiff > Math.Max(meleeWeapon.Range, 100)) { return; }
                    if (Enemy.WorldPosition.Y < character.WorldPosition.Y && yDiff > 25)
                    {
                        // The target is probably knocked down? -> try to reach it by crouching.
                        HumanAIController.AnimController.Crouching = true;
                    }
                }
                character.SetInput(InputType.Shoot, false, true);
                Weapon.Use(deltaTime, character);
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    if (sqrDist > repairTool.Range * repairTool.Range) { return; }
                }
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - Weapon.Position) < MathHelper.PiOver4)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var collisionCategories = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;
                    var pickedBody = Submarine.PickBody(Weapon.SimPosition, Enemy.SimPosition, myBodies, collisionCategories);
                    if (pickedBody != null)
                    {
                        Character target = null;
                        if (pickedBody.UserData is Character c)
                        {
                            target = c;
                        }
                        else if (pickedBody.UserData is Limb limb)
                        {
                            target = limb.character;
                        }
                        if (target != null && (target == Enemy || !HumanAIController.IsFriendly(target)))
                        {
                            character.SetInput(InputType.Shoot, false, true);
                            Weapon.Use(deltaTime, character);
                            float reloadTime = 0;
                            if (WeaponComponent is RangedWeapon rangedWeapon)
                            {
                                reloadTime = rangedWeapon.Reload;
                            }
                            if (WeaponComponent is MeleeWeapon mw)
                            {
                                reloadTime = mw.Reload;
                            }
                            aimTimer = reloadTime * Rand.Range(1f, 1.5f);
                        }
                    }
                }
            }
        }

        protected override void OnCompleted()
        {
            base.OnCompleted();
            if (Weapon != null)
            {
                Unequip();
            }
        }

        //private float CalculateEnemyStrength()
        //{
        //    float enemyStrength = 0;
        //    AttackContext currentContext = character.GetAttackContext();
        //    foreach (Limb limb in Enemy.AnimController.Limbs)
        //    {
        //        if (limb.attack == null) continue;
        //        if (!limb.attack.IsValidContext(currentContext)) { continue; }
        //        if (!limb.attack.IsValidTarget(AttackTarget.Character)) { continue; }
        //        enemyStrength += limb.attack.GetTotalDamage(false);
        //    }
        //    return enemyStrength;
        //}
    }
}
