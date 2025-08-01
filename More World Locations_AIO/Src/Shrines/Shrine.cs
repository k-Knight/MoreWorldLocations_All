using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Jotunn.Managers;
using More_World_Locations_AIO.RPCs;
using UnityEngine;

namespace More_World_Locations_AIO.Shrines;

public class Shrine : MonoBehaviour, Interactable, Hoverable
{
    public ShrineConfig shrineConfig;
    
    public bool hasBeenUsedOnce = false;

    public ZNetView znetView;

    public Heightmap.Biome biome;
    
    public const string ShrineConfigKey = "MWL_ShrineConfigName";
    private const string ShrineLastUsedDayKey = "MWL_Shrine_LastUsedDay";
    
    public void Awake()
    {
        znetView = GetComponent<ZNetView>();
        ZDO zdo = znetView.GetZDO();
        if (znetView.IsOwner())
        {
            if (!zdo.GetString(ShrineConfigKey, out string storedName) || string.IsNullOrEmpty(storedName))
            {
                biome = WorldGenerator.instance.GetBiome(this.transform.position);
                
                shrineConfig = ShrineDB.GetRandomShrineConfig(biome);
                shrineConfig.RollRandomValues();
                
                zdo.Set(ShrineConfigKey, shrineConfig.internalName);
                zdo.Set("MWL_Shrine_Duration", shrineConfig.duration);
                zdo.Set("MWL_Shrine_HealthRegenMult", shrineConfig.healthRegenMultiplier);
                zdo.Set("MWL_Shrine_StaminaRegenMult", shrineConfig.staminaRegenMultiplier);
                zdo.Set("MWL_Shrine_EitrRegenMult", shrineConfig.eitrRegenMultiplier);
                zdo.Set("MWL_Shrine_RaiseSkillMod", shrineConfig.raiseSkillModifier);
                zdo.Set("MWL_Shrine_FreeTag", hasBeenUsedOnce);
                
                zdo.Set("MWL_Shrine_Biome", biome.ToString());
                
                Debug.Log($"Shrine: Set random config '{shrineConfig.internalName}' to ZDO");
                Debug.Log($"Shrine Values - Duration: {shrineConfig.duration}, HealthRegen: {shrineConfig.healthRegenMultiplier}, StaminaRegen: {shrineConfig.staminaRegenMultiplier}, EitrRegen: {shrineConfig.eitrRegenMultiplier}, RaiseSkill: {shrineConfig.raiseSkillModifier}");
            }
        }
        
        string configName = zdo.GetString(ShrineConfigKey, "");
        shrineConfig = ShrineDB.GetShrineConfig(configName);
        
        shrineConfig.statusEffect.m_ttl = zdo.GetInt("MWL_Shrine_Duration", 900);
        if (shrineConfig.statusEffect is SE_Stats stats)
        {
            stats.m_healthRegenMultiplier += zdo.GetFloat("MWL_Shrine_HealthRegenMult", 0f);
            stats.m_staminaRegenMultiplier += zdo.GetFloat("MWL_Shrine_StaminaRegenMult", 0f);
            stats.m_eitrRegenMultiplier += zdo.GetFloat("MWL_Shrine_EitrRegenMult", 0f);
            stats.m_raiseSkillModifier += zdo.GetFloat("MWL_Shrine_RaiseSkillMod", 0f);
        }
        
        string biomeString = zdo.GetString("MWL_Shrine_Biome", "");
        Enum.TryParse<Heightmap.Biome>(biomeString, out Heightmap.Biome parsedBiome);
        biome = parsedBiome;
        
        hasBeenUsedOnce = zdo.GetBool("MWL_Shrine_FreeTag");
        
        Debug.Log("Shrine Awake: Loaded shrine config '" + shrineConfig.internalName + "'");
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (IsOnCooldown())
        {
            if (user is Player player)
            {
                player.Message(MessageHud.MessageType.Center, "Shrine can only be used once per day. Try again tomorrow!");
            }
            return false;
        }
        
        if (CheckUserInventory(user))
        {
            Debug.Log("User has the requirements, adding status effect");
            user.GetSEMan().AddStatusEffect(shrineConfig.statusEffect);
            List<Character> characters = Character.GetAllCharacters();
            foreach (Character character in characters)
            {
                character.GetSEMan().AddStatusEffect(shrineConfig.statusEffect);
            }
            
            SetCooldown();

            TryTriggerRaidEvent(user);
            return true;
        }

        return false;
    }
    
    private bool IsOnCooldown()
    {
        if (!hasBeenUsedOnce)
            return false; // First use is always free
            
        ZDO zdo = znetView.GetZDO();
        int lastUsedDay = zdo.GetInt(ShrineLastUsedDayKey, -1);
        
        if (lastUsedDay == -1)
            return false;
            
        int currentDay = EnvMan.instance.GetDay();
        
        return lastUsedDay >= currentDay;
    }
    
    private void SetCooldown()
    {
        if (znetView.IsOwner())
        {
            ZDO zdo = znetView.GetZDO();
            int currentDay = EnvMan.instance.GetDay();
            zdo.Set(ShrineLastUsedDayKey, currentDay);
            
            if (!hasBeenUsedOnce)
            {
                hasBeenUsedOnce = true;
                zdo.Set("MWL_Shrine_FreeTag", hasBeenUsedOnce);
            }
            
            Debug.Log($"Shrine: Set cooldown for day {currentDay}");
        }
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        return true;
    }

    public string GetHoverText()
    {
        ShrineOffering offering = shrineConfig.shrineOffering;
        
        string hoverText;
        string itemName = offering.offeringItem.m_shared.m_name;
        int quantity = offering.itemQuantity;
        string effectDetails = "";
        if (shrineConfig.statusEffect is SE_Stats stats)
        {
            effectDetails = ShrineUtils.GetEffectDescription(stats);
            float durationMinutes = stats.m_ttl / 60f;
            string durationText = durationMinutes > 0f ? $"Duration: <color=orange>{durationMinutes:0.#} min</color>\n" : "";
            
            if (IsOnCooldown())
            {
                hoverText = Localization.instance.Localize(
                    $"{shrineConfig.displayName}\n" +
                    $"<color=red>Can only be used once per day</color>\n"+
                    $"[<color=yellow><b>$KEY_Use</b></color>] Sacrifice {quantity}x <color=orange>{itemName}</color>\n"+
                    $"{durationText}" +
                    $"{effectDetails}");
            }
            else
            {
                hoverText = Localization.instance.Localize(
                    $"{shrineConfig.displayName}\n" +
                    $"[<color=yellow><b>$KEY_Use</b></color>] Sacrifice {quantity}x <color=orange>{itemName}</color>\n" +
                    $"{durationText}" +
                    $"{effectDetails}");
            }
            return hoverText;
        }

        hoverText = Localization.instance.Localize(
            $"{shrineConfig.displayName}\n" +
            $"[<color=yellow><b>$KEY_Use</b></color>] Sacrifice {quantity}x <color=orange>{itemName}</color>\n");

        return hoverText;
    }
    
    public void TryTriggerRaidEvent(Humanoid user)
    {
        float raidChance = 0.5f;
        
        if (!ZNet.instance.IsServer())
        {
            Debug.Log("Shrine: Not server, cannot trigger raid event");
            return;
        }
    
        if (UnityEngine.Random.value <= raidChance)
        {
            Debug.Log("Shrine: Triggering raid event!");
            
            RandomEvent raidEvent = GetBiomeSpecificRaidEvent(biome);
        
            if (raidEvent != null)
            {
                RPCUtils.SendTriggerRaidEvent(raidEvent.m_name, transform.position);
            }
        }
    }
    
    private RandomEvent GetBiomeSpecificRaidEvent(Heightmap.Biome targetBiome)
    {
        string[] eventNames = targetBiome switch
        {
            Heightmap.Biome.Meadows => new[] { "skeletons", "eikthyr" },
            Heightmap.Biome.BlackForest => new[] { "skeletons", "foresttrolls" },
            Heightmap.Biome.Swamp => new[] { "skeletons", "blobs" },
            Heightmap.Biome.Mountain => new[] { "wolves", "army_moder" },
            Heightmap.Biome.Plains => new[] { "army_goblin", "army_moder" },
            Heightmap.Biome.Mistlands => new[] { "army_seekers" },
            _ => new[] { "skeletons" }
        };
    
        string eventName = eventNames[UnityEngine.Random.Range(0, eventNames.Length)];
        return RandEventSystem.instance.m_events.Find(e => e.m_name == eventName);
    }

    public string GetHoverName()
    {
        return Localization.instance.Localize(this.name);
    }

    public bool CheckUserInventory(Humanoid user)
    {
        if (!hasBeenUsedOnce)
        {
            return true;   
        }
        
        bool hasRequiredItems = false;
        foreach (var item in user.GetInventory().GetAllItems())
        {
            Debug.Log(item.m_shared.m_name);
        }
        
        ShrineOffering offering = shrineConfig.shrineOffering;
        
        Debug.Log(offering.itemQuantity + " " + offering.offeringItem.m_shared.m_name);
        
        ItemDrop.ItemData? match = user.GetInventory().GetAllItems()
            .FirstOrDefault(i => i.m_shared.m_name == offering.offeringItem.m_shared.m_name);

        if (match != null && user.GetInventory().RemoveItem(match, offering.itemQuantity))
        {
            user.ShowRemovedMessage(match, offering.itemQuantity);
            hasRequiredItems = true;
        }
        
        return hasRequiredItems;
    }

    public class ShrineOffering
    {
        public ItemDrop.ItemData offeringItem;

        public int itemQuantity;

        public ShrineOffering(string itemName, int quantity)
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(itemName);
            if (prefab == null)
            {
                Debug.LogError($"ShrineOffering: prefab '{itemName}' not found");
                return;
            }

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Debug.LogError($"ShrineOffering: prefab '{itemName}' has no ItemDrop component");
                return;
            }

            offeringItem = drop.m_itemData;

            itemQuantity = quantity;
        }
    }
}