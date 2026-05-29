using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class FastTTS : MonoBehaviour
{
    [Header("TTS 文本（测试用）")]
    [TextArea(5, 10)]
    public string fullText = "";

    [Header("GPT?SoVITS 参数")]
    public string apiUrl = "http://127.0.0.1:9880/tts";
    public string referWavPath ;
    public string promptText;
    public string promptLang;
    public string textLang ;
    public int topK ;
    public float topP;
    public float temperature ;
    public float speed ;
    public int streamingMode ;
    public string mediaType;

    [Header("JSON 配置文件")]
    public bool useJsonConfig = false;
    public string configFileName = "tts_config.json";

    [Header("播放设置")]
    public AudioSource audioSource;
    public Text statusText;

    [Header("保存设置")]
    public bool saveMerged = true;
    public string saveFolder = "TTS_Recordings";

    // 事件：TTS 开始和结束
    public event Action onTTSStarted;
    public event Action onTTSFinished;

    private Queue<string> sentenceQueue = new Queue<string>();
    private Queue<AudioClip> clipQueue = new Queue<AudioClip>();
    private bool isPlaying = false;
    private bool allSynthesized = false;
    private List<byte[]> wavFragments = new List<byte[]>();

    private DateTime requestStartTime;
    private bool firstPlayStarted = false;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.volume = 1.0f;
        audioSource.spatialBlend = 0f;

        if (!Directory.Exists(saveFolder))
            Directory.CreateDirectory(saveFolder);

        if (useJsonConfig)
            LoadConfigFromJson();
    }

    void LoadConfigFromJson()
    {
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"TTS 配置文件不存在: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        TTSConfig config = JsonUtility.FromJson<TTSConfig>(json);
        if (config == null)
        {
            Debug.LogError("TTS 配置文件解析失败");
            return;
        }

        referWavPath = config.referWavPath;
        promptText = config.promptText;
        promptLang = config.promptLang;
        textLang = config.textLang;
        topK = config.topK;
        topP = config.topP;
        temperature = config.temperature;
        speed = config.speed;
        streamingMode = config.streamingMode;
        mediaType = config.mediaType;

        Debug.Log("TTS 配置已从 JSON 文件加载");
    }

    public void StartTTS(string text)
    {
        fullText = CleanText(text);
        StartTTS();
    }

    public void StartTTS()
    {
        StopAllCoroutines();
        wavFragments.Clear();
        clipQueue.Clear();
        sentenceQueue.Clear();
        isPlaying = false;
        allSynthesized = false;
        firstPlayStarted = false;

        requestStartTime = DateTime.Now;
        Debug.Log($"TTS 请求开始：{requestStartTime:HH:mm:ss.fff}");

        var sentences = SplitSentences(fullText);
        if (sentences.Count == 0)
        {
            Debug.LogError("文本切分后无句子");
            return;
        }

        Debug.Log($"共切分出 {sentences.Count} 个句子:");
        foreach (var s in sentences)
        {
            Debug.Log($"  - {s}");
            sentenceQueue.Enqueue(s);
        }

        StartCoroutine(SynthesizeAll());
        StartCoroutine(PlayAllClips());
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. 删除 *...* 或 _..._ 包围的动作/表情描写
        text = Regex.Replace(text, @"[*_].*?[*_]", "");

        // 2. 删除括号及其中的内容（全角“（）”和半角“()”）
        text = Regex.Replace(text, @"[（(][^）)]*[）)]", "");

        // 3. 保留多语言字符 + 常用标点
        // 允许：
        //   - 英文、数字：a-zA-Z0-9
        //   - 中文：\u4e00-\u9fa5
        //   - 中文/日文共通标点：，。！？、：；""''… 空格
        //   - 平假名：\u3040-\u309F
        //   - 片假名：\u30A0-\u30FF
        //   - 半角片假名：\uFF65-\uFF9F
        //   - 日文标点符号块（含「」?等）：\u3000-\u303F
        //   - 日语长音符「ー」：已在片假名块中（\u30FC）
        text = Regex.Replace(text,
            @"[^a-zA-Z0-9\u4e00-\u9fa5\u3040-\u30FF\uFF65-\uFF9F\u3000-\u303F，。！？、：；""''…\s]+",
            "");

        // 4. 合并连续空白并去除首尾
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private List<string> SplitSentences(string text)
    {
        var result = new List<string>();

        // 句子结束标点（中文、日文、英文）
        // 包含：。 ． . ？ ? ！ !  … 
        // 注意：日文句点“。”与中文相同，已包含；英文句点“.”需转义
        string endMarks = @"。．.？！?！";

        // 逗号类（中日英）用于细分短句
        string commaMarks = @"，,、";

        // 匹配一个句子：至少包含一个字符，后跟一个或多个结束标点，或者以逗号分隔的片段
        // 优先按结束标点切分，如果句子太长可考虑按逗号切分（保留原逻辑）
        string pattern = $@"([^{endMarks}{commaMarks}]+[{endMarks}]+)|([^{endMarks}{commaMarks}]+[{commaMarks}]+)";

        var matches = Regex.Matches(text, pattern);
        foreach (Match m in matches)
            result.Add(m.Value.Trim());

        // 处理剩余部分（没有结束标点的文本）
        string remain = Regex.Replace(text, pattern, "").Trim();
        if (!string.IsNullOrEmpty(remain))
            result.Add(remain);

        return result;
    }

    IEnumerator SynthesizeAll()
    {
        int index = 0;
        int total = sentenceQueue.Count;
        while (sentenceQueue.Count > 0)
        {
            string sentence = sentenceQueue.Dequeue();
            index++;
            if (statusText != null)
                statusText.text = $"合成中... ({index}/{total})";
            Debug.Log($"开始合成第 {index} 句: {sentence}");
            yield return StartCoroutine(SynthesizeSentence(sentence));
            total = sentenceQueue.Count + index;
        }
        allSynthesized = true;
        Debug.Log("所有句子合成请求已发送完毕");
    }

    IEnumerator SynthesizeSentence(string text)
    {
        var requestBody = new TTSRequest
        {
            text = text,
            text_lang = textLang,
            ref_audio_path = referWavPath,
            prompt_text = promptText,
            prompt_lang = promptLang,
            top_k = topK,
            top_p = topP,
            temperature = temperature,
            speed_factor = speed,
            streaming_mode = streamingMode,
            media_type = mediaType
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(apiUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"合成失败：{text}，错误：{req.error}");
                yield break;
            }

            byte[] wavBytes = req.downloadHandler.data;
            if (wavBytes == null || wavBytes.Length < 44)
            {
                Debug.LogError($"返回的音频数据无效，长度：{wavBytes?.Length}");
                yield break;
            }

            if (saveMerged)
                wavFragments.Add(wavBytes);

            AudioClip clip = CreateAudioClipFromWav(wavBytes);
            if (clip != null)
            {
                clipQueue.Enqueue(clip);
                Debug.Log($"片段加载成功：{text.Substring(0, Mathf.Min(text.Length, 10))}... (队列长度 {clipQueue.Count})");
            }
            else
            {
                Debug.LogError("无法从 WAV 数据创建 AudioClip");
            }
        }
    }

    private AudioClip CreateAudioClipFromWav(byte[] wavData)
    {
        if (wavData.Length < 44) return null;

        int channels = BitConverter.ToInt16(wavData, 22);
        int sampleRate = BitConverter.ToInt32(wavData, 24);
        int bitsPerSample = BitConverter.ToInt16(wavData, 34);
        if (bitsPerSample != 16)
            Debug.LogWarning($"非标准位深：{bitsPerSample}，预期16位");

        int dataOffset = FindDataChunkOffset(wavData);
        if (dataOffset < 0) return null;

        int dataLength = wavData.Length - dataOffset;
        int sampleCount = dataLength / (channels * (bitsPerSample / 8));

        AudioClip clip = AudioClip.Create("TTS_Fragment", sampleCount, channels, sampleRate, false);
        if (clip == null) return null;

        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short pcm = BitConverter.ToInt16(wavData, dataOffset + i * 2);
            samples[i] = pcm / 32768f;
        }

        clip.SetData(samples, 0);
        return clip;
    }

    private int FindDataChunkOffset(byte[] wav)
    {
        int pos = 12;
        while (pos < wav.Length - 8)
        {
            if (wav[pos] == 'd' && wav[pos + 1] == 'a' && wav[pos + 2] == 't' && wav[pos + 3] == 'a')
                return pos + 8;
            pos++;
        }
        return -1;
    }

    IEnumerator PlayAllClips()
    {
        while (!allSynthesized || clipQueue.Count > 0 || isPlaying)
        {
            if (clipQueue.Count > 0 && !isPlaying)
            {
                AudioClip clip = clipQueue.Dequeue();
                if (clip == null)
                {
                    Debug.LogWarning("队列中出现 null AudioClip，跳过");
                    continue;
                }

                if (!firstPlayStarted)
                {
                    TimeSpan elapsed = DateTime.Now - requestStartTime;
                    Debug.Log($"?? 首句播放延迟: {elapsed.TotalMilliseconds:F0} ms ({elapsed.TotalSeconds:F2} s)");
                    firstPlayStarted = true;
                    // 触发开始事件
                    onTTSStarted?.Invoke();
                }

                isPlaying = true;
                audioSource.clip = clip;
                audioSource.Play();
                if (statusText != null)
                    statusText.text = "播放中...";
                Debug.Log($"开始播放片段，剩余队列长度: {clipQueue.Count}");

                while (audioSource.isPlaying)
                    yield return null;

                Debug.Log("片段播放完毕");
                isPlaying = false;
                audioSource.clip = null;
            }
            else
            {
                yield return null;
            }
        }

        Debug.Log("所有音频播放完毕");
        // 触发结束事件
        onTTSFinished?.Invoke();

        if (saveMerged && wavFragments.Count > 0)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string mergedPath = Path.Combine(saveFolder, $"TTS_{timestamp}_merged.wav");
            MergeWavFiles(wavFragments, mergedPath);
            if (statusText != null)
                statusText.text = "完成，文件已保存：" + mergedPath;
        }
        else
        {
            if (statusText != null)
                statusText.text = "播放完成";
        }
    }

    private void MergeWavFiles(List<byte[]> wavs, string outputPath)
    {
        if (wavs.Count == 0) return;

        byte[] first = wavs[0];
        int sampleRate = BitConverter.ToInt32(first, 24);
        short channels = BitConverter.ToInt16(first, 22);
        short bitsPerSample = BitConverter.ToInt16(first, 34);

        List<byte[]> pcmChunks = new List<byte[]>();
        foreach (var wav in wavs)
        {
            int dataOffset = FindDataChunkOffset(wav);
            if (dataOffset < 0) continue;
            int dataSize = wav.Length - dataOffset;
            byte[] pcm = new byte[dataSize];
            Array.Copy(wav, dataOffset, pcm, 0, dataSize);
            pcmChunks.Add(pcm);
        }

        int totalPcmLength = 0;
        foreach (var pcm in pcmChunks) totalPcmLength += pcm.Length;

        using (FileStream fs = new FileStream(outputPath, FileMode.Create))
        {
            fs.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
            fs.Write(BitConverter.GetBytes(36 + totalPcmLength), 0, 4);
            fs.Write(Encoding.ASCII.GetBytes("WAVE"), 0, 4);
            fs.Write(Encoding.ASCII.GetBytes("fmt "), 0, 4);
            fs.Write(BitConverter.GetBytes(16), 0, 4);
            fs.Write(BitConverter.GetBytes((short)1), 0, 2);
            fs.Write(BitConverter.GetBytes(channels), 0, 2);
            fs.Write(BitConverter.GetBytes(sampleRate), 0, 4);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            fs.Write(BitConverter.GetBytes(byteRate), 0, 4);
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            fs.Write(BitConverter.GetBytes(blockAlign), 0, 2);
            fs.Write(BitConverter.GetBytes(bitsPerSample), 0, 2);
            fs.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
            fs.Write(BitConverter.GetBytes(totalPcmLength), 0, 4);

            foreach (var pcm in pcmChunks)
                fs.Write(pcm, 0, pcm.Length);
        }
    }

    [Serializable]
    private class TTSRequest
    {
        public string text;
        public string text_lang;
        public string ref_audio_path;
        public string prompt_text;
        public string prompt_lang;
        public int top_k;
        public float top_p;
        public float temperature;
        public float speed_factor;
        public int streaming_mode;
        public string media_type;
    }

    [Serializable]
    private class TTSConfig
    {
        public string referWavPath;
        public string promptText;
        public string promptLang;
        public string textLang;
        public int topK;
        public float topP;
        public float temperature;
        public float speed;
        public int streamingMode;
        public string mediaType;
    }
}