# VRC World Build Optimizer

A Unity Editor extension tool that automatically analyzes and optimizes VRChat world build size.

The recommended size for a VRChat world is **200 MB or less**. However, as assets such as textures, meshes, audio, reflection probes, and fonts accumulate, the build size can quickly balloon. This tool scans all assets in your project with a single click, automatically detects items that can be reduced, and applies optimizations in bulk. A backup is automatically created before any changes are applied, so you can revert with a single click if needed.

---

## Features

### ① Texture Optimization
Applies resolution limits, Crunch compression, Mipmap Streaming, and Read/Write disabling in bulk. Platform-specific import settings (Standalone, Android, etc.) can also be overwritten at the same time.

### ② Mesh Optimization
Applies Read/Write disabling and compression level settings in bulk.

### ③ Audio Optimization
Applies Vorbis / ADPCM compression, load type changes, and quality adjustments in bulk.

### ④ Reflection Probe Optimization
Reduces the maximum resolution of baked Cubemap EXR files in bulk. The EXR files themselves are also backed up, so they can be safely restored.

### ⑤ Font Optimization
Excludes TTF fonts from the build. Crunch compression is applied to the atlas textures of TextMeshPro SDF assets, enabling reductions of **90% or more** (character data is preserved, so no missing glyphs occur).

### ⑥ Backup & Restore
A backup is automatically created before any optimization is applied. Not only `.meta` files, but also asset body files such as SDF assets and reflection probe EXRs are protected.

### ⑦ Report Export
Analysis results, estimated size reductions, and a list of problematic assets can be saved to a `.txt` file.

---

## Tested Environment

- Unity 2022.3.22f1
- VRChat World SDK 3.10.3
- UdonSharp compatible

---

## Installation

1. Import the downloaded `VRCWorldBuildOptimizer.unitypackage` into your project.
2. Unity will automatically compile the scripts.
3. Launch from the Unity menu: **Tools > VRC World Build Optimizer**

---

## Basic Usage

**① Analyze the Entire Project**
Press the blue button on the Dashboard tab. All assets in the project will be scanned and optimizable items will be automatically detected. A summary is displayed once the analysis is complete.

**② Review Each Tab**
After analysis, each tab shows a list of target assets and the estimated size reduction. Use the **"Recommended Only"** button to automatically select only assets for which optimization is enabled.

**③ Apply**
Once you have confirmed the settings, apply them using the button on the Dashboard. A backup is automatically created before applying.

---

## Notes

- Optimizing reflection probes may reduce reflection quality in the scene. Always check the visuals after applying, and restore from the Backup tab if there are any issues.
- Applying compression to other assets may also significantly reduce their quality. In that case, restore from the backup, exclude the affected assets, and run compression again.
- Crunch compression on SDF fonts may slightly reduce text edge quality.
- Bulk-applying a large number of assets may cause Unity's import process to take a long time (this process depends on CPU performance, not GPU).
- In projects using UdonSharp, an `ArgumentNullException` may appear in the console after applying optimizations. This is a recompilation timing issue with UdonSharp, not this tool. It can be resolved by running **Tools > UdonSharp > Refresh All UdonSharp Component Definitions** or restarting Unity.

---

## License

This software is licensed under the **MIT License**. It may be used, modified, and redistributed for both personal and commercial purposes in the purchaser's own Unity projects.

This software was created with the author's intent to *"make it easy for anyone to compress their VRChat worlds."* While not prohibited under the MIT License, the author kindly requests that you **refrain from selling** modified versions of this software. *(This is merely the author's request; no contact will be made if this occurs.)*

---

## Disclaimer

This software is provided "as is." The author assumes no responsibility for:

1. Any damage to your project or data loss caused by the use of this software.
2. Cases where this software stops functioning due to VRChat updates or other changes.
3. Any issues related to uploading content optimized with this software to VRChat.

It is strongly recommended to **back up your entire project** before using this software. While this tool has a built-in backup feature, it is also recommended to save backups **outside the project directory**.

---

## Support

For bug reports and feature requests, please contact via BOOTH's messaging feature or X (Twitter) DM. While a response to every inquiry cannot be guaranteed, the author will respond as much as possible.