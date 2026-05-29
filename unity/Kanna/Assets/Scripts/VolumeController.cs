using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 独立的音量控制脚本：用两个滑动条分别控制两个 AudioSource 的音量，
/// 并在 TextMeshProUGUI 上实时显示百分比。
/// 挂载到任意 GameObject，在 Inspector 中拖入对应组件即可。
/// </summary>
public class VolumeController : MonoBehaviour
{
    [Header("音频源")]
    public AudioSource voiceSource;    // 人声 / 语音
    public AudioSource bgmSource;      // 背景音乐 / 伴奏

    [Header("UI 滑动条")]
    public Slider voiceSlider;
    public Slider bgmSlider;

    [Header("音量显示文本")]
    public TextMeshProUGUI voiceVolumeText;
    public TextMeshProUGUI bgmVolumeText;

    private void Start()
    {
        // 初始化语音音量滑动条
        if (voiceSource != null && voiceSlider != null)
        {
            voiceSlider.value = voiceSource.volume;
            UpdateVoiceText(voiceSource.volume);
            voiceSlider.onValueChanged.AddListener(OnVoiceSliderChanged);
        }

        // 初始化背景音乐音量滑动条
        if (bgmSource != null && bgmSlider != null)
        {
            bgmSlider.value = bgmSource.volume;
            UpdateBGMText(bgmSource.volume);
            bgmSlider.onValueChanged.AddListener(OnBGMSliderChanged);
        }
    }

    private void OnVoiceSliderChanged(float value)
    {
        if (voiceSource != null)
            voiceSource.volume = value;
        UpdateVoiceText(value);
    }

    private void OnBGMSliderChanged(float value)
    {
        if (bgmSource != null)
            bgmSource.volume = value;
        UpdateBGMText(value);
    }

    private void UpdateVoiceText(float value)
    {
        if (voiceVolumeText != null)
            voiceVolumeText.text = $"{(int)(value * 100)}%";
    }

    private void UpdateBGMText(float value)
    {
        if (bgmVolumeText != null)
            bgmVolumeText.text = $"{(int)(value * 100)}%";
    }
}