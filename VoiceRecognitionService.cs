using System.Speech.Recognition;

namespace RagdollPet;

/// <summary>
/// Uses the installed Windows speech recognizer. Audio is processed by the
/// recognizer installed on this PC and is never sent to the Ollama endpoint.
/// </summary>
public sealed class VoiceRecognitionService : IDisposable
{
    private SpeechRecognitionEngine? engine;

    public event Action<string>? TextRecognized;

    public bool IsListening => engine is not null;

    public void Start()
    {
        if (engine is not null) return;

        var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(item => item.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            ?? SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(item => item.Culture.TwoLetterISOLanguageName == "zh");

        if (recognizer is null)
            throw new InvalidOperationException("Windows 尚未安装中文语音识别组件。请在 Windows 语音/语言设置中添加中文语音识别。 ");

        var candidate = new SpeechRecognitionEngine(recognizer);
        try
        {
            candidate.SetInputToDefaultAudioDevice();
            candidate.LoadGrammar(new DictationGrammar());
            candidate.SpeechRecognized += OnSpeechRecognized;
            candidate.RecognizeCompleted += OnRecognizeCompleted;
            candidate.RecognizeAsync(RecognizeMode.Multiple);
            engine = candidate;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        var active = engine;
        engine = null;
        if (active is null) return;

        active.SpeechRecognized -= OnSpeechRecognized;
        active.RecognizeCompleted -= OnRecognizeCompleted;
        try { active.RecognizeAsyncCancel(); }
        catch (InvalidOperationException) { }
        active.Dispose();
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence >= .58 && !string.IsNullOrWhiteSpace(e.Result.Text))
            TextRecognized?.Invoke(e.Result.Text.Trim());
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error is not null)
            TextRecognized?.Invoke($"\u0001{e.Error.Message}");
    }

    public void Dispose() => Stop();
}
