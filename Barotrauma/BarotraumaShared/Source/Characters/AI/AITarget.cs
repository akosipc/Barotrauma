﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AITarget
    {
        public static List<AITarget> List = new List<AITarget>();

        public Entity Entity
        {
            get;
            private set;
        }

        /// <summary>
        /// Use as a minimum or static sight range.
        /// </summary>
        public static float StaticSightRange = 3000;
        
        private float soundRange;
        private float sightRange;

        /// <summary>
        /// How long does it take for the ai target to fade out if not kept alive.
        /// </summary>
        public float FadeOutTime { get; private set; } = 3;
        
        public float SoundRange
        {
            get { return soundRange; }
            set { soundRange = MathHelper.Clamp(value, MinSoundRange, MaxSoundRange); }
        }

        public float SightRange
        {
            get { return sightRange; }
            set { sightRange = MathHelper.Clamp(value, MinSightRange, MaxSightRange); }
        }

        private float sectorRad = MathHelper.TwoPi;
        public float SectorDegrees
        {
            get { return MathHelper.ToDegrees(sectorRad); }
            set { sectorRad = MathHelper.ToRadians(value); }
        }

        private Vector2 sectorDir;
        public Vector2 SectorDir
        {
            get { return sectorDir; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Invalid AITarget sector direction (" + value + ")\n" + Environment.StackTrace;
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.SectorDir:" + Entity?.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }
                sectorDir = value;
            }
        }

        public string SonarLabel;

        public bool Enabled = true;

        public float MinSoundRange, MinSightRange;
        public float MaxSoundRange = float.MaxValue, MaxSightRange = float.MaxValue;

        public TargetType Type { get; private set; }

        public enum TargetType
        {
            Any,
            HumanOnly,
            EnemyOnly
        }

        public Vector2 WorldPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace);
                    return Vector2.Zero;
                }

                return Entity.WorldPosition;
            }
        }

        public Vector2 SimPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace);
                    return Vector2.Zero;
                }

                return Entity.SimPosition;
            }
        }

        public AITarget(Entity e, XElement element) : this(e)
        {
            SightRange = element.GetAttributeFloat("sightrange", 0.0f);
            SoundRange = element.GetAttributeFloat("soundrange", 0.0f);
            MinSightRange = element.GetAttributeFloat("minsightrange", SightRange);
            MinSoundRange = element.GetAttributeFloat("minsoundrange", SoundRange);
            MaxSightRange = element.GetAttributeFloat("maxsightrange", SightRange);
            MaxSoundRange = element.GetAttributeFloat("maxsoundrange", SoundRange);
            FadeOutTime = element.GetAttributeFloat("fadeouttime", FadeOutTime);
            SonarLabel = element.GetAttributeString("sonarlabel", "");
            string typeString = element.GetAttributeString("type", "Any");
            if (Enum.TryParse(typeString, out TargetType t))
            {
                Type = t;
            }
        }

        public AITarget(Entity e, float sightRange = -1, float soundRange = 0)
        {
            Entity = e;
            if (sightRange < 0)
            {
                sightRange = StaticSightRange;
            }
            SightRange = sightRange;
            SoundRange = soundRange;
            List.Add(this);
        }

        public bool IsWithinSector(Vector2 worldPosition)
        {
            if (sectorRad >= MathHelper.TwoPi) return true;

            Vector2 diff = worldPosition - WorldPosition;
            return MathUtils.GetShortestAngle(MathUtils.VectorToAngle(diff), MathUtils.VectorToAngle(sectorDir)) <= sectorRad * 0.5f;
        }

        public void Remove()
        {
            List.Remove(this);
            Entity = null;
        }
    }
}
