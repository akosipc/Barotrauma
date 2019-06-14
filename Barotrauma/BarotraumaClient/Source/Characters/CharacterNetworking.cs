﻿using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        partial void UpdateNetInput()
        {
            if (GameMain.Client != null)
            {
                if (this != Controlled)
                {
                    //freeze AI characters if more than 1 seconds have passed since last update from the server
                    if (lastRecvPositionUpdateTime < NetTime.Now - 1.0f)
                    {
                        AnimController.Frozen = true;
                        memState.Clear();
                        //hide after 2 seconds
                        if (lastRecvPositionUpdateTime < NetTime.Now - 2.0f)
                        {
                            Enabled = false;
                            return;
                        }
                    }
                }
                else
                {
                    var posInfo = new CharacterStateInfo(
                        SimPosition,
                        AnimController.Collider.Rotation,
                        LastNetworkUpdateID,
                        AnimController.TargetDir,
                        SelectedCharacter,
                        SelectedConstruction,
                        AnimController.Anim);

                    memLocalState.Add(posInfo);

                    InputNetFlags newInput = InputNetFlags.None;
                    if (IsKeyDown(InputType.Left)) newInput |= InputNetFlags.Left;
                    if (IsKeyDown(InputType.Right)) newInput |= InputNetFlags.Right;
                    if (IsKeyDown(InputType.Up)) newInput |= InputNetFlags.Up;
                    if (IsKeyDown(InputType.Down)) newInput |= InputNetFlags.Down;
                    if (IsKeyDown(InputType.Run)) newInput |= InputNetFlags.Run;
                    if (IsKeyDown(InputType.Crouch)) newInput |= InputNetFlags.Crouch;
                    if (IsKeyHit(InputType.Select)) newInput |= InputNetFlags.Select; //TODO: clean up the way this input is registered
                    if (IsKeyHit(InputType.Deselect)) newInput |= InputNetFlags.Deselect;
                    if (IsKeyHit(InputType.Health)) newInput |= InputNetFlags.Health;
                    if (IsKeyHit(InputType.Grab)) newInput |= InputNetFlags.Grab;
                    if (IsKeyDown(InputType.Use)) newInput |= InputNetFlags.Use;
                    if (IsKeyDown(InputType.Aim)) newInput |= InputNetFlags.Aim;
                    if (IsKeyDown(InputType.Shoot)) newInput |= InputNetFlags.Shoot;
                    if (IsKeyDown(InputType.Attack)) newInput |= InputNetFlags.Attack;
                    if (IsKeyDown(InputType.Ragdoll)) newInput |= InputNetFlags.Ragdoll;

                    if (AnimController.TargetDir == Direction.Left) newInput |= InputNetFlags.FacingLeft;

                    Vector2 relativeCursorPos = cursorPosition - AimRefPosition;
                    relativeCursorPos.Normalize();
                    UInt16 intAngle = (UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI));

                    NetInputMem newMem = new NetInputMem
                    {
                        states = newInput,
                        intAim = intAngle
                    };
                    if (focusedItem != null && !CharacterInventory.DraggingItemToWorld && 
                        (!newMem.states.HasFlag(InputNetFlags.Grab) && !newMem.states.HasFlag(InputNetFlags.Health)))
                    {
                        newMem.interact = focusedItem.ID;
                    }
                    else if (focusedCharacter != null)
                    {
                        newMem.interact = focusedCharacter.ID;
                    }

                    memInput.Insert(0, newMem);
                    LastNetworkUpdateID++;
                    if (memInput.Count > 60)
                    {
                        memInput.RemoveRange(60, memInput.Count - 60);
                    }
                }
            }
            else //this == Character.Controlled && GameMain.Client == null
            {
                AnimController.Frozen = false;
            }
        }

        public virtual void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.WriteRangedInteger(0, 3, 0);
                        Inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Treatment:
                        msg.WriteRangedInteger(0, 3, 1);
                        msg.Write(AnimController.Anim == AnimController.Animation.CPR);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(0, 3, 2);
                        break;
                }
            }
            else
            {
                msg.Write((byte)ClientNetObject.CHARACTER_INPUT);

                if (memInput.Count > 60)
                {
                    memInput.RemoveRange(60, memInput.Count - 60);
                }

                msg.Write(LastNetworkUpdateID);
                byte inputCount = Math.Min((byte)memInput.Count, (byte)60);
                msg.Write(inputCount);
                for (int i = 0; i < inputCount; i++)
                {
                    msg.WriteRangedInteger(0, (int)InputNetFlags.MaxVal, (int)memInput[i].states);
                    msg.Write(memInput[i].intAim);
                    if (memInput[i].states.HasFlag(InputNetFlags.Select) || 
                        memInput[i].states.HasFlag(InputNetFlags.Deselect) ||
                        memInput[i].states.HasFlag(InputNetFlags.Use) || 
                        memInput[i].states.HasFlag(InputNetFlags.Health) ||
                        memInput[i].states.HasFlag(InputNetFlags.Grab))
                    {
                        msg.Write(memInput[i].interact);
                    }
                }
            }
            msg.WritePadBits();
        }

        public virtual void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            switch (type)
            {
                case ServerNetObject.ENTITY_POSITION:
                    bool facingRight = AnimController.Dir > 0.0f;

                    lastRecvPositionUpdateTime = (float)NetTime.Now;

                    AnimController.Frozen = false;
                    Enabled = true;

                    UInt16 networkUpdateID = 0;
                    if (msg.ReadBoolean())
                    {
                        networkUpdateID = msg.ReadUInt16();
                    }
                    else
                    {
                        bool aimInput = msg.ReadBoolean();
                        keys[(int)InputType.Aim].Held = aimInput;
                        keys[(int)InputType.Aim].SetState(false, aimInput);

                        bool shootInput = msg.ReadBoolean();
                        keys[(int)InputType.Shoot].Held = shootInput;
                        keys[(int)InputType.Shoot].SetState(false, shootInput);

                        bool useInput = msg.ReadBoolean();
                        keys[(int)InputType.Use].Held = useInput;
                        keys[(int)InputType.Use].SetState(false, useInput);

                        if (AnimController is HumanoidAnimController)
                        {
                            bool crouching = msg.ReadBoolean();
                            keys[(int)InputType.Crouch].Held = crouching;
                            keys[(int)InputType.Crouch].SetState(false, crouching);
                        }

                        bool attackInput = msg.ReadBoolean();
                        keys[(int)InputType.Attack].Held = attackInput;
                        keys[(int)InputType.Attack].SetState(false, attackInput);
                        
                        double aimAngle = msg.ReadUInt16() / 65535.0 * 2.0 * Math.PI;
                        cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 500.0f;
                        TransformCursorPos();
                        
                        bool ragdollInput = msg.ReadBoolean();
                        keys[(int)InputType.Ragdoll].Held = ragdollInput;
                        keys[(int)InputType.Ragdoll].SetState(false, ragdollInput);

                        facingRight = msg.ReadBoolean();
                    }

                    bool entitySelected = msg.ReadBoolean();
                    Character selectedCharacter = null;
                    Item selectedItem = null;

                    AnimController.Animation animation = AnimController.Animation.None;
                    if (entitySelected)
                    {
                        ushort characterID = msg.ReadUInt16();
                        ushort itemID = msg.ReadUInt16();
                        selectedCharacter = FindEntityByID(characterID) as Character;
                        selectedItem = FindEntityByID(itemID) as Item;
                        if (characterID != NullEntityID)
                        {
                            bool doingCpr = msg.ReadBoolean();
                            if (doingCpr && SelectedCharacter != null)
                            {
                                animation = AnimController.Animation.CPR;
                            }
                        }
                    }

                    Vector2 pos = new Vector2(
                        msg.ReadFloat(),
                        msg.ReadFloat());
                    float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
                    Vector2 linearVelocity = new Vector2(
                        msg.ReadRangedSingle(-MaxVel, MaxVel, 12), 
                        msg.ReadRangedSingle(-MaxVel, MaxVel, 12));
                    linearVelocity = NetConfig.Quantize(linearVelocity, -MaxVel, MaxVel, 12);

                    bool fixedRotation = msg.ReadBoolean();
                    float? rotation = null;
                    float? angularVelocity = null;
                    if (!fixedRotation)
                    {
                        rotation = msg.ReadFloat();
                        float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;
                        angularVelocity = msg.ReadRangedSingle(-MaxAngularVel, MaxAngularVel, 8);
                        angularVelocity = NetConfig.Quantize(angularVelocity.Value, -MaxAngularVel, MaxAngularVel, 8);
                    }

                    bool readStatus = msg.ReadBoolean();
                    if (readStatus)
                    {
                        ReadStatus(msg);
                    }

                    msg.ReadPadBits();

                    int index = 0;
                    if (GameMain.Client.Character == this && AllowInput)
                    {
                        var posInfo = new CharacterStateInfo(
                            pos, rotation, 
                            networkUpdateID, 
                            facingRight ? Direction.Right : Direction.Left, 
                            selectedCharacter, selectedItem, animation);

                        while (index < memState.Count && NetIdUtils.IdMoreRecent(posInfo.ID, memState[index].ID))
                            index++;
                        memState.Insert(index, posInfo);
                    }
                    else
                    {
                        var posInfo = new CharacterStateInfo(
                            pos, rotation, 
                            linearVelocity, angularVelocity, 
                            sendingTime, facingRight ? Direction.Right : Direction.Left, 
                            selectedCharacter, selectedItem, animation);
                        
                        while (index < memState.Count && posInfo.Timestamp > memState[index].Timestamp)
                            index++;
                        memState.Insert(index, posInfo);
                    }

                    break;
                case ServerNetObject.ENTITY_EVENT:

                    int eventType = msg.ReadRangedInteger(0, 3);
                    switch (eventType)
                    {
                        case 0:
                            if (Inventory == null)
                            {
                                string errorMsg = "Received an inventory update message for an entity with no inventory (" + Name + ")";
                                DebugConsole.ThrowError(errorMsg);
                                GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ClientRead:NoInventory" + ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                            }
                            else
                            {
                                Inventory.ClientRead(type, msg, sendingTime);
                            }
                            break;
                        case 1:
                            byte ownerID = msg.ReadByte();
                            ResetNetState();
                            if (ownerID == GameMain.Client.ID)
                            {
                                if (controlled != null)
                                {
                                    LastNetworkUpdateID = controlled.LastNetworkUpdateID;
                                }

                                Controlled = this;
                                IsRemotePlayer = false;
                                GameMain.Client.HasSpawned = true;
                                GameMain.Client.Character = this;
                                GameMain.LightManager.LosEnabled = true;
                            }
                            else
                            {
                                if (controlled == this)
                                {
                                    Controlled = null;
                                    IsRemotePlayer = ownerID > 0;
                                }
                            }
                            break;
                        case 2:
                            ReadStatus(msg);
                            break;
                        case 3:
                            int skillCount = msg.ReadByte();
                            for (int i = 0; i < skillCount; i++)
                            {
                                string skillIdentifier = msg.ReadString();
                                float skillLevel = msg.ReadSingle();
                                info?.SetSkillLevel(skillIdentifier, skillLevel, WorldPosition + Vector2.UnitY * 150.0f);
                            }
                            break;
                    }
                    msg.ReadPadBits();
                    break;
            }
        }

        public static Character ReadSpawnData(NetBuffer inc, bool spawn = true)
        {
            DebugConsole.NewMessage("READING CHARACTER SPAWN DATA", Color.Cyan);

            if (GameMain.Client == null) return null;

            bool noInfo = inc.ReadBoolean();
            ushort id = inc.ReadUInt16();
            string speciesName = inc.ReadString();
            string seed = inc.ReadString();

            Vector2 position = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            bool enabled = inc.ReadBoolean();

            DebugConsole.Log("Received spawn data for " + speciesName);

            string configPath = GetConfigFile(speciesName);
            if (string.IsNullOrEmpty(configPath))
            {
                throw new Exception("Error in character spawn data - could not find a config file for the character \"" + configPath + "\"!");
            }

            Character character = null;
            if (noInfo)
            {
                if (!spawn) return null;

                character = Create(configPath, position, seed, null, true);
                character.ID = id;
            }
            else
            {
                bool hasOwner = inc.ReadBoolean();
                int ownerId = hasOwner ? inc.ReadByte() : -1;
                byte teamID = inc.ReadByte();
                bool hasAi = inc.ReadBoolean();
                string infoSpeciesName = inc.ReadString();

                if (!spawn) return null;

                string infoConfigPath = GetConfigFile(infoSpeciesName);
                if (string.IsNullOrEmpty(infoConfigPath))
                {
                    throw new Exception("Error in character spawn data - could not find a config file for the character info \"" + configPath + "\"!");
                }

                CharacterInfo info = CharacterInfo.ClientRead(infoConfigPath, inc);

                character = Create(configPath, position, seed, info, GameMain.Client.ID != ownerId, hasAi);
                character.ID = id;
                character.TeamID = (TeamType)teamID;

                if (configPath == HumanConfigFile && character.TeamID != TeamType.FriendlyNPC)
                {
                    CharacterInfo duplicateCharacterInfo = GameMain.GameSession.CrewManager.GetCharacterInfos().FirstOrDefault(c => c.ID == info.ID);
                    GameMain.GameSession.CrewManager.RemoveCharacterInfo(duplicateCharacterInfo);
                    GameMain.GameSession.CrewManager.AddCharacter(character);
                }

                if (GameMain.Client.ID == ownerId)
                {
                    GameMain.Client.HasSpawned = true;
                    GameMain.Client.Character = character;
                    Controlled = character;

                    GameMain.LightManager.LosEnabled = true;

                    character.memInput.Clear();
                    character.memState.Clear();
                    character.memLocalState.Clear();
                }
            }

            character.Enabled = Controlled == character || enabled;

            return character;
        }
        
        private void ReadStatus(NetBuffer msg)
        {
            bool isDead = msg.ReadBoolean();
            if (isDead)
            {
                CauseOfDeathType causeOfDeathType = (CauseOfDeathType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1);
                AfflictionPrefab causeOfDeathAffliction = null;
                if (causeOfDeathType == CauseOfDeathType.Affliction)
                {
                    int afflictionIndex = msg.ReadRangedInteger(0, AfflictionPrefab.List.Count - 1);
                    causeOfDeathAffliction = AfflictionPrefab.List[afflictionIndex];
                }

                byte severedLimbCount = msg.ReadByte();
                if (!IsDead)
                {
                    if (causeOfDeathType == CauseOfDeathType.Pressure)
                    {
                        Implode(true);
                    }
                    else
                    {
                        Kill(causeOfDeathType, causeOfDeathAffliction?.Instantiate(1.0f), true);
                    }
                }

                for (int i = 0; i < severedLimbCount; i++)
                {
                    int severedJointIndex = msg.ReadByte();
                    AnimController.SeverLimbJoint(AnimController.LimbJoints[severedJointIndex]);
                }
            }
            else
            {
                if (IsDead) Revive();
                
                CharacterHealth.ClientRead(msg);
            }
        }
    }
}
