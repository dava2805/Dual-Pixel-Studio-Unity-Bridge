# PixelSync Link for Unity & Godot

Stop emailing yourself sprite sheets. Stop breaking your workflow.

The **PixelSync Link** is a lightweight, single-script bridge that connects your game engine directly to the **PixelSync** app on your mobile device over your local Wi-Fi network. 

Every pixel you draw and frame you animate updates instantly inside your game scene. No cables, no manual exporting, no hassle.

<div align="center">
  <h3>Live Demo</h3>
  
  <video src="https://github.com/user-attachments/assets/5a0e99d6-9484-40ef-af51-9adccd694ef4" width="80%" controls="controls" muted="muted" autoplay="autoplay"></video>

  <br><br>
  <em>Draw on your mobile device, see it instantly in your game.</em>
</div>


## Features

* **Zero Dependencies:** It's just one script. No DLLs, no bloated packages, no complicated setup wizards.
* **Multi-Engine Support:** Built specifically for **Unity (C#)** and **Godot 4 (GDScript)**.
* **Live Preview:** Auto-refresh your current sprite directly in the Editor while you draw.
* **Auto-Discovery:** Uses UDP multicast to automatically find your iPad/iPhone on the network. No IP typing required!
* **Engine-Ready Imports:** Automatically slices and configures imported spritesheets with perfect pixel art settings (Point Filtering, correct pixels-per-unit).

## Quick Start Guide

### 1. Get the App (It's Free)
Download **PixelSync** on your App Store / Google Play Store. It's a professional-grade pixel art editor built specifically for indie game devs. 


### 2. Connect & Start the Server
1. In the **PixelSync app**, open your project and tap the **Engine Sync (Gamepad Icon)** in the toolbar of a canvas to start the local server.

---

### 3A. Unity Setup
1. Download the `PixelSync.cs` file from this repository.
2. Drag and drop the script into your Unity project. 
   *(Important: It must be placed inside an `Editor` folder, e.g., `Assets/Editor/PixelSync.cs`)*
3. In Unity, open the bridge window by navigating to: `Tools > PixelSync Link`.
4. Your device should appear automatically under "Available Devices." Click it to connect!

### 3B. Godot Setup
1. Download the `PixelSync.gd` file from the `Godot` folder in this repository.
2. Drag it into your Godot project.
3. Select a `Sprite2D` or `TextureRect` node in your scene and attach the script to it.
4. Run your game—the script will automatically discover your device and fetch the live texture!

---

## How It Works (The Technical Details)

The Engine Bridge works by communicating with a lightweight HTTP server hosted locally within the PixelSync app on your mobile device.

### Supported Endpoints:
The bridge pulls data seamlessly via the following local endpoints:
* `/api/info`: Fetches project metadata (width, height, FPS, frame count).
* `/api/sprite.png`: Fetches the current frame (flattened).
* `/api/sheet.png`: Fetches a full horizontal spritesheet.
* `/api/atlas.json`: Fetches atlas metadata to automatically slice the spritesheet.

### Network Requirements:
* Your mobile device and your PC/Mac must be on the **same Wi-Fi network**.
* The app uses **Port 8642** (HTTP) for data transfer and **Port 8644** (UDP) for auto-discovery. Ensure these ports are not blocked by your firewall.
* If auto-discovery fails, you can manually enter your device's IP address (displayed in the app) into the Engine window.
