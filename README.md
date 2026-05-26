 # 🔁 Rotation‑Invariant Template Matching with OpenCvSharp

A WPF application that detects **multiple rotated instances** of a template image inside a larger scene using **OpenCvSharp** (C# bindings for OpenCV). Implements coarse‑to‑fine pyramid search, rotated template generation, and Non‑Maximum Suppression for robust multi‑object detection.

![Demo Result](Result/demo_result.png)

## 🧠 Core Strategy for Multi‑Object & Rotated Object Detection

### 1. Rotation Handling
The master template can be matched at any angle by **pre‑rotating the template** before each matching step.  
For every angle in the user‑defined range (`AngleFrom` … `AngleTo` with step `AngleStep`):
- The template is rotated using an affine warp.
- A binary mask is generated simultaneously to exclude padded areas.
- Matching is performed using `MatchTemplate` with `CCoeffNormed`.

### 2. Image Pyramid (Multi‑scale)
To keep performance acceptable, a pyramid is built for both the scene and the master.  
- **Coarsest level** (`pyrLevels - 1`) is processed first, with a **lower score threshold** to keep candidates.  
- This drastically reduces the number of full‑resolution evaluations.

### 3. Coarse‑to‑Fine Search
- **Coarse step**: At the lowest pyramid level, all angles are evaluated. Every position with a score above `minScore - 0.15` becomes a **coarse candidate**.
- **Fine step**: For each coarse candidate, a local window around its location is cropped from the **full‑resolution** scene. The full‑size master is rotated again (same angle) and matched only inside that small window.
- This strategy reduces runtime by >80% compared to a brute‑force full‑resolution angle scan.

### 4. Non‑Maximum Suppression (NMS)
After fine matching, many overlapping bounding boxes may appear around the same object.  
- Detections are sorted by score descending.
- NMS discards boxes whose **Intersection over Union (IoU)** with a higher‑score box exceeds `NMS Overlap`.
- The remaining top `Max Matches` are drawn on the result.

### 5. Drawing Rotated Boxes
Instead of axis‑aligned rectangles, each detection is rendered as a **rotated bounding box** (via `RotatedRect`) with a small crosshair at its center – giving a clear visual feedback of the object’s orientation.

## 🖼️ Example Result

*Below: detection of multiple rotated capacitors on a tray. The master was a single straight capacitor. Green boxes show all matches with their correct angles.*

![Detection example](Result/demo_result.png)

> The `Result` folder contains sample output images.

## 🚀 Getting Started

### Prerequisites
- Windows 10 / 11
- [.NET 6.0 SDK](https://dotnet.microsoft.com/en-us/download) or later
- Visual Studio 2022 (or any C# IDE)

### Install OpenCvSharp and Dependencies

This project uses **OpenCvSharp4** – the official C# wrapper for OpenCV.  
You need the following NuGet packages:

| Package | Purpose |
|---------|---------|
| `OpenCvSharp4.Windows` | Native OpenCV binaries for Windows |
| `OpenCvSharp4.WpfExtensions` | Converts `Mat` to `BitmapSource` for WPF |
| `OpenCvSharp4.runtime.windows` | (Optional, but included for compatibility) |

Install them via **Package Manager Console**:

```powershell
Install-Package OpenCvSharp4.Windows
Install-Package OpenCvSharp4.WpfExtensions
Install-Package OpenCvSharp4.runtime.windows

Or using .NET CLI:
	dotnet add package OpenCvSharp4.Windows
	dotnet add package OpenCvSharp4.WpfExtensions
	dotnet add package OpenCvSharp4.runtime.windows
Build & Run:
	git clone https://github.com/NgDong1808/Template-matching-for-multiple-rotated-instances-using-OpencvSharp.git
	cd rotation-template-matching
	dotnet restore
	dotnet build
	dotnet run
📁 Project Structure (as in your repository)
	📂 TemplateMatching
	├── 📁 bin/                 # Compiled binaries
	├── 📁 Image/               # Sample input images (scene + master)
	├── 📁 obj/                 # Intermediate objects
	├── 📁 Result/              # Output screenshots (e.g., demo_result.png)
	├── 📄 App.xaml             # Application resources
	├── 📄 App.xaml.cs          # App startup logic
	├── 📄 AssemblyInfo.cs      # Assembly metadata
	├── 📄 MainWindow.xaml      # WPF UI (basic styling)
	├── 📄 MainWindow.xaml.cs   # Full matching algorithm (pyramid, rotation, NMS)
	├── 📄 TemplateMatching.csproj       # Project file
	├── 📄 TemplateMatching.csproj.user  # User‑specific settings
	└── 📄 README.md            # This file
🎮 Usage
	1. Load Scene – click 📂 Open Scene and select a large image (e.g., from the Image folder).

	2. Load Master – click 📁 Load Master Image and select the small template (e.g., a single object).

	3. Adjust parameters (see table below).

	4. Run – click ▶ Run.

	5. View the result on the canvas; best match metrics and a detailed log appear on the right.
Parameter Guide
	Parameter	Description
	Max Matches	Maximum number of detections after NMS.
	Pyramid Levels	Down‑sampling levels (3 is a good balance). Higher = faster but may miss small objects.
	Min Score	Correlation threshold (0.8 = very reliable).
	NMS Overlap	IoU threshold for suppression (0.3 = aggressive, keeps distinct objects).
	Angle From / To	Rotation range in degrees (e.g., 0–180).
	Angle Step	Search granularity (5° = 36 angles). Smaller = slower but more precise.
⚙️ Technical Highlights
	Pure OpenCvSharp – all CV uses Mat, Point, Rect, RotatedRect, Scalar etc.

	Parallel processing – angles are processed concurrently with Parallel.For.

	Memory efficient – temporary matrices disposed after use.

	Thread‑safe collections (ConcurrentBag<MatchResult>) for candidate gathering.

	No external resource dictionaries – pure WPF styling.
📄 License
MIT – free for academic and commercial use.