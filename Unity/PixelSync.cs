// PixelSync.cs
// Place this file in your Unity project under: Assets/Editor/PixelSync.cs
//
// Opens via Unity menu: Tools > PixelSync Link
// Connects to the PixelSync Flutter app over your local network
// and imports sprites/spritesheets directly into your Unity project.

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace PixelSyncBridge
{
    [Serializable]
    public class ProjectInfo
    {
        public string name;
        public int width;
        public int height;
        public int frameCount;
        public int layerCount;
        public int fps;
        public bool isTransparent;
    }

    [Serializable]
    public class AtlasInfo
    {
        public string projectName;
        public int sheetWidth;
        public int sheetHeight;
        public int frameWidth;
        public int frameHeight;
        public int frameCount;
        public int fps;
        public int pixelsPerUnit;
        public SpriteEntry[] sprites;
    }

    [Serializable]
    public class SpriteEntry
    {
        public string name;
        public int x, y, width, height;
        public PivotPoint pivot;
    }

    [Serializable]
    public class PivotPoint
    {
        public float x, y;
    }

    public class PixelSync : EditorWindow
    {
        private string ipAddress = "192.168.1.100";
        private int port = 8642;
        private string importPath = "Assets/Sprites/PixelSync";
        private bool autoRefresh = false;
        private float refreshInterval = 2f;
        private double lastRefreshTime;

        private UdpClient udpListener;

        private ProjectInfo cachedInfo;
        private Texture2D previewTexture;
        private string statusMessage = "Not connected";
        private bool isConnected = false;
        private Vector2 scrollPos;

        // --- UDP Discovery Data ---
        private class DiscoveredDevice 
        {
            public string ip;
            public int port;
            public string projectName;
            public double lastSeen;
        }
        private Dictionary<string, DiscoveredDevice> discoveredDevices = new Dictionary<string, DiscoveredDevice>();
        // --------------------------

        [MenuItem("Tools/PixelSync Link")]
        public static void ShowWindow()
        {
            var win = GetWindow<PixelSync>("PixelSync Link");
            win.minSize = new Vector2(320, 520);
        }

        private void OnEnable()
        {
            // Load saved prefs
            ipAddress = EditorPrefs.GetString("PixelSync_IP", "192.168.1.100");
            port = EditorPrefs.GetInt("PixelSync_Port", 8642);
            importPath = EditorPrefs.GetString("PixelSync_Path", "Assets/Sprites/PixelSync");

            StartUDPListener();
        }

        private void OnDisable()
        {
            EditorPrefs.SetString("PixelSync_IP", ipAddress);
            EditorPrefs.SetInt("PixelSync_Port", port);
            EditorPrefs.SetString("PixelSync_Path", importPath);

            udpListener?.Close();
            udpListener = null;
        }

        private void Update()
        {
            if (autoRefresh && isConnected && EditorApplication.timeSinceStartup - lastRefreshTime > refreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                FetchPreview();
                Repaint();
            }
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Header
            GUILayout.Space(8);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            EditorGUILayout.LabelField("🎮 PixelSync Link", headerStyle);
            GUILayout.Space(4);

            // Available Devices (UDP Discovery)
            if (!isConnected && discoveredDevices.Count > 0)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Available Devices on Network", EditorStyles.boldLabel);
                
                List<string> staleKeys = new List<string>();
                
                foreach (var kvp in discoveredDevices)
                {
                    var dev = kvp.Value;
                    // If we haven't seen a broadcast in 6 seconds, mark as stale
                    if (EditorApplication.timeSinceStartup - dev.lastSeen > 6.0)
                    {
                        staleKeys.Add(kvp.Key);
                        continue;
                    }
                    
                    if (GUILayout.Button($"📱 Connect to: {dev.projectName}\n(IP: {dev.ip}:{dev.port})", GUILayout.Height(38)))
                    {
                        ipAddress = dev.ip;
                        port = dev.port;
                        Connect();
                        GUIUtility.ExitGUI();
                    }
                }
                
                // Clean up stale devices so disconnected phones disappear
                foreach (var k in staleKeys) discoveredDevices.Remove(k);
                
                EditorGUILayout.EndVertical();
                GUILayout.Space(8);
            }

            // Connection
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Manual Connection", EditorStyles.boldLabel);
            ipAddress = EditorGUILayout.TextField("Device IP", ipAddress);
            port = EditorGUILayout.IntField("Port", port);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isConnected ? "✅ Connected – Refresh" : "Connect", GUILayout.Height(28)))
            {
                Connect();
                GUIUtility.ExitGUI();
            }
            if (isConnected && GUILayout.Button("Disconnect", GUILayout.Width(80), GUILayout.Height(28)))
            {
                isConnected = false;
                cachedInfo = null;
                previewTexture = null;
                statusMessage = "Disconnected";
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(statusMessage, isConnected ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // Project info
            if (cachedInfo != null)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Project Info", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Name", cachedInfo.name);
                EditorGUILayout.LabelField("Size", $"{cachedInfo.width} × {cachedInfo.height}");
                EditorGUILayout.LabelField("Frames", $"{cachedInfo.frameCount} @ {cachedInfo.fps}fps");
                EditorGUILayout.LabelField("Layers", cachedInfo.layerCount.ToString());
                EditorGUILayout.EndVertical();

                GUILayout.Space(8);

                // Preview
                if (previewTexture != null)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                    float previewSize = Mathf.Min(position.width - 40, 256);
                    var rect = GUILayoutUtility.GetRect(previewSize, previewSize);
                    EditorGUI.DrawTextureTransparent(rect, previewTexture, ScaleMode.ScaleToFit);
                    EditorGUILayout.EndVertical();
                }

                GUILayout.Space(8);

                // Import options
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
                importPath = EditorGUILayout.TextField("Save to", importPath);

                GUILayout.Space(4);

                if (GUILayout.Button("📥 Import Current Frame", GUILayout.Height(30)))
                {
                    ImportCurrentFrame();
                }

                if (cachedInfo.frameCount > 1)
                {
                    if (GUILayout.Button("🎞️ Import Spritesheet", GUILayout.Height(30)))
                    {
                        ImportSpritesheet();
                    }

                    if (GUILayout.Button("📁 Import All Frames (Individual PNGs)", GUILayout.Height(30)))
                    {
                        ImportAllFrames();
                    }
                }

                GUILayout.Space(4);

                autoRefresh = EditorGUILayout.Toggle("Auto-refresh preview", autoRefresh);
                if (autoRefresh)
                {
                    refreshInterval = EditorGUILayout.Slider("Interval (sec)", refreshInterval, 0.5f, 10f);
                }

                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(8);

            // Help
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("How to use", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. Open a project in PixelSync on your device\n" +
                "2. Tap the Unity Link button (gamepad icon) in the tool bar\n" +
                "3. Click your device in the list above, or enter the IP manually\n" +
                "4. Import sprites directly into your Unity project!",
                MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        // ── Network ──

        private string BaseURL => $"http://{ipAddress}:{port}";

        private void Connect()
        {
            try
            {
                string json = FetchString("/api/info");
                cachedInfo = JsonUtility.FromJson<ProjectInfo>(json);
                isConnected = true;
                statusMessage = $"Connected to {cachedInfo.name} ({cachedInfo.width}×{cachedInfo.height})";
                FetchPreview();
            }
            catch (Exception ex)
            {
                isConnected = false;
                statusMessage = $"Connection failed: {ex.Message}";
            }
        }

        private void FetchPreview()
        {
            try
            {
                byte[] pngData = FetchBytes("/api/sprite.png");
                if (previewTexture == null)
                    previewTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                previewTexture.filterMode = FilterMode.Point; // pixel art!
                previewTexture.LoadImage(pngData);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PixelSync: Preview fetch failed: {ex.Message}");
            }
        }

        // ── Import ──

        private void ImportCurrentFrame()
        {
            try
            {
                EnsureDirectory();
                byte[] png = FetchBytes("/api/sprite.png");
                string filename = $"{SanitizeName(cachedInfo.name)}.png";
                string fullPath = Path.Combine(importPath, filename);
                File.WriteAllBytes(fullPath, png);
                AssetDatabase.Refresh();
                ConfigureTextureImporter(fullPath);
                statusMessage = $"Imported: {filename}";
                Debug.Log($"PixelSync: Imported {fullPath}");
            }
            catch (Exception ex)
            {
                statusMessage = $"Import failed: {ex.Message}";
                Debug.LogError($"PixelSync: {ex}");
            }
        }

        private void ImportSpritesheet()
        {
            try
            {
                EnsureDirectory();

                // Fetch spritesheet PNG
                byte[] png = FetchBytes("/api/sheet.png");
                string filename = $"{SanitizeName(cachedInfo.name)}_sheet.png";
                string fullPath = Path.Combine(importPath, filename);
                File.WriteAllBytes(fullPath, png);

                // Fetch atlas metadata
                string atlasJson = FetchString("/api/atlas.json");
                AtlasInfo atlas = JsonUtility.FromJson<AtlasInfo>(atlasJson);

                AssetDatabase.Refresh();

                // Configure texture as sprite with multiple mode
                TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.spritePixelsPerUnit = atlas.pixelsPerUnit;

                    // Set up sprite rects from atlas
                    List<SpriteMetaData> metas = new List<SpriteMetaData>();
                    for (int i = 0; i < atlas.sprites.Length; i++)
                    {
                        var s = atlas.sprites[i];
                        SpriteMetaData meta = new SpriteMetaData();
                        meta.name = s.name;
                        // Unity Y is bottom-up, our atlas is top-down
                        meta.rect = new Rect(s.x, 0, s.width, s.height);
                        meta.alignment = (int)SpriteAlignment.Center;
                        meta.pivot = new Vector2(s.pivot.x, s.pivot.y);
                        metas.Add(meta);
                    }
                    importer.spritesheet = metas.ToArray();

                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }

                statusMessage = $"Imported spritesheet: {atlas.frameCount} frames";
                Debug.Log($"PixelSync: Imported spritesheet {fullPath} with {atlas.frameCount} frames");
            }
            catch (Exception ex)
            {
                statusMessage = $"Spritesheet import failed: {ex.Message}";
                Debug.LogError($"PixelSync: {ex}");
            }
        }

        private void ImportAllFrames()
        {
            try
            {
                EnsureDirectory();
                string baseName = SanitizeName(cachedInfo.name);
                int count = 0;

                for (int i = 0; i < cachedInfo.frameCount; i++)
                {
                    byte[] png = FetchBytes($"/api/frame/{i}.png");
                    string filename = $"{baseName}_frame_{i:D3}.png";
                    string fullPath = Path.Combine(importPath, filename);
                    File.WriteAllBytes(fullPath, png);
                    count++;
                }

                AssetDatabase.Refresh();

                // Configure all imported textures
                for (int i = 0; i < count; i++)
                {
                    string filename = $"{baseName}_frame_{i:D3}.png";
                    string fullPath = Path.Combine(importPath, filename);
                    ConfigureTextureImporter(fullPath);
                }

                statusMessage = $"Imported {count} individual frames";
                Debug.Log($"PixelSync: Imported {count} frames to {importPath}");
            }
            catch (Exception ex)
            {
                statusMessage = $"Frame import failed: {ex.Message}";
                Debug.LogError($"PixelSync: {ex}");
            }
        }

        // ── Helpers ──

        private string FetchString(string endpoint)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Cache-Control", "no-cache");
                return client.DownloadString(BaseURL + endpoint);
            }
        }

        private byte[] FetchBytes(string endpoint)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Cache-Control", "no-cache");
                return client.DownloadData(BaseURL + endpoint);
            }
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(importPath))
            {
                Directory.CreateDirectory(importPath);
                AssetDatabase.Refresh();
            }
        }

        private void ConfigureTextureImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = Mathf.Clamp(cachedInfo.width, 8, 64);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        private string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    
        // ── Auto-Discovery (UDP) ──

        private void StartUDPListener()
        {
            try
            {
                udpListener = new UdpClient();
                udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, 8644));
                
                try { udpListener.JoinMulticastGroup(IPAddress.Parse("224.0.2.60")); } catch { }

                udpListener.BeginReceive(new AsyncCallback(OnUdpDataReceived), null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("PixelSync Auto-Discovery failed to bind. Port 8644 might be in use: " + e.Message);
            }
        }

        private void OnUdpDataReceived(IAsyncResult result)
        {
            if (udpListener == null) return;

            try
            {
                IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 8644);
                byte[] bytes = udpListener.EndReceive(result, ref groupEP);
                string message = Encoding.UTF8.GetString(bytes);

                // Parse the message: PIXELSYNC_UNITY:IP:PORT:ProjectName
                if (message.StartsWith("PIXELSYNC_UNITY:"))
                {
                    string[] parts = message.Split(':');
                    if (parts.Length >= 4)
                    {
                        string newIp = parts[1];
                        if (int.TryParse(parts[2], out int newPort))
                        {
                            string projName = parts[3];
                            string key = $"{newIp}:{newPort}";
                            
                            // Safely route back to main thread for UI updates
                            EditorApplication.delayCall += () => {
                                if (!discoveredDevices.ContainsKey(key)) {
                                    discoveredDevices[key] = new DiscoveredDevice();
                                }
                                discoveredDevices[key].ip = newIp;
                                discoveredDevices[key].port = newPort;
                                discoveredDevices[key].projectName = projName;
                                discoveredDevices[key].lastSeen = EditorApplication.timeSinceStartup;
                                Repaint();
                            };
                        }
                    }
                }

                // Continue listening
                udpListener.BeginReceive(new AsyncCallback(OnUdpDataReceived), null);
            }
            catch (ObjectDisposedException) { /* Handled gracefully on close */ }
            catch (Exception e) { Debug.LogError("UDP Receive error: " + e.Message); }
        }
    }
}
#endif
