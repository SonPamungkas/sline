using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Rewired;
using InputFramework;

namespace SLine
{
    [BepInPlugin("com.sline", "SLine Target Mod", "1.5")]
    public class SLineMod : BaseUnityPlugin
    {
        public static ConfigEntry<bool> GlobalToggle;
        public static ConfigEntry<float> LineThickness;
        public static ConfigEntry<bool> ShowFriendlyOnly;

        public static ConfigEntry<bool> AircraftHold;
        public static bool AircraftToggled = false;

        public static ConfigEntry<bool> GroundHold;
        public static bool GroundToggled = false;

        public static ConfigEntry<bool> ShipHold;
        public static bool ShipToggled = false;

        public static ConfigEntry<bool> MissileHold;
        public static bool MissileToggled = false;

        public static ConfigEntry<bool> CruiseMissileHold;
        public static bool CruiseMissileToggled = false;

        public static Dictionary<string, ConfigEntry<bool>> UnitWhitelists = new Dictionary<string, ConfigEntry<bool>>();

        public static SLineMod Instance;
        
        public static bool MapExists;

        private void Awake()
        {
            Instance = this;

            GlobalToggle = Config.Bind("1. Global Settings", "Global Toggle", true, "Master switch to show/hide lines by default.");
            LineThickness = Config.Bind("1. Global Settings", "Line Thickness", 0.1f, "Thickness of the lines drawn on the map.");
            ShowFriendlyOnly = Config.Bind("1. Global Settings", "Show Friendly Only", false, "Only show lines belonging to friendly units.");

            AircraftHold = Config.Bind("2. Keybinds", "Aircraft Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            GroundHold = Config.Bind("2. Keybinds", "Ground Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            ShipHold = Config.Bind("2. Keybinds", "Ship Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            MissileHold = Config.Bind("2. Keybinds", "Missile Lines Hold Mode", false, "If true, key must be held instead of toggled.");
            CruiseMissileHold = Config.Bind("2. Keybinds", "Cruise Missile Lines Hold Mode", false, "If true, key must be held instead of toggled.");

            // Register custom input actions via in-game controls system
            ExtraInputManager.LoadPendingActions();
            ExtraInputManager.RegisterAction("ToggleAircraftLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleGroundLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleShipLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleMissileLines", Rewired.InputActionType.Button, "Debug");
            ExtraInputManager.RegisterAction("ToggleCruiseMissileLines", Rewired.InputActionType.Button, "Debug");

            var harmony = new Harmony("com.sline");
            harmony.PatchAll();
            StartCoroutine(ScanRoutine());
            Logger.LogInfo("SLine Mod Initialized with extra Rewired keybinding system");
        }

        private IEnumerator ScanRoutine()
        {
            // Wait 5 seconds on startup for the game to populate definition assets in memory
            yield return new WaitForSeconds(5f);
            ScanAllUnitDefinitions();

            float lastDefScan = Time.time;
            while (true)
            {
                yield return new WaitForSeconds(5f);
                // Re-scan definitions periodically to catch dynamically loaded mod units
                if (Time.time - lastDefScan > 30f)
                {
                    ScanAllUnitDefinitions();
                    lastDefScan = Time.time;
                }
            }
        }

        private void ScanAllUnitDefinitions()
        {
            var defs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
            foreach (var def in defs)
            {
                if (def == null) continue;
                
                string unitName = def.unitName;
                if (string.IsNullOrEmpty(unitName)) unitName = def.name;
                if (string.IsNullOrEmpty(unitName)) continue;

                string category;
                if (def is AircraftDefinition) category = "Aircraft";
                else if (def is ShipDefinition) category = "Ship";
                else if (def is MissileDefinition) category = "Missile";
                else category = "Ground";

                unitName = SLineMod.SanitizeConfigKey(unitName);
                
                GetOrAddWhitelist(category, unitName);
            }
            Logger.LogInfo($"Pre-scanned {UnitWhitelists.Count} unit definitions into whitelist.");
        }

        private void Update()
        {
            if (!MapExists) return;

            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch {}
            if (inChat) return;

            Rewired.Player localPlayer = ReInput.players.GetPlayer(0);
            if (localPlayer == null) return;

            // Aircraft
            if (AircraftHold.Value)
            {
                AircraftToggled = localPlayer.GetButton("ToggleAircraftLines");
            }
            else if (localPlayer.GetButtonDown("ToggleAircraftLines"))
            {
                AircraftToggled = !AircraftToggled;
            }

            // Ground
            if (GroundHold.Value)
            {
                GroundToggled = localPlayer.GetButton("ToggleGroundLines");
            }
            else if (localPlayer.GetButtonDown("ToggleGroundLines"))
            {
                GroundToggled = !GroundToggled;
            }

            // Ship
            if (ShipHold.Value)
            {
                ShipToggled = localPlayer.GetButton("ToggleShipLines");
            }
            else if (localPlayer.GetButtonDown("ToggleShipLines"))
            {
                ShipToggled = !ShipToggled;
            }

            // Missile
            if (MissileHold.Value)
            {
                MissileToggled = localPlayer.GetButton("ToggleMissileLines");
            }
            else if (localPlayer.GetButtonDown("ToggleMissileLines"))
            {
                MissileToggled = !MissileToggled;
            }

            // Cruise Missile
            if (CruiseMissileHold.Value)
            {
                CruiseMissileToggled = localPlayer.GetButton("ToggleCruiseMissileLines");
            }
            else if (localPlayer.GetButtonDown("ToggleCruiseMissileLines"))
            {
                CruiseMissileToggled = !CruiseMissileToggled;
            }
        }

        public static string SanitizeConfigKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            return s.Replace("=", "").Replace("\n", "").Replace("\t", "").Replace("\\", "")
                    .Replace("\"", "").Replace("'", "").Replace("[", "(").Replace("]", ")").Trim();
        }

        public ConfigEntry<bool> GetOrAddWhitelist(string category, string unitName)
        {
            string safeCategory = SanitizeConfigKey(category);
            string safeUnitName = SanitizeConfigKey(unitName);
            string key = $"{safeCategory}_{safeUnitName}";
            if (!UnitWhitelists.TryGetValue(key, out var entry))
            {
                entry = Config.Bind($"3. Whitelist: {safeCategory}", safeUnitName, true, $"Enable SLine originating from {safeUnitName}");
                UnitWhitelists[key] = entry;
            }
            return entry;
        }
    }

    [HarmonyPatch(typeof(DynamicMap), "Update")]
    public class DynamicMap_Update_Patch
    {
        private static Dictionary<UnitMapIcon, GameObject> lines = new Dictionary<UnitMapIcon, GameObject>();

        private static FieldInfo missileTargetField = typeof(Missile).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(DynamicMap __instance)
        {
            try
            {
                var icons = __instance.mapIcons;
                if (icons == null) return;

                HashSet<UnitMapIcon> updatedIcons = new HashSet<UnitMapIcon>();

                foreach (var baseIcon in icons)
                {
                    var icon = baseIcon as UnitMapIcon;
                    if (icon == null || icon.unit == null || !icon.gameObject.activeInHierarchy) continue;

                    if (SLineMod.ShowFriendlyOnly.Value && !GameManager.IsLocalHQ(icon.unit.NetworkHQ))
                    {
                        HideLine(icon, lines);
                        continue;
                    }

                    string category;
                    bool categoryToggled = false;
                    if (icon.unit is Aircraft) {
                        category = "Aircraft";
                        categoryToggled = SLineMod.AircraftToggled;
                    } else if (icon.unit is Ship) {
                        category = "Ship";
                        categoryToggled = SLineMod.ShipToggled;
                    } else if (icon.unit is Missile) {
                        var missileType = icon.unit as Missile;
                        var seeker = missileType.GetComponent<MissileSeeker>();
                        string seekerType = seeker.GetSeekerType();
                        if (seekerType == "INS / Opt.")
                        {
                            category = "CruiseMissile";
                            categoryToggled = SLineMod.CruiseMissileToggled;
                        } else
                        {
                            category = "Missile";
                            categoryToggled = SLineMod.MissileToggled;
                        }
                        

                    } else {
                        category = "Ground";
                        categoryToggled = SLineMod.GroundToggled;
                    }

                    bool globalShow = SLineMod.GlobalToggle.Value;
                    bool finalShow = globalShow ^ categoryToggled;

                    if (!finalShow) {
                        HideLine(icon, lines);
                        continue;
                    }

                    string unitName = icon.unit.unitName;
                    if (string.IsNullOrEmpty(unitName)) unitName = icon.unit.gameObject.name.Replace("(Clone)", "").Trim();
                    
                    unitName = SLineMod.SanitizeConfigKey(unitName);

                    var whitelistEntry = SLineMod.Instance.GetOrAddWhitelist(category, unitName);
                    if (!whitelistEntry.Value) {
                        HideLine(icon, lines);
                        continue;
                    }

                    Unit target = null;
                    if (icon.unit is Missile missile)
                    {
                        if (missileTargetField != null)
                            target = (Unit)missileTargetField.GetValue(missile);
                    }
                    else if (icon.unit is Aircraft aircraft && aircraft.weaponManager != null)
                    {
                        var targets = aircraft.weaponManager.GetTargetList();
                        if (targets != null && targets.Count > 0)
                        {
                            target = targets[0];
                        }
                    }

                    if (target != null)
                    {
                        UnitMapIcon targetIcon = null;
                        if (DynamicMap.TryGetMapIcon(target, out targetIcon) && targetIcon != null && targetIcon.gameObject.activeInHierarchy)
                        {
                            UpdateLine(icon, targetIcon, lines, target);
                            updatedIcons.Add(icon);
                            continue;
                        }
                    }
                    
                    HideLine(icon, lines);
                }

                List<UnitMapIcon> toRemove = new List<UnitMapIcon>();
                foreach (var pair in lines)
                {
                    if (!updatedIcons.Contains(pair.Key))
                    {
                        if (pair.Value != null) pair.Value.SetActive(false);
                        if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy) toRemove.Add(pair.Key);
                    }
                }
                foreach (var icon in toRemove) lines.Remove(icon);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SLine Mod] Error in DynamicMap_Update_Patch: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void UpdateLine(UnitMapIcon strikerIcon, UnitMapIcon targetIcon, Dictionary<UnitMapIcon, GameObject> lines, Unit target)
        {
            if (!lines.TryGetValue(strikerIcon, out var lineObj) || lineObj == null)
            {
                lineObj = CreateLine(strikerIcon.transform.parent);
                lines[strikerIcon] = lineObj;
            }

            lineObj.SetActive(true);
            var img = lineObj.GetComponent<Image>();
            
            if (target is Aircraft)
            {
                if (strikerIcon.unit is Missile)
                {
                    img.color = new Color(0f, 1f, 1f, 0.8f); // Cyan
                }
                else
                {
                    img.color = new Color(1f, 1f, 1f, 0.8f); // White
                }
            }
            else if (strikerIcon.unit is Aircraft)
            {
                img.color = new Color(1f, 0f, 1f, 0.8f); // Magenta
            }
            else if (target is Missile)
            {
                img.color = new Color(0f, 1f, 1f, 0.8f); // Cyan
            }
            else if (target is Ship)
            {
                img.color = new Color(1f, 0f, 0f, 0.8f); // Red
            }
            else 
            {
                img.color = new Color(1f, 1f, 0f, 0.8f); // Yellow (Ground)
            }

            var rect = lineObj.GetComponent<RectTransform>();
            
            // Positions are in local space of the icon layer
            Vector3 startPos = strikerIcon.transform.localPosition;
            Vector3 endPos = targetIcon.transform.localPosition;
            
            Vector3 diff = endPos - startPos;
            float distance = diff.magnitude;
            
            if (distance < 5f)
            {
                lineObj.SetActive(false);
                return;
            }

            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            rect.localPosition = startPos;
            rect.localRotation = Quaternion.Euler(0, 0, angle);
            rect.sizeDelta = new Vector2(distance, SLineMod.LineThickness.Value);
        }

        private static void HideLine(UnitMapIcon strikerIcon, Dictionary<UnitMapIcon, GameObject> lines)
        {
            if (lines.TryGetValue(strikerIcon, out var lineObj) && lineObj != null)
            {
                lineObj.SetActive(false);
            }
        }

        public static void ExternalCleanup(UnitMapIcon icon)
        {
            if (lines.TryGetValue(icon, out var lineObj) && lineObj != null)
            {
                Object.Destroy(lineObj);
            }
            lines.Remove(icon);
        }

        private static GameObject CreateLine(Transform parent)
        {
            var go = new GameObject("StrikerTargetLine");
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling(); 
            
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0f, 0f, 0.8f);
            img.raycastTarget = false; 
            
            var rect = go.GetComponent<RectTransform>();
            // Use center anchor so that localPosition matches the Map icons' localPosition
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            
            return go;
        }
    }

    [HarmonyPatch(typeof(UnitMapIcon), "OnRemoveIcon")]
    public class UnitMapIcon_OnRemoveIcon_Patch
    {
        public static void Prefix(UnitMapIcon __instance)
        {
            DynamicMap_Update_Patch.ExternalCleanup(__instance);
        }
    }
    
    [HarmonyPatch]
    public class DynamicMapPatches
    {
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.OnEnable))]
        [HarmonyPostfix]
        private static void OnMapEnablePostfix()
        {
            SLineMod.MapExists = true;
        }
        
        [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.OnDestroy))]
        [HarmonyPostfix]
        private static void OnMapDestroyPostfix()
        {
            SLineMod.MapExists = false;
        }
    }
}
