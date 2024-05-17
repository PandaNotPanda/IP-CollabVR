using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using HuggingFace.API;

public class SpeechRecognitionTest : MonoBehaviour {
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private TextMeshProUGUI speechText;
    [SerializeField] private TextMeshProUGUI translationText;

    private AudioClip clip;
    private bool recording;
    private string apiKey = "AIzaSyBbGDKEk4eeyIcWOOPNxpn-OrMjF3L1eaE";  // Replace with your API key

    private void Start() {
        startButton.onClick.AddListener(StartRecording);
        stopButton.onClick.AddListener(StopRecording);
        stopButton.interactable = false;
    }

    private void Update() {
        if (recording && Microphone.GetPosition(null) >= clip.samples) {
            StopRecording();
        }
    }

    private void StartRecording() {
        speechText.color = Color.white;
        speechText.text = "Recording...";
        translationText.text = "";
        startButton.interactable = false;
        stopButton.interactable = true;
        clip = Microphone.Start(null, false, 10, 44100);
        recording = true;
    }

    private void StopRecording() {
        int position = Microphone.GetPosition(null);
        Microphone.End(null);
        float[] samples = new float[position * clip.channels];
        clip.GetData(samples, 0);
        byte[] bytes = EncodeAsWAV(samples, clip.frequency, clip.channels);
        recording = false;
        SendRecording(bytes);
    }

    private void SendRecording(byte[] bytes) {
        speechText.color = Color.yellow;
        speechText.text = "Sending...";
        stopButton.interactable = false;
        HuggingFaceAPI.AutomaticSpeechRecognition(bytes, response => {
            speechText.color = Color.white;
            speechText.text = response;
            startButton.interactable = true;
            StartCoroutine(Translate(response, "en", "gu"));
        }, error => {
            speechText.color = Color.red;
            speechText.text = error;
            startButton.interactable = true;
        });
    }

    private IEnumerator Translate(string input, string fromLanguage, string toLanguage) {
        string url = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}&q={UnityWebRequest.EscapeURL(input)}&source={fromLanguage}&target={toLanguage}";
        UnityWebRequest webRequest = UnityWebRequest.Get(url);
        yield return webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError) {
            translationText.color = Color.red;
            translationText.text = "Error: " + webRequest.error;
        } else {
            TranslationResponse response = JsonUtility.FromJson<TranslationResponse>(webRequest.downloadHandler.text);
            if (response.data.translations.Length > 0) {
                translationText.color = Color.white;
                translationText.text = response.data.translations[0].translatedText;
                StartCoroutine(TextToSpeech(response.data.translations[0].translatedText));
            }
        }
    }

    private IEnumerator TextToSpeech(string input) {
        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";
        string json = $"{{\"input\":{{\"text\":\"{input}\"}},\"voice\":{{\"languageCode\":\"gu-IN\",\"name\":\"gu-IN-Wavenet-A\"}},\"audioConfig\":{{\"audioEncoding\":\"LINEAR16\"}}}}";
        
        UnityWebRequest webRequest = UnityWebRequest.PostWwwForm(url, json);
        webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        webRequest.SetRequestHeader("Content-Type", "application/json");
        yield return webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError) {
            translationText.color = Color.red;
            translationText.text = "Error: " + webRequest.error;
        } else {
            TextToSpeechResponse response = JsonUtility.FromJson<TextToSpeechResponse>(webRequest.downloadHandler.text);
            byte[] audioData = System.Convert.FromBase64String(response.audioContent);
            PlayAudioClip(audioData);
        }
    }

    private void PlayAudioClip(byte[] audioData) {
        WAV wav = new WAV(audioData);
        AudioClip audioClip = AudioClip.Create("TTS_Audio", wav.SampleCount, 1, wav.Frequency, false);
        audioClip.SetData(wav.LeftChannel, 0);
        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = audioClip;
        audioSource.Play();
    }

    private byte[] EncodeAsWAV(float[] samples, int frequency, int channels) {
        using (MemoryStream memoryStream = new MemoryStream(44 + samples.Length * 2)) {
            using (BinaryWriter writer = new BinaryWriter(memoryStream)) {
                writer.Write("RIFF".ToCharArray());
                writer.Write(36 + samples.Length * 2);
                writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray());
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)channels);
                writer.Write(frequency);
                writer.Write(frequency * channels * 2);
                writer.Write((ushort)(channels * 2));
                writer.Write((ushort)16);
                writer.Write("data".ToCharArray());
                writer.Write(samples.Length * 2);

                foreach (var sample in samples) {
                    writer.Write((short)(sample * short.MaxValue));
                }

                return memoryStream.ToArray();
            }
        }
    }
}

[System.Serializable]
public class TranslationResponse {
    public TranslationData data;
}

[System.Serializable]
public class TranslationData {
    public Translation[] translations;
}

[System.Serializable]
public class Translation {
    public string translatedText;
}

[System.Serializable]
public class TextToSpeechResponse {
    public string audioContent;
}

public class WAV
{
    public float[] LeftChannel { get; private set; }
    public float[] RightChannel { get; private set; }
    public int ChannelCount { get; private set; }
    public int SampleCount { get; private set; }
    public int Frequency { get; private set; }

    public WAV(byte[] wav)
    {
        ChannelCount = wav[22];
        Frequency = wav[24] | (wav[25] << 8) | (wav[26] << 16) | (wav[27] << 24);
        int pos = 12;

        while (!(wav[pos] == 100 && wav[pos + 1] == 97 && wav[pos + 2] == 116 && wav[pos + 3] == 97))
        {
            pos += 4;
            int chunkSize = wav[pos] | (wav[pos + 1] << 8) | (wav[pos + 2] << 16) | (wav[pos + 3] << 24);
            pos += 4 + chunkSize;
        }
        pos += 8;

        SampleCount = (wav.Length - pos) / 2;
        if (ChannelCount == 2) SampleCount /= 2;

        LeftChannel = new float[SampleCount];
        RightChannel = new float[SampleCount];

        int i = 0;
        while (pos < wav.Length)
        {
            LeftChannel[i] = BytesToFloat(wav[pos], wav[pos + 1]);
            pos += 2;
            if (ChannelCount == 2)
            {
                RightChannel[i] = BytesToFloat(wav[pos], wav[pos + 1]);
                pos += 2;
            }
            i++;
        }
    }

    private static float BytesToFloat(byte firstByte, byte secondByte)
    {
        short s = (short)((secondByte << 8) | firstByte);
        return s / 32768.0F;
    }
}