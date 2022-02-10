// LocationMap
// Show custom locations added by JVL on the minimap
// 
// File:    LocationMap.cs
// Project: LocationMap

using BepInEx;
using Jotunn;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Utils;
using UnityEngine;

namespace LocationMap
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class LocationMap : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.LocationMap";
        public const string PluginName = "LocationMap";
        public const string PluginVersion = "0.0.1";

        private CustomRPC LocationsRPC;
        private ConfigEntry<bool> IgnoreFogConfig;

        private void Awake()
        {
            LocationsRPC = NetworkManager.Instance.AddRPC("locations", OnServerReceive, OnClientReceive);
            MinimapManager.OnVanillaMapDataLoaded += OnVanillaMapDataLoaded;
            IgnoreFogConfig = Config.Bind("General", "Ignore Fog", true,
                "If true, location icons are always visible regardless of exploration status. Set to false to hide the locations beneath the fog.");
        }

        private void OnVanillaMapDataLoaded()
        {
            Jotunn.Logger.LogDebug("Map loaded, querying location data");
            if (ZNet.instance.IsClientInstance())
            {
                LocationsRPC.Initiate();
            }
            else
            {
                CreateLocationOverlay(CreateLocationPackage());
            }
        }

        private ZPackage CreateLocationPackage()
        {
            var pkg = new ZPackage();
            var locations = ZoneSystem.instance.GetLocationList()
                .Where(x => CustomLocation.IsCustomLocation(x.m_location.m_prefabName))
                .OrderByDescending(x => x.m_position.z)
                .ThenBy(x => x.m_position.x)
                .ToList();

            Jotunn.Logger.LogDebug($"Found {locations.Count} custom location instances");
            pkg.Write(locations.Count);
            foreach (var location in locations)
            {
                pkg.Write(location.m_location.m_hash);
                pkg.Write(location.m_position);
            }
            pkg.SetPos(0);
            return pkg;
        }

        private void CreateLocationOverlay(ZPackage pkg)
        {
            int cnt = pkg.ReadInt();
            if (cnt == 0)
            {
                return;
            }
            
            var overlays = new Dictionary<int, MinimapManager.MapOverlay>();
            var textures = new Dictionary<int, Texture2D>();
            for (int i = 0; i < cnt; i++)
            {
                var hash = pkg.ReadInt();
                var pos = pkg.ReadVector3();

                try
                {
                    if (!ZoneSystem.instance.m_locationsByHash.TryGetValue(hash, out var location))
                    {
                        Jotunn.Logger.LogWarning($"Location hash {hash} not found");
                        continue;
                    }

                    var custom = ZoneManager.Instance.GetCustomLocation(location.m_prefabName);
                    if (custom == null)
                    {
                        Jotunn.Logger.LogWarning($"Location {location.m_prefabName} is no custom location");
                        continue;
                    }

                    if (!overlays.TryGetValue(hash, out var overlay))
                    {
                        overlay = MinimapManager.Instance.GetMapOverlay(custom.SourceMod.Name, IgnoreFogConfig.Value);
                        overlay.Enabled = false;
                        overlays.Add(hash, overlay);
                    }

                    if (!textures.TryGetValue(hash, out var tex))
                    {
                        tex = RenderManager.Instance.Render(
                            new RenderManager.RenderRequest(location.m_prefab)
                            {
                                Width = 32,
                                Height = 32,
                                Rotation = RenderManager.IsometricRotation
                            }).texture;
                        textures.Add(hash, tex);
                    }

                    var mappos = MinimapManager.Instance.WorldToOverlayCoords(pos, overlay.TextureSize);
                    var pixels = tex.GetPixels();
                    int pixel = 0;
                    for (int y = 0; y < tex.height; y++)
                    {
                        for (int x = 0; x < tex.width; x++)
                        {
                            int posx = (int)mappos.x + x - tex.width / 2;
                            int posy = (int)mappos.y + y;
                            if (pixels[pixel].a > 0f)
                            {
                                overlay.OverlayTex.SetPixel(posx, posy, pixels[pixel]);
                            }
                            ++pixel;
                        }
                    }
                    overlay.OverlayTex.SetPixels((int)mappos.x - 1, (int)mappos.y - 1, 2, 2, new Color[4].Populate(Color.red));
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"Exception caught while adding location icon: {ex}");
                }
            }

            foreach (var mapOverlay in overlays.Values)
            {
                mapOverlay.OverlayTex.Apply();
            }
        }

        private IEnumerator OnServerReceive(long sender, ZPackage package)
        {
            LocationsRPC.SendPackage(sender, CreateLocationPackage());
            yield break;
        }

        private IEnumerator OnClientReceive(long sender, ZPackage package)
        {
            CreateLocationOverlay(package);
            yield break;
        }
    }
}

