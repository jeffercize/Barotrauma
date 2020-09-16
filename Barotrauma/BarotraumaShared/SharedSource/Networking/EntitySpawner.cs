﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        private enum SpawnableType { Item, Character };
        
        interface IEntitySpawnInfo
        {
            Entity Spawn();
            void OnSpawned(Entity entity);
        }

        class ItemSpawnInfo : IEntitySpawnInfo
        {
            public readonly ItemPrefab Prefab;

            public readonly Vector2 Position;
            public readonly Inventory Inventory;
            public readonly Submarine Submarine;
            public readonly float Condition;

            private readonly Action<Item> onSpawned;

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 worldPosition, Action<Item> onSpawned, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                Condition = condition ?? prefab.Health;
                this.onSpawned = onSpawned;
            }

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 position, Submarine sub, Action<Item> onSpawned, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = position;
                Submarine = sub;
                Condition = condition ?? prefab.Health;
                this.onSpawned = onSpawned;
            }
            
            public ItemSpawnInfo(ItemPrefab prefab, Inventory inventory, Action<Item> onSpawned, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Inventory = inventory;
                Condition = condition ?? prefab.Health;
                this.onSpawned = onSpawned;
            }

            public Entity Spawn()
            {                
                if (Prefab == null)
                {
                    return null;
                }
                Item spawnedItem;
                if (Inventory?.Owner != null)
                {
                    spawnedItem = new Item(Prefab, Vector2.Zero, null);
                    if (!Inventory.Owner.Removed && !Inventory.TryPutItem(spawnedItem, null, spawnedItem.AllowedSlots))
                    {
                        spawnedItem.SetTransform(FarseerPhysics.ConvertUnits.ToSimUnits(Inventory.Owner?.WorldPosition ?? Vector2.Zero), spawnedItem.body?.Rotation ?? 0.0f, findNewHull: false);
                    }
                }
                else
                {
                    spawnedItem = new Item(Prefab, Position, Submarine);
                }
                return spawnedItem;
            }

            public void OnSpawned(Entity spawnedItem)
            {
                if (!(spawnedItem is Item item)) { throw new ArgumentException($"The entity passed to ItemSpawnInfo.OnSpawned must be an Item (value was {spawnedItem?.ToString() ?? "null"})."); }
                onSpawned?.Invoke(item);
            }
        }

        class CharacterSpawnInfo : IEntitySpawnInfo
        {
            public readonly string identifier;

            public readonly Vector2 Position;
            public readonly Submarine Submarine;

            private readonly Action<Character> onSpawned;

            public CharacterSpawnInfo(string identifier, Vector2 worldPosition, Action<Character> onSpawn = null)
            {
                this.identifier = identifier ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                this.onSpawned = onSpawn;
            }

            public CharacterSpawnInfo(string identifier, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
            {
                this.identifier = identifier ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = position;
                Submarine = sub;
                this.onSpawned = onSpawn;
            }


            public Entity Spawn()
            {
                var character = string.IsNullOrEmpty(identifier) ? null :
                    Character.Create(identifier,
                    Submarine == null ? Position : Submarine.Position + Position,
                    ToolBox.RandomSeed(8), createNetworkEvent: false);
                return character;
            }

            public void OnSpawned(Entity spawnedCharacter)
            {
                if (!(spawnedCharacter is Character character)) { throw new ArgumentException($"The entity passed to CharacterSpawnInfo.OnSpawned must be a Character (value was {spawnedCharacter?.ToString() ?? "null"})."); }
                onSpawned?.Invoke(character);
            }
        }

        private readonly Queue<IEntitySpawnInfo> spawnQueue;
        private readonly Queue<Entity> removeQueue;

        public class SpawnOrRemove
        {
            public readonly Entity Entity;

            public readonly UInt16 OriginalID, OriginalInventoryID;

            public readonly byte OriginalItemContainerIndex;

            public readonly bool Remove = false;

            public SpawnOrRemove(Entity entity, bool remove)
            {
                Entity = entity;
                OriginalID = entity.ID;
                if (entity is Item item && item.ParentInventory?.Owner != null)
                {
                    OriginalInventoryID = item.ParentInventory.Owner.ID;
                    //find the index of the ItemContainer this item is inside to get the item to
                    //spawn in the correct inventory in multi-inventory items like fabricators
                    if (item.Container != null)
                    {
                        foreach (ItemComponent component in item.Container.Components)
                        {
                            if (component is ItemContainer container &&
                                container.Inventory == item.ParentInventory)
                            {
                                OriginalItemContainerIndex = (byte)item.Container.GetComponentIndex(component);
                                break;
                            }
                        }
                    }
                }
                Remove = remove;
            }
        }
        
        public EntitySpawner()
            : base(null)
        {
            spawnQueue = new Queue<IEntitySpawnInfo>();
            removeQueue = new Queue<Entity>();
        }

        public override string ToString()
        {
            return "EntitySpawner";
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 worldPosition, float? condition = null, Action<Item> onSpawned = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue1:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, worldPosition, onSpawned, condition));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 position, Submarine sub, float? condition = null, Action<Item> onSpawned = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue2:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, position, sub, onSpawned, condition));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Inventory inventory, float? condition = null, Action<Item> onSpawned = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue3:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, inventory, onSpawned, condition));
        }

        public void AddToSpawnQueue(string speciesName, Vector2 worldPosition, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(speciesName))
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue4:SpeciesNameNullOrEmpty", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, worldPosition, onSpawn));
        }

        public void AddToSpawnQueue(string speciesName, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(speciesName))
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue5:SpeciesNameNullOrEmpty", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, position, sub, onSpawn));
        }

        public void AddToRemoveQueue(Entity entity)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(entity) || entity.Removed || entity == null) { return; }
            if (entity is Character)
            {
                Character character = entity as Character;
#if SERVER
                if (GameMain.Server != null)
                {
                    Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (client != null) GameMain.Server.SetClientCharacter(client, null);
                }
#endif
            }            

            removeQueue.Enqueue(entity);
        }

        public void AddToRemoveQueue(Item item)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(item) || item.Removed) { return; }

            removeQueue.Enqueue(item);
            if (item.ContainedItems == null) return;
            foreach (Item containedItem in item.ContainedItems)
            {
                if (containedItem != null) AddToRemoveQueue(containedItem);
            }
        }

        public void Update()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            while (spawnQueue.Count > 0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();
                if (spawnedEntity != null)
                {
                    CreateNetworkEventProjSpecific(spawnedEntity, false);
                    if (spawnedEntity is Item)
                    {
                        ((Item)spawnedEntity).Condition = ((ItemSpawnInfo)entitySpawnInfo).Condition;
                    }
                    entitySpawnInfo.OnSpawned(spawnedEntity);
                }
            }

            while (removeQueue.Count > 0)
            {
                var removedEntity = removeQueue.Dequeue();
                if (removedEntity is Item item)
                {
                    item.SendPendingNetworkUpdates();
                }
                CreateNetworkEventProjSpecific(removedEntity, true);
                removedEntity.Remove();
            }
        }

        partial void CreateNetworkEventProjSpecific(Entity entity, bool remove);

        public void Reset()
        {
            removeQueue.Clear();
            spawnQueue.Clear();
        }
    }
}
