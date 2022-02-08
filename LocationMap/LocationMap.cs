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
using UnityEngine;

namespace LocationMap
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    internal class LocationMap : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.LocationMap";
        public const string PluginName = "LocationMap";
        public const string PluginVersion = "0.0.1";

        private CustomRPC LocationsRPC;

        private void Awake()
        {
            MinimapManager.OnVanillaMapAvailable += OnVanillaMapAvailable;
        }

        private void OnVanillaMapAvailable()
        {
            Jotunn.Logger.LogInfo("Map loaded, querying location data");
            if (ZNet.instance.IsClientInstance())
            {
                LocationsRPC = NetworkManager.Instance.AddRPC("locations", OnServerReceive, OnClientReceive);
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

            var textures = new Dictionary<int, Texture2D>();
            var overlay = MinimapManager.Instance.GetMapOverlay("Custom Locations", true);
            overlay.Enabled = false;

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

                    if (!textures.TryGetValue(hash, out var tex))
                    {
                        tex = RenderManager.Instance.Render(
                            new RenderManager.RenderRequest(location.m_prefab)
                            {
                                Width = 32,
                                Height = 32,
                                Rotation = RenderManager.IsometricRotation
                            }).texture;
                        //ShaderHelper.ScaleTexture(tex, 32);
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

            overlay.OverlayTex.Apply();
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

