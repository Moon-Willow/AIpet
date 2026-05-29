using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 自动语音识别：VAD 检测说话起止，整句发送给 STT API
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceToChat : MonoBehaviour
{
    [Header("STT API 设置")]
    public string sttApiUrl = "http://127.0.0.1:7998/v1/stt";

    [Header("录音参数")]
    public string microphoneDevice = null;
    public int sampleRate = 16000;

    [Tooltip("音量阈值，建议 0.01~0.03")]
    [Range(0.0001f, 1.0f)]
    public float voiceThreshold = 0.02f;

    [Tooltip("判定为说话开始的最短持续音量（秒），过滤突发噪音")]
    public float minSpeechDuration = 0.2f;

    [Tooltip("静音持续多久视为说话结束（秒）")]
    public float silenceTimeout = 1.5f;

    [Tooltip("最大录音时长（秒），防止无限录音")]
    public int maxRecordingTime = 30;

    [Tooltip("音量检测窗口（毫秒），建议 50-100ms")]
    public int detectionWindowMs = 50;

    [Header("UI 控制")]
    public UnityEngine.UI.Toggle voiceToggle;
    public TMPro.TextMeshProUGUI statusText;
    public TMPro.TextMeshProUGUI ASRtext;

    [Header("聊天组件")]
    public TTSClient ttsClient;

    // ========== 内部状态 ==========
    private AudioClip microphoneClip;
    private bool isListening = false;      // 是否正在监听（总开关）
    private bool isRecording = false;      // 是否正在录制有效语音
    private int recordingStartSample = 0;  // 录音开始的样本位置
    private int lastSamplePos = 0;         // 上次读取的样本位置
    private float continuousVoiceTime = 0f; // 持续检测到语音的时长
    private float silenceDuration = 0f;    // 持续静音时长
    private bool isSending = false;

    // 累积的音频数据（动态列表，支持长句）
    private System.Collections.Generic.List<float> audioBuffer = new System.Collections.Generic.List<float>();

    private void Start()
    {
        if (voiceToggle != null)
        {
            voiceToggle.isOn = false;
            voiceToggle.onValueChanged.AddListener(OnToggleChanged);
        }
    }

    private void OnToggleChanged(bool isOn)
    {
        if (isOn) StartListening();
        else StopListening();
    }

    /// <summary>
    /// 开始监听（持续运行，不中断）
    /// </summary>
    public void StartListening()
    {
        if (isListening) return;

        // 使用足够长的循环缓冲区（maxRecordingTime + 缓冲）
        int bufferLength = Mathf.Max(maxRecordingTime, 10);
        microphoneClip = Microphone.Start(microphoneDevice, true, bufferLength, sampleRate);

        if (microphoneClip == null)
        {
            Debug.LogError("无法启动麦克风！");
            return;
        }

        isListening = true;
        isRecording = false;
        lastSamplePos = 0;
        audioBuffer.Clear();

        StartCoroutine(ListeningLoop());
        if (statusText != null) statusText.text = "等待说话...";
        Debug.Log("麦克风监听已启动");
    }

    public void StopListening()
    {
        isListening = false;
        isRecording = false;
        Microphone.End(microphoneDevice);
        StopAllCoroutines();
        audioBuffer.Clear();
        if (statusText != null) statusText.text = "语音输入已关闭";
        Debug.Log("麦克风监听已停止");
    }

    /// <summary>
    /// 核心监听循环：持续读取麦克风数据，VAD 检测
    /// </summary>
    IEnumerator ListeningLoop()
    {
        // 等待麦克风初始化
        yield return new WaitUntil(() => Microphone.GetPosition(microphoneDevice) > 0);

        while (isListening)
        {
            int currentPos = Microphone.GetPosition(microphoneDevice);
            if (currentPos < 0) yield return null;

            // 计算新采集的样本数
            int samplesToRead = 0;
            if (currentPos > lastSamplePos)
            {
                samplesToRead = currentPos - lastSamplePos;
            }
            else if (currentPos < lastSamplePos)  // 循环覆盖
            {
                samplesToRead = (microphoneClip.samples - lastSamplePos) + currentPos;
            }

            if (samplesToRead > 0)
            {
                // 读取新数据
                float[] newSamples = new float[samplesToRead];
                microphoneClip.GetData(newSamples, lastSamplePos);
                lastSamplePos = currentPos;

                // 检测音量（使用更大的窗口）
                float volume = CalculateVolume(newSamples);

                // 状态机处理
                ProcessVAD(volume, newSamples);
            }

            yield return null;  // 每帧检测，不丢数据
        }
    }

    /// <summary>
    /// 计算音频音量（RMS，使用较大窗口）
    /// </summary>
    private float CalculateVolume(float[] samples)
    {
        if (samples.Length == 0) return 0;

        // 使用最后 detectionWindowMs 的样本计算
        int windowSamples = Mathf.Min(samples.Length, sampleRate * detectionWindowMs / 1000);
        int startIdx = samples.Length - windowSamples;
        if (startIdx < 0) startIdx = 0;

        float sum = 0;
        for (int i = startIdx; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];  // RMS 用平方
        }
        return Mathf.Sqrt(sum / windowSamples);
    }

    /// <summary>
    /// VAD 状态机：Idle -> Speaking -> [Silence] -> Send -> Idle
    /// </summary>
    private void ProcessVAD(float volume, float[] newSamples)
    {
        if (isRecording)
        {
            // ========== 正在录音中 ==========
            audioBuffer.AddRange(newSamples);

            // 检查是否还在说话
            if (volume > voiceThreshold)
            {
                continuousVoiceTime += (float)newSamples.Length / sampleRate;
                silenceDuration = 0f;
            }
            else
            {
                silenceDuration += (float)newSamples.Length / sampleRate;
            }

            // 检查结束条件
            float totalRecordedTime = (float)audioBuffer.Count / sampleRate;
            bool maxDurationReached = totalRecordedTime >= maxRecordingTime;
            bool silenceTimeoutReached = silenceDuration >= silenceTimeout;

            if (maxDurationReached)
            {
                Debug.Log($"录音达到最大时长 {maxRecordingTime}s，强制发送");
                StartCoroutine(SendAndRestart());
            }
            else if (silenceTimeoutReached && continuousVoiceTime >= minSpeechDuration)
            {
                Debug.Log($"静音超时 {silenceTimeout}s，发送识别");
                StartCoroutine(SendAndRestart());
            }
            else if (silenceTimeoutReached && continuousVoiceTime < minSpeechDuration)
            {
                // 语音太短，丢弃（可能是噪音）
                Debug.Log($"语音太短 ({continuousVoiceTime:F2}s < {minSpeechDuration}s)，丢弃");
                ResetRecording();
            }
        }
        else
        {
            // ========== 等待说话 ==========
            if (volume > voiceThreshold)
            {
                continuousVoiceTime += (float)newSamples.Length / sampleRate;

                if (continuousVoiceTime >= minSpeechDuration && !isRecording)
                {
                    // 确认开始说话
                    isRecording = true;
                    recordingStartSample = lastSamplePos;
                    audioBuffer.AddRange(newSamples);
                    silenceDuration = 0f;

                    if (statusText != null) statusText.text = "正在听...";
                    Debug.Log("检测到说话开始");
                }
            }
            else
            {
                // 重置连续语音计时（噪音过滤）
                continuousVoiceTime = Mathf.Max(0, continuousVoiceTime - (float)newSamples.Length / sampleRate);
            }
        }
    }

    /// <summary>
    /// 发送音频并重新开始监听
    /// </summary>
    IEnumerator SendAndRestart()
    {
        isRecording = false;
        isSending = true;

        // 复制音频数据
        float[] samplesToSend = audioBuffer.ToArray();
        audioBuffer.Clear();
        continuousVoiceTime = 0f;
        silenceDuration = 0f;

        if (statusText != null) statusText.text = "识别中...";

        // 转换为 PCM 并发送
        byte[] pcmData = ConvertToPCM16(samplesToSend);
        yield return StartCoroutine(SendAudioToSTT(pcmData));

        isSending = false;
        if (statusText != null) statusText.text = "等待说话...";
    }

    /// <summary>
    /// 重置录音状态（丢弃当前数据）
    /// </summary>
    private void ResetRecording()
    {
        isRecording = false;
        audioBuffer.Clear();
        continuousVoiceTime = 0f;
        silenceDuration = 0f;
        if (statusText != null) statusText.text = "等待说话...";
    }

    /// <summary>
    /// 发送音频到 STT API
    /// </summary>
    IEnumerator SendAudioToSTT(byte[] pcmData)
    {
        if (pcmData == null || pcmData.Length == 0) yield break;

        byte[] wavBytes = CreateWavFile(pcmData);

        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wavBytes, "recording.wav", "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(sttApiUrl, form))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string json = req.downloadHandler.text;
                STTResponse response = JsonUtility.FromJson <STTResponse> (json);

                if (response != null && response.code == 200 && response.data != null)
                {
                    string text = response.data.text;
                    ASRtext.text = text;
                    Debug.Log("识别结果：" + text);

                    if (!string.IsNullOrWhiteSpace(text) && ttsClient != null)
                    {
                        ttsClient.ASRChat(text);
                    }
                }
                else
                {
                    Debug.LogWarning("STT 返回错误：" + json);
                }
            }
            else
            {
                Debug.LogError("STT 请求失败：" + req.error);
            }
        }
    }

    /// <summary>
    /// float[] -> 16-bit PCM little-endian
    /// </summary>
    private byte[] ConvertToPCM16(float[] audioData)
    {
        byte[] pcm = new byte[audioData.Length * 2];
        for (int i = 0; i < audioData.Length; i++)
        {
            short val = (short)(Mathf.Clamp(audioData[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(val & 0xff);
            pcm[i * 2 + 1] = (byte)((val >> 8) & 0xff);
        }
        return pcm;
    }

    /// <summary>
    /// 添加 WAV 文件头
    /// </summary>
    private byte[] CreateWavFile(byte[] pcmData)
    {
        int totalSize = 44 + pcmData.Length;
        byte[] wav = new byte[totalSize];

        // RIFF
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        BitConverter.GetBytes(totalSize - 8).CopyTo(wav, 4);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

        // fmt
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        BitConverter.GetBytes(16).CopyTo(wav, 16);
        BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);
        BitConverter.GetBytes((ushort)1).CopyTo(wav, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);
        BitConverter.GetBytes((ushort)2).CopyTo(wav, 32);
        BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);

        // data
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        BitConverter.GetBytes(pcmData.Length).CopyTo(wav, 40);
        Array.Copy(pcmData, 0, wav, 44, pcmData.Length);

        return wav;
    }

    [Serializable]
    private class STTResponse
    {
        public int code;
        public string msg;
        public STTData data;
    }

    [Serializable]
    private class STTData
    {
        public string id;
        public string lang;
        public string emotion;
        public string text;
    }

    private void OnDestroy()
    {
        StopListening();
    }
}