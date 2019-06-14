﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RelatedItem
    {
        public enum RelationType
        {
            None,
            Contained,
            Equipped,
            Picked,
            Container
        }

        public bool IsOptional { get; set; }
        public bool MatchOnEmpty { get; set; }

        public bool IgnoreInEditor { get; set; }

        private string[] excludedIdentifiers;

        private RelationType type;

        public List<StatusEffect> statusEffects;
        
        public string Msg;

        public RelationType Type
        {
            get { return type; }
        }

        public string JoinedIdentifiers
        {
            get { return string.Join(",", Identifiers); }
            set
            {
                if (value == null) return;

                Identifiers = value.Split(',');
                for (int i = 0; i < Identifiers.Length; i++)
                {
                    Identifiers[i] = Identifiers[i].Trim().ToLowerInvariant();
                }
            }
        }

        public string[] Identifiers { get; private set; }

        public string JoinedExcludedIdentifiers
        {
            get { return string.Join(",", excludedIdentifiers); }
            set
            {
                if (value == null) return;

                excludedIdentifiers = value.Split(',');
                for (int i = 0; i < excludedIdentifiers.Length; i++)
                {
                    excludedIdentifiers[i] = excludedIdentifiers[i].Trim().ToLowerInvariant();
                }
            }
        }

        public bool MatchesItem(Item item)
        {
            if (item == null) return false;
            if (excludedIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id))) return false;
            return Identifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id));
        }

        public RelatedItem(string[] identifiers, string[] excludedIdentifiers)
        {
            for (int i = 0; i < identifiers.Length; i++)
            {
                identifiers[i] = identifiers[i].Trim().ToLowerInvariant();
            }
            this.Identifiers = identifiers;

            for (int i = 0; i < excludedIdentifiers.Length; i++)
            {
                excludedIdentifiers[i] = excludedIdentifiers[i].Trim().ToLowerInvariant();
            }
            this.excludedIdentifiers = excludedIdentifiers;

            statusEffects = new List<StatusEffect>();
        }

        public bool CheckRequirements(Character character, Item parentItem)
        {
            switch (type)
            {
                case RelationType.Contained:
                    if (parentItem == null) return false;

                    var containedItems = parentItem.ContainedItems;
                    if (containedItems == null) return false;

                    if (MatchOnEmpty && !containedItems.Any(ci => ci != null))
                    {
                        return true;
                    }

                    foreach (Item contained in containedItems)
                    {
                        if (contained.Condition > 0.0f && MatchesItem(contained)) return true;
                    }
                    break;
                case RelationType.Container:
                    if (parentItem == null || parentItem.Container == null) return false;

                    return parentItem.Container.Condition > 0.0f && MatchesItem(parentItem.Container);
                case RelationType.Equipped:
                    if (character == null) return false;
                    foreach (Item equippedItem in character.SelectedItems)
                    {
                        if (equippedItem == null) continue;

                        if (equippedItem.Condition > 0.0f && MatchesItem(equippedItem)) return true;
                    }
                    break;
                case RelationType.Picked:
                    if (character == null || character.Inventory == null) return false;
                    foreach (Item pickedItem in character.Inventory.Items)
                    {
                        if (pickedItem == null) continue;

                        if (MatchesItem(pickedItem)) return true;
                    }
                    break;
                default:
                    return true;
            }

            return false;
        }

        public void Save(XElement element)
        {
            element.Add(
                new XAttribute("identifiers", JoinedIdentifiers),
                new XAttribute("type", type.ToString()),
                new XAttribute("optional", IsOptional),
                new XAttribute("ignoreineditor", IgnoreInEditor));

            if (excludedIdentifiers.Length > 0)
            {
                element.Add(new XAttribute("excludedidentifiers", JoinedExcludedIdentifiers));
            }

            if (!string.IsNullOrWhiteSpace(Msg)) element.Add(new XAttribute("msg", Msg));
        }

        public static RelatedItem Load(XElement element, string parentDebugName)
        {
            string[] identifiers;
            if (element.Attribute("name") != null)
            {
                //backwards compatibility + a console warning
                DebugConsole.ThrowError("Error in RelatedItem config (" + (string.IsNullOrEmpty(parentDebugName) ? element.ToString() : parentDebugName) + ") - use item identifiers or tags instead of names.");
                string[] itemNames = element.GetAttributeStringArray("name", new string[0]);
                //attempt to convert to identifiers and tags
                List<string> convertedIdentifiers = new List<string>();
                foreach (string itemName in itemNames)
                {
                    if (MapEntityPrefab.List.Find(me => me.Name == itemName) is ItemPrefab matchingItem)
                    {
                        convertedIdentifiers.Add(matchingItem.Identifier);
                    }
                    else
                    {
                        //no matching item found, this must be a tag
                        convertedIdentifiers.Add(itemName);
                    }
                }
                identifiers = convertedIdentifiers.ToArray();
            }
            else
            {
                identifiers = element.GetAttributeStringArray("identifiers", new string[0]);
                if (identifiers.Length == 0) identifiers = element.GetAttributeStringArray("identifier", new string[0]);
            }

            string[] excludedIdentifiers = element.GetAttributeStringArray("excludedidentifiers", new string[0]);
            if (excludedIdentifiers.Length == 0) excludedIdentifiers = element.GetAttributeStringArray("excludedidentifier", new string[0]);

            if (identifiers.Length == 0 && excludedIdentifiers.Length == 0) return null;

            RelatedItem ri = new RelatedItem(identifiers, excludedIdentifiers);
            
            string typeStr = element.GetAttributeString("type", "");
            if (string.IsNullOrEmpty(typeStr))
            {
                if (element.Name.ToString().ToLowerInvariant() == "containable") typeStr = "Contained";
            }
            if (!Enum.TryParse(typeStr, true, out ri.type))
            {
                DebugConsole.ThrowError("Error in RelatedItem config ("+parentDebugName+") - \""+ typeStr +"\" is not a valid relation type.");
                return null;
            }

            string msgTag = element.GetAttributeString("msg", "");
            string msg = TextManager.Get(msgTag, true);
            if (msg == null)
            {
                ri.Msg = msgTag;
            }
            else
            {
#if CLIENT
                foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
                {
                    msg = msg.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameMain.Config.KeyBind(inputType).ToString());
                }
                ri.Msg = msg;
#endif
            }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "statuseffect") continue;
                ri.statusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
            }

            ri.IsOptional = element.GetAttributeBool("optional", false);
            ri.IgnoreInEditor = element.GetAttributeBool("ignoreineditor", false);
            ri.MatchOnEmpty = element.GetAttributeBool("matchonempty", false);
            return ri;
        }
    }
}
