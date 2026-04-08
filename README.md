# MacSpeechApp

Simple C# application for macOS with:
- Text-to-Speech (TTS)
- Speech-to-Text (STT)

## Requirements

- .NET SDK (8+ or 10)
- macOS (`say` command is used for TTS)
- ffmpeg (required for microphone recording)

Install ffmpeg:

```bash
brew install ffmpeg
```

## Run

```bash
cd MacSpeechApp
dotnet run
```

Menu:
- `1` -> Speaks the text you enter using macOS voice
- `2` -> Records from microphone and transcribes with Whisper

## Notes

- Whisper model is downloaded automatically on first STT run.
- Model path: `~/Library/Application Support/MacSpeechApp/ggml-base.bin`
- Transcript is saved as `stt-output.txt` in the project folder.
