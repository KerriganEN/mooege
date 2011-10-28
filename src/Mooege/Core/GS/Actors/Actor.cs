﻿/*
 * Copyright (C) 2011 mooege project
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */

using System.Collections.Generic;
using Mooege.Core.GS.Common.Types.Math;
using Mooege.Core.GS.Common.Types.SNO;
using Mooege.Core.GS.Objects;
using Mooege.Core.GS.Map;
using Mooege.Net.GS.Message;
using Mooege.Net.GS.Message.Definitions.World;
using Mooege.Net.GS.Message.Fields;
using Mooege.Net.GS.Message.Definitions.ACD;
using Mooege.Net.GS.Message.Definitions.Misc;
using Mooege.Core.Common.Items;
using Mooege.Core.GS.Actors.Buffs;
using Mooege.Net.GS.Message.Definitions.Actor;
using System;
using Mooege.Net.GS.Message.Definitions.Effect;
using Mooege.Core.GS.Powers;

// TODO: Need to move all of the remaining ACD fields into Actor (such as the affix list)

namespace Mooege.Core.GS.Actors
{
    // This is used for GBHandle.Type; uncertain if used elsewhere
    public enum GBHandleType : int
    {
        Invalid = 0,
        Monster = 1,
        Gizmo = 2,
        ClientEffect = 3,
        ServerProp = 4,
        Environment = 5,
        Critter = 6,
        Player = 7,
        Item = 8,
        AxeSymbol = 9,
        Projectile = 10,
        CustomBrain = 11
    }

    // This should probably be the same as GBHandleType (probably merge them once all actor classes are created)
    public enum ActorType
    {
        Player,
        NPC,
        Monster,
        Item,
        Portal,
        Effect,
        Gizmo
    }

    // Base actor
    public /*abstract*/ class Actor : WorldObject
    {
        // Actors can change worlds and have a specific addition/removal scheme
        // We'll just override the setter to handle all of this automagically
        public override World World
        {
            get { return this._world; }
            set
            {
                if (this._world == value) return;

                if (this._world != null) // if actor is already in a existing-world
                    this._world.Leave(this); // make him leave it first.

                this._world = value;
                if (this._world != null) // if actor got into a new world.
                    this._world.Enter(this); // let him enter first.
            }
        }

        protected Scene _currentScene;
        public virtual Scene CurrentScene
        {
            get { return this._currentScene; }
            protected set { this._currentScene = value; }
        }

        public override Vector3D Position
        {
            set
            {
                var old = new Vector3D(this._position);
                this._position.Set(value);
                this.OnPositionChange(old);
            }
        }

        public virtual /*abstract*/ ActorType ActorType { get { return Actors.ActorType.Item; } }

        public GameAttributeMap Attributes { get; private set; }
        public List<Affix> AffixList { get; set; }
        public int Tag;

        protected int _actorSNO;
        public int ActorSNO
        {
            get { return _actorSNO; }
            set
            {
                this._actorSNO = value;
                this.SNOName.SNOId = this.ActorSNO;
            }
        }

        public int CollFlags { get; set; }
        public GBHandle GBHandle { get; private set; }
        public SNOName SNOName { get; private set; }

        // Some ACD uncertainties
        public int Field2 = 0x00000000; // TODO: Probably flags or actor type. 0x8==monster, 0x1a==item, 0x10=npc, 0x01=other player, 0x09=player-itself
        public int Field3 = 0x00000001; // TODO: What dis? <-- I guess its just 0 for WorldItem and 1 for InventoryItem // Farmy
        public int Field7 = -1;
        public int Field8 = -1; // Animation set SNO?
        public int Field9; // SNOName.Group?
        public byte Field10 = 0x00;
        public int? /* sno */ Field11 = null;
        public int? Field12 = null;
        public int? Field13 = null;

        public virtual WorldLocationMessageData WorldLocationMessage
        {
            get
            {
                return new WorldLocationMessageData { Scale = this.Scale, Transform = this.Transform, WorldID = this.World.DynamicID };
            }
        }

        public virtual bool HasWorldLocation
        {
            get { return true; }
        }

        public virtual PRTransform Transform
        {
            get { return new PRTransform { Quaternion = new Quaternion { W = this.RotationAmount, Vector3D = this.RotationAxis }, Vector3D = this.Position }; }
        }

        // Only used in Item; stubbed here to prevent an overrun in some cases. /komiga
        public virtual InventoryLocationMessageData InventoryLocationMessage
        {
            get { return new InventoryLocationMessageData{ OwnerID = 0, EquipmentSlot = 0, InventoryLocation = new Vector2D() }; }
        }

        public virtual ACDWorldPositionMessage ACDWorldPositionMessage
        {
            get { return new ACDWorldPositionMessage { ActorID = this.DynamicID, WorldLocation = this.WorldLocationMessage }; }
        }

        public virtual ACDInventoryPositionMessage ACDInventoryPositionMessage
        {
            get
            {
                return new ACDInventoryPositionMessage()
                {
                    ItemId = this.DynamicID,
                    InventoryLocation = this.InventoryLocationMessage,
                    Field2 = 1 // TODO: find out what this is and why it must be 1...is it an enum?
                };
            }
        }

        public Actor(World world, uint dynamicID)
            : base(world, dynamicID)
        {
            this.Attributes = new GameAttributeMap();
            this.AffixList = new List<Affix>();
            this.GBHandle = new GBHandle() { Type = -1, GBID = -1 }; // Seems to be the default. /komiga
            this.SNOName = new SNOName() { Group =  SNOGroup.Actor, SNOId = this.ActorSNO };
            this.ActorSNO = -1;
            this.CollFlags = 0x00000000;
            this.Scale = 1.0f;
            this.RotationAmount = 0.0f;
            this.RotationAxis.Set(0.0f, 0.0f, 1.0f);
        }

        // NOTE: When using this, you should *not* set the actor's world. It is done for you
        public void TransferTo(World targetWorld, Vector3D pos)
        {
            var player = this as Player.Player;
            if (player == null) return; // return if current actor is not a player. 

            this.Position = pos;
            //this.RotationAmount = location.Quaternion.W;
            //this.RotationAxis = location.Quaternion.Vector3D;

            this.World = targetWorld; // Will Leave() from its current world and then Enter() to the target world
            
            player.InGameClient.SendMessage(new EnterWorldMessage()
            {
                EnterPosition = this.Position,
                WorldID = targetWorld.DynamicID,
                WorldSNO = targetWorld.WorldSNO,
            });

            player.InGameClient.SendMessage(new ACDWorldPositionMessage()
            {
                ActorID = this.DynamicID,
                WorldLocation = new WorldLocationMessageData()
                {
                    WorldID = targetWorld.DynamicID,
                    Scale = this.Scale,
                    Transform = new PRTransform()
                    {
                        Quaternion = new Quaternion() { W = 1, Vector3D = new Vector3D(0, 0, 0) },
                        Vector3D = pos
                    }
                }

            });
        }

        public virtual void OnEnter(World world)
        {
        }

        public virtual void OnLeave(World world)
        {
        }

        protected virtual void OnPositionChange(Vector3D prevPosition)
        {
            if (!this.HasWorldLocation) return;

            // We need this here for positioning actors on world (like when item drops)
            this.World.BroadcastIfRevealed(this.ACDWorldPositionMessage, this);   
        }

        public virtual void OnTargeted(Mooege.Core.GS.Player.Player player, TargetMessage message)
        {
        }

        #region Buffs and Debuffs

        List<TimedBuff> _activeBuffs = new List<TimedBuff>(); //< list of all active buffs on the actor
        
        public void AddBuff(TimedBuff buff)
        {
            buff.Target = this;
            // lookup by .net type and update it already exists
            Type buffType = buff.GetType();
            int activeIndex = _activeBuffs.FindIndex(b => b.GetType() == buffType);
            if (activeIndex != -1)
            {
                _activeBuffs[activeIndex] = buff;
            }
            else
            {
                _activeBuffs.Add(buff);
                buff.Apply();
            }
        }

        public void UpdateBuffs()
        {
            _activeBuffs.RemoveAll(buff => buff.Update()); // Update() returns true if finished
        }

        public void RemoveAllBuffs()
        {
            foreach (TimedBuff tb in _activeBuffs)
                tb.Remove();
        }

        #endregion

        #region Movement/Translation

        public void MoveWorldPosition(Vector3D destination)
        {
            this.Position.Set(destination);
            this.World.BroadcastIfRevealed(this.ACDWorldPositionMessage, this);
        }

        public void MoveNormal(Vector3D destination, float speed = 1.0f)
        {
            Position.Set(destination);

            World.BroadcastIfRevealed(new NotifyActorMovementMessage
            {
                Id = 0x6e,
                ActorId = (int)DynamicID,
                Position = destination,
                Angle = (float)Math.Acos(this.RotationAmount) * 2f,
                Field3 = false,
                Speed = speed,
            }, this);
        }

        public void MoveSnapped(Vector3D destination)
        {
            Position.Set(destination);

            World.BroadcastIfRevealed(new ACDTranslateSnappedMessage
            {
                Id = 0x6f,
                Field0 = (int)this.DynamicID,
                Field1 = destination,
                Field2 = (float)Math.Acos(this.RotationAmount) * 2f,
                Field3 = false,
                Field4 = 0x900 // ?
            }, this);
        }

        public void FacingTranslate(Vector3D target, bool immediately = false)
        {
            float radianAngle = PowerMath.AngleLookAt(this.Position, target);

            // convert to quaternion and store in instance
            this.RotationAmount = (float)Math.Cos(radianAngle / 2f);
            this.RotationAxis = new Vector3D(0, 0, (float)Math.Sin(radianAngle / 2f));

            World.BroadcastIfRevealed(new ACDTranslateFacingMessage
            {
                Id = 0x70,
                ActorId = DynamicID,
                Angle = radianAngle,
                Immediately = immediately
            }, this);
        }

        #endregion

        #region Effects

        public void PlayEffectGroup(int effectGroupSNO)
        {
            World.BroadcastIfRevealed(new PlayEffectMessage
            {
                Id = 0x7a,
                ActorId = DynamicID,
                Effect = Net.GS.Message.Definitions.Effect.Effect.PlayEffectGroup,
                OptionalParameter = effectGroupSNO
            }, this);
        }

        public void PlayEffectGroup(int effectGroupSNO, Actor target)
        {
            if (target == null) return;

            World.BroadcastIfRevealed(new EffectGroupACDToACDMessage
            {
                Id = 0xaa,
                Field0 = effectGroupSNO,
                Field1 = (int)DynamicID,
                Field2 = (int)target.DynamicID
            }, this);
        }

        public void PlayHitEffect(int hitEffect, Actor hitDealer)
        {
            World.BroadcastIfRevealed(new PlayHitEffectMessage
            {
                Id = 0x7b,
                ActorID = DynamicID,
                HitDealer = hitDealer.DynamicID,
                Field2 = hitEffect,
                Field3 = false
            }, this);
        }

        public void PlayEffect(Effect effect, int? param = null)
        {
            World.BroadcastIfRevealed(new PlayEffectMessage
            {
                ActorId = this.DynamicID,
                Effect = effect,
                OptionalParameter = param
            }, this);
        }

        public void AddRopeEffect(int ropeSNO, Actor target)
        {
            if (target == null) return;

            World.BroadcastIfRevealed(new RopeEffectMessageACDToACD
            {
                Id = 0xab,
                Field0 = ropeSNO,
                Field1 = (int)DynamicID,
                Field2 = 4,
                Field3 = (int)target.DynamicID,
                Field4 = 1
            }, this);
        }

        public void AttachActor(Actor actor)
        {
            GameAttributeMap map = new GameAttributeMap();
            map[GameAttribute.Attached_To_ACD] = unchecked((int)DynamicID);
            map[GameAttribute.Attachment_Handled_By_Client] = true;
            map[GameAttribute.Actor_Updates_Attributes_From_Owner] = true;
            foreach (var msg in map.GetMessageList(actor.DynamicID))
                actor.World.BroadcastIfRevealed(msg, actor);

            // TODO: track attached actors
        }

        public void AddComplexEffect(int effectGroupSNO, Actor target)
        {
            if (target == null) return;

            // TODO: Might need to track complex effects
            World.BroadcastIfRevealed(new ComplexEffectAddMessage
            {
                Id = 0x81,
                Field0 = (int)World.NewActorID, // TODO: maybe not use actor ids?
                Field1 = 1,
                Field2 = effectGroupSNO,
                Field3 = (int)this.DynamicID,
                Field4 = (int)target.DynamicID,
                Field5 = 0,
                Field6 = 0
            }, target);
        }
        #endregion

        public override void Update()
        {
            base.Update();
            UpdateBuffs();
        }
        
        /// <summary>
        /// Reveals an actor to a player.
        /// </summary>
        /// <returns>true if the actor was revealed or false if the actor was already revealed.</returns>
        public override bool Reveal(Mooege.Core.GS.Player.Player player)
        {
            if (player.RevealedObjects.ContainsKey(this.DynamicID)) 
                return false; // already revealed
            player.RevealedObjects.Add(this.DynamicID, this);

            var msg = new ACDEnterKnownMessage
            {
                ActorID = this.DynamicID,
                ActorSNO = this.ActorSNO,
                Field2 = Field2,
                Field3 = Field3, // this.hasWorldLocation ? 0 : 1;
                WorldLocation = this.HasWorldLocation ? this.WorldLocationMessage : null,
                InventoryLocation = this.HasWorldLocation ? null : this.InventoryLocationMessage,
                GBHandle = this.GBHandle,
                Field7 = Field7,
                Field8 = Field8,
                Field9 = Field9,
                Field10 = Field10,
                Field11 = Field11,
                Field12 = Field12,
                Field13 = Field13,
            };

            // normally when we send ACDEnterKnown for players own actor it's set to 0x09. But while sending the ACDEnterKnown for another player's actor we should set it to 0x01. /raist
            if ((this is Player.Player) && this != player) 
                msg.Field2 = 0x01; 

            player.InGameClient.SendMessage(msg);

            // Affixes of the actor, two messages with 1 and 2,i guess prefix and suffix so it does not
            // make sense to send the same list twice. server does not do this
            var affixGbis = new int[AffixList.Count];
            for (int i = 0; i < AffixList.Count; i++)
            {
                affixGbis[i] = AffixList[i].AffixGbid;
            }

            player.InGameClient.SendMessage(new AffixMessage()
            {
                ActorID = DynamicID,
                Field1 = 0x00000001,
                aAffixGBIDs = affixGbis,
            });

            player.InGameClient.SendMessage(new AffixMessage()
            {
                ActorID = DynamicID,
                Field1 = 0x00000002,
                aAffixGBIDs = affixGbis,
            });

            // Collision Flags
            player.InGameClient.SendMessage(new ACDCollFlagsMessage()
            {
                ActorID = DynamicID,
                CollFlags = this.CollFlags
            });

            // Send Attributes
            Attributes.SendMessage(player.InGameClient, DynamicID);

            // Actor group
            player.InGameClient.SendMessage(new ACDGroupMessage()
            {
                ActorID = DynamicID,
                Field1 = -1,
                Field2 = -1,
            });

            // Reveal actor (creates actor and makes it visible to the player)
            player.InGameClient.SendMessage(new ACDCreateActorMessage(this.DynamicID));

            // This is always sent even though it doesn't identify the actor. /komiga
            player.InGameClient.SendMessage(new SNONameDataMessage()
            {
                Name = this.SNOName
            });

            return true;
        }

        /// <summary>
        /// Unreveals an actor from a player.
        /// </summary>
        /// <returns>true if the actor was unrevealed or false if the actor wasn't already revealed.</returns>
        public override bool Unreveal(Mooege.Core.GS.Player.Player player)
        {
            if (!player.RevealedObjects.ContainsKey(this.DynamicID)) return false; // not revealed yet
            player.InGameClient.SendMessage(new ACDDestroyActorMessage(this.DynamicID));
            player.RevealedObjects.Remove(this.DynamicID);
            return true;
        }
    }
}
