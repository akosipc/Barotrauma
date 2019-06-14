﻿using Barotrauma.Items.Components;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    enum ClientPacketHeader
    {
        REQUEST_AUTH,   //ask the server if a password is needed, if so we'll get nonce for encryption
        REQUEST_STEAMAUTH, //the same as REQUEST_AUTH, but in addition we want to authenticate the player's Steam ID
        REQUEST_INIT,   //ask the server to give you initialization
        UPDATE_LOBBY,   //update state in lobby
        UPDATE_INGAME,  //update state ingame

        SERVER_SETTINGS, //change server settings
        
        CAMPAIGN_SETUP_INFO,

        FILE_REQUEST,   //request a (submarine) file from the server

        VOICE,
        
        RESPONSE_STARTGAME, //tell the server whether you're ready to start
        SERVER_COMMAND,     //tell the server to end a round or kick/ban someone (special permissions required)

        ERROR           //tell the server that an error occurred
    }
    enum ClientNetObject
    {
        END_OF_MESSAGE, //self-explanatory
        SYNC_IDS,       //ids of the last changes the client knows about
        CHAT_MESSAGE,   //also self-explanatory
        VOTE,           //you get the idea
        CHARACTER_INPUT,
        ENTITY_STATE
    }

    enum ClientNetError
    {
        MISSING_EVENT, //client was expecting a previous event
        MISSING_ENTITY //client can't find an entity of a certain ID
    }

    enum ServerPacketHeader
    {
        AUTH_RESPONSE,      //tell the player if they require a password to log in
        AUTH_FAILURE,       //the server won't authorize player yet, however connection is still alive
        UPDATE_LOBBY,       //update state in lobby (votes and chat messages)
        UPDATE_INGAME,      //update state ingame (character input and chat messages)

        PERMISSIONS,        //tell the client which special permissions they have (if any)
        ACHIEVEMENT,        //give the client a steam achievement
        CHEATS_ENABLED,     //tell the clients whether cheats are on or off

        CAMPAIGN_SETUP_INFO,

        FILE_TRANSFER,

        VOICE,

        QUERY_STARTGAME,    //ask the clients whether they're ready to start
        STARTGAME,          //start a new round
        ENDGAME
    }
    enum ServerNetObject
    {
        END_OF_MESSAGE,
        SYNC_IDS,
        CHAT_MESSAGE,
        VOTE,
        CLIENT_LIST,
        ENTITY_POSITION,
        ENTITY_EVENT,
        ENTITY_EVENT_INITIAL,
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound,
        Kick,
        StartRound
    }

    enum DisconnectReason
    {
        Unknown,
        Banned,
        Kicked,
        ServerShutdown,
        ServerFull,
        AuthenticationRequired,
        SteamAuthenticationRequired,
        SteamAuthenticationFailed,
        SessionTaken,
        TooManyFailedLogins,
        NoName,
        InvalidName,
        NameTaken,
        InvalidVersion,
        MissingContentPackage,
        IncompatibleContentPackage,
        NotOnWhitelist,
    }

    abstract partial class NetworkMember
    {
        public UInt16 LastClientListUpdateID
        {
            get;
            set;
        }

        public virtual bool IsServer
        {
            get { return false; }
        }

        public virtual bool IsClient
        {
            get { return false; }
        }

        public abstract void CreateEntityEvent(INetSerializable entity, object[] extraData = null);

#if DEBUG
        public Dictionary<string, long> messageCount = new Dictionary<string, long>();
#endif

        public NetPeer NetPeer
        {
            get;
            protected set;
        }

        protected string name;

        protected ServerSettings serverSettings;
        
        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        public int EndVoteCount, EndVoteMax;

        protected bool gameStarted;

        protected RespawnManager respawnManager;

        public bool ShowNetStats;

        public int Port
        {
            get;
            set;
        }

        public int TickRate
        {
            get { return serverSettings.TickRate; }
            set
            {
                serverSettings.TickRate = MathHelper.Clamp(value, 1, 60);
                updateInterval = new TimeSpan(0, 0, 0, 0, MathHelper.Clamp(1000 / serverSettings.TickRate, 1, 500));
            }
        }
        
        public string Name
        {
            get { return name; }
            set
            {
                if (string.IsNullOrEmpty(value)) { return; }
                name = value.Replace(":", "").Replace(";", "");
            }
        }

        public bool GameStarted
        {
            get { return gameStarted; }
        }

        public virtual List<Client> ConnectedClients
        {
            get { return null; }
        }

        public RespawnManager RespawnManager
        {
            get { return respawnManager; }
        }

        public ServerSettings ServerSettings
        {
            get { return serverSettings; }
        }
        
        public NetPeerConfiguration NetPeerConfiguration
        {
            get;
            protected set;
        }
        
        public bool CanUseRadio(Character sender)
        {
            if (sender == null) return false;

            var radio = sender.Inventory.Items.FirstOrDefault(i => i != null && i.GetComponent<WifiComponent>() != null);
            if (radio == null || !sender.HasEquippedItem(radio)) return false;
                       
            var radioComponent = radio.GetComponent<WifiComponent>();
            if (radioComponent == null) return false;
            return radioComponent.HasRequiredContainedItems(false);
        }

        public void AddChatMessage(string message, ChatMessageType type, string senderName = "", Character senderCharacter = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderCharacter));
        }

        public virtual void AddChatMessage(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Text)) { return; }
                        
            if (message.Sender != null && !message.Sender.IsDead)
            {
                message.Sender.ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)message.Type]);
            }
        }

        public virtual void KickPlayer(string kickedName, string reason) { }

        public virtual void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null) { }

        public virtual void UnbanPlayer(string playerName, string playerIP) { }

        public virtual void Update(float deltaTime) { }

        public virtual void Disconnect() { }
    }

}
