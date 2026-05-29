using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 控制单个 AudioSource 的播放/暂停与清空，两个按钮分别绑定对应功能。
/// </summary>
public class AudioControl : MonoBehaviour
{
    [Header("音频源")]
    [SerializeField] private AudioSource audioSource;

    [Header("控制按钮")]
    [SerializeField] private Button playPauseButton;
    [SerializeField] private Button clearButton;

    private void Start()
    {
        if (playPauseButton != null)
            playPauseButton.onClick.AddListener(TogglePlayPause);

        if (clearButton != null)
            clearButton.onClick.AddListener(ClearAudio);
    }

    /// <summary>
    /// 播放/暂停切换：如果正在播放则暂停，否则播放（从当前位置继续）。
    /// </summary>
    public void TogglePlayPause()
    {
        if (audioSource == null) return;

        if (audioSource.isPlaying)
        {
            audioSource.Pause();
        }
        else
        {
            // 如果没有音频片段则无法播放
            if (audioSource.clip == null)
            {
                Debug.LogWarning("AudioControl: 没有音频片段，无法播放。");
                return;
            }
            audioSource.UnPause(); // 如果之前是暂停状态，则继续播放
            if (!audioSource.isPlaying) // 如果 UnPause 后仍未播放（说明从未开始过），则重新播放
                audioSource.Play();
        }
    }

    /// <summary>
    /// 清空音频：停止播放并移除 AudioClip。
    /// </summary>
    public void ClearAudio()
    {
        if (audioSource == null) return;

        audioSource.Stop();
        audioSource.clip = null;
    }

    private void OnDestroy()
    {
        if (playPauseButton != null)
            playPauseButton.onClick.RemoveListener(TogglePlayPause);
        if (clearButton != null)
            clearButton.onClick.RemoveListener(ClearAudio);
    }
}