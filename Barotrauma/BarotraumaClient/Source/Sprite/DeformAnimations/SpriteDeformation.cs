﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.SpriteDeformations
{
    abstract class SpriteDeformationParams : ISerializableEntity
    {
        /// <summary>
        /// A negative value means that the deformation is used only by one sprite only (default). 
        /// A positive value means that this deformation is or could be used for multiple sprites.
        /// This behaviour is not automatic, and has to be implemented for any particular case separately (currently only used in Limbs).
        /// </summary>
        [Serialize(-1, true)]
        public int Sync
        {
            get;
            private set;
        }

        [Serialize("", true)]
        public string TypeName
        {
            get;
            set;
        }

        [Serialize(SpriteDeformation.DeformationBlendMode.Add, true), Editable]
        public SpriteDeformation.DeformationBlendMode BlendMode
        {
            get;
            set;
        }

        public string Name => GetType().Name;

        [Serialize(false, true)]
        public bool UseMovementSine { get; set; }

        [Serialize(false, true)]
        public bool StopWhenHostIsDead { get; set; }

        /// <summary>
        /// Only used if UseMovementSine is enabled. Multiplier for Pi.
        /// </summary>
        [Serialize(0f, true)]
        public float SineOffset { get; set; }

        public virtual float Frequency { get; set; } = 1;

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        /// <summary>
        /// Defined in the shader.
        /// </summary>
        public static readonly Point ShaderMaxResolution = new Point(15, 15);

        private Point _resolution;
        [Serialize("2,2", true)]
        public Point Resolution
        {
            get { return _resolution; }
            set
            {
                if (_resolution == value) { return; }
                _resolution = value.Clamp(new Point(2, 2), ShaderMaxResolution);
            }
        }

        public SpriteDeformationParams(XElement element)
        {
            if (element != null)
            {
                TypeName = element.GetAttributeString("type", "").ToLowerInvariant();
            }
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }

    abstract class SpriteDeformation
    {
        public enum DeformationBlendMode
        {
            Add, 
            Multiply,
            Override
        }

        public virtual float Phase { get; set; }

        protected Vector2[,] Deformation { get; private set; }

        protected SpriteDeformationParams deformationParams;

        private static readonly string[] deformationTypes = new string[] { "Inflate", "Custom", "Noise", "BendJoint", "ReactToTriggerers" };
        public static IEnumerable<string> DeformationTypes
        {
            get { return deformationTypes; }
        }

        public Point Resolution
        {
            get { return deformationParams.Resolution; }
            set { SetResolution(value); }
        }

        public SpriteDeformationParams DeformationParams
        {
            get { return deformationParams; }
            set { deformationParams = value; }
        }

        public string TypeName => deformationParams.TypeName;

        public int Sync => deformationParams.Sync;

        public static SpriteDeformation Load(string deformationType, string parentDebugName)
        {
            return Load(null, deformationType, parentDebugName);
        }
        public static SpriteDeformation Load(XElement element, string parentDebugName)
        {
            return Load(element, null, parentDebugName);
        }

        private static SpriteDeformation Load(XElement element, string deformationType, string parentDebugName)
        {
            string typeName = deformationType;

            if (element != null)
            {
                typeName = element.GetAttributeString("typename", null) ?? element.GetAttributeString("type", "");
            }
            
            SpriteDeformation newDeformation = null;
            switch (typeName.ToLowerInvariant())
            {
                case "inflate":
                    newDeformation = new Inflate(element);
                    break;
                case "custom":
                    newDeformation = new CustomDeformation(element);
                    break;
                case "noise":
                    newDeformation = new NoiseDeformation(element);
                    break;
                case "jointbend":
                case "bendjoint":
                    newDeformation = new JointBendDeformation(element);
                    break;
                case "reacttotriggerers":
                    return new PositionalDeformation(element);
                default:
                    if (Enum.TryParse(typeName, out PositionalDeformation.ReactionType reactionType))
                    {
                        newDeformation = new PositionalDeformation(element)
                        {
                            Type = reactionType
                        };
                    }
                    else
                    {
                        DebugConsole.ThrowError("Could not load sprite deformation animation in " + parentDebugName + " - \"" + typeName + "\" is not a valid deformation type.");
                    }
                    break;
            }

            if (newDeformation != null)
            {
                newDeformation.deformationParams.TypeName = typeName;
            }
            return newDeformation;
        }

        protected SpriteDeformation(XElement element, SpriteDeformationParams deformationParams)
        {
            this.deformationParams = deformationParams;
            SerializableProperty.DeserializeProperties(deformationParams, element);
            Deformation = new Vector2[deformationParams.Resolution.X, deformationParams.Resolution.Y];
        }

        public void SetResolution(Point resolution)
        {
            deformationParams.Resolution = resolution;
            Deformation = new Vector2[deformationParams.Resolution.X, deformationParams.Resolution.Y];
        }

        protected abstract void GetDeformation(out Vector2[,] deformation, out float multiplier);

        public abstract void Update(float deltaTime);

        public static Vector2[,] GetDeformation(IEnumerable<SpriteDeformation> animations, Vector2 scale)
        {
            foreach (SpriteDeformation animation in animations)
            {
                if (animation.deformationParams.Resolution.X != animation.Deformation.GetLength(0) ||
                    animation.deformationParams.Resolution.Y != animation.Deformation.GetLength(1))
                {
                    animation.Deformation = new Vector2[animation.deformationParams.Resolution.X, animation.deformationParams.Resolution.Y];
                }
            }

            Point resolution = animations.First().Resolution;
            if (animations.Any(a => a.Resolution != resolution))
            {
                DebugConsole.ThrowError("All animations must have the same resolution! Using the lowest resolution.");
                resolution = animations.OrderBy(anim => anim.Resolution.X + anim.Resolution.Y).First().Resolution;
                animations.ForEach(a => a.Resolution = resolution);
            }

            Vector2[,] deformation = new Vector2[resolution.X, resolution.Y];
            foreach (SpriteDeformation animation in animations)
            {
                animation.GetDeformation(out Vector2[,] animDeformation, out float multiplier);

                for (int x = 0; x < resolution.X; x++)
                {
                    for (int y = 0; y < resolution.Y; y++)
                    {
                        switch (animation.deformationParams.BlendMode)
                        {
                            case DeformationBlendMode.Override:
                                deformation[x,y] = animDeformation[x,y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Add:
                                deformation[x, y] += animDeformation[x, y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Multiply:
                                deformation[x, y] *= animDeformation[x, y] * multiplier;
                                break;
                        }
                    }
                }
            }
            return deformation;
        }

        public virtual void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(deformationParams, element);
        }
    }
}
