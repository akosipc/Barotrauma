﻿using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class AutonomousObjective
    {
        public string aiTag;
        public string option;
        public float priorityModifier;

        public AutonomousObjective(XElement element)
        {
            aiTag = element.GetAttributeString("aitag", null);
            option = element.GetAttributeString("option", null);
            priorityModifier = element.GetAttributeFloat("prioritymodifier", 1);
            priorityModifier = MathHelper.Max(priorityModifier, 0);
        }
    }

    partial class JobPrefab
    {
        public static List<JobPrefab> List;

        public readonly XElement Items;
        public readonly List<string> ItemNames = new List<string>();
        public readonly List<SkillPrefab> Skills = new List<SkillPrefab>();
        public readonly List<AutonomousObjective> AutomaticOrders = new List<AutonomousObjective>();
        
        [Serialize("1,1,1,1", false)]
        public Color UIColor
        {
            get;
            private set;
        }

        [Serialize("notfound", false)]
        public string Identifier
        {
            get;
            private set;
        }

        [Serialize("notfound", false)]
        public string Name
        {
            get;
            private set;
        }

        [Serialize("", false)]
        public string Description
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool OnlyJobSpecificDialog
        {
            get;
            private set;
        }

        //the number of these characters in the crew the player starts with in the single player campaign
        [Serialize(0, false)]
        public int InitialCount
        {
            get;
            private set;
        }

        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        [Serialize(false, false)]
        public bool AllowAlways
        {
            get;
            private set;
        }

        //how many crew members can have the job (only one captain etc) 
        [Serialize(100, false)]
        public int MaxNumber
        {
            get;
            private set;
        }

        //how many crew members are REQUIRED to have the job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        [Serialize(0, false)]
        public int MinNumber
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float MinKarma
        {
            get;
            private set;
        }

        [Serialize(10.0f, false)]
        public float Commonness
        {
            get;
            private set;
        }

        //how much the vitality of the character is increased/reduced from the default value
        [Serialize(0.0f, false)]
        public float VitalityModifier
        {
            get;
            private set;
        }

        public XElement ClothingElement { get; private set; }

        public JobPrefab(XElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);
            Name = TextManager.Get("JobName." + Identifier);
            Description = TextManager.Get("JobDescription." + Identifier);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "items":
                        Items = subElement;
                        foreach (XElement itemElement in subElement.Elements())
                        {
                            if (itemElement.Element("name") != null)
                            {
                                DebugConsole.ThrowError("Error in job config \"" + Name + "\" - use identifiers instead of names to configure the items.");
                                ItemNames.Add(itemElement.GetAttributeString("name", ""));
                                continue;
                            }

                            string itemIdentifier = itemElement.GetAttributeString("identifier", "");
                            if (string.IsNullOrWhiteSpace(itemIdentifier))
                            {
                                DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item with no identifier.");
                                ItemNames.Add("");
                            }
                            else
                            {
                                var prefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                                if (prefab == null)
                                {
                                    DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item prefab \""+itemIdentifier+"\" not found.");
                                    ItemNames.Add("");
                                }
                                else
                                {
                                    ItemNames.Add(prefab.Name);
                                }
                            }                            
                        }
                        break;
                    case "skills":
                        foreach (XElement skillElement in subElement.Elements())
                        {
                            Skills.Add(new SkillPrefab(skillElement));
                        }
                        break;
                    case "autonomousobjectives":
                        subElement.Elements().ForEach(order => AutomaticOrders.Add(new AutonomousObjective(order)));
                        break;
                }
            }

            Skills.Sort((x,y) => y.LevelRange.X.CompareTo(x.LevelRange.X));

            ClothingElement = element.Element("PortraitClothing");
            if (ClothingElement == null)
            {
                ClothingElement = element.Element("portraitclothing");
            }
        }

        public static JobPrefab Random()
        {
            return List[Rand.Int(List.Count)];
        }

        public static void LoadAll(IEnumerable<string> filePaths)
        {
            List = new List<JobPrefab>();

            foreach (string filePath in filePaths)
            {
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    JobPrefab job = new JobPrefab(element);
                    List.Add(job);
                }
            }
        }
    }
}
