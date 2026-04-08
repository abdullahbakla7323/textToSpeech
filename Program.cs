using System.Diagnostics;
using Whisper.net;

const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "MacSpeechApp");
var modelPath = Path.Combine(appDataPath, "ggml-base.bin");

Directory.CreateDirectory(appDataPath);
Console.WriteLine("MacSpeechApp (C#) - Text To Speech + Speech To Text");
Console.WriteLine("-----------------------------------------------------");

while (true)
{
    Console.WriteLine("\nOptions:");
    Console.WriteLine("1) Text-to-Speech (speak text)");
    Console.WriteLine("2) Speech-to-Text (record from microphone)");
    Console.WriteLine("0) Exit");
    Console.Write("Your choice: ");
    var choice = Console.ReadLine()?.Trim();

    switch (choice)
    {
        case "1":
            RunTextToSpeech();
            break;
        case "2":
            await RunSpeechToTextFromMicrophoneAsync(modelPath);
            break;
        case "0":
            return;
        default:
            Console.WriteLine("Invalid choice, please try again.");
            break;
    }
}

static void RunTextToSpeech()
{
    Console.Write("Enter text to speak: ");
    var text = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine("Text cannot be empty.");
        return;
    }

    var psi = new ProcessStartInfo
    {
        FileName = "say",
        ArgumentList = { text },
        RedirectStandardError = true,
        RedirectStandardOutput = true
    };

    using var process = Process.Start(psi);
    process!.WaitForExit();

    if (process.ExitCode == 0)
    {
        Console.WriteLine("Text was spoken.");
    }
    else
    {
        var error = process.StandardError.ReadToEnd();
        Console.WriteLine($"Speech error: {error}");
    }
}

static async Task RunSpeechToTextFromMicrophoneAsync(string modelPath)
{
    Console.Write("How many seconds to record? (default 5): ");
    var durationInput = Console.ReadLine()?.Trim();
    var duration = 5;

    if (!string.IsNullOrWhiteSpace(durationInput) &&
        (!int.TryParse(durationInput, out duration) || duration <= 0))
    {
        Console.WriteLine("Invalid value. Using default 5 seconds.");
        duration = 5;
    }

    var tempWavPath = Path.Combine(Directory.GetCurrentDirectory(), "mic-input.wav");
    var recorded = RecordFromMicrophone(tempWavPath, duration);
    if (!recorded)
    {
        Console.WriteLine("Microphone recording failed. Make sure ffmpeg is installed.");
        Console.WriteLine("Install: brew install ffmpeg");
        return;
    }

    await RunSpeechToTextFromWavAsync(modelPath, tempWavPath);
}

static bool RecordFromMicrophone(string outputWavPath, int durationSeconds)
{
    if (File.Exists(outputWavPath))
    {
        File.Delete(outputWavPath);
    }

    Console.WriteLine("Recording started... Speak now.");
    var ffmpegPath = ResolveFfmpegPath();
    if (ffmpegPath is null)
    {
        Console.WriteLine("ffmpeg was not found.");
        Console.WriteLine("Install: brew install ffmpeg");
        Console.WriteLine("Alternative: check /opt/homebrew/bin/ffmpeg or /usr/local/bin/ffmpeg.");
        return false;
    }

    var psi = new ProcessStartInfo
    {
        FileName = ffmpegPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };

    psi.ArgumentList.Add("-y");
    psi.ArgumentList.Add("-f");
    psi.ArgumentList.Add("avfoundation");
    psi.ArgumentList.Add("-i");
    psi.ArgumentList.Add(":0");
    psi.ArgumentList.Add("-t");
    psi.ArgumentList.Add(durationSeconds.ToString());
    psi.ArgumentList.Add("-ar");
    psi.ArgumentList.Add("16000");
    psi.ArgumentList.Add("-ac");
    psi.ArgumentList.Add("1");
    psi.ArgumentList.Add(outputWavPath);

    try
    {
        using var process = Process.Start(psi);
        process!.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            Console.WriteLine($"ffmpeg error: {error}");
            return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Recording error: {ex.Message}");
        return false;
    }

    Console.WriteLine($"Recording completed: {outputWavPath}");
    return File.Exists(outputWavPath);
}

static string? ResolveFfmpegPath()
{
    var candidates = new[]
    {
        "ffmpeg",
        "/opt/homebrew/bin/ffmpeg",
        "/usr/local/bin/ffmpeg"
    };

    foreach (var candidate in candidates)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = candidate,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-version");

            using var process = Process.Start(psi);
            process!.WaitForExit();

            if (process.ExitCode == 0)
            {
                return candidate;
            }
        }
        catch
        {
            // Try the next candidate path.
        }
    }

    return null;
}

static async Task RunSpeechToTextFromWavAsync(string modelPath, string? providedWavPath = null)
{
    var wavPath = providedWavPath ?? string.Empty;
    if (string.IsNullOrWhiteSpace(wavPath))
    {
        var defaultPath = ResolveDefaultInputAudioPath();
        if (!string.IsNullOrWhiteSpace(defaultPath))
        {
            Console.WriteLine($"Default audio file: {defaultPath}");
        }
        else
        {
            Console.WriteLine("No default audio file was found.");
        }

        Console.Write("Audio file path (Enter = default): ");
        var userInput = Console.ReadLine()?.Trim() ?? "";
        wavPath = string.IsNullOrWhiteSpace(userInput) ? (defaultPath ?? string.Empty) : userInput;
    }

    if (!File.Exists(wavPath))
    {
        Console.WriteLine("File not found.");
        return;
    }

    var ext = Path.GetExtension(wavPath).ToLowerInvariant();
    if (ext != ".wav")
    {
        Console.WriteLine("Non-WAV file detected, auto-conversion will be attempted...");
        var convertedPath = Path.Combine(Directory.GetCurrentDirectory(), "converted-input.wav");
        if (!TryConvertToWav(wavPath, convertedPath))
        {
            Console.WriteLine("Failed to convert file to WAV format.");
            return;
        }

        wavPath = convertedPath;
        Console.WriteLine($"Converted file: {wavPath}");
    }
    else if (!IsValidRiffWave(wavPath))
    {
        Console.WriteLine("This file has .wav extension but is not a valid RIFF/WAVE file.");
        Console.WriteLine("Auto-conversion will be attempted...");
        var fixedPath = Path.Combine(Directory.GetCurrentDirectory(), "fixed-input.wav");
        if (!TryConvertToWav(wavPath, fixedPath))
        {
            Console.WriteLine("WAV file could not be fixed. Please try another audio file.");
            return;
        }

        wavPath = fixedPath;
        Console.WriteLine($"Fixed file: {wavPath}");
    }

    await EnsureModelAsync(modelPath);
    Console.WriteLine("Transcription started...");

    using var whisperFactory = WhisperFactory.FromPath(modelPath);
    using var processor = whisperFactory.CreateBuilder()
        .WithLanguage("tr")
        .Build();

    await using var audioFile = File.OpenRead(wavPath);
    var pieces = new List<string>();

    try
    {
        await foreach (var segment in processor.ProcessAsync(audioFile))
        {
            pieces.Add(segment.Text.Trim());
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Transcription error: {ex.Message}");
        Console.WriteLine("Tip: 16kHz mono WAV gives the best compatibility.");
        return;
    }

    var finalText = string.Join(" ", pieces.Where(p => !string.IsNullOrWhiteSpace(p)));
    Console.WriteLine("\n--- TRANSCRIPT ---");
    Console.WriteLine(string.IsNullOrWhiteSpace(finalText) ? "(empty result)" : finalText);

    var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "stt-output.txt");
    await File.WriteAllTextAsync(outputPath, finalText);
    Console.WriteLine($"\nOutput saved to file: {outputPath}");
}

static string? ResolveDefaultInputAudioPath()
{
    var cwd = Directory.GetCurrentDirectory();
    var parent = Directory.GetParent(cwd)?.FullName;

    var candidates = new List<string>
    {
        Path.Combine(cwd, "mic-input.wav"),
        Path.Combine(cwd, "input.wav"),
        Path.Combine(cwd, "ornek.wav"),
        Path.Combine(cwd, "input.aiff"),
        Path.Combine(cwd, "ornek.aiff"),
        Path.Combine(cwd, "input.m4a"),
        Path.Combine(cwd, "ornek.m4a"),
        Path.Combine(cwd, "input.mp3"),
        Path.Combine(cwd, "ornek.mp3")
    };

    if (!string.IsNullOrWhiteSpace(parent))
    {
        candidates.Add(Path.Combine(parent, "mic-input.wav"));
        candidates.Add(Path.Combine(parent, "input.wav"));
        candidates.Add(Path.Combine(parent, "ornek.wav"));
        candidates.Add(Path.Combine(parent, "input.aiff"));
        candidates.Add(Path.Combine(parent, "ornek.aiff"));
        candidates.Add(Path.Combine(parent, "input.m4a"));
        candidates.Add(Path.Combine(parent, "ornek.m4a"));
        candidates.Add(Path.Combine(parent, "input.mp3"));
        candidates.Add(Path.Combine(parent, "ornek.mp3"));
    }

    foreach (var path in candidates.Distinct())
    {
        if (!File.Exists(path))
        {
            continue;
        }

        // Skip invalid WAV files as defaults.
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            !IsValidRiffWave(path))
        {
            continue;
        }

        return path;
    }

    return null;
}

static bool IsValidRiffWave(string path)
{
    try
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 12)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[12];
        var read = fs.Read(header);
        if (read < 12)
        {
            return false;
        }

        var riff = System.Text.Encoding.ASCII.GetString(header[..4]);
        var wave = System.Text.Encoding.ASCII.GetString(header[8..12]);
        return riff == "RIFF" && wave == "WAVE";
    }
    catch
    {
        return false;
    }
}

static bool TryConvertToWav(string inputPath, string outputWavPath)
{
    var ffmpegPath = ResolveFfmpegPath();
    if (ffmpegPath is null)
    {
        Console.WriteLine("ffmpeg was not found. Install: brew install ffmpeg");
        return false;
    }

    if (File.Exists(outputWavPath))
    {
        File.Delete(outputWavPath);
    }

    var psi = new ProcessStartInfo
    {
        FileName = ffmpegPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };

    psi.ArgumentList.Add("-y");
    psi.ArgumentList.Add("-i");
    psi.ArgumentList.Add(inputPath);
    psi.ArgumentList.Add("-ar");
    psi.ArgumentList.Add("16000");
    psi.ArgumentList.Add("-ac");
    psi.ArgumentList.Add("1");
    psi.ArgumentList.Add(outputWavPath);

    try
    {
        using var process = Process.Start(psi);
        process!.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            Console.WriteLine($"ffmpeg conversion error: {error}");
            return false;
        }

        return File.Exists(outputWavPath) && IsValidRiffWave(outputWavPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Conversion start error: {ex.Message}");
        return false;
    }
}

static async Task EnsureModelAsync(string modelPath)
{
    if (File.Exists(modelPath))
    {
        return;
    }

    Console.WriteLine("Downloading Whisper model (only once on first run)...");

    using var client = new HttpClient();
    await using var source = await client.GetStreamAsync(ModelUrl);
    await using var target = File.Create(modelPath);
    await source.CopyToAsync(target);
}
