# Somnusmind Genesis

**Version 0.1.0** • Windows only

A powerful Unity-based application for creating **high-quality, personalized hypnosis, meditation, mantra, and subliminal audio sessions** in just a few minutes.

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

## Screenshots



---

## Trailer

[![Watch the trailer](https://img.youtube.com/vi/T-zPGHifm4Y/maxresdefault.jpg)](https://www.youtube.com/watch?v=T-zPGHifm4Y)

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
3. Run `Somnusmind Genesis.exe`
4. Import or create configurations via the **Config Creator**
5. Add your OpenRouter API key in the settings (stored encrypted locally)

---

## Requirements

- Windows 10 / 11
- OpenRouter API key (free to create)

---

## License

This project is released under the **MIT License** (see `LICENSE` file).

---

## Credits

Created with care by Somnusmind.

If you find this tool useful, consider starring the repository — it helps others discover it.
