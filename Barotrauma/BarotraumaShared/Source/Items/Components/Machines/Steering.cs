﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private const float AutopilotRayCastInterval = 0.5f;
        private const float RecalculatePathInterval = 5.0f;

        private Vector2 currVelocity;
        private Vector2 targetVelocity;

        private Vector2 steeringInput;

        private bool autoPilot;

        private Vector2? posToMaintain;

        private SteeringPath steeringPath;

        private PathFinder pathFinder;

        private float networkUpdateTimer;
        private bool unsentChanges;

        private float autopilotRayCastTimer;
        private float autopilotRecalculatePathTimer;

        private Vector2 avoidStrength;

        private float neutralBallastLevel;

        private float steeringAdjustSpeed = 1.0f;

        private Character user;

        private Sonar sonar;

        private Submarine controlledSub;
                
        public bool AutoPilot
        {
            get { return autoPilot; }
            set
            {
                if (value == autoPilot) return;
                autoPilot = value;
#if CLIENT
                autopilotTickBox.Selected = autoPilot;
                manualTickBox.Selected = !autoPilot;
                maintainPosTickBox.Enabled = autoPilot;
                levelEndTickBox.Enabled = autoPilot;
                levelStartTickBox.Enabled = autoPilot;
#endif
                if (autoPilot)
                {
                    if (pathFinder == null) pathFinder = new PathFinder(WayPoint.WayPointList, false);
                    MaintainPos = true;
                }
                else
                {
                    PosToMaintain = null;
                    MaintainPos = false;
                    LevelEndSelected = false;
                    LevelStartSelected = false;
                }
            }
        }
        
        [Editable(0.0f, 1.0f, decimals: 3, ToolTip = "How full the ballast tanks should be when the submarine is not being steered upwards/downwards."
            +" Can be used to compensate if the ballast tanks are too large/small relative to the size of the submarine."), Serialize(0.5f, true)]
        public float NeutralBallastLevel
        {
            get { return neutralBallastLevel; }
            set
            {
                neutralBallastLevel = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Serialize(1000.0f, true)]
        public float DockingAssistThreshold
        {
            get;
            set;
        }

        public Vector2 TargetVelocity
        {
            get { return targetVelocity;}
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }

        public Vector2 SteeringInput
        {
            get { return steeringInput; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                steeringInput.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                steeringInput.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }

        public SteeringPath SteeringPath
        {
            get { return steeringPath; }
        }

        public Vector2? PosToMaintain
        {
            get { return posToMaintain; }
            set { posToMaintain = value; }
        }

        struct ObstacleDebugInfo
        {
            public Vector2 Point1;
            public Vector2 Point2;

            public Vector2? Intersection;

            public float Dot;

            public Vector2 AvoidStrength;

            public ObstacleDebugInfo(GraphEdge edge, Vector2? intersection, float dot, Vector2 avoidStrength)
            {
                Point1 = edge.Point1;
                Point2 = edge.Point2;
                Intersection = intersection;
                Dot = dot;
                AvoidStrength = avoidStrength;
            }
        }

        //edge point 1, edge point 2, avoid strength
        private List<ObstacleDebugInfo> debugDrawObstacles = new List<ObstacleDebugInfo>();

        public Steering(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
            sonar = item.GetComponent<Sonar>();
        }

        public override bool Select(Character character)
        {
            if (!CanBeSelected) return false;

            user = character;
            return true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            networkUpdateTimer -= deltaTime;
            if (unsentChanges)
            {
                if (networkUpdateTimer <= 0.0f)
                {
#if CLIENT
                    if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }
                    else
#endif
#if SERVER
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                    }
#endif

                    networkUpdateTimer = 0.1f;
                    unsentChanges = false;
                }
            }

            controlledSub = item.Submarine;
            var sonar = item.GetComponent<Sonar>();
            if (sonar != null && sonar.UseTransducers)
            {
                controlledSub = sonar.ConnectedTransducers.Any() ? sonar.ConnectedTransducers.First().Item.Submarine : null;
            }

            currPowerConsumption = powerConsumption;

            if (voltage < minVoltage && currPowerConsumption > 0.0f) { return; }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (autoPilot)
            {
                UpdateAutoPilot(deltaTime);
            }
            else
            {
                if (user != null && user.Info != null && user.SelectedConstruction == item)
                {
                    user.Info.IncreaseSkillLevel("helm", 0.005f * deltaTime, user.WorldPosition + Vector2.UnitY * 150.0f);
                }

                Vector2 velocityDiff = steeringInput - targetVelocity;
                if (velocityDiff != Vector2.Zero)
                {
                    if (steeringAdjustSpeed >= 0.99f)
                    {
                        TargetVelocity = steeringInput;
                    }
                    else
                    {
                        float steeringChange = 1.0f / (1.0f - steeringAdjustSpeed);
                        steeringChange *= steeringChange * 10.0f;

                        TargetVelocity += Vector2.Normalize(velocityDiff) * 
                            Math.Min(steeringChange * deltaTime, velocityDiff.Length());
                    }
                }
            }

            item.SendSignal(0, targetVelocity.X.ToString(CultureInfo.InvariantCulture), "velocity_x_out", null);

            float targetLevel = -targetVelocity.Y;
            targetLevel += (neutralBallastLevel - 0.5f) * 100.0f;

            item.SendSignal(0, targetLevel.ToString(CultureInfo.InvariantCulture), "velocity_y_out", null);

            voltage -= deltaTime;
        }

        private void UpdateAutoPilot(float deltaTime)
        {
            if (controlledSub == null) return;
            if (posToMaintain != null)
            {
                SteerTowardsPosition((Vector2)posToMaintain);
                return;
            }

            autopilotRayCastTimer -= deltaTime;
            autopilotRecalculatePathTimer -= deltaTime;
            if (autopilotRecalculatePathTimer <= 0.0f)
            {
                //periodically recalculate the path in case the sub ends up to a position 
                //where it can't keep traversing the initially calculated path
                UpdatePath();
                autopilotRecalculatePathTimer = RecalculatePathInterval;
            }

            steeringPath.CheckProgress(ConvertUnits.ToSimUnits(controlledSub.WorldPosition), 10.0f);

            if (autopilotRayCastTimer <= 0.0f && steeringPath.NextNode != null)
            {
                Vector2 diff = ConvertUnits.ToSimUnits(steeringPath.NextNode.Position - controlledSub.WorldPosition);

                //if the node is close enough, check if it's visible
                float lengthSqr = diff.LengthSquared();
                if (lengthSqr > 0.001f && lengthSqr < 500.0f)
                {
                    diff = Vector2.Normalize(diff);

                    //check if the next waypoint is visible from all corners of the sub
                    //(i.e. if we can navigate directly towards it or if there's obstacles in the way)
                    bool nextVisible = true;
                    for (int x = -1; x < 2; x += 2)
                    {
                        for (int y = -1; y < 2; y += 2)
                        {
                            Vector2 cornerPos =
                                new Vector2(controlledSub.Borders.Width * x, controlledSub.Borders.Height * y) / 2.0f;

                            cornerPos = ConvertUnits.ToSimUnits(cornerPos * 1.2f + controlledSub.WorldPosition);

                            float dist = Vector2.Distance(cornerPos, steeringPath.NextNode.SimPosition);

                            if (Submarine.PickBody(cornerPos, cornerPos + diff * dist, null, Physics.CollisionLevel) == null) continue;

                            nextVisible = false;
                            x = 2;
                            y = 2;
                        }
                    }

                    if (nextVisible) steeringPath.SkipToNextNode();
                }

                

                autopilotRayCastTimer = AutopilotRayCastInterval;
            }

            if (steeringPath.CurrentNode != null)
            {
                SteerTowardsPosition(steeringPath.CurrentNode.WorldPosition);
            }

            Vector2 avoidDist = new Vector2(
                Math.Max(1000.0f * Math.Abs(controlledSub.Velocity.X), controlledSub.Borders.Width * 1.5f),
                Math.Max(1000.0f * Math.Abs(controlledSub.Velocity.Y), controlledSub.Borders.Height * 1.5f));

            float avoidRadius = avoidDist.Length();

            Vector2 newAvoidStrength = Vector2.Zero;

            debugDrawObstacles.Clear();

            //steer away from nearby walls
            var closeCells = Level.Loaded.GetCells(controlledSub.WorldPosition, 4);
            foreach (VoronoiCell cell in closeCells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (MathUtils.GetLineIntersection(edge.Point1, edge.Point2, controlledSub.WorldPosition, cell.Center, out Vector2 intersection))
                    {
                        Vector2 diff = controlledSub.WorldPosition - intersection;

                        //far enough -> ignore
                        if (Math.Abs(diff.X) > avoidDist.X && Math.Abs(diff.Y) > avoidDist.Y)
                        {
                            debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, 0.0f, Vector2.Zero));
                            continue;
                        }
                        if (diff.LengthSquared() < 1.0f) diff = Vector2.UnitY;

                        Vector2 normalizedDiff = Vector2.Normalize(diff);
                        float dot = controlledSub.Velocity == Vector2.Zero ?
                            0.0f : Vector2.Dot(controlledSub.Velocity, -normalizedDiff);

                        //not heading towards the wall -> ignore
                        if (dot < 0.5)
                        {
                            debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, dot, Vector2.Zero));
                            continue;
                        }
                        
                        Vector2 change = (normalizedDiff * Math.Max((avoidRadius - diff.Length()), 0.0f)) / avoidRadius;                        
                        newAvoidStrength += change * dot;
                        debugDrawObstacles.Add(new ObstacleDebugInfo(edge, intersection, dot, change * dot));
                    }
                }
            }

            avoidStrength = Vector2.Lerp(avoidStrength, newAvoidStrength, deltaTime * 10.0f);

            targetVelocity += avoidStrength * 100.0f;

            //steer away from other subs
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == controlledSub) continue;
                if (controlledSub.DockedTo.Contains(sub)) continue;

                float thisSize = Math.Max(controlledSub.Borders.Width, controlledSub.Borders.Height);
                float otherSize = Math.Max(sub.Borders.Width, sub.Borders.Height);

                Vector2 diff = controlledSub.WorldPosition - sub.WorldPosition;
                float dist = diff == Vector2.Zero ? 0.0f : diff.Length();

                //far enough -> ignore
                if (dist > thisSize + otherSize) continue;

                Vector2 dir = dist <= 0.0001f ? Vector2.UnitY : diff / dist;
                float dot = controlledSub.Velocity == Vector2.Zero ?
                    0.0f : Vector2.Dot(Vector2.Normalize(controlledSub.Velocity), -dir);

                //heading away -> ignore
                if (dot < 0.0f) continue;

                targetVelocity += diff * 200.0f;
            }

            //clamp velocity magnitude to 100.0f
            float velMagnitude = targetVelocity.Length();
            if (velMagnitude > 100.0f)
            {
                targetVelocity *= 100.0f / velMagnitude;
            }
        }

        private void UpdatePath()
        {
            if (pathFinder == null) pathFinder = new PathFinder(WayPoint.WayPointList, false);

            Vector2 target;
            if (LevelEndSelected)
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.EndPosition);
            }
            else
            {
                target = ConvertUnits.ToSimUnits(Level.Loaded.StartPosition);
            }
            steeringPath = pathFinder.FindPath(ConvertUnits.ToSimUnits(controlledSub == null ? item.WorldPosition : controlledSub.WorldPosition), target, errorMsgStr: "(Autopilot, target: " + target + ")");
        }

        public void SetDestinationLevelStart()
        {
            AutoPilot = true;
            MaintainPos = false;
            posToMaintain = null;
            LevelEndSelected = false;
            if (!LevelStartSelected)
            {
                LevelStartSelected = true;
                UpdatePath();
            }
        }

        public void SetDestinationLevelEnd()
        {
            AutoPilot = true;
            MaintainPos = false;
            posToMaintain = null;
            LevelStartSelected = false;
            if (!LevelEndSelected)
            {
                LevelEndSelected = true;
                UpdatePath();
            }
        }
        private void SteerTowardsPosition(Vector2 worldPosition)
        {
            float prediction = 10.0f;

            Vector2 futurePosition = ConvertUnits.ToDisplayUnits(controlledSub.Velocity) * prediction;
            Vector2 targetSpeed = ((worldPosition - controlledSub.WorldPosition) - futurePosition);

            if (targetSpeed.Length() > 500.0f)
            {
                targetSpeed = Vector2.Normalize(targetSpeed);
                TargetVelocity = targetSpeed * 100.0f;
            }
            else
            {
                TargetVelocity = targetSpeed / 5.0f;
            }
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (user != character && user != null && user.SelectedConstruction == item)
            {
                character.Speak(TextManager.Get("DialogSteeringTaken"), null, 0.0f, "steeringtaken", 10.0f);
            }

            user = character;

            switch (objective.Option.ToLowerInvariant())
            {
                case "maintainposition":
                    if (!posToMaintain.HasValue)
                    {
                        unsentChanges = true;
                        posToMaintain = controlledSub != null ?
                            controlledSub.WorldPosition :
                            item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
                    }

                    if (!AutoPilot || !MaintainPos) unsentChanges = true;

                    AutoPilot = true;
                    MaintainPos = true;
                    break;
                case "navigateback":
                    if (!AutoPilot || MaintainPos || LevelEndSelected || !LevelStartSelected)
                    {
                        unsentChanges = true;
                    }
                    SetDestinationLevelStart();
                    break;
                case "navigatetodestination":
                    if (!AutoPilot || MaintainPos || !LevelEndSelected || LevelStartSelected)
                    {
                        unsentChanges = true;
                    }
                    SetDestinationLevelEnd();
                    break;
            }

            sonar?.AIOperate(deltaTime, character, objective);

            return false;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection.Name == "velocity_in")
            {
                currVelocity = XMLExtensions.ParseVector2(signal, false);
            }
            else
            {
                base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power, signalStrength);
            }
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c)
        {
            bool autoPilot              = msg.ReadBoolean();
            bool dockingButtonClicked   = msg.ReadBoolean();
            Vector2 newSteeringInput    = targetVelocity;
            bool maintainPos            = false;
            Vector2? newPosToMaintain   = null;
            bool headingToStart         = false;

            if (autoPilot)
            {
                maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadFloat(), 
                        msg.ReadFloat());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newSteeringInput = new Vector2(msg.ReadFloat(), msg.ReadFloat());
            }

            if (!item.CanClientAccess(c)) return;

            user = c.Character;
            AutoPilot = autoPilot;

            if (dockingButtonClicked)
            {
                item.SendSignal(0, "1", "toggle_docking", sender: Character.Controlled);
            }

            if (!AutoPilot)
            {
                steeringInput = newSteeringInput;
                steeringAdjustSpeed = MathHelper.Lerp(0.2f, 1.0f, c.Character.GetSkillLevel("helm") / 100.0f);
            }
            else
            {
                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }

            //notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            msg.Write(autoPilot);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(steeringInput.X);
                msg.Write(steeringInput.Y);
                msg.Write(targetVelocity.X);
                msg.Write(targetVelocity.Y);
                msg.Write(steeringAdjustSpeed);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
    }
}
