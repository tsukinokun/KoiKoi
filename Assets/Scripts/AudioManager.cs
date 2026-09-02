using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BGM・ボイス・SEの再生を一元管理するコンポーネント
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Audio Settings (BGM)")]
    [SerializeField] private AudioClip gameBgmClip;      // 流したいBGMのクリップ
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.1f; // BGMの音量調整

    [Header("Audio Settings (Volume)")]
    [Range(0f, 1f)][SerializeField] private float seVolume = 1.0f; // ボイス/SEの音量

    [Header("Audio Settings (Player)")]
    [SerializeField] private AudioClip playerKoiKoiClip;
    [SerializeField] private AudioClip playerAgariClip;

    [Header("Audio Settings (Enemy/NPC)")]
    [SerializeField] private AudioClip enemyKoiKoiClip;
    [SerializeField] private AudioClip enemyAgariClip;

    [Header("Audio Settings (Voices)")]
    [SerializeField] private List<AudioClip> playerVoiceClips; // プレイヤー用の掛け声3種
    [SerializeField] private List<AudioClip> enemyVoiceClips;  // NPC用の掛け声3種

    private AudioSource _audioSource; // ボイス・SE再生用コンポーネント
    private AudioSource _bgmAudioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.spatialBlend = 0f;
        _audioSource.playOnAwake = false;

        _bgmAudioSource = gameObject.AddComponent<AudioSource>();
        _bgmAudioSource.spatialBlend = 0f;
        _bgmAudioSource.loop = true;          // BGMなのでループ再生を有効に
        _bgmAudioSource.playOnAwake = false;   // 管理をコード側で行うため一旦false
        _bgmAudioSource.volume = bgmVolume;

        PlayBGM(gameBgmClip);
    }

    public void PlayPlayerVoice() => PlayRandomVoice(playerVoiceClips);
    public void PlayEnemyVoice() => PlayRandomVoice(enemyVoiceClips);

    public void PlayPlayerKoiKoiVoice() => PlayVoice(playerKoiKoiClip);
    public void PlayPlayerAgariVoice() => PlayVoice(playerAgariClip);
    public void PlayEnemyKoiKoiVoice() => PlayVoice(enemyKoiKoiClip);
    public void PlayEnemyAgariVoice() => PlayVoice(enemyAgariClip);

    /// 指定されたオーディオクリップを安全に再生するヘルパー関数
    private void PlayVoice(AudioClip clip)
    {
        if (_audioSource != null && clip != null)
        {
            _audioSource.volume = seVolume; // ここで音量を適用する
            _audioSource.Stop();
            _audioSource.PlayOneShot(clip);
        }
    }

    private void PlayBGM(AudioClip clip)
    {
        if (_bgmAudioSource == null || clip == null) return;

        _bgmAudioSource.clip = clip;
        _bgmAudioSource.Play();
    }

    private void PlayRandomVoice(List<AudioClip> clips)
    {
        if (clips != null && clips.Count > 0)
        {
            AudioClip clip = clips[Random.Range(0, clips.Count)];
            PlayVoice(clip);
        }
    }
}
