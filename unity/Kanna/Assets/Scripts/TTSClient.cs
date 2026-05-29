using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;               // 引入 Toggle
using System.Collections;
using System.Text;
using TMPro;
using Live2D.Cubism.Framework.Expression;

[System.Serializable]
public class ChatResponse
{
    public string llmres;
    public string tts_text;
    public string audio_url;
    public string music_url;
    public string music_type;
    public string voice_url;
    public string voice_type;
    public string bgm_url;
    public string bgm_type;
    public string type;
    public Emotion emotion;
}

[System.Serializable]
public class Emotion
{
    public int expression;
    public string motion;
}

public class TTSClient : MonoBehaviour
{
    [Header("服务器配置")]
    private string serverUrl = "http://127.0.0.1:9000/chat";

    [Header("气泡与UI")]
    public ChatBubble chatBubble;
    public TextMeshProUGUI restext;

    [Header("TTS 控制")]
    public bool enableClientTTS = true;
    public FastTTS fastTTS;
    public Toggle ttsToggle;                     // 拖入场景中的 Toggle 控件

    public AudioSource voice;
    public AudioSource bgm;
    public GameObject model;
    public TMP_InputField inputField;
    private CubismExpressionController expressionController;

    [Header("无 TTS 时的气泡停留时间")]
    public float defaultBubbleDuration = 3f;

    void Start()
    {
        expressionController = model.GetComponent<CubismExpressionController>();

        if (fastTTS == null && enableClientTTS)
        {
            fastTTS = FindFirstObjectByType<FastTTS>();
            if (fastTTS == null)
                Debug.LogWarning("场景中未找到 FastTTS 组件，客户端 TTS 不可用");
        }

        if (fastTTS != null)
        {
            fastTTS.onTTSStarted += OnTTSStarted;
            fastTTS.onTTSFinished += OnTTSFinished;
        }

        // 初始化 Toggle 状态并监听
        if (ttsToggle != null)
        {
            ttsToggle.isOn = enableClientTTS;
            ttsToggle.onValueChanged.AddListener(OnTTSToggleChanged);
        }
    }

    private void OnTTSToggleChanged(bool isOn)
    {
        enableClientTTS = isOn;
        if (!isOn)
        {
            // 关闭 TTS：立即停止正在进行的合成与播放
            StopCurrentTTS();
        }
    }

    /// <summary>
    /// 强制停止当前 TTS 合成和播放，并隐藏气泡
    /// </summary>
    private void StopCurrentTTS()
    {
        if (fastTTS != null)
        {
            fastTTS.StopAllCoroutines();          // 停止所有合成/播放协程
            if (fastTTS.audioSource != null)
                fastTTS.audioSource.Stop();       // 停止音频播放
        }
        // 隐藏气泡（如果它正在显示）
        if (chatBubble != null)
            chatBubble.Hide();
    }

    private void OnTTSStarted()
    {
        if (chatBubble != null)
            chatBubble.Show();
    }

    private void OnTTSFinished()
    {
        if (chatBubble != null)
            chatBubble.Hide();
    }

    public void SendChat()
    {
        if (inputField.text != null)
        {
            StartCoroutine(PostChat(inputField.text));
            inputField.text = "";
        }
    }
    public void ASRChat(string text)
    {
        if(text != null)
        {
            StartCoroutine(PostChat(text));
        }
    }
    IEnumerator PostChat(string text)
    {
        string json = $"{{\"query\":\"{text}\"}}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(serverUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string responseJson = req.downloadHandler.text;
                ChatResponse response = JsonUtility.FromJson<ChatResponse>(responseJson);

                // 设置气泡文本
                if (chatBubble != null)
                    chatBubble.SetText(response.llmres);
                if (restext != null)
                    restext.text = response.llmres;

                Debug.Log($"AI回复: {response.llmres}");

                bool isTTSPlayed = false;

                // 处理唱歌/音乐
                if (response.type == "sing")
                {
                    yield return LoadAndSing(response.voice_url, response.voice_type,
                                             response.bgm_url, response.bgm_type);
                    StartCoroutine(AutoHideBubble(defaultBubbleDuration));
                    isTTSPlayed = true;
                }
                else if (response.type == "play")
                {
                    yield return LoadAndPlayAudio(response.music_url, response.music_type);
                    StartCoroutine(AutoHideBubble(defaultBubbleDuration));
                    isTTSPlayed = true;
                }
                else
                {
                    // 客户端 TTS（只有开关开启时才使用）
                    if (enableClientTTS && fastTTS != null)
                    {
                        string ttsText = string.IsNullOrEmpty(response.tts_text)
                                         ? response.llmres
                                         : response.tts_text;
                        fastTTS.StartTTS(ttsText);
                        isTTSPlayed = true;
                    }
                }

                // 如果没有播放任何音频，则使用默认气泡停留时间
                if (!isTTSPlayed)
                {
                    if (chatBubble != null)
                        StartCoroutine(AutoHideBubble(defaultBubbleDuration));
                }
            }
            else
            {
                Debug.LogError($"请求失败: {req.error}");
                chatBubble.SetText(req.error);
                StartCoroutine(AutoHideBubble(defaultBubbleDuration));
            }
        }
    }

    IEnumerator AutoHideBubble(float delay)
    {
        chatBubble.Show();
        yield return new WaitForSeconds(delay);
        chatBubble.Hide();
    }

    IEnumerator LoadAndPlayAudio(string musicUrl, string music_type)
    {
        AudioType mtype = AudioType.WAV;
        switch (music_type)
        {
            case "wav": mtype = AudioType.WAV; break;
            case "mp3": mtype = AudioType.MPEG; break;
            case "ogg": mtype = AudioType.OGGVORBIS; break;
        }
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(musicUrl, mtype))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                bgm.clip = clip;
                bgm.Play();
            }
        }
    }

    IEnumerator LoadAndSing(string voiceUrl, string voice_type, string bgmUrl, string bgm_type)
    {
        bool can_voice = false, can_bgm = false;
        AudioType vtype = AudioType.WAV, btype = AudioType.WAV;
        switch (voice_type)
        {
            case "wav": vtype = AudioType.WAV; break;
            case "mp3": vtype = AudioType.MPEG; break;
            case "ogg": vtype = AudioType.OGGVORBIS; break;
        }
        switch (bgm_type)
        {
            case "wav": btype = AudioType.WAV; break;
            case "mp3": btype = AudioType.MPEG; break;
            case "ogg": btype = AudioType.OGGVORBIS; break;
        }

        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(voiceUrl, vtype))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                voice.clip = clip;
                can_voice = true;
            }
        }
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(bgmUrl, btype))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                bgm.clip = clip;
                can_bgm = true;
            }
        }
        if (can_voice && can_bgm)
        {
            voice.Play();
            bgm.Play();
        }
    }

}