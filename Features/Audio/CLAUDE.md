# Audio

Audio capture and spectrum analysis for headsets — WASAPI capture, FFT, spectrograms — plus an audio-device reboot test. Uses NAudio + CSCore (extra `PackageReference`s in the `.csproj`). Real-time DSP runs off the `Update()` / capture callbacks; keep the UI thread clear.
