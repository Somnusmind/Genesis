# Somnusmind Genesis

**Version 0.1.0** • Windows only

A powerful Unity-based application for creating **high-quality, personalized hypnosis, meditation, mantra, and subliminal audio sessions** in just a few seconds.

Genesis uses a YAML configuration system combined with OpenRouter TTS, a professional-grade audio pipeline, and advanced brainwave entrainment to deliver consistent, studio-quality results.

---

## Features

### Core System
- **5-Stage Hypnosis Architecture** — Induction → Deepening → Pre-Suggestive → Suggestions → Coming Back (each stage support looping)
- **YAML-driven configuration** — Full control over every aspect of the session
- **Sequential** and **Static** session modes
- **Mantra / Subliminal track** with optional spatial movement

### AI-Powered Content Creation
- **Config Creator** — Generate complete, ready-to-use hypnosis/mediation/subliminal sessions from a single prompt
- Access to **600+ LLMs** via OpenRouter
- Customizable system prompt (`SystemPrompt.md`) for fine-tuned output style

### Audio Quality & Processing
- **OpenRouter TTS support** — Use any TTS model available on the platform
- **EBU R128 loudness normalization** (libebur128) for consistent speech levels
- **High-quality audio mixer** with:
  - 5 independent channels + Master
  - Parametric EQ, Echo, High-pass filter, Compressor
- **30 Reverb presets** (including 4 optimized custom presets: Hypnosis, Meditation, Mantra, Subliminal)

### Brainwave Entrainment & Visuals
- **4 Brainwave modes**: Binaural, Binaural (Golden Ratio), Panning, Isochronic
- **Frequency transitions** between stages with multiple curve types
- **Photic Driving** (closed-eye strobe) synchronized with the audio
- **Spatial Subliminizer** — subliminal track moves in sync with the current brainwave frequency

### Background Audio
- White / Pink / Brown noise
- 14 built-in SFX tracks + custom MP3 import support

### Workflow & Usability
- Import & Export full session configurations (zipped)
- Encrypted local storage of OpenRouter API key
- Real-time Live Mixer during playback
- Session length calculation and preview indicators
- Extensive real-world testing and refinement

---

## Trailer

[![Watch the trailer](https://img.youtube.com/vi/T-zPGHifm4Y/maxresdefault.jpg)](https://www.youtube.com/watch?v=T-zPGHifm4Y)

---

## Images

<img width="1920" height="1080" alt="1" src="https://github.com/user-attachments/assets/7e01108c-d67b-493e-9520-eb6633f30299" />

<img width="1920" height="1080" alt="2" src="https://github.com/user-attachments/assets/bdef9bea-01e1-4c97-8bae-8be451e8334f" />

<img width="1920" height="1080" alt="4" src="https://github.com/user-attachments/assets/6ce11528-9d83-4f18-a512-f86fbc738f91" />

<img width="1920" height="1080" alt="5" src="https://github.com/user-attachments/assets/c5cd4235-4c48-4fca-b906-d2cba42e9d98" />

<img width="1920" height="1080" alt="6" src="https://github.com/user-attachments/assets/ab8eabc4-2e3f-4a05-a92f-e54aa5a24a82" />

<img width="1920" height="1080" alt="7" src="https://github.com/user-attachments/assets/6df8bd86-c1fd-4456-b4c6-50afa00e4f4d" />


---

## Data Location

All user-generated data is stored in the following location on Windows:

**`%localappdata%low\Somnusmind\Genesis`**

(or manually: `C:\Users\YourName\AppData\LocalLow\Somnusmind\Genesis`)

This folder contains:
- Your encrypted OpenRouter API key
- All YAML configuration files + media folders
- TTS audio cache
- The editable `SystemPrompt.md`

You can freely inspect, edit, or back up these files.

---

## Important Notes

### Repository Contents
This repository contains only the **essential C# scripts** of the project.  
The full Unity project cannot be shared because it uses multiple paid Unity assets and third-party tools that I do not have redistribution rights for.

### Philosophy
This project was built with genuine passion and tested extensively in real-world scenarios over many months. Every component (mixer routing, reverb presets, normalization, spatial movement, etc.) was refined through actual use rather than theoretical design.

---

## Getting Started

1. Download the latest release (Windows build)
2. Place the application in a folder of your choice
3. Run `Genesis.exe`
4. Add your OpenRouter API key in the settings (stored encrypted locally)
5. Create configurations via the **Config Creator** or play Pre-Cached configurations

---

## Requirements

- Windows 10 / 11
- OpenRouter API key (free to create)

---

## License

This project is released under the **MIT License** (see `LICENSE` file).

---

## Credits

Created with passion by Somnusmind.

If you find this tool useful, consider starring the repository — it helps others discover it.
